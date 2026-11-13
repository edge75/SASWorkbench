using System.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm;

/// <summary>The hand pay the software machine is holding, and how far the host has got with it.</summary>
/// <param name="Kind">What the attendant is paying.</param>
/// <param name="AmountCents">What is owed.</param>
/// <param name="Level">The level byte long poll 1B reports: $80 for cancelled credit, $00 for a game win.</param>
/// <param name="ReadAcknowledged">True once a hand pay reading has been acknowledged, after which 1B reads zero.</param>
/// <param name="Reset">True once the attendant has reset it, while $52 waits for the reading to be acknowledged.</param>
internal sealed record HandpayOwed(HandpayKind Kind, long AmountCents, byte Level, bool ReadAcknowledged, bool Reset = false);

public sealed partial class FakeEgm
{
    /// <summary>Long poll 1B's level byte for a cancelled-credit hand pay.</summary>
    private const byte CancelledCreditLevel = 0x80;

    /// <summary>How often hand pay pending is reissued while its reading is unacknowledged.</summary>
    private static readonly TimeSpan HandpayReissue = TimeSpan.FromSeconds(15);

    private HandpayOwed? _handpay;
    private long _handpayIssuedAt;

    /// <summary>True while the machine is locked up waiting for an attendant.</summary>
    public bool HandpayPending
    {
        get
        {
            lock (_gate)
                return _handpay is { Reset: false };
        }
    }

    /// <summary>
    /// Locks the machine up owing a hand pay, and reports hand pay pending ahead of anything queued.
    /// </summary>
    /// <remarks>
    /// A cancelled-credit hand pay takes the note first and then every credit on the machine, the
    /// way a real one is driven into it, so the note's own exceptions and meters move as they would.
    /// A jackpot is the game's win, which this machine takes from outside.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A hand pay is already pending.</exception>
    public void RaiseHandpay(HandpayKind kind, long amountCents)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountCents);

        lock (_gate)
        {
            if (_handpay is not null)
                throw new InvalidOperationException(
                    "the machine already holds a hand pay, and this one keeps only one at a time");
        }

        long owed = amountCents;
        if (kind == HandpayKind.CancelledCredit)
        {
            InsertBill(checked((int)amountCents));
            long credits = Meters.Read(SasMeterCode.CurrentCredits);
            owed = credits * AccountingCents;
            Meters.SetCurrentCredits(0);
        }

        byte level = kind == HandpayKind.CancelledCredit ? CancelledCreditLevel : (byte)0x00;
        lock (_gate)
        {
            _handpay = new HandpayOwed(kind, owed, level, ReadAcknowledged: false);
            _handpayIssuedAt = Stopwatch.GetTimestamp();
        }

        Queue((byte)SasExceptionCode.HandpayIsPending, [], priority: true);
    }

    /// <summary>The attendant pays the hand pay and turns the reset key.</summary>
    /// <remarks>$52 waits until the hand pay reading is acknowledged, as the hand pay queue has it (SAS 6.02 7.8.1).</remarks>
    /// <returns>False when there was nothing to reset, which leaves the machine as it was.</returns>
    public bool ResetHandpay()
    {
        HandpayOwed? paid;
        bool reported;
        lock (_gate)
        {
            paid = _handpay is { Reset: false } ? _handpay : null;
            reported = paid is { ReadAcknowledged: true };
            if (paid is not null)
                _handpay = reported ? null : paid with { Reset = true };
        }

        if (paid is null)
            return false;

        long credits = paid.AmountCents / AccountingCents;
        // A cancelled-credit hand pay is also a cancelled credit; a jackpot is also an attendant-paid paytable win.
        if (paid.Kind == HandpayKind.CancelledCredit)
        {
            Meters.Increment(SasMeterCode.TotalHandPaidCancelledCredits, credits);
            Meters.Increment(SasMeterCode.TotalCancelledCredits, credits);
        }
        else
        {
            Meters.Increment(SasMeterCode.TotalJackpotCredits, credits);
            Meters.Increment(SasMeterCode.TotalAttendantPaidPaytableWin_Crd, credits);
        }

        if (reported)
            Queue((byte)SasExceptionCode.HandpayWasReset, [], priority: true);
        return true;
    }

    /// <summary>What long poll 1B answers: the hand pay until its reading is acknowledged, zeros after.</summary>
    /// <remarks>The amount is in accounting units, as Table 7.8 has it when no progressive contributed.</remarks>
    private Dictionary<string, object> HandpayInformation()
    {
        HandpayOwed? owed;
        lock (_gate)
            owed = _handpay;

        bool reports = owed is { ReadAcknowledged: false };
        return new Dictionary<string, object>
        {
            ["progressive_group"] = (byte)0,
            ["level"] = reports ? owed!.Level : (byte)0,
            ["amount"] = reports ? owed!.AmountCents / AccountingCents : 0L,
            ["partial_pay"] = 0L,
            ["reset_id"] = (byte)0,
            ["session_game_win"] = 0L,
            ["session_game_pay"] = 0L,
        };
    }

    /// <summary>A loop break is an implied NACK: the reading stays unacknowledged, so no later message acknowledges it.</summary>
    private static void ForgetHandpayReading(MachineLink link) => link.UnacknowledgedHandpayPoll = null;

    /// <summary>Remembers a hand pay reading just sent, so the next message can acknowledge it.</summary>
    private void RememberHandpayReading(MachineLink link, ReadOnlySpan<byte> frame)
    {
        lock (_gate)
            link.UnacknowledgedHandpayPoll = _handpay is { ReadAcknowledged: false } ? frame.ToArray() : null;
    }

    /// <summary>
    /// Any message other than the same 1B again acknowledges the reading, as section 3 makes any
    /// next message an implied acknowledgement - a poll to another address and a broadcast included.
    /// </summary>
    private void NoteHandpayAcknowledgement(MachineLink link, ReadOnlySpan<byte> frame)
    {
        bool reissue;
        lock (_gate)
        {
            // A queue of hand pays reissues $51 every fifteen seconds until the reading is acknowledged (GSA 4.12).
            reissue = _handpay is { ReadAcknowledged: false }
                      && Stopwatch.GetElapsedTime(_handpayIssuedAt) >= HandpayReissue;
            if (reissue)
                _handpayIssuedAt = Stopwatch.GetTimestamp();
        }

        if (reissue)
            Queue((byte)SasExceptionCode.HandpayIsPending, [], priority: true);

        bool resetNowReported = false;
        lock (_gate)
        {
            if (link.UnacknowledgedHandpayPoll is not { } sent)
                return;

            if (frame.SequenceEqual(sent))
                return;

            link.UnacknowledgedHandpayPoll = null;
            if (_handpay is { Reset: true })
            {
                _handpay = null;
                resetNowReported = true;
            }
            else if (_handpay is not null)
            {
                _handpay = _handpay with { ReadAcknowledged = true };
            }
        }

        if (resetNowReported)
            Queue((byte)SasExceptionCode.HandpayWasReset, [], priority: true);
    }
}
