using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// A case that never reaches a machine is skipped for something - and that something is the line
/// a reader acts on. These pin that the reason names what the machine really lacks, judged from
/// what the stimulus backend says it would offer, and that a case still to be written says so
/// instead of being reported as broken.
/// </summary>
public class LinklessSkipTests
{
    private static ScriptTest ParseOne(string body)
    {
        ScriptDocument document = ScriptParser.Parse(
            $"test \"t\" {{\n{body}\n}}\n", "memory.sas");
        return Assert.Single(document.Tests);
    }

    /// <summary>
    /// The peripheral driver's fixed table offers no buttons, because which buttons exist is the
    /// machine's to say. With the machine's offer supplied, a button it has is no longer named as
    /// the reason; the requirement it does lack is.
    /// </summary>
    [Fact]
    public void A_case_without_a_link_is_judged_against_what_the_backend_would_offer()
    {
        ScriptTest test = ParseOne("""
              requires stimulus.button.collect, stimulus.handpay
              note "Not written yet."
            """);

        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Qpm,
            StimulusOffer = new Dictionary<string, string?>
            {
                ["stimulus"] = "qpm",
                ["stimulus.button.collect"] = "yes",
            },
        });

        TestOutcome outcome = runner.Run(test);

        Assert.Equal(Verdict.Skip, outcome.Verdict);
        Assert.StartsWith("stimulus.handpay:", outcome.SkipReason);
    }

    /// <summary>
    /// Without an offer the backend's fixed table still decides, as before.
    /// </summary>
    [Fact]
    public void Without_an_offer_the_backends_fixed_table_decides()
    {
        ScriptTest test = ParseOne("""
              requires stimulus.button.collect
              note "Not written yet."
            """);

        var runner = new ScriptRunner(new ScriptRunnerOptions { Stimulus = StimulusBackend.Qpm });

        TestOutcome outcome = runner.Run(test);

        Assert.Equal(Verdict.Skip, outcome.Verdict);
        Assert.StartsWith("stimulus.button.collect:", outcome.SkipReason);
    }

    /// <summary>
    /// Once nothing it requires is missing, a case of notes alone is a case still to be written.
    /// Reporting it as an error would turn every honest skeleton red the day its capabilities
    /// arrive.
    /// </summary>
    [Fact]
    public void A_case_of_notes_alone_with_everything_it_needs_is_skipped_as_unwritten()
    {
        ScriptTest test = ParseOne("""
              requires stimulus.button.collect
              note "Not written yet."
            """);

        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Qpm,
            StimulusOffer = new Dictionary<string, string?> { ["stimulus.button.collect"] = "yes" },
        });

        TestOutcome outcome = runner.Run(test);

        Assert.Equal(Verdict.Skip, outcome.Verdict);
        Assert.Contains("no steps are written yet", outcome.SkipReason);
    }

    /// <summary>
    /// A case with nothing in it at all holds no notes either, so it is not a case still to be
    /// written; it stays an error.
    /// </summary>
    [Fact]
    public void An_empty_case_without_a_link_is_still_an_error()
    {
        ScriptTest test = ParseOne("");

        TestOutcome outcome = new ScriptRunner().Run(test);

        Assert.Equal(Verdict.Error, outcome.Verdict);
    }

    /// <summary>
    /// When the link is refused, a requirement the backend would have met is not the reason
    /// given; the refusal is.
    /// </summary>
    [Fact]
    public void A_refused_link_reports_the_refusal_when_the_offer_meets_the_case()
    {
        ScriptTest test = ParseOne("""
              requires stimulus.button.collect
              host connect tcp 127.0.0.1:7200 address 01
            """);

        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Qpm,
            StimulusOffer = new Dictionary<string, string?> { ["stimulus.button.collect"] = "yes" },
            Environment = (_, _, _, _, _, _, _, _) => throw new TransportRefusedException("no machine dialled in"),
        });

        TestOutcome outcome = runner.Run(test);

        Assert.Equal(Verdict.Skip, outcome.Verdict);
        Assert.Equal("no machine dialled in", outcome.SkipReason);
    }
}
