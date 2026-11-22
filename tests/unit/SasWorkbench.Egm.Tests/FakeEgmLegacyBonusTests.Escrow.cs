using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// When the software machine pays a legacy bonus it has acknowledged, and when it refuses one:
/// QSP's escrow (AsyncLpLegacyBonus and ProcessLegacyBonus), its busy states
/// (IsLegacyBonusBusyCondition) and the section 13.7 game delay (GameDelay).
/// </summary>
public sealed partial class FakeEgmLegacyBonusTests
{
    [Fact]
    public void A_door_open_answers_the_bonus_busy_and_pays_nothing()
    {
        // The library's busy response is the address followed by 00 (libsas_threads_sync.cpp:374),
        // sent instead of the acknowledgement while the platform has set BR_LP8A (lp.cpp:8652).
        Start();
        Machine.SetDoor("main", open: true);
        Drain();

        LongPollResult result = Send8A(5, 0x00);

        Assert.Equal(new byte[] { 0x01, 0x00 }, result.Frame);
        Assert.Equal(0, Machine.Meters.Read(SasMeterCode.CurrentCredits));

        Machine.SetDoor("main", open: false);
        Assert.DoesNotContain(LegacyBonusAwarded, Drain());
    }

    [Fact]
    public void A_machine_the_host_has_shut_down_answers_the_bonus_busy()
    {
        Start();
        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x01, []).Outcome);

        Assert.Equal(new byte[] { 0x01, 0x00 }, Send8A(5, 0x00).Frame);
    }

    [Fact]
    public void Bonuses_sent_during_a_hand_pay_are_escrowed_added_up_and_paid_after_the_reset()
    {
        // QSP escrows rather than refuses during a hand pay, adds later bonuses of the same tax
        // status to it, and pays the sum when the hand pay condition clears.
        Start();
        Machine.RaiseHandpay(HandpayKind.Jackpot, 150000);
        Drain();

        Award(6, 0x02);
        Award(6, 0x02);
        Assert.Equal(0, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.DoesNotContain(LegacyBonusAwarded, Drain());

        Assert.True(Machine.ResetHandpay());

        Assert.True(Wait(() => Machine.Meters.Read(SasMeterCode.CurrentCredits) == 12),
            "the escrow was not paid after the reset");
        Assert.Contains(LegacyBonusAwarded, Drain());
        Assert.Equal(12, ReadWin().Number("bonus"));
    }

    [Fact]
    public void The_game_delay_holds_a_bonus_sent_after_a_game_until_it_runs_out()
    {
        // The delay is two bytes, least significant first, in tenths of a second (lp.cpp:1755);
        // QSP starts it at game end and pays the escrow once it has run (SasNetwork_Conditions.hpp:691).
        Start();
        Machine.InsertBill(2000);
        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x2E, [0x0A, 0x00]).Outcome);
        Machine.PlayGames(1, 1);
        long afterGame = Machine.Meters.Read(SasMeterCode.CurrentCredits);

        Award(5, 0x00);
        Assert.Equal(afterGame, Machine.Meters.Read(SasMeterCode.CurrentCredits));

        Assert.True(Wait(() => Machine.Meters.Read(SasMeterCode.CurrentCredits) == afterGame + 5),
            "the escrow was not paid once the delay ran out");
    }

    [Fact]
    public void A_delay_of_zero_sent_during_a_delay_ends_it_at_once()
    {
        // Section 13.7 as QSP has it: a new delay replaces what is left, and zero cancels.
        Start();
        Machine.InsertBill(2000);
        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x2E, [0x58, 0x02]).Outcome);
        Machine.PlayGames(1, 1);
        long afterGame = Machine.Meters.Read(SasMeterCode.CurrentCredits);
        Award(5, 0x00);

        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x2E, [0x00, 0x00]).Outcome);

        Assert.True(Wait(() => Machine.Meters.Read(SasMeterCode.CurrentCredits) == afterGame + 5, 1000),
            "a cancelled delay still held the escrow");
    }

    [Fact]
    public void A_delay_set_with_no_game_played_holds_nothing()
    {
        Start();
        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x2E, [0x32, 0x00]).Outcome);

        Award(5, 0x00);

        Assert.Equal(5, Machine.Meters.Read(SasMeterCode.CurrentCredits));
    }
}
