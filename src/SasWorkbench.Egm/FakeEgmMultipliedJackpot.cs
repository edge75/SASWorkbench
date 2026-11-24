using System.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>What the host last enabled with long poll $8B: the win range in accounting units, the multiplier byte and the wager restriction.</summary>
internal readonly record struct MultipliedJackpotSetting(long MinimumWin, long MaximumWin, byte Multiplier, bool MaxBetOnly)
{
    /// <summary>Bit 7 of the multiplier byte marks the extra part non-deductible.</summary>
    public bool NonDeductible => (Multiplier & 0x80) != 0;

    /// <summary>The multiplier itself, one to ten.</summary>
    public int Times => Multiplier & 0x7F;
}

/// <summary>
/// Multiplied jackpots: long poll $8B, and the extra part of a multiplied win paid as a legacy bonus with $7C.
/// </summary>
/// <remarks>
/// <para>
/// The protocol half is the machine library's: <c>lp8B_InitiateMultipliedJackpot</c> tests both feature bits, the
/// channel and the address, acknowledges, and hands the five fields to the application unchecked (lp.cpp:8674);
/// the award is <c>SAS_LegacyBonusAward</c> (libsas.cpp:2535). QSP never sets the jackpot multiplier bit and ignores
/// $8B (SasNetwork.cpp:286, SasNetwork_Callbacks.hpp:198), so the application half is this machine's own (D52):
/// </para>
/// <list type="bullet">
/// <item>A command it cannot apply - a multiplier outside one to ten, an enable or wager byte past 01, or a minimum
/// above the maximum - is acknowledged and changes nothing.</item>
/// <item>A game multiplies by the setting in force when it started, and only a paytable win inside the inclusive
/// range, of a game at the maximum bet when the host asked for that; a progressive's part is not multiplied.</item>
/// <item>The extra part is the win times one less than the multiplier, paid to the credit meter as a machine-paid
/// external bonus, onto the legacy meter bit 7 of the multiplier chooses, and reported by $7C with tax status 00;
/// a bonus waiting for the game end is paid in the same award.</item>
/// <item>A win paid by hand takes the extra part into its hand pay, and the $7C follows the $52.</item>
/// <item>The setting is in working memory: a lost link or a restart switches it off.</item>
/// </list>
/// </remarks>
public sealed partial class FakeEgm
{
    // JACKPOT_MULTIPLIER_BIT of the first feature byte (libsas.h:186).
    private const byte JackpotMultiplierBit = 0x01;

    private MultipliedJackpotSetting? _multipliedJackpot;
    private MultipliedJackpotSetting? _roundMultipliedJackpot;
    private LegacyBonusAward? _multipliedAwardAfterHandpay;

    /// <summary>The jackpot multiplier bit, set on a machine built multiplying jackpots while it takes legacy bonuses, which carry the award.</summary>
    private byte MultipliedJackpotFeatureBits() =>
        _options.MultipliedJackpot && LegacyBonusOn ? JackpotMultiplierBit : (byte)0;

    /// <summary>Long poll $8B, after the check sequence and channel gate: silent unless both feature bits are set and the address is this machine's.</summary>
    private void AnswerMultipliedJackpot(MachineLink link, ReadOnlySpan<byte> frame)
    {
        // Both feature bits, then the channel, then the address (lp.cpp:8689, :8691, :8700, :8708).
        if (MultipliedJackpotFeatureBits() == 0 || frame[0] != link.Address.Value)
            return;

        // Address, command, eight bytes of BCD range, multiplier, enable, wager type and the check sequence.
        if (frame.Length < 15)
        {
            Reject(link);
            return;
        }

        // No busy response: the library's test is commented out (lp.cpp:8719).
        Send(link, [SasSymbol.Data8(link.Address.Value)]);

        byte multiplier = frame[10];
        byte enable = frame[11];
        byte wager = frame[12];
        long minimum = BcdValue(frame.Slice(2, 4));
        long maximum = BcdValue(frame.Slice(6, 4));

        lock (_gate)
        {
            if (enable == 0x01)
                _multipliedJackpot = null;
            else if (enable == 0x00 && wager <= 0x01 && minimum <= maximum && (multiplier & 0x7F) is >= 1 and <= 10)
                _multipliedJackpot = new MultipliedJackpotSetting(minimum, maximum, multiplier, wager == 0x01);
        }
    }

    /// <summary>A game takes the setting in force as it starts; one changed while it runs waits for the next.</summary>
    private void CaptureRoundMultiplierLocked(long betCredits) =>
        _roundMultipliedJackpot = _multipliedJackpot is { } setting && (!setting.MaxBetOnly || betCredits >= _options.MaxBet)
            ? setting
            : null;

    /// <summary>The extra part the ending round's win earns, in accounting units, and the round's setting with it.</summary>
    private (long Extra, MultipliedJackpotSetting Setting) RoundMultipliedExtraLocked(long won)
    {
        MultipliedJackpotSetting? setting = _roundMultipliedJackpot;
        _roundMultipliedJackpot = null;

        if (setting is not { } applied || won <= 0 || won < applied.MinimumWin || won > applied.MaximumWin)
            return (0, default);

        return (won * (applied.Times - 1), applied);
    }

    /// <summary>
    /// Settles the extra part at the round's end: to the credit meter with a bonus waiting for the game end, or into
    /// the hand pay the round raised.
    /// </summary>
    private void SettleRoundMultiplierLocked(long won, bool handpaid)
    {
        (long extra, MultipliedJackpotSetting setting) = RoundMultipliedExtraLocked(won);
        if (extra <= 0)
            return;

        if (handpaid && _handpay is { Reset: false } owed)
        {
            _handpay = owed with
            {
                AmountCents = owed.AmountCents + (extra * AccountingCents),
                MultipliedCents = owed.MultipliedCents + (extra * AccountingCents),
            };
            _multipliedAwardAfterHandpay = new LegacyBonusAward(setting.Multiplier, extra, 0x00, 0);
            return;
        }

        (byte taxStatus, long bonus) = TakeEscrowForMultipliedAwardLocked();
        long paid = extra + bonus;
        Meters.SetCurrentCredits(Meters.Read(SasMeterCode.CurrentCredits) + paid);
        Meters.Increment(SasMeterCode.TotalCoinOutCredits, paid);
        Meters.Increment(SasMeterCode.TotalMachinePaidExternalBonusWin_Crd, paid);
        _legacyMeters[taxStatus] += bonus;
        AwardMultipliedLocked(new LegacyBonusAward(setting.Multiplier, extra, taxStatus, bonus));
    }

    /// <summary>
    /// A bonus held for the game end that would be paid now joins the award: the first tax status in escrow, unless
    /// the game delay, a busy state or a hand pay holds it, or it is itself above the jackpot limit.
    /// </summary>
    private (byte TaxStatus, long Bonus) TakeEscrowForMultipliedAwardLocked()
    {
        if (_gameDelayTenths != 0 || IsLegacyBonusBusyLocked() || _handpay is not null || _cashoutDue != 0)
            return (0, 0);

        for (byte taxStatus = 0; taxStatus < LegacyTaxStatuses; taxStatus++)
        {
            long held = _legacyEscrow[taxStatus];
            if (held <= 0 || OverJackpotLimit(held * AccountingCents))
                continue;

            _legacyEscrow[taxStatus] = 0;
            return (taxStatus, held);
        }

        return (0, 0);
    }

    /// <summary>Meters the extra part on the legacy meter bit 7 chooses, stores the award for $90 and reports it with $7C.</summary>
    private void AwardMultipliedLocked(LegacyBonusAward award)
    {
        _legacyMeters[(award.Multiplier & 0x80) != 0 ? 1 : 0] += award.MultipliedWin;
        RaiseLegacyBonusAwardLocked(award);
    }

    /// <summary>Stores an award for $90 and queues the payload <c>SAS_LegacyBonusAward</c> builds (libsas.cpp:2594, :2634 to :2643).</summary>
    private void RaiseLegacyBonusAwardLocked(LegacyBonusAward award)
    {
        _lastLegacyAward = award;
        RaiseEvent((byte)SasExceptionCode.LegacyBonusPayOrJackpotAwarded,
        [
            award.Multiplier,
            .. BcdBytes(award.MultipliedWin, 4),
            award.TaxStatus,
            .. BcdBytes(award.Bonus, 4),
        ]);
    }

    /// <summary>Queues hand pay was reset, then the multiplied award that waited for it.</summary>
    private void QueueHandpayWasReset()
    {
        Queue((byte)SasExceptionCode.HandpayWasReset, [], priority: true);

        lock (_gate)
        {
            if (_multipliedAwardAfterHandpay is not { } award)
                return;

            _multipliedAwardAfterHandpay = null;
            AwardMultipliedLocked(award);
        }
    }

    /// <summary>The channel that owns legacy bonusing going quiet past the loop break switches multiplied jackpots off.</summary>
    private void TickMultipliedJackpot(MachineLink link)
    {
        if (!_options.MultipliedJackpot || _options.LinkLossAfter <= TimeSpan.Zero || link.LastHeard == 0
            || !Channels.Answers(link.Channel, (byte)SasLongPollCode.LP_INITMULTIPLEJACKPOT)
            || Stopwatch.GetElapsedTime(link.LastHeard) <= _options.LinkLossAfter)
        {
            return;
        }

        lock (_gate)
            _multipliedJackpot = null;
    }

    /// <summary>A restart forgets the host's setting, which is held in working memory only.</summary>
    private void RestartMultipliedJackpotLocked()
    {
        _multipliedJackpot = null;
        _roundMultipliedJackpot = null;
    }
}
