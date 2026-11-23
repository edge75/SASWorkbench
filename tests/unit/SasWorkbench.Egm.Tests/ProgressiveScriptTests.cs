using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// Progressives end to end through the script runner: the declaration builds the software machine,
/// its SAS group is read back off $1F, a background broadcast keeps a level fed, and the win is a
/// stimulus.
/// </summary>
public class ProgressiveScriptTests
{
    private static TestOutcome Run(string script)
    {
        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromMilliseconds(300),
            QuietGap = TimeSpan.FromMilliseconds(15),
        });
        return runner.Run(ScriptParser.Parse(script, "progressive.sas").Tests[0]);
    }

    private static string Explain(TestOutcome result) =>
        string.Join('\n', result.Steps.Select(s => s.ToString()));

    [Fact]
    public void A_declared_group_is_read_back_and_a_broadcast_level_is_won()
    {
        TestOutcome result = Run("""
            test "win" {
              requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
              machine progressive sas group 1 levels 1
              host connect loopback address 01
              host poll every 20ms
              host expect link up within 2s
              host broadcast lp 80 with progressive_group=1 level=1 amount=500 every 500ms
              egm bill insert 1000
              host wait 1s
              egm progressive win level=1
              host expect exception sas_progressive_level_hit within 5s
              host send lp 84
              host expect field amount of lp 84 == 500
              host broadcast stop
            }
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }

    [Fact]
    public void A_machine_in_no_group_skips_a_case_that_needs_one()
    {
        TestOutcome result = Run("""
            test "none" {
              requires machine.sas_progressives
              host connect loopback address 01
              host poll every 20ms
              host expect link up within 2s
            }
            """);

        Assert.Equal(Verdict.Skip, result.Verdict);
        Assert.Contains("group 00", result.SkipReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_sas_levels_are_claimed_only_by_a_machine_built_with_them()
    {
        const string body = """
              requires machine.progressives
              host connect loopback address 01
              host poll every 20ms
              host expect link up within 2s
            }
            """;

        Assert.Equal(Verdict.Skip, Run("test \"bare\" {\n" + body).Verdict);
        Assert.Equal(Verdict.Pass, Run("test \"built\" {\n  machine progressive non_sas levels 1-4\n" + body).Verdict);
    }

    [Fact]
    public void A_group_mismatch_on_several_levels_is_silence_which_an_answer_assertion_fails()
    {
        TestOutcome result = Run("""
            test "mismatch" {
              machine progressive sas group 1 levels 1
              host connect loopback address 01
              host poll every 20ms
              host expect link up within 2s
              host expect answer to lp 80 data 02010000000500
              host expect answer to lp 86 data 0702010000000500
            }
            """);

        Assert.Equal(Verdict.Fail, result.Verdict);
        Assert.Single(result.Steps, step => step.Verdict == Verdict.Fail);
    }

    [Theory]
    [InlineData("machine progressive sas group 0 levels 1")]
    [InlineData("machine progressive sas group 1 levels 0")]
    [InlineData("machine progressive non_sas levels 1-33")]
    [InlineData("host broadcast lp 1F every 1s")]
    [InlineData("host broadcast lp 80 with progressive_group=1 level=1 amount=5 every 50ms")]
    [InlineData("egm progressive win")]
    [InlineData("egm progressive win level=1,1")]
    public void A_malformed_progressive_line_stops_the_parse(string line) =>
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse($"test \"t\" {{\n  {line}\n}}\n", "bad.sas"));

    [Fact]
    public void A_level_cannot_be_both_fed_by_a_host_and_run_by_the_machine() =>
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse(
            "test \"t\" {\n  machine progressive sas group 1 levels 1-2\n  machine progressive non_sas levels 2\n}\n", "bad.sas"));
}
