using System.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>
/// Long poll $72: transfers to and from the machine, the history they leave, and exception $69.
/// </summary>
/// <remarks>
/// <para>
/// The protocol half follows <c>lp72_TransferFunds</c> (lp.cpp:4864) and
/// <c>internalUpdateAftTransfer</c> (libsas_internal.cpp:3073) check for check, so a refusal carries
/// the code the library would choose, in the library's order. The library leaves a transfer pending
/// and waits for the application to move the money; here the application is the simplest one that
/// does it - it waits <see cref="FakeEgmOptions.AftCompletionDelay"/>, moves cashable credits, and
/// reports what it moved.
/// </para>
/// <para>
/// D45: in-house transfers to the machine and to the host, bonus awards, receipts, and debit
/// transfers as far as the library takes them. The platform performs no debit and no transfer to a
/// ticket, so a debit the library accepts fails at the platform, and a ticket is refused by the
/// library because <see cref="AvailableTransfers"/> offers none.
/// </para>
/// </remarks>
public sealed partial class FakeEgm
{
    private const int HistoryDepth = 128;
    private const long AllAvailable = 9_999_999_999;

    private const byte Pending = 0x40;
    private const byte NoReceipt = 0xFF;
    private const byte FlagHostCashoutControl = 0x01;
    private const byte FlagCashoutFromMachine = 0x08;
    private const byte FlagLockRequired = 0x40;
    private const byte FlagReceipt = 0x80;

    private readonly AftRecord[] _history = [.. Enumerable.Range(0, HistoryDepth).Select(_ => new AftRecord())];
    private AftCycle _aftCycle = AftCycle.None;
    private int _aftIndex = 1;
    private bool _aftOverflow;
    private AftZeroAmount _zeroAmount = AftZeroAmount.NonZero;
    private long _completionDue;
    private long _completionReissueDue;
    private bool _cancelRequested;

    // Where to print whatever a host cashout's transfer leaves on the machine, the platform's usual means.
    private string? _payAfterTransfer;

    // The part of the credit meter that is non-restricted promotional credit, in cents.
    private long _nonrestrictedCents;

    /// <summary>Where the history will put the next completed transfer, one to 127, as the library counts.</summary>
    public int AftHistoryIndex
    {
        get
        {
            lock (_gate)
                return _aftIndex;
        }
    }

    /// <summary>Answers long poll $72, or refuses a request the library would NACK.</summary>
    private void AnswerTransferFunds(MachineLink link, ReadOnlySpan<byte> frame)
    {
        byte[]? reply;
        List<Action> after = [];
        lock (_gate)
            reply = TransferFunds(frame, link.Address.Value, after);

        if (reply is null)
        {
            Reject(link);
            return;
        }

        for (int i = 0; i < reply.Length; i++)
            link.Outbound[i] = SasSymbol.Data8(reply[i]);
        Send(link, link.Outbound.AsSpan(0, reply.Length));

        foreach (Action action in after)
            action();
    }

    private byte[]? TransferFunds(ReadOnlySpan<byte> frame, byte address, List<Action> after)
    {
        if (frame.Length < 6)
            return null;

        byte code = frame[3];
        byte index = frame[4];
        AftRecord response;
        int position = index;
        bool interrogation = code is 0xFE or 0xFF;

        if (code is 0x00 or 0x01)
        {
            if (_aftCycle is not (AftCycle.Completed or AftCycle.None))
                return Compose(address, position, new AftRecord { TransferStatus = 0xC0 }, interrogation);

            if (ParseTransfer(frame) is not { } request)
                return null;

            response = Request(request, after);
            position = request.Index;
        }
        else if (code == 0x80)
        {
            if (index != 0 && frame[2] >= 2)
                return null;

            response = new AftRecord();
            if (_aftCycle is AftCycle.Pending or AftCycle.PendingPartial)
            {
                int slot = _zeroAmount == AftZeroAmount.Pending ? 0 : _aftIndex;
                _history[slot].TransferCode = 0x80;
                _cancelRequested = true;
                response = _history[slot].Copy();
                position = 0;
            }
        }
        else if (interrogation)
        {
            (position, response) = Interrogate(code, index);
        }
        else
        {
            response = new AftRecord { TransferStatus = 0xC1 };
        }

        return Compose(address, position, response, interrogation);
    }

    /// <summary>What a transfer request carries, read at the library's fixed offsets.</summary>
    private sealed record TransferRequest(
        byte Code, byte Index, byte Type, long Cashable, long Restricted, long Nonrestricted,
        bool AmountsAreBcd, byte Flags, uint Asset, byte[] Key, byte[] Id, byte[] Expiration, bool ExpirationIsBcd,
        ushort PoolId, AftReceiptData Receipt);

    /// <summary>Reads a transfer request, or null where the library sends a NACK (lp.cpp:5001 and its receipt checks).</summary>
    private static TransferRequest? ParseTransfer(ReadOnlySpan<byte> frame)
    {
        int end = frame.Length - 2;
        if (end < 47)
            return null;

        int idLength = frame[46];
        if (idLength is 0 or > 20)
            return null;

        int cursor = 47 + idLength;
        if (cursor + 7 > end)
            return null;

        byte[] expiration = frame.Slice(cursor, 4).ToArray();
        cursor += 4;
        ushort pool = (ushort)(frame[cursor] | (frame[cursor + 1] << 8));
        cursor += 2;
        int receiptLength = Math.Min((int)frame[cursor], 100);
        cursor++;
        if (cursor + receiptLength + 2 > end || !ReceiptDataIsWellFormed(frame.Slice(cursor, receiptLength)))
            return null;

        return new TransferRequest(
            frame[3], frame[4], frame[5],
            BcdValue(frame.Slice(6, 5)), BcdValue(frame.Slice(11, 5)), BcdValue(frame.Slice(16, 5)),
            IsBcd(frame.Slice(6, 15)), frame[21],
            (uint)(frame[22] | (frame[23] << 8) | (frame[24] << 16) | (frame[25] << 24)),
            frame.Slice(26, 20).ToArray(),
            frame.Slice(47, idLength).ToArray(), expiration, IsBcd(expiration), pool,
            AftReceiptData.Read(frame.Slice(cursor, receiptLength)));
    }

    /// <summary>Each receipt field the library knows, at a length it accepts; anything else is a NACK.</summary>
    private static bool ReceiptDataIsWellFormed(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i + 1 < data.Length;)
        {
            byte field = data[i];
            int length = data[i + 1];
            bool fits = field switch
            {
                0x00 or 0x10 or 0x11 => length <= 22,
                0x01 => length == 7,
                0x13 or 0x42 or 0x43 => length == 5,
                0x41 => length == 2,
                _ => false,
            };
            if (!fits)
                return false;
            i += length + 2;
        }

        return true;
    }

    private static bool IsBcd(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            if ((b >> 4) > 9 || (b & 0x0F) > 9)
                return false;
        }

        return true;
    }

    /// <summary>The library's checks for a transfer request, in its order, and what the request leaves in the history.</summary>
    private AftRecord Request(TransferRequest request, List<Action> after)
    {
        DateTime received = DateTime.Now;
        bool toTicket = request.Type is 0x20 or 0x60;
        bool debit = request.Type is 0x40 or 0x60;
        bool bonus = request.Type is 0x10 or 0x11;
        bool toHost = request.Type is 0x80 or 0x90;
        bool toMachine = request.Type is 0x00 or 0x10 or 0x40;
        bool inHouse = request.Type is 0x00 or 0x20 or 0x80 or 0x90;

        // A transfer from the machine is the answer to a host cashout request, which then stops (lp.cpp:5213).
        if (toHost && _hostCashoutDue != 0)
        {
            _payAfterTransfer = _hostCashoutWallet;
            EndHostCashout();
            Withdraw((byte)SasExceptionCode.AFTRequestForHostCashout);
        }

        byte responseFlags = (byte)(request.Flags & ~0x07);
        responseFlags |= (_hostCashoutStatus & HostControllable) != 0
            ? (byte)(_hostCashoutStatus & 0x07)
            : (byte)(_hostCashoutStatus & 0x04);
        responseFlags &= unchecked((byte)~0x10);

        byte status = Refusal(request, toTicket, debit, bonus, toHost, toMachine, inHouse);
        int slot = 0;
        if (status == Pending)
        {
            _aftCycle = AftCycle.Pending;
            bool zero = request.Cashable == 0 && request.Restricted == 0 && request.Nonrestricted == 0 && !bonus;
            _zeroAmount = zero ? AftZeroAmount.Pending : AftZeroAmount.NonZero;
            slot = zero ? 0 : _aftIndex;
        }
        else
        {
            _aftCycle = AftCycle.Completed;
        }

        long cashable = request.Cashable, restricted = request.Restricted, nonrestricted = request.Nonrestricted;
        long total = cashable + restricted + nonrestricted;
        if (total > _options.AftTransferLimitCents && toMachine && !bonus)
        {
            // Over the limit: restricted first, then non-restricted, then cashable, up to it (lp.cpp:5547).
            if (_aftCycle == AftCycle.Pending)
                _aftCycle = AftCycle.PendingPartial;
            long remaining = _options.AftTransferLimitCents;
            restricted = Math.Min(restricted, remaining);
            remaining -= restricted;
            nonrestricted = remaining > 0 ? Math.Min(nonrestricted, remaining) : 0;
            remaining -= nonrestricted;
            cashable = remaining;
        }

        bool keepsRestricted = request.Type is 0x00 or 0x20 or 0x80 or 0x90;
        bool keepsNonrestricted = request.Type is 0x00 or 0x10 or 0x11 or 0x80 or 0x90;
        var record = new AftRecord
        {
            TransferStatus = status,
            ReceiptStatus = (request.Flags & FlagReceipt) != 0 && status == Pending ? Pending : NoReceipt,
            TransferCode = request.Code,
            TransferType = request.Type,
            RequestedCashable = request.Cashable,
            Cashable = cashable,
            RequestedRestricted = keepsRestricted ? request.Restricted : 0,
            Restricted = keepsRestricted ? restricted : 0,
            RequestedNonrestricted = keepsNonrestricted ? request.Nonrestricted : 0,
            Nonrestricted = keepsNonrestricted ? nonrestricted : 0,
            RequestFlags = request.Flags,
            ResponseFlags = responseFlags,
            TransactionId = request.Id,
            Expiration = request.Expiration,
            PoolId = request.PoolId,
            Receipt = request.Receipt,
        };

        if (status != Pending && _payAfterTransfer is { } wallet)
        {
            _payAfterTransfer = null;
            after.Add(() => PayOut(wallet));
        }

        if (status != Pending)
        {
            // Refused outright, so the cycle is over: stamped with when it arrived, meters filled (lp.cpp:5623).
            FillCumulativeMeters(record);
            record.CompletedAt = received;
        }

        _history[slot] = record;
        _history[0] = record.Copy();

        if (status == Pending)
        {
            _cancelRequested = false;
            _completionDue = Stopwatch.GetTimestamp() + Ticks(_options.AftCompletionDelay);

            // The library holds the reply to a zero cash-out from the machine until it completes; here it completes first.
            if (_zeroAmount == AftZeroAmount.Pending && (request.Flags & FlagCashoutFromMachine) != 0)
            {
                after.AddRange(CompleteTransfer());
                return _history[0].Copy();
            }
        }

        // The pending reply is built from the request itself, which has no completion time and no meters.
        AftRecord reply = record.Copy();
        if (status == Pending)
            reply.CompletedAt = null;
        return reply;
    }

    /// <summary>Returns the first refusal lp72_TransferFunds reaches, or pending when none applies.</summary>
    private byte Refusal(
        TransferRequest request, bool toTicket, bool debit, bool bonus, bool toHost, bool toMachine, bool inHouse)
    {
        byte available = AvailableTransfers();
        byte aftStatus = AftStatus();
        long cashableCents = CurrentCashableCents();
        long nonrestrictedCents = CurrentNonrestrictedCents();
        long total = request.Cashable + request.Restricted + request.Nonrestricted;
        bool anyAft = (aftStatus & 0x80) != 0;
        bool locked = _lockStatus == Locked;
        bool lockRequired = (request.Flags & FlagLockRequired) != 0;
        bool receipt = (request.Flags & FlagReceipt) != 0;

        if (!_options.Aft || request.Index != 0)
            return 0x82;
        if (!request.ExpirationIsBcd)
            return 0x83;
        if (!ExpirationIsValid(request.Expiration))
            return 0x83;
        if ((bonus && request.Restricted != 0)
            || (request.Type == 0x20 && request.Nonrestricted != 0)
            || (request.Type == 0x20 && request.Cashable != 0 && request.Restricted != 0)
            || (debit && (request.Restricted != 0 || request.Nonrestricted != 0)))
        {
            return 0x82;
        }

        // The platform's busy answer while it cannot be played (QSP RefreshBusyResponse, the library's BR_LP72).
        if (!IsPlayable() && _hostCashoutDue == 0)
            return 0x87;

        // Registration governs debit transfers alone (internalAftRegistrationStatusForKey, libsas_internal.cpp:2971).
        if (RegistrationRefusal(debit, request.Key) is var registration and not 0)
            return registration;
        if (debit && (aftStatus & 0x40) == 0 && !anyAft)
            return 0x82;
        if (inHouse && (aftStatus & 0x10) == 0 && !anyAft)
            return 0x82;
        if (bonus && (aftStatus & 0x20) == 0 && !anyAft)
            return 0x82;
        if (bonus && (available & 0x10) == 0)
            return 0x87;
        if (toMachine && (available & 0x01) == 0)
            return 0x87;
        if (toHost && (available & 0x02) == 0)
            return 0x87;
        if (toTicket && (available & 0x04) == 0)
            return 0x82;
        if (toTicket && total == 0)
            return 0x82;
        if (receipt && (aftStatus & ReceiptsAftStatus) == 0)
            return 0x90;
        if (lockRequired && !locked)
            return 0x94;
        if (total > _options.AftTransferLimitCents && request.Code == 0x00 && toMachine && !bonus)
            return 0x84;
        if (request.Asset != AssetNumber)
            return 0x93;

        // An in-house receipt needs a patron account and a debit one a card number (lp.cpp:5406).
        if (receipt && ((!request.Receipt.HasPatronAccount && inHouse) || (request.Receipt.DebitCardNumber == 0 && debit)))
            return 0x91;
        if (bonus && receipt)
            return 0x92;
        if (_aftCycle != AftCycle.None && IdMatches(_history[PreviousIndex()], request.Id))
            return 0x81;
        if (toHost && (aftStatus & 0x02) == 0
            && (request.Cashable < cashableCents || request.Nonrestricted < nonrestrictedCents))
        {
            return 0x83;
        }

        if (toHost && request.Code == 0x00
            && (request.Cashable > cashableCents || request.Restricted > 0 || request.Nonrestricted > nonrestrictedCents))
        {
            return 0x83;
        }

        if (!request.AmountsAreBcd)
            return 0x83;
        if (NotAMultiple(request.Cashable) || NotAMultiple(request.Restricted) || NotAMultiple(request.Nonrestricted))
            return 0x85;

        return Pending;
    }

    private bool NotAMultiple(long cents) => cents != AllAvailable && cents % AccountingCents != 0;

    private int PreviousIndex() => _aftIndex > 1 ? _aftIndex - 1 : HistoryDepth - 1;

    private static bool IdMatches(AftRecord stored, byte[] id) => stored.TransactionId.AsSpan().SequenceEqual(id);

    /// <summary>A date with a day or month set has to be a real one; zeros, or a count of days, pass (lp.cpp:5263).</summary>
    private static bool ExpirationIsValid(byte[] expiration)
    {
        long month = BcdValue(expiration.AsSpan(0, 1));
        long day = BcdValue(expiration.AsSpan(1, 1));
        long year = BcdValue(expiration.AsSpan(2, 2));
        if (day == 0 && month == 0)
            return true;
        return day is >= 1 and <= 31 && month is >= 1 and <= 12 && year >= 1900;
    }

    /// <summary>The platform finishing a pending transfer: it moves cashable credits and reports what it moved.</summary>
    private List<Action> TickTransfer(long now)
    {
        List<Action> after = [];
        if (_completionDue != 0 && now >= _completionDue)
            after.AddRange(CompleteTransfer());

        after.AddRange(TickReceipt(now));

        if (_completionReissueDue != 0 && now >= _completionReissueDue)
        {
            // Reissued every fifteen seconds until the host reads the completion (internalReissueAftTransferCompleteIfOutstanding).
            if (_aftCycle is AftCycle.Completed or AftCycle.None)
            {
                _completionReissueDue = 0;
            }
            else
            {
                _completionReissueDue = now + Ticks(_options.AftCompletionReissue);
                QueueRanked((byte)SasExceptionCode.AFTTransferComplete);
            }
        }

        return after;
    }

    private List<Action> CompleteTransfer()
    {
        _completionDue = 0;

        // Whatever the answer to a host cashout leaves on the machine is paid the usual way, as QSP's fallback does (AftCashoutFallback.h).
        string? wallet = _payAfterTransfer;
        _payAfterTransfer = null;
        List<Action> after = FinishTransfer();
        if (wallet is not null)
            after.Add(() => PayOut(wallet));
        return after;
    }

    private List<Action> FinishTransfer()
    {
        if (_aftCycle is not (AftCycle.Pending or AftCycle.PendingPartial))
            return [];

        int slot = _zeroAmount != AftZeroAmount.NonZero ? 0 : _aftIndex;
        AftRecord record = _history[slot];
        if (_cancelRequested)
            return UpdateTransfer(0x80, 0, 0, 0);

        // Restricted credits are not modelled, so the platform fails such a transfer as unexpected.
        if (record.Restricted != 0)
            return UpdateTransfer(0x9F, 0, 0, 0);

        switch (record.TransferType)
        {
            case 0x80 or 0x90:
            {
                long credits = Meters.Read(SasMeterCode.CurrentCredits);
                long cash = Math.Min(record.Cashable, CurrentCashableCents());
                long nonrestricted = Math.Min(record.Nonrestricted, CurrentNonrestrictedCents());
                _nonrestrictedCents = CurrentNonrestrictedCents() - nonrestricted;
                Meters.SetCurrentCredits(credits - ((cash + nonrestricted) / AccountingCents));

                // A partial request that moved less than it named, all-available included, is a partial success (libsas test/blackbox/blackbox_test.cpp:1077).
                bool less = cash < record.Cashable || nonrestricted < record.Nonrestricted;
                return UpdateTransfer(record.TransferCode == 0x01 && less ? (byte)0x01 : (byte)0x00, cash, 0, nonrestricted);
            }

            case 0x00:
                AddCredits(record.Cashable, record.Nonrestricted);
                return UpdateTransfer(0x00, record.Cashable, 0, record.Nonrestricted);

            case 0x10:
            {
                // A coin-out bonus is a win the machine pays onto the credit meter (QSP BonusIn).
                AddCredits(record.Cashable, record.Nonrestricted);
                long won = (record.Cashable + record.Nonrestricted) / AccountingCents;
                Meters.Increment(SasMeterCode.TotalCoinOutCredits, won);
                Meters.Increment(SasMeterCode.TotalMachinePaidExternalBonusWin_Crd, won);
                return UpdateTransfer(0x00, record.Cashable, 0, record.Nonrestricted);
            }

            case 0x11:
            {
                // A jackpot bonus is always a hand pay, even of nothing, and completes as the machine locks up (QSP ShouldPerformBonusAward).
                long owed = record.Cashable + record.Nonrestricted;
                List<Action> after = [() => RaiseBonusHandpay(owed)];
                after.AddRange(UpdateTransfer(0x00, record.Cashable, 0, record.Nonrestricted));
                return after;
            }

            default:
                // The platform performs no debit transfer and fails one the library accepted as not valid (QSP AftTransferTypePerformed).
                return UpdateTransfer(0x82, 0, 0, 0);
        }
    }

    /// <summary>Puts cashable and non-restricted cents on the credit meter.</summary>
    private void AddCredits(long cashableCents, long nonrestrictedCents)
    {
        long credits = Meters.Read(SasMeterCode.CurrentCredits);
        _nonrestrictedCents = CurrentNonrestrictedCents() + nonrestrictedCents;
        Meters.SetCurrentCredits(credits + ((cashableCents + nonrestrictedCents) / AccountingCents));
    }

    /// <summary>The cashable part of the credit meter, in cents.</summary>
    private long CurrentCashableCents() =>
        (Meters.Read(SasMeterCode.CurrentCredits) * AccountingCents) - CurrentNonrestrictedCents();

    /// <summary>The non-restricted part of the credit meter, in cents, never more than the meter holds.</summary>
    private long CurrentNonrestrictedCents()
    {
        lock (_gate)
            return Math.Min(_nonrestrictedCents, Meters.Read(SasMeterCode.CurrentCredits) * AccountingCents);
    }

    /// <summary>Keeps the non-restricted part within a credit meter that has just gone down.</summary>
    private void ClampNonrestricted()
    {
        lock (_gate)
            _nonrestrictedCents = CurrentNonrestrictedCents();
    }

    /// <summary>Takes a wager from the non-restricted part first, and returns how many credits of it that was.</summary>
    /// <remarks>Called before the credit meter goes down by the wager.</remarks>
    private long PlayNonrestricted(long credits)
    {
        lock (_gate)
        {
            long held = CurrentNonrestrictedCents();
            long played = Math.Min(credits, held / AccountingCents);
            _nonrestrictedCents = held - (played * AccountingCents);
            return played;
        }
    }

    /// <summary>The application's report of a finished transfer (internalUpdateAftTransfer, libsas_internal.cpp:3073).</summary>
    private List<Action> UpdateTransfer(byte status, long cashable, long restricted, long nonrestricted)
    {
        int slot = _zeroAmount != AftZeroAmount.NonZero ? 0 : _aftIndex;
        AftRecord record = _history[slot];
        if (_aftCycle == AftCycle.PendingPartial && status == 0x00)
            status = 0x01;

        record.TransferStatus = status;
        record.CompletedAt = DateTime.Now;
        _aftCycle = AftCycle.Updated;
        if (_lockStatus != NotLocked)
            UnlockGame();
        List<Action> after = [];

        if (status is 0x00 or 0x01)
        {
            if ((record.RequestFlags & FlagHostCashoutControl) != 0 && (_hostCashoutStatus & HostControllable) != 0)
            {
                const byte cashoutState = 0x06;
                _hostCashoutStatus = (byte)((_hostCashoutStatus & ~cashoutState) | (record.RequestFlags & cashoutState));
                record.ResponseFlags = (byte)((record.ResponseFlags & ~cashoutState) | (_hostCashoutStatus & cashoutState));
            }

            record.Cashable = Math.Min(record.Cashable, cashable);
            record.Restricted = Math.Min(record.Restricted, restricted);
            record.Nonrestricted = Math.Min(record.Nonrestricted, nonrestricted);
            MoveTransferMeters(record);
        }

        // The platform's half of the receipt: printing for a non-zero success that may carry one, otherwise none (QSP ResolveAftTransactionReceipt).
        record.ReceiptStatus = ResolveReceipt(record, status);

        FillCumulativeMeters(record);
        _history[0] = record.Copy();
        _completionReissueDue = Stopwatch.GetTimestamp() + Ticks(_options.AftCompletionReissue);
        QueueRanked((byte)SasExceptionCode.AFTTransferComplete);
        return after;
    }

    /// <summary>The cents and count meters of each kind of transfer, which the library moves on success.</summary>
    private void MoveTransferMeters(AftRecord record)
    {
        (SasMeterCode Cents, SasMeterCode Count)? cash = null, restricted = null, nonrestricted = null;
        switch (record.TransferType)
        {
            case 0x00:
                cash = (SasMeterCode.InHouseCashableTransfersToGamingMachine_Cents,
                    SasMeterCode.InHouseTransfersToGamingMachineThatIncludedCashableAmounts_Qty);
                restricted = (SasMeterCode.InHouseRestrictedTransfersToGamingMachine_Cents,
                    SasMeterCode.InHouseTransfersToGamingMachineThatIncludedRestrictedAmounts_Qty);
                nonrestricted = (SasMeterCode.InHouseNonrestrictedTransfersToGamingMachine_Cents,
                    SasMeterCode.InHouseTransfersToGamingMachineThatIncludedNonrestrictedAmounts_Qty);
                break;
            case 0x10 or 0x11:
                cash = (SasMeterCode.BonusCashableTransfersToGamingMachine_Cents,
                    SasMeterCode.BonusTransfersToGamingMachineThatIncludedCashableAmounts_Qty);
                nonrestricted = (SasMeterCode.BonusNonrestrictedTransfersToGamingMachine_Cents,
                    SasMeterCode.BonusTransfersToGamingMachineThatIncludedNonrestrictedAmounts_Qty);
                break;
            case 0x80 or 0x90:
                cash = (SasMeterCode.InHouseCashableTransfersToHost_Cents,
                    SasMeterCode.InHouseTransfersToHostThatIncludedCashableAmounts_Qty);
                restricted = (SasMeterCode.InHouseRestrictedTransfersToHost_Cents,
                    SasMeterCode.InHouseTransfersToHostThatIncludedRestrictedAmounts_Qty);
                nonrestricted = (SasMeterCode.InHouseNonRestrictedTransfersToHost_Cents,
                    SasMeterCode.InHouseTransfersToHostThatIncludedNonrestrictedAmounts_Qty);
                break;
        }

        Move(cash, record.Cashable);
        Move(restricted, record.Restricted);
        Move(nonrestricted, record.Nonrestricted);

        // The credit meters for the same money are the platform's to keep, so this machine keeps them as Table C-7 defines them.
        long total = (record.Cashable + record.Restricted + record.Nonrestricted) / AccountingCents;
        if (record.TransferType == 0x00)
        {
            Meters.Increment(SasMeterCode.TotalElectronicTransfersToMachine_Crd, total);
            Meters.Increment(SasMeterCode.ElectronicRegularCashableTransfersToGamingMachine_Crd, record.Cashable / AccountingCents);
            if (record.Nonrestricted != 0)
                Meters.Increment(SasMeterCode.ElectronicNonrestrictedPromotionalTransfersToGamingMachine_Crd, record.Nonrestricted / AccountingCents);
        }
        else if (record.TransferType is 0x80 or 0x90)
        {
            Meters.Increment(SasMeterCode.TotalElectronicTransfersToHost_Crd, total);
            Meters.Increment(SasMeterCode.ElectronicRegularCashableTransfersToHost_Crd, record.Cashable / AccountingCents);
            if (record.Nonrestricted != 0)
                Meters.Increment(SasMeterCode.ElectronicNonrestrictedPromotionalTransfersToHost, record.Nonrestricted / AccountingCents);
        }

        void Move((SasMeterCode Cents, SasMeterCode Count)? meters, long cents)
        {
            if (meters is not { } pair || cents == 0)
                return;
            Meters.Increment(pair.Cents, cents);
            Meters.Increment(pair.Count, 1);
        }
    }

    /// <summary>Reads every cumulative meter the transfer's type has into the record (internalFillCompletedAftCumulativeMeters).</summary>
    private void FillCumulativeMeters(AftRecord record)
    {
        (SasMeterCode? cash, SasMeterCode? restricted, SasMeterCode? nonrestricted) = record.TransferType switch
        {
            0x00 => ((SasMeterCode?)SasMeterCode.InHouseCashableTransfersToGamingMachine_Cents,
                (SasMeterCode?)SasMeterCode.InHouseRestrictedTransfersToGamingMachine_Cents,
                (SasMeterCode?)SasMeterCode.InHouseNonrestrictedTransfersToGamingMachine_Cents),
            0x10 or 0x11 => (SasMeterCode.BonusCashableTransfersToGamingMachine_Cents, null,
                SasMeterCode.BonusNonrestrictedTransfersToGamingMachine_Cents),
            0x20 => (SasMeterCode.InHouseCashableTransfersToTicket_Cents,
                SasMeterCode.InHouseRestrictedTransfersToTicket_Cents, null),
            0x40 => (SasMeterCode.DebitTransfersToGamingMachine_Cents, null, null),
            0x60 => (SasMeterCode.DebitTransfersToTicket_Cents, null, null),
            0x80 or 0x90 => (SasMeterCode.InHouseCashableTransfersToHost_Cents,
                SasMeterCode.InHouseRestrictedTransfersToHost_Cents,
                SasMeterCode.InHouseNonRestrictedTransfersToHost_Cents),
            _ => (null, null, null),
        };

        record.CashableMeter = Cumulative(cash);
        record.RestrictedMeter = Cumulative(restricted);
        record.NonrestrictedMeter = Cumulative(nonrestricted);

        AftMeter Cumulative(SasMeterCode? meter)
        {
            if (meter is not { } code)
                return default;
            long value = Meters.Read(code);
            return new AftMeter(value, WidthOf((ushort)code, value));
        }
    }
}
