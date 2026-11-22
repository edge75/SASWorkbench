using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// Legacy bonusing end to end through the script runner: the capabilities are read off the
/// software machine's enabled-features reply, so they mean the same against a real board, and the
/// operator's switch reaches the machine as a stimulus.
/// </summary>
public class LegacyBonusScriptTests
{
    private static TestOutcome Run(string script, FakeEgmOptions? machine = null)
    {
        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(machine),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(5),
            QuietGap = TimeSpan.FromMilliseconds(15),
        });
        return runner.Run(ScriptParser.Parse(script, "legacy.sas").Tests[0]);
    }

    private static string Explain(TestOutcome result) =>
        string.Join('\n', result.Steps.Select(s => s.ToString()));

    [Fact]
    public void Legacy_bonusing_is_read_off_the_machine_and_a_machine_without_it_skips()
    {
        const string script = """
            requires machine.legacy_bonus
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host send lp 8A data 0000000500
            host expect exception legacy_bonus_pay_or_jackpot_awarded within 5s
            """;

        TestOutcome on = Run(script);
        Assert.True(on.Verdict == Verdict.Pass, Explain(on));

        TestOutcome off = Run(script, new FakeEgmOptions { LegacyBonus = false });
        Assert.Equal(Verdict.Skip, off.Verdict);
        Assert.Contains("legacy bonus", off.SkipReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_machine_that_offers_no_multiplied_jackpots_skips_a_case_that_needs_them()
    {
        TestOutcome result = Run("""
            requires machine.legacy_bonus, machine.multiplied_jackpot
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            """);

        Assert.Equal(Verdict.Skip, result.Verdict);
        Assert.StartsWith("machine.multiplied_jackpot:", result.SkipReason!, StringComparison.Ordinal);
        Assert.Contains("multiplied jackpots off", result.SkipReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_operator_switch_turns_legacy_bonusing_off_so_the_bonus_is_ignored()
    {
        TestOutcome result = Run("""
            requires stimulus.legacy_bonus
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            egm set legacy bonus off
            host expect no reply to lp 8A data 0000000500
            host expect no reply to lp 2E data 3200
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }
}
