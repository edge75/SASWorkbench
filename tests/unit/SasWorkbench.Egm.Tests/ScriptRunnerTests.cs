using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The whole engine, end to end: a script is parsed, a link is opened, the machine is stimulated
/// and the claims are turned into verdicts. Both ends are our own code here, so what these prove
/// is that the layers agree with each other — which is what a thin vertical slice is for.
/// </summary>
public class ScriptRunnerTests
{
    private static ScriptRunner Runner(FakeEgmOptions? machine = null) => new(new ScriptRunnerOptions
    {
        Stimulus = StimulusBackend.Software,
        Environment = SoftwareMachineEnvironment.For(machine),
        PollInterval = TimeSpan.FromMilliseconds(20),

        // Generous on purpose: these tests are about what a script does, not about how fast a
        // machine answers, and a tight timeout turned a loaded build agent into a failing one.
        ResponseTimeout = TimeSpan.FromSeconds(5),
        QuietGap = TimeSpan.FromMilliseconds(15),
    });

    private static TestOutcome Run(string script, FakeEgmOptions? machine = null)
    {
        ScriptDocument document = ScriptParser.Parse(script, "runner.sas");
        return Runner(machine).Run(document.Tests[0]);
    }

    private static string Explain(TestOutcome result) =>
        string.Join('\n', result.Steps.Select(s => s.ToString()));

    /// <summary>Whether the machine performs funds transfer is asked of it, so a machine with it off skips the case.</summary>
    [Fact]
    public void Funds_transfer_is_read_off_the_machine_and_a_machine_without_it_skips()
    {
        const string script = """
            requires machine.aft
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host send lp 74 data FF000000
            host expect field available_transfers == $13
            """;

        TestOutcome on = Run(script);
        Assert.True(on.Verdict == Verdict.Pass, Explain(on));

        TestOutcome off = Run(script, new FakeEgmOptions { Aft = false });
        Assert.Equal(Verdict.Skip, off.Verdict);
        Assert.Contains("funds transfer off", off.SkipReason!, StringComparison.Ordinal);
    }

    /// <summary>The transfer limit is read off a $74 status read, so a machine with funds transfer off skips a case needing it.</summary>
    [Fact]
    public void The_transfer_limit_is_read_off_the_machine_and_a_machine_without_transfers_skips()
    {
        const string script = """
            requires machine.transfer_limit
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            egm set credit limit 100000
            egm bill insert 100
            host send lp 74 data FF000000
            host expect field gaming_machine_transfer_limit == 99900
            """;

        TestOutcome on = Run(script);
        Assert.True(on.Verdict == Verdict.Pass, Explain(on));

        TestOutcome off = Run(script, new FakeEgmOptions { Aft = false });
        Assert.Equal(Verdict.Skip, off.Verdict);
        Assert.StartsWith("machine.transfer_limit:", off.SkipReason!, StringComparison.Ordinal);
    }

    /// <summary>Each capability asked of the machine keeps its own reason, so the first unmet one is reported with its own.</summary>
    [Fact]
    public void A_requirement_the_machine_cannot_answer_skips_with_its_own_reason()
    {
        TestOutcome result = Run("""
            requires egm.validation == standard
            requires machine.aft
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            """, new FakeEgmOptions { UnansweredCommands = [0xA0] });

        Assert.Equal(Verdict.Skip, result.Verdict);
        Assert.StartsWith("egm.validation:", result.SkipReason!, StringComparison.Ordinal);
        Assert.DoesNotContain("funds transfer", result.SkipReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_that_comes_up_and_answers_passes()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host send lp 1F
            host expect field game_id == QX
            """);

        Assert.Equal(Verdict.Pass, result.Verdict);
        Assert.True(result.Assertions >= 3, Explain(result));
        Assert.Equal(0, result.Failed);
    }

    /// <summary>
    /// The shape the whole project exists for: stimulate the machine, then assert over the wire
    /// that the protocol reported it — both the exception and the meter.
    /// </summary>
    [Fact]
    public void A_note_produces_its_exception_and_moves_its_meter()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s

            egm bill insert 2000
            host expect exception bill_accepted_20 within 5s
            host expect meter total_bill_in += 20 within 2s
            """);

        Assert.Equal(Verdict.Pass, result.Verdict);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public void Playing_games_moves_the_game_meters()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s

            egm bill insert 2000
            egm play games=2 bet=5
            host expect meter games_played += 2 within 2s
            host expect meter current_credits == 200 within 2s
            """);

        Assert.Equal(Verdict.Pass, result.Verdict);
    }

    /// <summary>
    /// The baseline is taken again before each stimulus, so two relative assertions in one test
    /// each measure their own stimulus rather than the sum of everything before them.
    /// </summary>
    [Fact]
    public void A_relative_assertion_measures_the_stimulus_before_it_and_not_the_whole_test()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s

            egm bill insert 2000
            host expect meter total_bill_in += 20 within 2s

            egm bill insert 500
            host expect meter total_bill_in += 5 within 2s
            """);

        Assert.Equal(Verdict.Pass, result.Verdict);
    }

    /// <summary>
    /// The unit the machine publishes is the unit it counts in. A twenty-dollar note on a machine
    /// accounting in five cents is four hundred accounting units, which is what the real machine
    /// was measured reporting; a fixture that published five cents and counted in dollars would
    /// disagree with it by a factor of twenty while every reply still looked well formed.
    /// </summary>
    [Fact]
    public void The_machine_counts_money_in_the_denomination_it_publishes()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s

            host send lp 1F
            host expect field denomination == 2

            egm bill insert 2000
            host expect meter credits += 400 within 2s
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }

    /// <summary>
    /// The bill-value meter is counted in whole currency units, so it reads the same on a machine
    /// accounting in ten cents as on one accounting in one. The credit meter beside it does move,
    /// which is what makes the pair worth asserting together: one unit is fixed and one is not.
    /// </summary>
    [Fact]
    public void The_bill_value_meter_does_not_move_with_the_accounting_denomination()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s

            egm bill insert 2000
            host expect meter total_bill_in += 20 within 2s
            host expect meter credits += 200 within 2s
            """, new FakeEgmOptions { AccountingDenomination = 0x03 });

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }

    /// <summary>
    /// A long poll that carries data is answered once, and the assertions that follow read that
    /// answer. Asking again without the data is a different question - the machine rejects it -
    /// so a runner that re-polled would make every selected-meter case unassertable.
    /// </summary>
    [Fact]
    public void A_field_assertion_reads_the_reply_its_send_obtained()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s

            host send lp 6F data 0400000B00
            host expect field meters[0].code == 11
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }

    /// <summary>
    /// And it stops reading it once the machine has been acted on: a reply captured before a note
    /// went in describes the machine as it was, so asserting on it afterwards would pass or fail
    /// for a reason that has nothing to do with the note.
    /// </summary>
    [Fact]
    public void A_captured_reply_is_not_reused_after_the_machine_is_stimulated()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s

            host send lp 6F data 0400000B00
            egm bill insert 2000
            host expect field meters[0].code == 11
            """);

        Assert.NotEqual(Verdict.Pass, result.Verdict);
    }

    [Fact]
    public void A_meter_that_did_not_move_the_expected_amount_fails_and_says_both_numbers()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s

            egm bill insert 500
            host expect meter total_bill_in += 2000 within 500ms
            """);

        Assert.Equal(Verdict.Fail, result.Verdict);
        StepResult failed = result.Steps.Single(s => s.Verdict == Verdict.Fail);
        Assert.Contains("2000", failed.Expected!);

        // A five-dollar note is five credits, and the point of the message is that it says so
        // rather than only saying that the assertion did not hold.
        Assert.Contains("5", failed.Actual!);
    }

    [Fact]
    public void An_exception_that_never_arrives_fails_rather_than_hanging()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host expect exception slot_door_opened within 300ms
            """);

        Assert.Equal(Verdict.Fail, result.Verdict);
        Assert.Contains("nothing in", result.Steps.Single(s => s.Verdict == Verdict.Fail).Actual!);
    }

    [Fact]
    public void An_exception_expected_not_to_arrive_passes_when_it_does_not()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host expect no exception slot_door_opened within 300ms
            """);

        Assert.Equal(Verdict.Pass, result.Verdict);
    }

    [Fact]
    public void An_exception_expected_not_to_arrive_fails_when_it_does()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            egm door slot open
            host expect no exception slot_door_opened within 1s
            """);

        Assert.Equal(Verdict.Fail, result.Verdict);
    }

    /// <summary>
    /// Setting a machine up raises exceptions the test is not about — shutting a machine's doors
    /// raises a pair for each one — and they are still unclaimed when the assertions begin. This
    /// is how a script says which part of the traffic it is asserting on.
    /// </summary>
    [Fact]
    public void Forgetting_leaves_earlier_exceptions_out_of_what_follows()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            egm door slot open
            host wait 200ms
            host forget exceptions
            egm door cashbox open
            host expect exception cashbox_door_opened within 1s
            host expect no exception slot_door_opened within 300ms
            """);

        Assert.Equal(Verdict.Pass, result.Verdict);
    }

    /// <summary>
    /// Without the forgetting, the same script fails — and the failure has to say why, because
    /// "the slot door opened" is true and unhelpful on its own.
    /// </summary>
    [Fact]
    public void A_negated_expectation_caught_by_an_earlier_step_says_where_it_came_from()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            egm door slot open
            host wait 200ms
            egm door cashbox open
            host expect exception cashbox_door_opened within 1s
            host expect no exception slot_door_opened within 300ms
            """);

        Assert.Equal(Verdict.Fail, result.Verdict);
        StepResult failed = result.Steps.Single(s => s.Verdict == Verdict.Fail);
        Assert.Contains("forget exceptions", failed.Reason ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// One door opening must not satisfy two assertions. Without consuming each match, a single
    /// event would make every assertion in the file that mentioned it pass.
    /// </summary>
    [Fact]
    public void One_exception_satisfies_only_one_assertion()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s

            egm door slot open
            host expect exception slot_door_opened within 2s
            host expect exception slot_door_opened within 300ms
            """);

        Assert.Equal(Verdict.Fail, result.Verdict);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public void Two_occurrences_satisfy_two_assertions()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s

            egm door slot open
            egm door slot close
            host expect exception slot_door_opened within 2s
            host expect exception slot_door_closed within 2s
            """);

        Assert.Equal(Verdict.Pass, result.Verdict);
    }

    /// <summary>
    /// An unmet requirement is a skip with a reason, never a failure. Failing a test because the
    /// environment cannot run it teaches people that red means nothing.
    /// </summary>
    [Fact]
    public void An_unmet_requirement_skips_the_test_and_names_what_was_missing()
    {
        TestOutcome result = Run("""
            requires printer
            host connect loopback address 01
            host expect link up within 2s
            """);

        Assert.Equal(Verdict.Skip, result.Verdict);
        Assert.Contains("printer", result.SkipReason!);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public void A_requirement_with_the_wrong_value_skips_and_says_both_values()
    {
        TestOutcome result = Run("""
            requires stimulus == qpm
            host connect loopback address 01
            host expect link up within 2s
            """);

        Assert.Equal(Verdict.Skip, result.Verdict);
        Assert.Contains("software", result.SkipReason!);
        Assert.Contains("qpm", result.SkipReason!);
    }

    [Fact]
    public void A_requirement_the_environment_meets_lets_the_test_run()
    {
        TestOutcome result = Run("""
            requires stimulus.bill, decoder.lp1F
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            """);

        Assert.Equal(Verdict.Pass, result.Verdict);
    }

    /// <summary>
    /// What the machine is gets asked of the machine, over the link, and answered from its reply.
    /// </summary>
    /// <remarks>
    /// The point of asking rather than being told is that the same question reaches both peers the
    /// same way. A capability published from this fixture's own configuration would agree with the
    /// fixture by construction and could never disagree with a real machine, which is the one
    /// thing running a script against both peers is for.
    /// </remarks>
    [Theory]
    [InlineData(SasCashoutValidation.Standard, "standard")]
    [InlineData(SasCashoutValidation.SecureEnhanced, "secure_enhanced")]
    [InlineData(SasCashoutValidation.System, "system")]
    public void The_validation_style_a_case_requires_is_read_off_the_wire(
        SasCashoutValidation configured, string named)
    {
        TestOutcome result = Run($"""
            requires egm.validation == {named}
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            """,
            new FakeEgmOptions { CashoutValidation = configured });

        Assert.Equal(Verdict.Pass, result.Verdict);
    }

    [Fact]
    public void A_machine_in_another_validation_style_skips_the_case_and_names_the_style_it_is_in()
    {
        TestOutcome result = Run("""
            requires egm.validation == standard
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            """,
            new FakeEgmOptions { CashoutValidation = SasCashoutValidation.SecureEnhanced });

        Assert.Equal(Verdict.Skip, result.Verdict);
        Assert.Contains("secure_enhanced", result.SkipReason!);
        Assert.Equal(0, result.Failed);
    }

    /// <summary>
    /// A machine that will not answer the question leaves the case skipped saying so.
    /// </summary>
    /// <remarks>
    /// Never a default. A machine on fresh memory is in the standard style, so guessing standard
    /// would be right often enough to be trusted and wrong on exactly the machines a ticket case
    /// is run against.
    /// </remarks>
    [Fact]
    public void A_machine_that_does_not_answer_the_enabled_features_poll_skips_the_case()
    {
        TestOutcome result = Run("""
            requires egm.validation == standard
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            """,
            new FakeEgmOptions { UnansweredCommands = [0xA0] });

        Assert.Equal(Verdict.Skip, result.Verdict);
        Assert.Contains("enabled-features", result.SkipReason!);
        Assert.DoesNotContain("standard", result.SkipReason!);
    }

    /// <summary>
    /// A send with a condition goes out only to a machine that says it meets it.
    /// </summary>
    /// <remarks>
    /// The validation identifier is the case this exists for: a machine in secure enhanced
    /// validation keeps its acceptor off until a host sets one, and every other machine answers the
    /// command with silence, so a ticket case can neither always send it nor never send it.
    /// </remarks>
    [Fact]
    public void A_conditional_send_reaches_a_machine_that_meets_the_condition()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
            """,
            new FakeEgmOptions { CashoutValidation = SasCashoutValidation.SecureEnhanced });

        Assert.Equal(Verdict.Pass, result.Verdict);
        StepResult sent = result.Steps.Single(s => s.Description.StartsWith("send lp 4C", StringComparison.Ordinal));
        Assert.True(sent.IsAssertion);
        Assert.Contains("Answered", sent.Actual!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A machine that answers otherwise is not sent the command, and not sending it is no claim.
    /// </summary>
    /// <remarks>
    /// Sent anyway, the standard machine would answer with silence and fail a case that is about
    /// something else. Counted as a pass, it would make a case with nothing else in it look green.
    /// </remarks>
    [Fact]
    public void A_conditional_send_is_not_sent_to_a_machine_that_answers_otherwise_and_claims_nothing()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
            """);

        Assert.Equal(Verdict.Skip, result.Verdict);
        StepResult held = result.Steps.Single(s => s.Description.StartsWith("send lp 4C", StringComparison.Ordinal));
        Assert.Equal(Verdict.Pass, held.Verdict);
        Assert.False(held.IsAssertion);
        Assert.Contains("not sent", held.Actual!, StringComparison.Ordinal);
        Assert.Contains("standard", held.Actual!, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Wire, w => w.ToHex().StartsWith("1.01 0.4C", StringComparison.Ordinal));
    }

    /// <summary>
    /// A machine that cannot be asked fails the step rather than having the command withheld.
    /// </summary>
    [Fact]
    public void A_conditional_send_fails_when_the_machine_cannot_say_whether_it_meets_the_condition()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
            """,
            new FakeEgmOptions { UnansweredCommands = [0xA0] });

        Assert.Equal(Verdict.Fail, result.Verdict);
        StepResult failed = result.Steps.Single(s => s.Verdict == Verdict.Fail);
        Assert.Contains("enabled-features", failed.Reason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A stimulus this backend cannot carry out is a skip, and the assertion that would have
    /// depended on it must not be allowed to pass on the machine's earlier state.
    /// </summary>
    [Fact]
    public void A_stimulus_the_backend_cannot_do_skips_the_test_rather_than_passing_it()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            egm press bet
            """);

        Assert.Equal(Verdict.Skip, result.Verdict);
        StepResult skipped = result.Steps.Single(s => s.Verdict == Verdict.Skip);
        Assert.Contains("stimulus.button.bet", skipped.Reason!);
    }

    /// <summary>
    /// A test that never made a claim has not passed. Reporting it green is the one outcome this
    /// project treats as worse than a failure.
    /// </summary>
    [Fact]
    public void A_test_that_asserts_nothing_is_a_skip_and_not_a_pass()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            note "nothing is checked here"
            """);

        Assert.Equal(Verdict.Skip, result.Verdict);
    }

    [Fact]
    public void A_test_with_no_link_is_an_error_and_says_what_is_missing()
    {
        ScriptDocument document = ScriptParser.Parse("""
            test "no link" {
              note "this test forgot to connect"
              egm settle
            }
            """, "noconnect.sas");

        TestOutcome result = Runner().Run(document.Tests[0]);

        Assert.Equal(Verdict.Error, result.Verdict);
        Assert.Contains("host connect", result.SkipReason!);
    }

    [Fact]
    public void A_field_that_disagrees_fails_and_prints_both_values()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect field base_percentage of lp 1F == 8000
            """);

        Assert.Equal(Verdict.Fail, result.Verdict);
        StepResult failed = result.Steps.Single(s => s.Verdict == Verdict.Fail);
        Assert.Equal("8000", failed.Expected);
        Assert.Equal("9211", failed.Actual);
    }

    [Fact]
    public void A_machine_configured_differently_is_asserted_against_its_own_configuration()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect field base_percentage of lp 1F == 8750
            host expect field serial_number of lp 54 == QX-RIG-7
            """, new FakeEgmOptions { BasePercentage = 8750, SerialNumber = "QX-RIG-7" });

        Assert.Equal(Verdict.Pass, result.Verdict);
    }

    [Fact]
    public void The_wire_log_is_kept_so_a_failure_can_be_read_back_byte_by_byte()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host send lp 1F
            """);

        Assert.NotEmpty(result.Wire);
        Assert.Contains(result.Wire, r => r.ToHex() == "1.80");
        Assert.Contains(result.Wire, r => r.ToHex() == "1.01 0.1F");
    }

    [Fact]
    public void Every_step_result_names_the_line_it_came_from()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            """);

        Assert.Equal(1, result.Steps[0].Location.Line);
        Assert.Equal(3, result.Steps[2].Location.Line);
    }

    /// <summary>
    /// A serial port with no device behind it takes no data, and the write times out. That used
    /// to end sasctl with a stack trace in the middle of a run. The step that needed the link now
    /// fails, and its reason names the link and what went wrong with it.
    /// </summary>
    [Fact]
    public void A_link_that_stops_taking_messages_fails_the_step_with_the_reason()
    {
        TestOutcome result = RunOver(inner => new RefusingLine(inner), """
            host connect loopback address 01
            host poll every 20ms
            host send lp 1F
            """);

        StepResult send = result.Steps[2];
        Assert.Equal(Verdict.Fail, send.Verdict);
        Assert.Contains("stopped taking messages: The write timed out.", send.Reason);
        Assert.Equal(Verdict.Fail, result.Verdict);
    }

    /// <summary>
    /// The round's cycle on the line is held here for longer than the sequence will wait for it.
    /// Its general poll can still draw what the case sends for once the sequence has begun, so
    /// the step that began it fails and says why, instead of the case running a sequence that is
    /// not the one it describes.
    /// </summary>
    [Fact]
    public void A_sequence_that_cannot_wait_out_the_cycle_on_the_line_fails_and_says_why()
    {
        TestOutcome result = RunOver(inner => new HeldAfterReplyLine(inner, TimeSpan.FromSeconds(5)), """
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host wait 100ms
            host poll sequence
            """, responseTimeout: TimeSpan.FromSeconds(1));

        StepResult sequence = result.Steps[4];
        Assert.Equal(Verdict.Fail, sequence.Verdict);
        Assert.Contains("still on the line", sequence.Actual);
        Assert.Contains("general poll can still draw", sequence.Reason);
        Assert.Equal(Verdict.Fail, result.Verdict);
    }

    private static TestOutcome RunOver(
        Func<ISasTransport, ISasTransport> wrap, string script, TimeSpan? responseTimeout = null)
    {
        EnvironmentFactory software = SoftwareMachineEnvironment.For();
        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = (step, options, declared, channels, validation, progressive, authentication, multiplied, handpay) =>
            {
                ScriptEnvironment environment = software(step, options, declared, channels, validation, progressive, authentication, multiplied, handpay);
                return environment with { HostTransport = wrap(environment.HostTransport) };
            },
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = responseTimeout ?? TimeSpan.FromSeconds(5),
            QuietGap = TimeSpan.FromMilliseconds(15),
        });

        return runner.Run(ScriptParser.Parse(script, "runner.sas").Tests[0]);
    }

    /// <summary>Refuses every write, the way a port with no device behind it does.</summary>
    private sealed class RefusingLine(ISasTransport inner) : ISasTransport
    {
        public string Description => inner.Description;

        public TransportCapabilities Capabilities => inner.Capabilities;

        public bool IsConnected => inner.IsConnected;

        public void Open() => inner.Open();

        public void Close() => inner.Close();

        public void Write(ReadOnlySpan<SasSymbol> symbols) =>
            throw new TimeoutException("The write timed out.");

        public int Read(Span<SasSymbol> destination, TimeSpan timeout) => inner.Read(destination, timeout);

        public void Dispose() => inner.Dispose();
    }

    /// <summary>
    /// Holds the first synchronisation poll sent after the machine has answered, which puts a
    /// timed cycle on the line once the link is up and keeps it there for <paramref name="hold"/>.
    /// </summary>
    private sealed class HeldAfterReplyLine(ISasTransport inner, TimeSpan hold) : ISasTransport
    {
        private readonly ManualResetEventSlim _released = new(false);
        private volatile bool _heard;
        private int _holds = 1;

        public string Description => inner.Description;

        public TransportCapabilities Capabilities => inner.Capabilities;

        public bool IsConnected => inner.IsConnected;

        public void Open() => inner.Open();

        public void Close() => inner.Close();

        public void Write(ReadOnlySpan<SasSymbol> symbols)
        {
            bool sync = symbols.Length == 1 && symbols[0].Wakeup && symbols[0].Data == 0x80;
            if (sync && _heard && Interlocked.Exchange(ref _holds, 0) == 1)
                _released.Wait(hold);

            inner.Write(symbols);
        }

        public int Read(Span<SasSymbol> destination, TimeSpan timeout)
        {
            int read = inner.Read(destination, timeout);
            if (read > 0)
                _heard = true;

            return read;
        }

        public void Dispose()
        {
            _released.Set();
            inner.Dispose();
        }
    }

    /// <summary>
    /// A warm-up is paid once per machine in a run: a machine just started reports its start-up
    /// exceptions while the first case waits, and every later case finds it already warm. Without
    /// that, a suite of nineteen sequenced cases would spend six minutes waiting.
    /// </summary>
    [Fact]
    public void A_warm_up_waits_in_the_first_case_of_a_run_only()
    {
        ScriptDocument document = ScriptParser.Parse("""
            test "first" {
              host connect loopback address 01
              host poll every 20ms
              host expect link up within 2s
              host warm up 600ms
            }
            test "second" {
              host connect loopback address 01
              host poll every 20ms
              host expect link up within 2s
              host warm up 600ms
            }
            """, "warm.sas");

        RunReport report = Runner().RunAll([document], TestContext.Current.CancellationToken);

        StepResult first = report.Tests[0].Steps.Single(s => s.Description.StartsWith("warm up"));
        StepResult second = report.Tests[1].Steps.Single(s => s.Description.StartsWith("warm up"));
        Assert.Equal(Verdict.Pass, first.Verdict);
        Assert.StartsWith("waited", first.Actual);
        Assert.True(report.Tests[0].Duration >= TimeSpan.FromMilliseconds(500), Explain(report.Tests[0]));
        Assert.Equal(Verdict.Pass, second.Verdict);
        Assert.StartsWith("no wait", second.Actual);
    }

    [Fact]
    public void A_warm_up_without_a_duration_is_refused()
    {
        ScriptParseException error = Assert.Throws<ScriptParseException>(() => ScriptParser.Parse("""
            host connect loopback address 01
            host warm up
            """, "warm.sas"));
        Assert.Contains("host warm up <duration>", error.Message);
    }
}

public class RunReportTests
{
    private static RunReport RunAll(string script)
    {
        ScriptDocument document = ScriptParser.Parse(script, "suite.sas");
        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(),
            PollInterval = TimeSpan.FromMilliseconds(20),
            QuietGap = TimeSpan.FromMilliseconds(15),
        });

        return runner.RunAll([document]);
    }

    [Fact]
    public void Several_tests_in_one_file_each_get_their_own_verdict()
    {
        RunReport report = RunAll("""
            test "the link comes up" {
              host connect loopback address 01
              host poll every 20ms
              host expect link up within 2s
            }

            test "this one needs a printer" {
              requires printer
              host connect loopback address 01
              host expect link up within 2s
            }

            test "this one is wrong" {
              host connect loopback address 01
              host poll every 20ms
              host expect field base_percentage of lp 1F == 1
            }
            """);

        Assert.Equal(3, report.Tests.Count);
        Assert.Equal(1, report.Passed);
        Assert.Equal(1, report.Skipped);
        Assert.Equal(1, report.Failed);
        Assert.Equal(Verdict.Fail, report.Verdict);
        Assert.Equal(1, report.ExitCode);
    }

    /// <summary>
    /// A run in which nothing could be tested must not look like a run that succeeded, and a
    /// build should be able to tell the difference from the exit code alone.
    /// </summary>
    [Fact]
    public void A_run_where_everything_was_skipped_is_not_a_success()
    {
        RunReport report = RunAll("""
            test "needs a printer" {
              requires printer
              host connect loopback address 01
              host expect link up within 2s
            }
            """);

        Assert.Equal(Verdict.Skip, report.Verdict);
        Assert.Equal(3, report.ExitCode);
        Assert.Contains("1 skipped", report.Summary());
    }

    /// <summary>
    /// Honest labelling: the report has to say what the run was carried over, because a pass on a
    /// link between two pieces of our own code is not the same claim as a pass against hardware.
    /// </summary>
    [Fact]
    public void The_report_says_what_the_run_was_carried_over()
    {
        RunReport report = RunAll("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            """);

        RunProvenance provenance = Assert.Single(report.Provenance);
        Assert.Contains("loopback", provenance.Transport);
        Assert.Equal(StimulusBackend.Software, provenance.Stimulus);
        Assert.NotNull(provenance.Caveat);
        Assert.Contains("decoder.lp1F", report.Capabilities);
    }
}

public class YamlScriptTests
{
    /// <summary>
    /// The same test in both forms must come to the same verdict. Two notations for the same
    /// language only earn their keep if neither can drift from the other.
    /// </summary>
    [Fact]
    public void The_same_test_written_as_yaml_and_as_script_agree()
    {
        const string script = """
            requires stimulus.bill
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            egm bill insert 2000
            host expect exception bill_accepted_20 within 5s
            host expect meter total_bill_in += 20 within 2s
            """;

        const string yaml = """
            name: a note is accepted
            requires:
              - stimulus.bill
            steps:
              - host connect loopback address 01
              - host poll every 20ms
              - host expect link up within 2s
              - egm bill insert 2000
              - host expect exception bill_accepted_20 within 5s
              - host expect meter total_bill_in += 20 within 2s
            """;

        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(),
            PollInterval = TimeSpan.FromMilliseconds(20),
            QuietGap = TimeSpan.FromMilliseconds(15),
        });

        TestOutcome fromScript = runner.Run(ScriptParser.Parse(script, "a.sas").Tests[0]);
        TestOutcome fromYaml = runner.Run(YamlScriptReader.Parse(yaml, "a.yaml").Tests[0]);

        Assert.Equal(Verdict.Pass, fromScript.Verdict);
        Assert.Equal(Verdict.Pass, fromYaml.Verdict);
        Assert.Equal(fromScript.Assertions, fromYaml.Assertions);
    }

    [Fact]
    public void A_yaml_document_carries_the_name_requirements_and_tags()
    {
        ScriptDocument document = YamlScriptReader.Parse("""
            tests:
              - name: first
                tags: [slow, "@destructive"]
                requires: [printer, "egm.validation == secure_enhanced"]
                steps:
                  - host connect loopback address 01
              - name: second
                steps:
                  - host connect loopback address 01
            """, "two.yaml");

        Assert.Equal(2, document.Tests.Count);
        Assert.Equal("first", document.Tests[0].Name);
        Assert.Equal<string[]>(["slow", "destructive"], document.Tests[0].Tags.ToArray());
        Assert.Equal(2, document.Tests[0].Requirements.Count);
        Assert.Equal("secure_enhanced", document.Tests[0].Requirements[1].ExpectedValue);
    }

    /// <summary>
    /// A step is a line of the same language, so a mistake in one produces the same message it
    /// would in the other — pointed at the line of the YAML file the person actually edited.
    /// </summary>
    [Fact]
    public void A_bad_step_in_yaml_reports_the_yaml_line()
    {
        ScriptParseException error = Assert.Throws<ScriptParseException>(() =>
            YamlScriptReader.Parse("""
                name: broken
                steps:
                  - host connect loopback address 01
                  - host expect exception slot_door_ajar
                """, "broken.yaml"));

        Assert.Equal("broken.yaml", error.Location.File);
        Assert.Contains("slot_door_ajar", error.Reason);
    }

    [Fact]
    public void A_yaml_test_with_no_steps_is_refused()
    {
        ScriptParseException error = Assert.Throws<ScriptParseException>(() =>
            YamlScriptReader.Parse("name: empty\n", "empty.yaml"));

        Assert.Contains("no 'steps'", error.Reason);
    }

    [Fact]
    public void Text_that_is_not_yaml_is_refused_with_its_line()
    {
        Assert.Throws<ScriptParseException>(() =>
            YamlScriptReader.Parse("steps: [\n  - unterminated\n", "bad.yaml"));
    }
}
