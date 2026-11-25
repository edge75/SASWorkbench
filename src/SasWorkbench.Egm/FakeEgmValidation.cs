using System.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Tickets;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>One record of the validation buffer, as the library keeps it (sCashoutHandpayTicket).</summary>
internal sealed record ValidationRecord(
    byte Type,
    DateTime? At,
    ulong Number,
    long AmountCents,
    ushort TicketNumber,
    byte SystemId,
    ushort ExpirationDays,
    bool Completed);

/// <summary>Where a cashout validation has got to (STATE_CASHOUT_VALIDATION).</summary>
internal enum CashoutValidationState
{
    None,
    Started,
    Lp57Received,
    Lp58Received,
}

/// <summary>
/// Secure enhanced and system validation, mirrored from the machine library: the validation buffer
/// and its wrap, the identifier and sequence number, the system validation exchange and the timers
/// that reissue $3D, $3F and $57.
/// </summary>
public sealed partial class FakeEgm
{
    /// <summary>The library's buffer: thirty-two slots, slot zero used only before the first wrap (CASHOUT_HANDPAY_TICKET_BUFFER_ITEMS).</summary>
    private const int BufferItems = 32;

    /// <summary>Where the write index wraps back to (sasConf.validationBufferStart, libsas_internal.cpp:820).</summary>
    private const int BufferStart = 1;

    /// <summary>The control mask bits of long poll $7B (LP_CONTROL_MASK, libsas.h:2083).</summary>
    private const ushort PrinterAsCashoutDevice = 0x0001;
    private const ushort TicketRedemptionAllowed = 0x0020;
    private const ushort SecureEnhancedConfigured = 0x8000;

    /// <summary>The library's default ticket expiration in days (DEFAULT_TICKET_EXPIRATION).</summary>
    private const ushort DefaultExpirationDays = 30;

    private static readonly TimeSpan ValidationIdReissue = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PrintedReissue = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan UnreadLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SystemRequestReissue = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan SystemTimeout = TimeSpan.FromSeconds(10);

    // One spare slot past the end holds what the current write overwrote, so a cancel can put it back.
    private readonly ValidationRecord?[] _buffer = new ValidationRecord?[BufferItems + 1];
    private int _bufferIndex;
    private int _pendingRecords;
    private long _unreadSince;
    private long _printedReissueDue;
    private bool _validationRecordsUnread;
    private bool _validationBufferFull;

    private bool _seIsSet;
    private bool _seIncremented;
    private uint _seBaseSequence;
    private long _validationIdDue;
    private long _validationStartedAt;

    // The library's two status words: what the host asked for and what the machine can do (libsas_internal.cpp:783).
    private ushort _hostValidationStatus = 0x803F;

    // QSP clears handpay receipts and handpay validation, and restricted tickets stay off on this machine (QSP SasNetwork.cpp:617).
    private const ushort GameValidationStatus = 0x8021;

    // What QSP last heard from long poll $7B, its default until then (QSP CashoutPrinterRules.h:142).
    private ushort _platformValidationStatus = 0x0039;
    private ushort _cashableExpirationDays = DefaultExpirationDays;
    private ushort _restrictedExpirationDays = DefaultExpirationDays;

    private CashoutValidationState _cashoutState;
    private bool _awaitingValidationNumber;
    private long _systemStartedAt;
    private long _systemRequestDue;
    private (long Cents, long Credits, string Wallet)? _awaitingPrint;

    private readonly Dictionary<SasChannel, byte[]> _unacknowledgedValidationReads = [];

    /// <summary>Unread records in the validation buffer, for a test that asks directly.</summary>
    public int UnreadValidationRecords
    {
        get
        {
            lock (_gate)
                return _pendingRecords;
        }
    }

    /// <summary>True when the host has withdrawn the printer as the cash-out device with $7B bit 0.</summary>
    private bool HostAllowsPrinter()
    {
        lock (_gate)
            return (_platformValidationStatus & PrinterAsCashoutDevice) != 0;
    }

    /// <summary>False when the host has withdrawn ticket redemption with $7B bit 5.</summary>
    private bool HostAllowsRedemption()
    {
        lock (_gate)
            return (_platformValidationStatus & TicketRedemptionAllowed) != 0;
    }

    /// <summary>Reports a missing validation identifier at power-up, as sasInit does (libsas.cpp:1999).</summary>
    private void StartValidationIdReporting()
    {
        _validationStartedAt = Stopwatch.GetTimestamp();
        bool report;
        lock (_gate)
        {
            report = CashoutValidation == SasCashoutValidation.SecureEnhanced && !_seIsSet;
            if (report)
                _validationIdDue = Stopwatch.GetTimestamp() + Ticks(ValidationIdReissue);
        }

        if (report)
            QueueRanked((byte)SasExceptionCode.ValidationIdNotConfigured);
    }

    /// <summary>
    /// Pays a cash-out in a validation style, or by hand where the platform may not print (QSP
    /// IsTicketOutAvailable, SasNetwork_Ticket.hpp:756).
    /// </summary>
    private Ticket? PayOutValidated(string walletDirectory)
    {
        TickValidation();

        long restrictedCredits = CurrentRestrictedCents() / AccountingCents;
        long credits = Meters.Read(SasMeterCode.CurrentCredits) - restrictedCredits;
        if (credits <= 0)
            return null;

        long cents = credits * AccountingCents;
        if (!MayPrintValidated())
        {
            RaiseCashoutHandpay(cents);
            return null;
        }

        Meters.SetCurrentCredits(restrictedCredits);
        ClampNonrestricted();

        // QSP numbers the ticket before the library is asked, and wraps at ten thousand (SasNetwork_Ticket.hpp:288).
        uint printed = (uint)Interlocked.Increment(ref _ticketsPrinted);
        var ticketNumber = (ushort)(printed % 10_000);

        ulong number;
        bool started;
        lock (_gate)
        {
            started = TryStartCashout(cents, ticketNumber, out number);
            if (started && CashoutValidation == SasCashoutValidation.System)
                _awaitingPrint = (cents, credits, walletDirectory);
        }

        // sasStartCashout refused, so the cash-out is paid by hand (QSP SasNetwork_Ticket.hpp:1040).
        if (!started)
        {
            RaiseCashoutHandpay(cents);
            return null;
        }

        // System validation prints only once the host sends a number (QSP SasNetwork_Ticket.hpp:366).
        if (CashoutValidation == SasCashoutValidation.System)
        {
            QueueRanked((byte)SasExceptionCode.SystemValidationRequest);
            return null;
        }

        return PrintValidated(walletDirectory, cents, credits, number, systemId: 0);
    }

    /// <summary>Whether the platform may print now: the printer allowed, the link up and no record unread (QSP CashoutPrinterRules.h:65).</summary>
    private bool MayPrintValidated()
    {
        if (!HostAllowsPrinter())
            return false;
        if (CashoutValidation == SasCashoutValidation.Standard)
            return true;
        if (ValidationLinkDown())
            return false;

        lock (_gate)
            return !_validationRecordsUnread;
    }

    /// <summary>True when the channel that owns validation has heard nothing past the loop break.</summary>
    private bool ValidationLinkDown()
    {
        if (_options.LinkLossAfter <= TimeSpan.Zero)
            return false;

        foreach (MachineLink link in Links())
        {
            if (!Channels.Answers(link.Channel, (byte)SasLongPollCode.LP_SENDENHANCEDVALIDATIONINFO))
                continue;

            // A link not yet heard from has been quiet since the machine started, as the library counts it.
            long since = link.LastHeard != 0 ? link.LastHeard : _validationStartedAt;
            return Stopwatch.GetElapsedTime(since) > _options.LinkLossAfter;
        }

        return true;
    }

    /// <summary>
    /// sasStartCashout: refused while a validation is in progress or the identifier is unset, then
    /// the write index advances and the slot is written (libsas_wrapper.cpp:2212). Called under the gate.
    /// </summary>
    private bool TryStartCashout(long cents, ushort ticketNumber, out ulong number, byte type = 0x00)
    {
        number = 0;
        if (_cashoutState != CashoutValidationState.None)
            return false;
        if (CashoutValidation == SasCashoutValidation.SecureEnhanced && !_seIsSet)
            return false;

        _cashoutState = CashoutValidationState.Started;
        _bufferIndex = _bufferIndex == BufferItems - 1 ? BufferStart : _bufferIndex + 1;
        _buffer[BufferItems] = _buffer[_bufferIndex];

        DateTime? at = null;
        if (CashoutValidation == SasCashoutValidation.SecureEnhanced)
        {
            number = NextSecureEnhancedNumber();
            at = DateTime.Now;
        }
        else
        {
            long now = Stopwatch.GetTimestamp();
            _awaitingValidationNumber = true;
            _systemStartedAt = now;
            _systemRequestDue = now + Ticks(SystemRequestReissue);
        }

        // QSP reports the expiration it prints, in days (SasNetwork_Ticket.hpp:313).
        _buffer[_bufferIndex] = new ValidationRecord(
            Type: type, at, number, cents, ticketNumber, SystemId: 0, _cashableExpirationDays, Completed: false);
        return true;
    }

    /// <summary>Prints a validated ticket and completes the cash-out, as QSP's print completion does (SasNetwork_Ticket.hpp:1173).</summary>
    private Ticket PrintValidated(string walletDirectory, long cents, long credits, ulong number, byte systemId)
    {
        ushort ticketNumber;
        lock (_gate)
            ticketNumber = _buffer[_bufferIndex]?.TicketNumber ?? 0;

        MeterTicketOut(credits, cents);
        var ticket = new Ticket
        {
            ValidationNumber = number,
            ValidationSystemId = systemId,
            ValidationType = TicketValidationType.CashableFromCashout,
            AmountCents = cents,
            TicketNumber = ticketNumber,
            PrintedAt = DateTime.Now,
            Location = TicketText().Location,
            Address1 = TicketText().Address1,
            Address2 = TicketText().Address2,
            Note = "printed by the software machine",
        };

        FileTicket(walletDirectory, ticket);
        CompleteCashout();
        return ticket;
    }

    /// <summary>internalCompleteCashout(TRUE): the record is complete and counted unread, and $3D goes out for the first (libsas_internal.cpp:5800).</summary>
    private void CompleteCashout()
    {
        bool announce;
        byte code;
        lock (_gate)
        {
            _cashoutState = CashoutValidationState.None;
            if (_buffer[_bufferIndex] is { } record)
                _buffer[_bufferIndex] = record with { Completed = true };

            if (_pendingRecords == 0)
                _unreadSince = Stopwatch.GetTimestamp();
            _pendingRecords = Math.Min(_pendingRecords + 1, BufferItems - 1);
            announce = _pendingRecords <= 1;
            if (_pendingRecords == 1)
                _printedReissueDue = Stopwatch.GetTimestamp() + Ticks(PrintedReissue);

            // A full buffer is an inhibiting lockup on the platform (QSP SasNetwork_Callbacks.hpp:367).
            if (_pendingRecords >= BufferItems - 1)
                _validationBufferFull = true;
            code = UnreadAnnouncementLocked();
        }

        if (announce)
            QueueRanked(code);
        WithdrawLockIfUnplayable();
    }

    /// <summary>internalCompleteCashout(FALSE): the overwritten slot is put back and the write index steps back (libsas_internal.cpp:5845).</summary>
    private void CancelCashout()
    {
        if (_cashoutState == CashoutValidationState.None)
            return;

        _cashoutState = CashoutValidationState.None;
        _buffer[_bufferIndex] = _buffer[BufferItems];
        _bufferIndex = _bufferIndex == BufferStart ? BufferItems - 1 : _bufferIndex - 1;
    }

    /// <summary>A cash-out the platform cannot print is paid by hand as cancelled credit (QSP EscalateCancelledTicketOutToHandpay).</summary>
    private void RaiseCashoutHandpay(long cents)
    {
        long restricted = CurrentRestrictedCents() / AccountingCents;
        lock (_gate)
        {
            if (_handpay is not null)
                return;

            StartHandpayLocked(new HandpayOwed(HandpayKind.CancelledCredit, cents, CancelledCreditLevel));
        }

        if (Meters.Read(SasMeterCode.CurrentCredits) > restricted)
        {
            Meters.SetCurrentCredits(restricted);
            ClampNonrestricted();
        }

        WithdrawLockIfUnplayable();
    }

    /// <summary>
    /// internalCalculateSecureEnhancedValidationId: the sequence number advances first, then six
    /// bytes of sequence and identifier are folded through three check sequences into sixteen digits
    /// (libsas_internal.cpp:5630). Called under the gate.
    /// </summary>
    private ulong NextSecureEnhancedNumber()
    {
        _secureSequenceNumber++;
        _seIncremented = true;
        return SecureEnhancedNumber(_secureValidationId, _secureSequenceNumber);
    }

    /// <summary>The library's secure enhanced algorithm for one identifier and sequence number.</summary>
    internal static ulong SecureEnhancedNumber(uint validationId, uint sequenceNumber)
    {
        Span<byte> a = stackalloc byte[6];
        a[0] = (byte)sequenceNumber;
        a[1] = (byte)(sequenceNumber >> 8);
        a[2] = (byte)(sequenceNumber >> 16);
        a[3] = (byte)validationId;
        a[4] = (byte)(validationId >> 8);
        a[5] = (byte)(validationId >> 16);

        Span<byte> b = [a[0], a[1], (byte)(a[2] ^ a[0]), (byte)(a[3] ^ a[1]), (byte)(a[4] ^ a[0]), (byte)(a[5] ^ a[1])];
        Span<byte> c = stackalloc byte[6];
        for (int pair = 0; pair < 3; pair++)
        {
            ushort crc = SasCrc.Compute(b.Slice(pair * 2, 2));
            c[pair * 2] = (byte)crc;
            c[(pair * 2) + 1] = (byte)(crc >> 8);
        }

        Span<byte> n = stackalloc byte[8];
        WriteReversedBcd(c[0] | (c[1] << 8) | (c[2] << 16), n[..4]);
        WriteReversedBcd(c[3] | (c[4] << 8) | (c[5] << 16), n[4..]);

        n[3] |= CheckNibble(n[..4]);
        n[7] |= CheckNibble(n[4..]);

        ulong value = 0;
        for (int i = 7; i >= 0; i--)
            value = (value * 100) + (ulong)((n[i] >> 4) * 10) + (ulong)(n[i] & 0x0F);
        return value;
    }

    /// <summary>Eight BCD digits, least significant byte first, as decToBcdArray then internalReverseCopy leave them.</summary>
    private static void WriteReversedBcd(int value, Span<byte> target)
    {
        for (int i = 0; i < 4; i++)
        {
            target[i] = (byte)((value % 10) | (((value / 10) % 10) << 4));
            value /= 100;
        }
    }

    /// <summary>Step five: the digit sum modulo five, doubled, in the top nibble.</summary>
    private static byte CheckNibble(ReadOnlySpan<byte> digits)
    {
        int sum = 0;
        for (int i = 0; i < 8; i++)
            sum += (digits[i / 2] >> ((i % 2) * 4)) & 0x0F;
        return (byte)((((sum % 5) << 1) << 4) & 0xF0);
    }

    /// <summary>
    /// internalGetValidationBufferReadIndex: the oldest unread record, 0 before anything was written
    /// and $FF when nothing is unread (libsas_internal.cpp:5714). Called under the gate.
    /// </summary>
    private int ValidationReadIndex()
    {
        int index = _bufferIndex;
        if (index == 0)
            return 0;
        if (_pendingRecords == 0)
            return 0xFF;

        // A record whose cash-out has not completed is not one to read yet (WYMA-41).
        if (_buffer[index] is not { Completed: true })
            index = index == BufferStart ? BufferItems - 1 : index - 1;

        int read = index - _pendingRecords + 1;
        if (read <= 0)
            read = (BufferItems - 1) - (_pendingRecords - index - 1);
        return read;
    }

    /// <summary>
    /// internalDecrementValidationBufferPendingCounter, run on the implied acknowledgement of a $4D
    /// read: one fewer unread, and $3D again while any remain (libsas_pending.cpp:63).
    /// </summary>
    private void RetireValidationRecord()
    {
        bool announce;
        byte code;
        lock (_gate)
        {
            if (_pendingRecords == 0)
                return;

            _pendingRecords--;
            announce = _pendingRecords > 0;
            if (_pendingRecords == BufferItems - 2)
                _validationBufferFull = false;
            if (_pendingRecords == 0)
            {
                // The host read the buffer, so the printer is the cash-out device again (QSP SasNetwork_Callbacks.hpp:392).
                _validationRecordsUnread = false;
                _unreadSince = 0;
            }

            code = UnreadAnnouncementLocked();
        }

        if (announce)
            QueueRanked(code);
    }

    /// <summary>Runs the validation timers: $3F every fifteen seconds, $3D every fifteen, the ten-second unread rule and $57.</summary>
    private void TickValidation()
    {
        if (CashoutValidation == SasCashoutValidation.Standard)
            return;

        long now = Stopwatch.GetTimestamp();
        bool idNotConfigured = false, reissuePrinted = false, requestAgain = false, timedOut = false;
        byte printedCode = (byte)SasExceptionCode.CashoutTicketPrinted;
        long? owed = null;

        lock (_gate)
        {
            // libsas_threads.cpp:3213, every fifteen seconds until the identifier is set.
            if (CashoutValidation == SasCashoutValidation.SecureEnhanced && !_seIsSet
                && _validationIdDue != 0 && now >= _validationIdDue)
            {
                idNotConfigured = true;
                _validationIdDue = now + Ticks(ValidationIdReissue);
            }

            // libsas_threads.cpp:2338, $3D again every fifteen seconds while records are unread.
            if (_pendingRecords > 0 && now >= _printedReissueDue)
            {
                reissuePrinted = true;
                _printedReissueDue = now + Ticks(PrintedReissue);
                printedCode = UnreadAnnouncementLocked();
            }

            // libsas_threads.cpp:2365, unread past ten seconds takes the printer out of use (QSP SasNetwork_Callbacks.hpp:381).
            if (_pendingRecords > 0 && _unreadSince != 0 && Stopwatch.GetElapsedTime(_unreadSince, now) >= UnreadLimit)
                _validationRecordsUnread = true;

            // libsas_threads.cpp:2791, $57 every 800 ms until $57 is read, and ten seconds to wait for $58.
            bool waiting = _cashoutState == CashoutValidationState.Started
                           || (_cashoutState == CashoutValidationState.Lp57Received && _awaitingValidationNumber);
            if (CashoutValidation == SasCashoutValidation.System && waiting)
            {
                if (Stopwatch.GetElapsedTime(_systemStartedAt, now) >= SystemTimeout)
                {
                    timedOut = true;
                    _cashoutState = CashoutValidationState.None;
                    _awaitingValidationNumber = false;
                    owed = _awaitingPrint?.Cents;
                    _awaitingPrint = null;
                }
                else if (_cashoutState == CashoutValidationState.Started && now >= _systemRequestDue)
                {
                    requestAgain = true;
                    _systemRequestDue = now + Ticks(SystemRequestReissue);
                }
            }
        }

        if (idNotConfigured)
            QueueRanked((byte)SasExceptionCode.ValidationIdNotConfigured);
        if (reissuePrinted)
            QueueRanked(printedCode);
        if (requestAgain)
            QueueRanked((byte)SasExceptionCode.SystemValidationRequest);
        if (timedOut)
        {
            Withdraw((byte)SasExceptionCode.SystemValidationRequest);

            // The host never answered, so the player is paid by hand (QSP SasNetwork_Callbacks.hpp:329).
            if (owed is { } cents)
                RaiseCashoutHandpay(cents);
        }
    }
}
