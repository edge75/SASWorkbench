using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// Remembered values against the software machine, end to end: one reply is kept, the machine is
/// acted on, and a later reply is compared with it. What matters is that a wrong relation fails
/// with both numbers in the report, and that a value never taken skips rather than passing.
/// </summary>
public class SavedValueRunnerTests
{
    private static TestOutcome Run(string body)
    {
        string script = "test \"t\" {\n  host connect loopback address 01\n  host poll every 20ms\n"
            + "  host expect link up within 2s\n" + body + "\n}\n";
        ScriptDocument document = ScriptParser.Parse(script, "saved.sas");
        return new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(null),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(5),
            QuietGap = TimeSpan.FromMilliseconds(15),
        }).Run(document.Tests[0]);
    }

    private static string Explain(TestOutcome result) =>
        string.Join('\n', result.Steps.Select(s => s.ToString()));

    [Fact]
    public void Two_polls_reporting_the_same_meter_agree()
    {
        TestOutcome result = Run("""
              egm bill insert 2000
              egm play games=2 bet=1
              host send lp 6F data 0400000500
              host remember field meters[0].value as games
              host send lp AF data 0400000500
              host expect field meters[0].value == saved.games
            """);

        Assert.Equal(Verdict.Pass, result.Verdict);
        Assert.Equal(0, result.Failed);
    }

    /// <summary>The gain is the difference between two readings, with play between them.</summary>
    [Fact]
    public void A_gain_is_measured_across_play()
    {
        TestOutcome result = Run("""
              egm bill insert 2000
              host send lp 6F data 0400000500
              host remember field meters[0].value as before
              egm play games=3 bet=1
              host send lp 6F data 0400000500
              host expect field meters[0].value == saved.before + 3
            """);

        Assert.Equal(Verdict.Pass, result.Verdict);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public void A_wrong_relation_fails_and_shows_both_numbers()
    {
        TestOutcome result = Run("""
              egm bill insert 2000
              host send lp 6F data 0400000500
              host remember field meters[0].value as before
              egm play games=3 bet=1
              host send lp 6F data 0400000500
              host expect field meters[0].value == saved.before + 4
            """);

        Assert.Equal(Verdict.Fail, result.Verdict);
        StepResult failed = result.Steps.Single(s => s.Verdict == Verdict.Fail);
        Assert.Equal("4", failed.Expected);
        Assert.Equal("3", failed.Actual);
        Assert.Contains("saved.before + 4", failed.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// A remember whose reply never came cannot feed a comparison. The comparison skips with the
    /// reason it was not taken, so the report shows one failure, not two, and nothing passes on a
    /// number nobody read.
    /// </summary>
    [Fact]
    public void A_value_never_taken_skips_the_comparison_with_the_reason()
    {
        TestOutcome result = Run("""
              host send lp 6F data 0400000500
              host remember field meters[5].value as nothing
              host expect field meters[0].value == saved.nothing
            """);

        StepResult remember = result.Steps.Single(s => s.Description.StartsWith("remember", StringComparison.Ordinal));
        Assert.Equal(Verdict.Fail, remember.Verdict);
        StepResult compare = result.Steps[^1];
        Assert.Equal(Verdict.Skip, compare.Verdict);
        Assert.Contains("saved.nothing was not taken", compare.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_report_shows_what_was_remembered()
    {
        TestOutcome result = Run("""
              host send lp 1F
              host remember field game_id as id
              host expect field game_id of lp 1F == saved.id
            """);

        Assert.Equal(Verdict.Pass, result.Verdict);
        StepResult remember = result.Steps.Single(s => s.Description.StartsWith("remember", StringComparison.Ordinal));
        Assert.Equal("saved.id = QX", remember.Actual);
    }
}
