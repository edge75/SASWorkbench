using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// The steps of the game-in-progress group: a game started and ended by the case, a cash out that
/// lasts, the jackpot limit, a paytable win beside a progressive, and who offers them.
/// </summary>
public sealed class GameInProgressStepTests
{
    [Fact]
    public void A_game_is_started_with_a_bet_and_ended_with_a_win()
    {
        ScriptTest test = ScriptParser.Parse("egm game start bet=2\negm game end win=5\negm game start\negm game end").Tests[0];

        var start = Assert.IsType<StartGameStep>(test.Steps[0]);
        var end = Assert.IsType<EndGameStep>(test.Steps[1]);
        Assert.Equal(2, start.BetCredits);
        Assert.Equal(5, end.WinCredits);
        Assert.Equal(0, Assert.IsType<StartGameStep>(test.Steps[2]).BetCredits);
        Assert.Equal(0, Assert.IsType<EndGameStep>(test.Steps[3]).WinCredits);
        Assert.Equal("stimulus.game_in_progress", start.RequiredCapability);
        Assert.Equal("stimulus.game_in_progress", end.RequiredCapability);
    }

    [Fact]
    public void A_cash_out_lasts_as_long_as_it_is_told_and_a_plain_press_is_unchanged()
    {
        ScriptTest test = ScriptParser.Parse("egm press collect lasting 3s\negm press collect").Tests[0];

        var slow = Assert.IsType<SlowCashoutStep>(test.Steps[0]);
        Assert.Equal(TimeSpan.FromSeconds(3), slow.Lasting);
        Assert.Equal("stimulus.cashout_in_progress", slow.RequiredCapability);
        Assert.IsType<PressButtonStep>(test.Steps[1]);
    }

    [Fact]
    public void The_jackpot_limit_is_set_in_cents()
    {
        var step = Assert.IsType<SetJackpotLimitStep>(Assert.Single(ScriptParser.Parse("egm set jackpot limit 400").Tests[0].Steps));

        Assert.Equal(400, step.Cents);
        Assert.Equal("stimulus.jackpot_limit", step.RequiredCapability);
    }

    [Fact]
    public void A_progressive_win_can_carry_a_paytable_win()
    {
        var step = Assert.IsType<ProgressiveWinStep>(
            Assert.Single(ScriptParser.Parse("egm progressive win level=1 win=10").Tests[0].Steps));

        Assert.Equal(10, step.WinCredits);
        Assert.Equal("progressive win level=1 bet=1 win=10", step.Describe());
    }

    [Theory]
    [InlineData("egm game")]
    [InlineData("egm game begin")]
    [InlineData("egm game start win=3")]
    [InlineData("egm game end bet=3")]
    [InlineData("egm game start bet=lots")]
    [InlineData("egm press collect lasting")]
    [InlineData("egm press collect lasting 0s")]
    [InlineData("egm press service lasting 3s")]
    [InlineData("egm set jackpot limit")]
    [InlineData("egm set jackpot limit -5")]
    [InlineData("egm set jackpot ceiling 5")]
    public void A_malformed_step_is_refused(string line)
    {
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse(line));
    }

    [Fact]
    public void The_software_machine_has_a_jackpot_limit_and_a_person_can_hold_a_game_or_a_cash_out()
    {
        IReadOnlyDictionary<string, string?> software = CapabilityProbe.ForStimulus(StimulusBackend.Software);
        foreach (string key in new[]
                 {
                     "machine.jackpot_limit", "stimulus.jackpot_limit", "stimulus.game_in_progress", "stimulus.cashout_in_progress",
                 })
        {
            Assert.True(software.ContainsKey(key), key);
        }

        IReadOnlyDictionary<string, string?> manual = CapabilityProbe.ForStimulus(StimulusBackend.Manual);
        Assert.True(manual.ContainsKey("stimulus.game_in_progress"));
        Assert.True(manual.ContainsKey("stimulus.cashout_in_progress"));
        Assert.True(manual.ContainsKey("stimulus.jackpot_limit"));
        Assert.False(manual.ContainsKey("machine.jackpot_limit"));
        Assert.True(manual.ContainsKey("machine.poker"));
    }
}
