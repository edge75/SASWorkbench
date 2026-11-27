using System.Diagnostics;
using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Egm;

/// <summary>The validation long polls, $4C, $4D, $57, $58 and $7B, each in the library handler's order.</summary>
public sealed partial class FakeEgm
{
    /// <summary>Function codes of long poll $4D: read and advance, or look without advancing.</summary>
    private const byte ReadCurrentRecord = 0x00;
    private const byte LookAtCurrentRecord = 0xFF;

    /// <summary>
    /// lp4C: silent unless secure enhanced (lp.cpp:2454); a non-zero identifier restarts the sequence
    /// only when the identifier, the base or the fact of a ticket printed since says so (lp.cpp:2479).
    /// </summary>
    private Dictionary<string, object>? SecureEnhancedValidationId(SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        if (CashoutValidation != SasCashoutValidation.SecureEnhanced)
            return NoAnswer;

        SasDecoded asked = SasMessageDecoder.DecodeRequest(layout, request);
        if (asked.Number("validation_id") is not { } identifier
            || asked.Number("sequence_number") is not { } sequence)
        {
            return null;
        }

        lock (_gate)
        {
            if (identifier != 0)
            {
                if (_secureValidationId != (uint)identifier || !_seIncremented || _seBaseSequence != (uint)sequence)
                {
                    _secureSequenceNumber = (uint)sequence;
                    _seBaseSequence = (uint)sequence;
                    _seIncremented = false;
                }

                _secureValidationId = (uint)identifier;
                _seIsSet = true;
                _validationIdDue = 0;
                _hostValidationStatus |= SecureEnhancedConfigured;
            }

            return new Dictionary<string, object>
            {
                ["validation_id"] = (long)(_secureValidationId & 0xFFFFFF),
                ["sequence_number"] = (long)(_secureSequenceNumber & 0xFFFFFF),
            };
        }
    }

    /// <summary>
    /// lp4D: silent under standard validation (lp.cpp:2582); otherwise the record the function code
    /// chooses, or every field zero when nothing is unread (lp.cpp:2675).
    /// </summary>
    private Dictionary<string, object>? EnhancedValidationInformation(ReadOnlySpan<byte> request)
    {
        if (CashoutValidation == SasCashoutValidation.Standard || request.Length < 3)
            return NoAnswer;

        byte function = request[2];
        lock (_gate)
        {
            int index = function is ReadCurrentRecord or LookAtCurrentRecord
                ? ValidationReadIndex()
                : function > BufferItems - 1 ? 0xFF : function;

            if (index == 0xFF)
                return ValidationRecordValues(null, 0);

            return ValidationRecordValues(_buffer[index], (byte)index);
        }
    }

    private static Dictionary<string, object> ValidationRecordValues(ValidationRecord? record, byte index)
    {
        DateTime at = record?.At ?? default;
        bool dated = record?.At is not null;
        return new Dictionary<string, object>
        {
            ["validation_type"] = record?.Type ?? 0,
            ["index"] = index,
            ["month"] = dated ? (long)at.Month : 0L,
            ["day"] = dated ? (long)at.Day : 0L,
            ["year"] = dated ? (long)at.Year : 0L,
            ["hour"] = dated ? (long)at.Hour : 0L,
            ["minute"] = dated ? (long)at.Minute : 0L,
            ["second"] = dated ? (long)at.Second : 0L,
            ["validation_number"] = (long)(record?.Number ?? 0),
            ["amount"] = record?.AmountCents ?? 0L,
            ["ticket_number"] = (long)(record?.TicketNumber ?? 0),
            ["validation_system_id"] = (long)(record?.SystemId ?? 0),

            // QSP prints the expiration as a number of days, which the date form carries as month and day zero (SasNetwork_Ticket.hpp:319).
            ["expiration_month"] = 0L,
            ["expiration_day"] = 0L,
            ["expiration_year"] = (long)(record?.ExpirationDays ?? 0),
            ["pool_id"] = 0L,
        };
    }

    /// <summary>
    /// lp57: reading it moves a started cash-out on, and it answers the newest record while one is
    /// waiting for system validation and $80 otherwise (lp.cpp:3442).
    /// </summary>
    private Dictionary<string, object> PendingCashout()
    {
        lock (_gate)
        {
            if (_cashoutState == CashoutValidationState.Started)
                _cashoutState = CashoutValidationState.Lp57Received;

            if (_cashoutState != CashoutValidationState.Lp57Received || _buffer[_bufferIndex] is not { } record)
            {
                return new Dictionary<string, object> { ["cashout_type"] = (byte)0x80, ["amount"] = 0L };
            }

            // Every type but a restricted ticket is a cashable one (lp.cpp:3464).
            byte type = record.Type is 0x01 or 0x03 or 0x81 or 0x82 ? (byte)0x01 : (byte)0x00;
            return new Dictionary<string, object> { ["cashout_type"] = type, ["amount"] = record.AmountCents };
        }
    }

    /// <summary>
    /// lp58: a zero or non-BCD system ID, or a number without $57 first, is a denial that cancels the
    /// cash-out; otherwise the number is the host's and the ticket prints (lp.cpp:3580).
    /// </summary>
    private Dictionary<string, object>? ReceiveValidationNumber(SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        if (request.Length < 12)
            return null;

        // A system ID that is not BCD is read as zero, a denial (lp.cpp:3571).
        byte rawId = request[2];
        bool bcd = (rawId >> 4) <= 9 && (rawId & 0x0F) <= 9;
        var systemId = (byte)(bcd ? ((rawId >> 4) * 10) + (rawId & 0x0F) : 0);
        ulong number = 0;
        foreach (byte b in request.Slice(3, 8))
            number = (number * 100) + (ulong)((b >> 4) * 10) + (ulong)(b & 0x0F);

        byte status;
        bool granted;
        (long Cents, long Credits, string Wallet)? toPrint = null;
        long deniedCents = 0;
        lock (_gate)
        {
            CashoutValidationState state = _cashoutState;
            ValidationRecord record = _buffer[_bufferIndex] ?? new ValidationRecord(0, null, 0, 0, 0, 0, 0, false);
            granted = systemId != 0 && state == CashoutValidationState.Lp57Received;
            status = granted ? (byte)0x00 : ResolveValidationNumberStatus(systemId, state);
            _buffer[_bufferIndex] = record with
            {
                Number = granted ? number : 0,
                SystemId = granted ? systemId : (byte)0,
                At = granted ? DateTime.Now : record.At,
            };

            if (granted)
            {
                _awaitingValidationNumber = false;

                // QSP prints once per cash-out and ignores a repeated $58 (SasNetwork_Ticket.hpp:455).
                toPrint = _awaitingPrint;
                _awaitingPrint = null;
            }
            else if (state is not (CashoutValidationState.None or CashoutValidationState.Lp58Received))
            {
                _cashoutState = CashoutValidationState.Lp58Received;
                CancelCashout();
                _awaitingValidationNumber = false;
                deniedCents = _awaitingPrint?.Cents ?? 0;
                _awaitingPrint = null;
            }
        }

        Withdraw((byte)SasExceptionCode.SystemValidationRequest);

        // The library answers first and only then hands the print or the hand pay to the platform (lp.cpp:3629).
        lock (_gate)
            _afterValidationNumber = new ValidationNumberFollowUp(toPrint, number, systemId, deniedCents);

        return new Dictionary<string, object> { ["status"] = status };
    }

    /// <summary>What a $58 leaves for the platform once its reply has gone out.</summary>
    private sealed record ValidationNumberFollowUp(
        (long Cents, long Credits, string Wallet)? Print, ulong Number, byte SystemId, long DeniedCents);

    private ValidationNumberFollowUp? _afterValidationNumber;

    /// <summary>Prints the granted ticket, or pays a denied one by hand, after the $58 reply (QSP SasNetwork_Ticket.hpp:1093).</summary>
    private void FinishValidationNumber()
    {
        ValidationNumberFollowUp? work;
        lock (_gate)
        {
            work = _afterValidationNumber;
            _afterValidationNumber = null;
        }

        if (work is null)
            return;

        if (work.Print is { } print)
            PrintValidated(print.Wallet, print.Cents, print.Credits, work.Number, work.SystemId);

        // A denied validation is paid by hand, never printed.
        if (work.DeniedCents > 0)
            RaiseCashoutHandpay(work.DeniedCents);
    }

    /// <summary>internalResolveValidationNumberStatus (libsas_internal.cpp:2501).</summary>
    private static byte ResolveValidationNumberStatus(byte systemId, CashoutValidationState state)
    {
        if (state == CashoutValidationState.None)
            return 0x80;
        if (systemId == 0)
            return 0x00;
        return state != CashoutValidationState.Lp57Received ? (byte)0x81 : (byte)0x00;
    }

    /// <summary>
    /// lp7B without the automatic reply QSP leaves off: each masked bit set or cleared, the reply the
    /// host's status and the machine's, and bit 15 cleared cancels the identifier (lp.cpp:7071, :7243).
    /// </summary>
    private Dictionary<string, object>? ExtendedValidationStatus(ReadOnlySpan<byte> request)
    {
        SasDecoded asked = SasMessageDecoder.DecodeRequest(SasLayouts.For(0x7B)!, request);
        var mask = (ushort)(asked.Number("control_mask") ?? 0);
        var states = (ushort)(asked.Number("status_bit_control_states") ?? 0);
        var cashable = (ushort)(asked.Number("cashable_ticket_expiration") ?? 0);
        var restricted = (ushort)(asked.Number("restricted_ticket_expiration") ?? 0);

        bool cancelled = false;
        ushort reply;
        lock (_gate)
        {
            ushort current = _hostValidationStatus;
            for (int bit = 0; bit < 16; bit++)
            {
                var flag = (ushort)(1 << bit);
                if ((mask & flag) == 0)
                    continue;

                current = (states & flag) != 0 ? (ushort)(current | (flag & 0x803F)) : (ushort)(current & ~flag);
            }

            _hostValidationStatus = current;
            reply = (ushort)(current & GameValidationStatusNow);

            if (_seIsSet && (mask & SecureEnhancedConfigured) != 0 && (states & SecureEnhancedConfigured) == 0)
            {
                cancelled = true;
                _seIsSet = false;
                _validationIdDue = Stopwatch.GetTimestamp() + Ticks(ValidationIdReissue);
                _cashableExpirationDays = DefaultExpirationDays;
                _restrictedExpirationDays = DefaultExpirationDays;
            }
            else
            {
                if (cashable != 0)
                    _cashableExpirationDays = cashable;
                if (restricted != 0)
                    _restrictedExpirationDays = restricted;
            }

            // QSP acts on the status word the library reports to it (SasNetwork_Ticket.hpp:212).
            _platformValidationStatus = reply;
        }

        if (cancelled)
            QueueRanked((byte)SasExceptionCode.ValidationIdNotConfigured);

        lock (_gate)
        {
            return new Dictionary<string, object>
            {
                ["length"] = 0x0A,
                ["asset_number"] = (long)AssetNumber,
                ["current_validation_status"] = (long)reply,
                ["cashable_ticket_expiration"] = (long)_cashableExpirationDays,
                ["restricted_ticket_expiration"] = (long)_restrictedExpirationDays,
            };
        }
    }

    /// <summary>Remembers a $4D read that advances, so the next message can acknowledge it (lp.cpp:2720).</summary>
    private void RememberValidationRead(MachineLink link, ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 3 || frame[2] != ReadCurrentRecord || CashoutValidation == SasCashoutValidation.Standard)
            return;

        lock (_gate)
            _unacknowledgedValidationReads.TryAdd(link.Channel, frame.ToArray());
    }

    /// <summary>A loop break flushes the pending action, so the read it cut off is never counted (libsas_threads.cpp:139).</summary>
    private void ForgetValidationRead(MachineLink link) => _unacknowledgedValidationReads.Remove(link.Channel);

    /// <summary>Any message other than the same $4D again is the implied acknowledgement that retires the record.</summary>
    private void NoteValidationAcknowledgement(MachineLink link, ReadOnlySpan<byte> frame)
    {
        lock (_gate)
        {
            if (!_unacknowledgedValidationReads.TryGetValue(link.Channel, out byte[]? sent) || frame.SequenceEqual(sent))
                return;

            _unacknowledgedValidationReads.Remove(link.Channel);
        }

        RetireValidationRecord();
    }
}
