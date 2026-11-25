using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The line steps end to end through the script runner against the software machine, with the loop
/// break after one second so the chirp comes quickly.
/// </summary>
public class LineScriptRunTests
{
    private static TestOutcome Run(string script, FakeEgmOptions? machine = null)
    {
        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(machine ?? new FakeEgmOptions { LinkLossAfter = TimeSpan.FromSeconds(1) }),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromMilliseconds(300),
            QuietGap = TimeSpan.FromMilliseconds(15),
        });
        return runner.Run(ScriptParser.Parse(script, "line.sas").Tests[0]);
    }

    private static string Explain(TestOutcome result) =>
        string.Join('\n', result.Steps.Select(s => s.ToString()));

    [Fact]
    public void A_sequence_hears_the_chirp_and_a_poll_without_wakeup_draws_nothing()
    {
        TestOutcome result = Run("""
            requires host.chirp, host.poll_without_wakeup, host.poll_sequence
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host poll sequence
            host send lp 1F without wakeup
            host send lp 1F
            host expect no chirp for 700ms
            host expect chirp within 1s
            host send sync
            host send lp 1F
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }

    [Fact]
    public void Expecting_a_chirp_on_a_polled_line_fails_rather_than_hearing_nothing()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host expect chirp within 1s
            """);

        Assert.Equal(Verdict.Fail, result.Verdict);
    }

    [Fact]
    public void The_operator_moves_the_machine_and_the_host_follows_it()
    {
        TestOutcome result = Run("""
            requires machine.address_configuration, host.poll_sequence
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host poll sequence
            egm set address 26
            host poll address 26
            host expect no reply to lp 1F
            host send sync
            host send gp draws operator_changed_options
            host send lp 1F
            host send gp draws silence to 01
            egm set address 00
            host send sync
            host send gp draws silence
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }

    private const string Repeat = """
        requires host.timing
        host connect loopback address 01
        host poll every 20ms
        host expect link up within 2s
        egm set asset number 12345
        host forget exceptions within 1s
        host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000131000000000000000000
        host expect exception aft_transfer_complete within 15s
        host expect exception aft_transfer_complete every 1s
        host expect exception aft_transfer_complete every 1s
        """;

    [Fact]
    public void An_exception_repeated_on_its_period_passes()
    {
        TestOutcome result = Run(Repeat, new FakeEgmOptions { AftCompletionReissue = TimeSpan.FromSeconds(1) });

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }

    [Fact]
    public void The_repeat_of_an_exception_is_timed_from_the_last_one()
    {
        TestOutcome result = Run("""
            requires host.timing
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            egm set asset number 12345
            host forget exceptions within 1s
            host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000131000000000000000000
            host expect exception aft_transfer_complete within 15s
            host expect exception aft_transfer_complete every 2s
            """);

        // The software machine repeats it every fifteen seconds, so a two-second period is not met.
        Assert.Equal(Verdict.Fail, result.Verdict);
        Assert.Contains("not again", Explain(result), StringComparison.Ordinal);
    }
}
