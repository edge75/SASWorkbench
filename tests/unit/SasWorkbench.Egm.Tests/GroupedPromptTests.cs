using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// Door and button actions in a row are asked of a person once, run end to end against a software
/// machine with the manual stimulus.
/// </summary>
/// <remarks>
/// On the board a person answered forty prompts for the twenty actions of one queue case, and lost
/// count of the service presses. One prompt for the whole run of actions is both quicker and harder
/// to get wrong, as long as each step still reports what happened to it.
/// </remarks>
public class GroupedPromptTests
{
    private static (TestOutcome Outcome, List<string> Asked) Run(string body, PromptAnswer answer)
    {
        var asked = new List<string>();
        var person = new ManualStimulusBackend((request, _) =>
        {
            asked.Add(request);
            return answer;
        }, TimeSpan.FromSeconds(5));

        // The software machine with a person in place of its own stimulus, so the prompts are real.
        EnvironmentFactory software = SoftwareMachineEnvironment.For();
        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Manual,
            Environment = (step, options, declared, channels, validation, progressive, authentication) =>
                software(step, options, declared, channels, validation, progressive, authentication) with { Stimulus = person },
            PollInterval = TimeSpan.Zero,
            ResponseTimeout = TimeSpan.FromSeconds(5),
            QuietGap = TimeSpan.FromMilliseconds(15),
        });

        ScriptDocument document = ScriptParser.Parse(
            $"test \"grouped\" {{\n{body}\n}}\n", "grouped.sas");
        return (runner.Run(document.Tests[0]), asked);
    }

    private const string TwoGroups = """
          host connect loopback address 01
          host expect link up within 5s
          egm door main open
          egm door main close
          egm press service
          egm press service
          egm door main open
          egm door main close
          egm press service
          egm press service
          host send lp 1F to 01
          egm door main open
          egm door main close
        """;

    [Fact]
    public void Actions_in_a_row_are_asked_once_and_a_host_step_ends_the_group()
    {
        (TestOutcome outcome, List<string> asked) = Run(TwoGroups, PromptAnswer.Done);

        Assert.Equal(2, asked.Count);
        Assert.Contains("these 8 actions", asked[0], StringComparison.Ordinal);
        Assert.Contains("  4. press service once", asked[0], StringComparison.Ordinal);
        Assert.Contains("  8. press service once", asked[0], StringComparison.Ordinal);
        Assert.Contains("these 2 actions", asked[1], StringComparison.Ordinal);

        List<StepResult> actions = outcome.Steps
            .Where(s => s.Description.StartsWith("door", StringComparison.Ordinal)
                        || s.Description.StartsWith("press", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(10, actions.Count);
        Assert.All(actions, s => Assert.Equal(Verdict.Pass, s.Verdict));
    }

    [Fact]
    public void Declining_the_group_skips_every_action_in_it_with_the_reason()
    {
        (TestOutcome outcome, List<string> asked) = Run(TwoGroups, PromptAnswer.Declined);

        Assert.Equal(2, asked.Count);
        List<StepResult> actions = outcome.Steps
            .Where(s => s.Description.StartsWith("door", StringComparison.Ordinal)
                        || s.Description.StartsWith("press", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(10, actions.Count);
        Assert.All(actions, s =>
        {
            Assert.Equal(Verdict.Skip, s.Verdict);
            Assert.Contains("declined", s.Reason!, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_lone_action_keeps_its_one_line_prompt()
    {
        (_, List<string> asked) = Run("""
              host connect loopback address 01
              host expect link up within 5s
              egm door main open
              host send lp 1F to 01
            """, PromptAnswer.Done);

        string only = Assert.Single(asked);
        Assert.Contains("door main open", only, StringComparison.Ordinal);
        Assert.DoesNotContain("actions", only, StringComparison.Ordinal);
    }
}
