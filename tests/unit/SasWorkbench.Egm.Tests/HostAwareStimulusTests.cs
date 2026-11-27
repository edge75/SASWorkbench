using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// A backend that confirms its steps through the host, run end to end against a software machine (D63).
/// </summary>
/// <remarks>
/// The real demo's driver presses a button and then needs the wire to say whether anything happened. These pin the two
/// things the runner owes it: a view of the host that sees exceptions without taking them from the case, and a refusal
/// that ends the case, so the assertions after a stimulus that never happened are skipped rather than failed.
/// </remarks>
public class HostAwareStimulusTests
{
    private sealed class Recording(Func<StimulusStep, IHostView, string?> act, Func<StimulusStep, string?>? preflight = null)
        : IHostAwareStimulusBackend, IStimulusPreflight
    {
        public int Applied { get; private set; }

        public string? Refusal(StimulusStep step) => preflight?.Invoke(step);

        public StimulusBackend Kind => StimulusBackend.Qpm;

        public IReadOnlyDictionary<string, string?> Capabilities { get; } =
            new Dictionary<string, string?> { ["stimulus"] = "qpm", ["stimulus.button.service"] = "yes" };

        public void Apply(StimulusStep step, SasAddress? machine = null) =>
            throw new InvalidOperationException("the runner must hand a host-aware backend its host view");

        public void Apply(StimulusStep step, SasAddress? machine, IHostView host)
        {
            Applied++;
            if (act(step, host) is { } refusal)
                throw new StimulusUnavailableException(refusal) { EndsTheCase = true };
        }
    }

    private static TestOutcome Run(string body, IStimulusBackend backend, FakeEgm? egm = null)
    {
        EnvironmentFactory software = SoftwareMachineEnvironment.For();
        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = (step, options, declared, channels, validation, progressive, authentication, multiplied, handpay, games, devices) =>
                software(step, options, declared, channels, validation, progressive, authentication, multiplied, handpay, games, devices)
                    with { Stimulus = backend },
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(2),
            QuietGap = TimeSpan.FromMilliseconds(15),
        });

        ScriptDocument document = ScriptParser.Parse($"test \"aware\" {{\n{body}\n}}\n", "aware.sas");
        return runner.Run(document.Tests[0]);
    }

    [Fact]
    public void A_refusal_that_ends_the_case_skips_every_step_after_it_and_the_case_with_it()
    {
        var backend = new Recording((_, _) => "the machine decides its own outcomes");

        TestOutcome outcome = Run("""
              host connect loopback address 01
              host expect link up within 5s
              egm press service
              host expect exception change_lamp_on within 2s
              host send lp 1F
            """, backend);

        Assert.Equal(Verdict.Skip, outcome.Verdict);
        Assert.Contains("decides its own outcomes", outcome.SkipReason, StringComparison.Ordinal);
        Assert.All(outcome.Steps.Skip(2), step => Assert.Equal(Verdict.Skip, step.Verdict));
    }

    [Fact]
    public void A_step_refused_before_the_case_starts_skips_it_before_anything_is_driven()
    {
        var backend = new Recording((_, _) => null,
            step => step is PlayGamesStep ? "a bet of 1 credits cannot be placed" : null);

        TestOutcome outcome = Run("""
              host connect loopback address 01
              host expect link up within 5s
              egm press service
              egm play games=1 bet=1
            """, backend);

        Assert.Equal(Verdict.Skip, outcome.Verdict);
        Assert.Contains("'play games=1 bet=1': a bet of 1 credits", outcome.SkipReason, StringComparison.Ordinal);
        Assert.Equal(0, backend.Applied);
    }

    [Fact]
    public void The_host_view_reads_a_long_poll_and_is_refused_in_a_sequenced_case()
    {
        var unavailable = new List<string?>();
        var answered = new List<bool>();
        var backend = new Recording((_, host) =>
        {
            unavailable.Add(host.Unavailable);
            if (host.Unavailable is null)
                answered.Add(host.Ask(0x1F, []).Succeeded);
            return null;
        });

        Run("""
              host connect loopback address 01
              host expect link up within 5s
              egm press service
              host poll sequence
              egm press service
            """, backend);

        Assert.Equal(2, unavailable.Count);
        Assert.Null(unavailable[0]);
        Assert.Contains("own sequence", unavailable[1], StringComparison.Ordinal);
        Assert.Equal([true], answered);
    }

    [Fact]
    public void The_host_view_sees_an_exception_after_its_mark_without_taking_it_from_the_case()
    {
        ExceptionReport? heard = null;
        var backend = new Recording((_, host) =>
        {
            long mark = host.Mark();
            heard = host.ExceptionAfter(0x11, mark, TimeSpan.FromSeconds(3));
            return null;
        });

        TestOutcome outcome = Run("""
              host connect loopback address 01
              host expect link up within 5s
              egm press service
              host expect exception slot_door_opened within 3s
            """, backend);

        // Nothing opened the door, so the view must not invent one, and the case's own wait fails on its own terms.
        Assert.Null(heard);
        Assert.Equal(Verdict.Fail, outcome.Verdict);
    }

    [Fact]
    public void A_play_with_a_win_written_as_zero_remembers_that_one_was_written() =>
        Assert.True(((PlayGamesStep)ScriptParser.Parse("""
            test "t" {
              egm play games=1 bet=1 win=0
            }
            """, "t.sas").Tests[0].Steps[0]).WinStated);
}
