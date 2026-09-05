using SasWorkbench.Core.Scripting;

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
            egm press collect
            """);

        Assert.Equal(Verdict.Skip, result.Verdict);
        StepResult skipped = result.Steps.Single(s => s.Verdict == Verdict.Skip);
        Assert.Contains("stimulus.button.collect", skipped.Reason!);
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
        Assert.Equal("9210", failed.Actual);
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
