using System.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm;

/// <summary>The hand pay the platform is locked up for, until the attendant resets it.</summary>
/// <param name="Kind">What the attendant is paying.</param>
/// <param name="AmountCents">What is owed.</param>
/// <param name="Level">The level byte long poll 1B reports: $80 for cancelled credit, $00 for a game win.</param>
/// <param name="Bonus">True for a bonus jackpot the host sent over funds transfer, which the attendant pays as an external bonus.</param>
internal sealed record HandpayOwed(HandpayKind Kind, long AmountCents, byte Level, bool Bonus = false)
{
    /// <summary>The progressive group long poll 1B reports: the game's for a SAS level, zero otherwise.</summary>
    public byte Group { get; init; }

    /// <summary>The part of the amount a progressive won, which the attendant-paid progressive meter takes.</summary>
    public long ProgressiveCents { get; init; }

    /// <summary>The extra part of a multiplied win, which the attendant-paid external bonus meter takes.</summary>
    public long MultipliedCents { get; init; }

    /// <summary>A progressive level is reported in cents, every other level in accounting units (libsas_internal.cpp:4899).</summary>
    public bool InCents => Level is >= 0x01 and <= 0x20;
}

/// <summary>
/// Hand pays: the platform's lockup and reset, and the library's queue on each channel, with $51 reissued every
/// fifteen seconds until its reading is acknowledged and $52 sent as the reporting mode has it.
/// </summary>
public sealed partial class FakeEgm
{
    /// <summary>Long poll 1B's level byte for a cancelled-credit hand pay.</summary>
    private const byte CancelledCreditLevel = 0x80;

    private const byte PendingCode = (byte)SasExceptionCode.HandpayIsPending;
    private const byte WasResetCode = (byte)SasExceptionCode.HandpayWasReset;

    /// <summary>Thirty ticks of the library's half-second hand pay timer (libsas_threads.cpp:3055).</summary>
    private static readonly TimeSpan HandpayReissue = TimeSpan.FromSeconds(15);

    private HandpayOwed? _handpay;

    // The library's handpayState: registered from sasHandpayStart until a reset it accepts (libsas_internal.cpp:4840).
    private bool _handpayRegistered;

    // The library's hand pay timer, alive while any channel holds a hand pay, and when its count last started.
    private bool _handpayTimerAlive;
    private long _handpayTimerEpoch;

    /// <summary>True while the machine is locked up waiting for an attendant.</summary>
    public bool HandpayPending
    {
        get
        {
            lock (_gate)
                return _handpay is not null;
        }
    }

    /// <summary>
    /// Locks the machine up owing a hand pay, and registers it with the library's queue on every channel.
    /// </summary>
    /// <remarks>
    /// A cancelled-credit hand pay takes the note first and then every credit on the machine, the
    /// way a real one is driven into it, so the note's own exceptions and meters move as they would.
    /// A jackpot is the game's win, which this machine takes from outside. One that the host has not yet
    /// read stays queued after its reset, so another can follow it (SAS 6.02 7.8.1).
    /// </remarks>
    /// <exception cref="InvalidOperationException">The machine is still locked up for a hand pay not yet reset.</exception>
    public void RaiseHandpay(HandpayKind kind, long amountCents)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountCents);

        lock (_gate)
        {
            if (_handpay is not null)
                throw new InvalidOperationException(
                    "the machine is locked up for a hand pay that has not been reset, and the platform takes one at a time");
        }

        long owed = amountCents;
        if (kind == HandpayKind.CancelledCredit)
        {
            InsertBill(checked((int)amountCents));
            long restricted = CurrentRestrictedCents() / AccountingCents;
            long credits = Meters.Read(SasMeterCode.CurrentCredits) - restricted;
            owed = credits * AccountingCents;

            // Restricted credit is not cancelled to an attendant, as it may not be cashed out at all.
            Meters.SetCurrentCredits(restricted);
            ClampNonrestricted();
        }

        byte level = kind == HandpayKind.CancelledCredit ? CancelledCreditLevel : (byte)0x00;
        lock (_gate)
            StartHandpayLocked(new HandpayOwed(kind, owed, level));

        WithdrawLockIfUnplayable();
    }

    /// <summary>Locks the machine up for a bonus jackpot, which may be of nothing (QSP ShouldPerformBonusAward).</summary>
    private void RaiseBonusHandpay(long amountCents)
    {
        lock (_gate)
        {
            // A second bonus is added to the external bonus hand pay still owed (QSP MoneyInHandler BonusIn).
            if (_handpay is { Bonus: true } owed)
                AmendHandpayLocked(owed with { AmountCents = owed.AmountCents + amountCents });
            else
                StartHandpayLocked(new HandpayOwed(HandpayKind.Jackpot, amountCents, 0x00, Bonus: true));
        }

        WithdrawLockIfUnplayable();
    }

    /// <summary>
    /// The platform locks up and calls sasHandpayStart, which the library refuses while its last hand pay is
    /// not reset (QSP SasNetwork_Conditions.hpp:794, libsas_internal.cpp:4834). Called under the gate.
    /// </summary>
    private void StartHandpayLocked(HandpayOwed owed)
    {
        _handpay = owed;
        if (_handpayRegistered)
            return;

        _handpayRegistered = true;
        HandpayItem item = HandpayItemFor(owed);
        foreach (MachineLink link in _links)
        {
            link.Handpays.Register(item);
            WriteHandpayExceptionLocked(link, PendingCode);
        }
    }

    /// <summary>A win the platform adds to the lockup it already holds: its record, and the queue's newest entry, change in place.</summary>
    private void AmendHandpayLocked(HandpayOwed owed)
    {
        _handpay = owed;
        HandpayItem item = HandpayItemFor(owed);
        foreach (MachineLink link in _links)
            link.Handpays.AmendNewest(item.Group, item.Level, item.Amount);
    }

    /// <summary>The entry the library stores, its amount in accounting units unless a progressive contributed (libsas_internal.cpp:4895).</summary>
    private HandpayItem HandpayItemFor(HandpayOwed owed) =>
        new(owed.Group, owed.Level, owed.InCents ? owed.AmountCents : owed.AmountCents / AccountingCents, HandpayItemState.Registered);

    /// <summary>The attendant pays the hand pay and turns the reset key.</summary>
    /// <remarks>$52 is the library's to send: at once in legacy reporting, and in the queue once the reading is acknowledged (SAS 6.02 7.8).</remarks>
    /// <returns>False when there was nothing to reset, which leaves the machine as it was.</returns>
    public bool ResetHandpay()
    {
        HandpayOwed? paid;
        lock (_gate)
        {
            paid = _handpay;
            _handpay = null;
        }

        if (paid is null)
            return false;

        MeterHandpay(paid);
        ValidateHandpay(paid);

        lock (_gate)
            ResetWithLibraryLocked();
        return true;
    }

    /// <summary>What the attendant paid, on the meters QSP's AsyncHandpayComplete moves.</summary>
    private void MeterHandpay(HandpayOwed paid)
    {
        long credits = paid.AmountCents / AccountingCents;
        // A cancelled-credit hand pay is also a cancelled credit; a jackpot is also an attendant-paid paytable win.
        if (paid.Kind == HandpayKind.CancelledCredit)
        {
            Meters.Increment(SasMeterCode.TotalHandPaidCancelledCredits, credits);
            Meters.Increment(SasMeterCode.TotalCancelledCredits, credits);
        }
        else if (paid.Bonus)
        {
            // An attendant-paid external bonus, as QSP's AsyncHandpayComplete meters one.
            Meters.Increment(SasMeterCode.TotalJackpotCredits, credits);
            Meters.Increment(SasMeterCode.TotalAttendantPaidExternalBonusWin_Crd, credits);
        }
        else
        {
            // A progressive's part is an attendant-paid progressive win, a multiplier's an external bonus, the rest a paytable win (QSP AsyncHandpayComplete).
            long progressive = paid.ProgressiveCents / AccountingCents;
            long multiplied = paid.MultipliedCents / AccountingCents;
            Meters.Increment(SasMeterCode.TotalJackpotCredits, credits);
            if (progressive > 0)
                Meters.Increment(SasMeterCode.TotalAttendantPaidProgressiveWin_Crd, progressive);
            if (multiplied > 0)
                Meters.Increment(SasMeterCode.TotalAttendantPaidExternalBonusWin_Crd, multiplied);
            if (credits - progressive - multiplied > 0)
                Meters.Increment(SasMeterCode.TotalAttendantPaidPaytableWin_Crd, credits - progressive - multiplied);
        }
    }

    /// <summary>
    /// internalHandpayReset: refused unless a hand pay is registered, then counted on each channel in turn, the
    /// first channel with nothing to match ending it (libsas_internal.cpp:5222). Called under the gate.
    /// </summary>
    private void ResetWithLibraryLocked()
    {
        if (!_handpayRegistered)
            return;

        foreach (MachineLink link in _links)
        {
            if (!link.Handpays.Reset(_options.LegacyHandpayReporting, out HandpayWrite write))
            {
                if (link.Handpays.Count == 0 && link.Handpays.ResetQueued == 0)
                    _handpayRegistered = false;
                return;
            }

            ApplyHandpayWriteLocked(link, write);
        }

        _handpayRegistered = false;
    }

    /// <summary>Carries out what a step of the queue asked for on one channel. Called under the gate.</summary>
    private void ApplyHandpayWriteLocked(MachineLink link, HandpayWrite write)
    {
        if ((write & HandpayWrite.WithdrawPending) != 0)
            link.Exceptions.Remove(PendingCode);
        if ((write & HandpayWrite.Pending) != 0)
            WriteHandpayExceptionLocked(link, PendingCode);
        if ((write & HandpayWrite.WasReset) != 0)
            WriteHandpayExceptionLocked(link, WasResetCode);
    }

    /// <summary>
    /// internalWriteException for $51 or $52: ranked, never twice, and a $52 dropped while the queue's front still
    /// waits (libsas_internal.cpp:3975). A multiplied award that waited for the reset follows its $52.
    /// </summary>
    private void WriteHandpayExceptionLocked(MachineLink link, byte code)
    {
        if (!Channels.Hears(link.Channel, code) || !link.Handpays.Accepts(code))
            return;

        link.Exceptions.AddRanked(new PendingEvent(code, []));
        if (code == WasResetCode)
            AwardMultipliedAfterHandpayLocked();
    }

    /// <summary>
    /// internalUpdateHandpayTransaction: an acknowledged $51 marks the hand pay announced and starts the timer, and
    /// an acknowledged $52 dequeues the hand pay it reported. Called under the gate.
    /// </summary>
    private void NoteHandpayExceptionAcknowledgedLocked(MachineLink link, byte code)
    {
        bool synced = !link.AwaitingSync;
        if (code == PendingCode)
        {
            if (link.Handpays.PendingAcknowledged(synced))
                ArmHandpayTimerLocked();
        }
        else if (code == WasResetCode)
        {
            ApplyHandpayWriteLocked(link, link.Handpays.ResetAcknowledged(synced));
        }
    }

    /// <summary>internalSetTimer on the hand pay timer: a timer already running keeps its count.</summary>
    private void ArmHandpayTimerLocked()
    {
        if (_handpayTimerAlive)
            return;

        _handpayTimerAlive = true;
        _handpayTimerEpoch = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// The library's hand pay timer: every thirtieth tick a channel whose hand pay still awaits its reading gets
    /// $51 again, except in legacy reporting, and the timer stops once no channel holds one (libsas_threads.cpp:3014).
    /// </summary>
    private void TickHandpay()
    {
        lock (_gate)
        {
            if (!_handpayTimerAlive)
                return;

            if (!_links.Exists(link => link.Handpays.Count > 0))
            {
                _handpayTimerAlive = false;
                return;
            }

            long now = Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(_handpayTimerEpoch, now) < HandpayReissue)
                return;

            List<MachineLink> awaiting = _links.FindAll(link => link.Handpays.AwaitingRead);
            if (awaiting.Count == 0)
            {
                // The count runs on to the next thirtieth tick.
                _handpayTimerEpoch += Ticks(HandpayReissue);
                return;
            }

            _handpayTimerEpoch = now;
            if (_options.LegacyHandpayReporting)
                return;

            foreach (MachineLink link in awaiting)
                WriteHandpayExceptionLocked(link, PendingCode);
        }
    }

    /// <summary>What long poll 1B answers on this link, and what the reading does to the queue (lp.cpp:965).</summary>
    /// <remarks>The amount is in accounting units, or in cents when a progressive contributed (Table 7.8).</remarks>
    private Dictionary<string, object> ReadHandpay(MachineLink link, ReadOnlySpan<byte> request)
    {
        HandpayItem? item;
        lock (_gate)
        {
            item = link.Handpays.Read(_options.LegacyHandpayReporting, !link.AwaitingSync, out bool dequeue);

            // A retransmission is answered again without its acknowledgement being owed twice (lp.cpp:1101).
            if (dequeue && link.UnacknowledgedHandpayPoll is null)
            {
                link.Exceptions.Remove(PendingCode);
                link.UnacknowledgedHandpayPoll = request.ToArray();
            }
        }

        return new Dictionary<string, object>
        {
            ["progressive_group"] = item?.Group ?? (byte)0,
            ["level"] = item?.Level ?? (byte)0,
            ["amount"] = item?.Amount ?? 0L,
            ["partial_pay"] = item?.PartialPay ?? 0L,
            ["reset_id"] = item?.ResetId ?? (byte)0,
            ["session_game_win"] = 0L,
            ["session_game_pay"] = 0L,
        };
    }

    /// <summary>A loop break is an implied NACK: the reading's pending dequeue is dropped, so the reading stays owed.</summary>
    private static void ForgetHandpayReading(MachineLink link) => link.UnacknowledgedHandpayPoll = null;

    /// <summary>
    /// Any message other than the same 1B again acknowledges the reading, as section 3 makes any next message an
    /// implied acknowledgement, and runs the dequeue the reading left pending (libsas_pending.cpp:388).
    /// </summary>
    private void NoteHandpayAcknowledgement(MachineLink link, ReadOnlySpan<byte> frame)
    {
        lock (_gate)
        {
            if (link.UnacknowledgedHandpayPoll is not { } sent || frame.SequenceEqual(sent))
                return;

            link.UnacknowledgedHandpayPoll = null;
            ApplyHandpayWriteLocked(link, link.Handpays.Dequeue(!link.AwaitingSync));
        }
    }

    /// <summary>At power up the library announces again a hand pay still awaiting its reading, and its timer starts afresh.</summary>
    private void RestartHandpaysLocked()
    {
        _handpayTimerAlive = false;
        foreach (MachineLink link in _links)
            ApplyHandpayWriteLocked(link, link.Handpays.Restart());
    }

    /// <summary>A memory clear empties every queue and the platform's lockup.</summary>
    private void ClearHandpaysLocked()
    {
        _handpay = null;
        _handpayRegistered = false;
        _handpayTimerAlive = false;
        foreach (MachineLink link in _links)
            link.Handpays.Clear();
    }
}
