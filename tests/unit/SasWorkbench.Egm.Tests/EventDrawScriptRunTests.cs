using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// What a poll placed in a sequence drew and in which format, end to end through the script runner
/// against the software machine.
/// </summary>
public class EventDrawScriptRunTests
{
    private static TestOutcome Run(string script)
    {
        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(new FakeEgmOptions { LinkLossAfter = TimeSpan.FromSeconds(1) }),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromMilliseconds(300),
            QuietGap = TimeSpan.FromMilliseconds(15),
        });
        return runner.Run(ScriptParser.Parse(script, "events.sas").Tests[0]);
    }

    private static string Explain(TestOutcome result) =>
        string.Join('\n', result.Steps.Select(s => s.ToString()));

    private const string Opened = """
        requires host.poll_sequence, stimulus.real_time_events, stimulus.door.main
        host connect loopback address 01
        host poll every 20ms
        host expect link up within 2s
        host send lp 0E data 01
        host forget exceptions within 500ms
        host poll sequence
        egm door main open
        egm door main close
        """;

    [Fact]
    public void A_long_poll_draws_the_oldest_event_again_until_another_poll_acknowledges_it()
    {
        // The repeat is an implied NACK and the 7E between is the acknowledgement (SAS 6.02 3.20).
        TestOutcome result = Run(Opened + "\n" + """
            host send lp 1F draws event slot_door_opened
            host send lp 1F draws event slot_door_opened
            host send lp 7E draws event slot_door_closed
            host send gp draws event nothing
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }

    [Fact]
    public void A_long_poll_answered_with_its_own_reply_does_not_draw_an_event()
    {
        TestOutcome result = Run("""
            requires host.poll_sequence, stimulus.real_time_events
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host send lp 0E data 01
            host forget exceptions within 500ms
            host poll sequence
            host send lp 1F draws event slot_door_opened
            """);

        Assert.Equal(Verdict.Fail, result.Verdict);
    }

    [Fact]
    public void A_general_poll_draws_an_event_in_the_event_format_and_not_as_a_byte()
    {
        TestOutcome inEvent = Run(Opened + "\n" + """
            host send gp draws event slot_door_opened
            """);
        TestOutcome asByte = Run(Opened + "\n" + """
            host send gp draws bare slot_door_opened
            """);

        Assert.True(inEvent.Verdict == Verdict.Pass, Explain(inEvent));
        Assert.Equal(Verdict.Fail, asByte.Verdict);
    }

    [Fact]
    public void With_reporting_off_a_general_poll_draws_a_single_byte()
    {
        TestOutcome result = Run("""
            requires host.poll_sequence, stimulus.door.main
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host forget exceptions within 500ms
            host poll sequence
            egm door main open
            host send lp 1F
            host send gp draws bare slot_door_opened
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }

    [Fact]
    public void A_saved_jackpot_or_credit_limit_reports_operator_changed_options_once()
    {
        TestOutcome result = Run("""
            requires host.poll_sequence, stimulus.jackpot_limit, stimulus.credit_limit
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host forget exceptions within 500ms
            host poll sequence
            egm set jackpot limit 100000
            host send gp draws operator_changed_options
            host send lp 1F
            host send gp draws nothing
            egm set credit limit 100000
            host send lp 1F
            host send gp draws any
            host send lp 1F
            host send gp draws nothing
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }

    [Fact]
    public void An_identical_long_poll_repeated_with_reporting_off_acknowledges_nothing()
    {
        // The first identification poll acknowledges slot door opened; its repeat must not take slot door closed with it.
        TestOutcome result = Run("""
            requires host.poll_sequence, stimulus.door.main
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host forget exceptions within 500ms
            host poll sequence
            egm door main open
            egm door main close
            host send gp draws bare slot_door_opened
            host send lp 1F
            host send lp 1F
            host send gp draws bare slot_door_closed
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }

    [Fact]
    public void Or_silence_admits_no_answer()
    {
        TestOutcome result = Run("""
            requires host.poll_sequence
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host poll sequence
            egm set address 00
            host send gp draws event slot_door_opened or silence
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }

    [Theory]
    [InlineData("host send lp 1F draws slot_door_opened")]
    [InlineData("host send lp 1F draws bare slot_door_opened")]
    [InlineData("host send lp 1F draws event slot_door_opened to 02")]
    [InlineData("host send gp draws event silence")]
    [InlineData("host send gp draws event slot_door_opened or")]
    public void Malformed_draws_are_refused_when_read(string line)
    {
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse($"test \"x\" {{\n  {line}\n}}\n", "x.sas"));
    }

    [Fact]
    public void The_drawn_event_is_named_in_the_report()
    {
        ScriptTest test = ScriptParser.Parse("""
            test "x" {
              host send lp 1F draws event slot_door_opened or silence
              host send gp draws bare $11
            }
            """, "x.sas").Tests[0];

        Assert.Equal("send lp 1F draws event slot_door_opened or silence", test.Steps[0].Describe());
        Assert.Equal("send gp draws bare slot_door_opened", test.Steps[1].Describe());
    }

    [Fact]
    public void A_power_cycle_without_sync_leaves_the_machine_deaf_to_its_own_polls()
    {
        TestOutcome result = Run("""
            requires host.poll_sequence, machine.power_cycle
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host poll sequence
            egm power cycle without sync
            host send gp draws silence
            host send gp draws silence to 7F
            host send gp draws any
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }
}
