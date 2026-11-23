using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

public class ScriptParserTests
{
    private static ScriptTest ParseOne(string text)
    {
        ScriptDocument document = ScriptParser.Parse(text, "test.sas");
        return Assert.Single(document.Tests);
    }

    [Fact]
    public void A_file_with_no_test_block_is_one_test_named_after_the_file()
    {
        ScriptTest test = ParseOne("""
            host connect loopback address 01
            host expect link up
            """);

        Assert.Equal("test", test.Name);
        Assert.Equal(2, test.Steps.Count);
    }

    [Fact]
    public void A_test_block_names_the_test()
    {
        ScriptTest test = ParseOne("""
            test "10.1.1 Standard Validation" {
              host connect loopback address 01
              host expect link up
            }
            """);

        Assert.Equal("10.1.1 Standard Validation", test.Name);
    }

    [Fact]
    public void Comments_and_blank_lines_are_ignored()
    {
        ScriptTest test = ParseOne("""
            # what this test is for

            host connect loopback address 01   # the machine under test

            host expect link up
            """);

        Assert.Equal(2, test.Steps.Count);
    }

    [Fact]
    public void Requirements_are_read_with_or_without_commas()
    {
        ScriptTest test = ParseOne("""
            requires stimulus.bill, printer
            requires egm.validation == secure_enhanced
            host connect loopback address 01
            """);

        Assert.Equal(3, test.Requirements.Count);
        Assert.Equal("stimulus.bill", test.Requirements[0].Key);
        Assert.Equal("printer", test.Requirements[1].Key);
        Assert.Equal("egm.validation", test.Requirements[2].Key);
        Assert.Equal("secure_enhanced", test.Requirements[2].ExpectedValue);
    }

    [Fact]
    public void Tags_are_read_with_or_without_the_marker()
    {
        ScriptTest test = ParseOne("""
            tag @slow @needs-stimulus
            host connect loopback address 01
            """);

        Assert.Equal<string[]>(["slow", "needs-stimulus"], test.Tags.ToArray());
    }

    [Fact]
    public void A_connect_step_reads_transport_target_address_and_channel()
    {
        ScriptTest test = ParseOne("host connect tcp 127.0.0.1:7200 address 1F channel 2");

        var connect = Assert.IsType<ConnectStep>(test.Steps[0]);
        Assert.Equal(ScriptTransport.Tcp, connect.Transport);
        Assert.Equal("127.0.0.1:7200", connect.Target);
        Assert.Equal(0x1F, connect.Address.Value);
        Assert.Equal(SasChannel.Two, connect.Channel);
    }

    [Theory]
    [InlineData("200ms", 200)]
    [InlineData("5s", 5000)]
    [InlineData("2m", 120_000)]
    [InlineData("1500", 1500)]
    public void Durations_are_read_in_the_forms_people_write(string text, int milliseconds)
    {
        TimeSpan duration = ScriptParser.ParseDuration(text, SourceLocation.None);

        Assert.Equal(milliseconds, duration.TotalMilliseconds);
    }

    [Fact]
    public void Exceptions_may_be_named_or_written_as_a_code()
    {
        ScriptTest test = ParseOne("""
            host connect loopback address 01
            host expect exception slot_door_opened within 5s
            host expect exception $4A
            host expect exception bill_accepted_20
            """);

        var byName = Assert.IsType<ExpectExceptionStep>(test.Steps[1]);
        var byCode = Assert.IsType<ExpectExceptionStep>(test.Steps[2]);
        var byAlias = Assert.IsType<ExpectExceptionStep>(test.Steps[3]);

        Assert.Equal(0x11, byName.Code);
        Assert.Equal(0x4A, byCode.Code);
        Assert.Equal(0x4A, byAlias.Code);
        Assert.Equal(TimeSpan.FromSeconds(5), byName.Within);
    }

    [Fact]
    public void A_negated_exception_assertion_is_read_as_one()
    {
        ScriptTest test = ParseOne("""
            host connect loopback address 01
            host expect no exception slot_door_opened within 2s
            """);

        var step = Assert.IsType<ExpectExceptionStep>(test.Steps[1]);
        Assert.True(step.Negated);
        Assert.Equal(0x11, step.Code);
    }

    [Fact]
    public void Settling_the_machine_is_a_step_of_its_own()
    {
        ScriptTest test = ParseOne("""
            host connect loopback address 01
            egm settle
            """);

        var step = Assert.IsType<SettleStep>(test.Steps[1]);
        Assert.Equal("stimulus.settle", step.RequiredCapability);
    }

    /// <summary>
    /// A condition closes a send of any form, after the machine it is sent to.
    /// </summary>
    [Fact]
    public void A_send_may_close_with_the_condition_the_machine_has_to_meet()
    {
        ScriptTest test = ParseOne("""
            host connect loopback address 01
            host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
            host send lp 1F to 01 when egm.validation == standard
            host send lp 1F
            """);

        var fields = Assert.IsType<SendLongPollStep>(test.Steps[1]);
        Assert.Equal(new Requirement(fields.Location, "egm.validation", "secure_enhanced"), fields.When);
        Assert.Equal(0x4C, fields.Command);
        Assert.Equal(
            "send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced",
            fields.Describe());

        var addressed = Assert.IsType<SendLongPollStep>(test.Steps[2]);
        Assert.Equal("standard", addressed.When!.ExpectedValue);
        Assert.Equal(SasAddress.Of(0x01), addressed.To);

        Assert.Null(Assert.IsType<SendLongPollStep>(test.Steps[3]).When);
    }

    [Theory]
    [InlineData("host send lp 1F when egm.validation")]
    [InlineData("host send lp 1F when egm.validation = standard")]
    [InlineData("host send lp 1F when egm.validation == standard extra")]
    public void A_condition_that_is_not_a_comparison_is_refused(string line)
    {
        ScriptParseException refused = Assert.Throws<ScriptParseException>(() => ParseOne($"""
            host connect loopback address 01
            {line}
            """));

        Assert.Contains("when <capability> == <value>", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_machine_action_nobody_implements_names_the_ones_that_exist()
    {
        ScriptParseException refused = Assert.Throws<ScriptParseException>(() => ParseOne("""
            host connect loopback address 01
            egm levitate
            """));

        Assert.Contains("settle", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Meter_comparisons_are_read_including_the_relative_ones()
    {
        ScriptTest test = ParseOne("""
            host connect loopback address 01
            host expect meter total_bill_in += 2000
            host expect meter games_played +>= 2
            host expect meter current_credits == 1800
            host expect meter games_won unchanged
            """);

        Assert.Equal(MeterComparison.IncreasedBy, Meter(test, 1).Comparison);
        Assert.Equal(2000, Meter(test, 1).Value);
        Assert.Equal(MeterComparison.IncreasedByAtLeast, Meter(test, 2).Comparison);
        Assert.Equal(MeterComparison.Equals, Meter(test, 3).Comparison);
        Assert.Equal(MeterComparison.Unchanged, Meter(test, 4).Comparison);
    }

    [Fact]
    public void A_field_assertion_binds_to_the_preceding_send()
    {
        ScriptTest test = ParseOne("""
            host connect loopback address 01
            host send lp 1F
            host expect field game_id == QX
            """);

        var step = Assert.IsType<ExpectFieldStep>(test.Steps[2]);
        Assert.Equal(0x1F, step.Command);
        Assert.Equal("QX", step.ExpectedText);
    }

    [Fact]
    public void A_field_assertion_may_name_its_own_long_poll()
    {
        ScriptTest test = ParseOne("""
            host connect loopback address 01
            host expect field base_percentage of lp 1F >= 9000
            """);

        var step = Assert.IsType<ExpectFieldStep>(test.Steps[1]);
        Assert.Equal(0x1F, step.Command);
        Assert.Equal(SasComparison.AtLeast, step.Comparison);
        Assert.Equal(9000, step.ExpectedNumber);
    }

    [Fact]
    public void The_long_poll_must_be_named_before_the_comparison_not_after()
    {
        // 'of lp' after the comparison reads as a value, which is not a number, so it is refused.
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse("""
            host connect loopback address 01
            host expect field base_percentage >= 9000 of lp 1F
            """, "order.sas"));
    }

    [Fact]
    public void Machine_steps_are_read()
    {
        ScriptTest test = ParseOne("""
            host connect loopback address 01
            egm bill insert 2000
            egm play games=2 bet=100 win=50
            egm door slot open
            egm press collect
            """);

        Assert.Equal(2000, Assert.IsType<InsertBillStep>(test.Steps[1]).Cents);
        var play = Assert.IsType<PlayGamesStep>(test.Steps[2]);
        Assert.Equal(2, play.Games);
        Assert.Equal(100, play.BetCredits);
        Assert.Equal(50, play.WinCredits);
        Assert.True(Assert.IsType<DoorStep>(test.Steps[3]).Open);
        Assert.Equal("collect", Assert.IsType<PressButtonStep>(test.Steps[4]).Button);
    }

    [Fact]
    public void Watched_meters_are_gathered_so_a_baseline_can_be_taken_for_all_of_them()
    {
        ScriptTest test = ParseOne("""
            host connect loopback address 01
            host expect meter total_bill_in += 2000
            host expect meter games_played += 2
            host expect meter total_bill_in += 2000
            """);

        Assert.Equal(2, test.WatchedMeters.Count);
    }

    private static ExpectMeterStep Meter(ScriptTest test, int index) =>
        Assert.IsType<ExpectMeterStep>(test.Steps[index]);
}

/// <summary>
/// Every one of these is a script that must not run. A step the parser did not understand and
/// silently dropped would be a test that passes without testing anything, so each of these
/// checks both that it is refused and that the message says where and what was expected.
/// </summary>
public class ScriptParserRefusalTests
{
    private static ScriptParseException Refuse(string text) =>
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse(text, "bad.sas"));

    [Fact]
    public void An_unknown_statement_is_refused_and_the_alternatives_are_listed()
    {
        ScriptParseException error = Refuse("""
            host connect loopback address 01
            frobnicate the widget
            """);

        Assert.Equal(2, error.Location.Line);
        Assert.Contains("frobnicate", error.Reason);
        Assert.Contains("host", error.Reason);
    }

    [Fact]
    public void An_unknown_host_action_is_refused()
    {
        ScriptParseException error = Refuse("host teleport now");

        Assert.Contains("teleport", error.Reason);
        Assert.Contains("expect", error.Reason);
    }

    [Fact]
    public void A_misspelled_exception_is_refused_before_a_link_is_opened()
    {
        ScriptParseException error = Refuse("""
            host connect loopback address 01
            host expect exception slot_door_ajar
            """);

        Assert.Equal(2, error.Location.Line);
        Assert.Contains("slot_door_ajar", error.Reason);
    }

    /// <summary>
    /// A meter this build cannot read must be refused at parse time. Accepting it and skipping at
    /// run time would hide a typo behind a skip, which reads as "the environment could not do it"
    /// rather than "you spelled it wrong".
    /// </summary>
    [Fact]
    public void A_meter_this_build_cannot_read_is_refused_and_the_readable_ones_are_listed()
    {
        ScriptParseException error = Refuse("""
            host connect loopback address 01
            host expect meter total_widgets += 1
            """);

        Assert.Contains("total_widgets", error.Reason);
        Assert.Contains("total_coin_in", error.Reason);
    }

    [Fact]
    public void A_connect_step_without_an_address_is_refused_with_the_reason_why()
    {
        ScriptParseException error = Refuse("host connect tcp 127.0.0.1:7200");

        Assert.Contains("address", error.Reason);
        Assert.Contains("silence", error.Reason);
    }

    [Fact]
    public void An_endpoint_that_is_not_an_endpoint_is_refused()
    {
        ScriptParseException error = Refuse("host connect tcp not-a-host address 01");

        Assert.Contains("not-a-host", error.Reason);
    }

    [Fact]
    public void A_field_assertion_with_nothing_to_assert_on_is_refused()
    {
        ScriptParseException error = Refuse("""
            host connect loopback address 01
            host expect field game_id == QX
            """);

        Assert.Contains("no reply", error.Reason);
    }

    [Fact]
    public void A_field_that_the_reply_does_not_hold_is_refused_and_the_real_fields_are_listed()
    {
        ScriptParseException error = Refuse("""
            host connect loopback address 01
            host expect field sprocket_count of lp 1F == 3
            """);

        Assert.Contains("sprocket_count", error.Reason);
        Assert.Contains("game_id", error.Reason);
    }

    /// <summary>
    /// A command whose reply this build cannot decode is a missing capability, not a mistake in
    /// the script, so it parses and the runner skips it with that reason. This is what lets a
    /// conformance test be written against a command before its layout exists, and still report
    /// honestly in the meantime rather than being deleted or quietly passing.
    /// </summary>
    [Fact]
    public void A_field_of_a_reply_this_build_cannot_decode_parses_and_is_left_to_the_runner()
    {
        ScriptDocument document = ScriptParser.Parse("""
            host connect loopback address 01
            host expect field amount of lp 75 == 100
            """, "future.sas");

        var step = Assert.IsType<ExpectFieldStep>(document.Tests[0].Steps[1]);
        Assert.Equal(0x75, step.Command);
        Assert.True(SasLayouts.IsKnownButUndecoded(step.Command));
    }

    [Fact]
    public void A_field_of_a_command_that_does_not_exist_at_all_is_still_refused()
    {
        Assert.Contains("not one the gaming machine library implements", Refuse("""
            host connect loopback address 01
            host expect field amount of lp FE == 100
            """).Reason);
    }

    [Fact]
    public void An_unclosed_test_block_is_refused_and_points_at_the_opening_line()
    {
        ScriptParseException error = Refuse("""
            test "unfinished" {
              host connect loopback address 01
            """);

        Assert.Equal(1, error.Location.Line);
        Assert.Contains("never closed", error.Reason);
    }

    [Fact]
    public void A_stray_closing_brace_is_refused()
    {
        Assert.Contains("no open", Refuse("}").Reason);
    }

    [Fact]
    public void An_empty_script_is_refused_rather_than_treated_as_a_passing_test()
    {
        Assert.Contains("no steps", Refuse("# nothing but a comment\n").Reason);
    }

    [Fact]
    public void A_duration_that_is_not_a_duration_is_refused()
    {
        Assert.Contains("soon", Refuse("host poll every soon").Reason);
    }

    [Fact]
    public void Hex_data_with_an_odd_number_of_digits_is_refused()
    {
        Assert.Contains("even", Refuse("host send lp 08 data 010").Reason);
    }

    [Fact]
    public void A_long_poll_the_library_does_not_implement_is_refused()
    {
        Assert.Contains("not one the gaming machine library implements",
            Refuse("host send lp FE").Reason);
    }

    [Fact]
    public void Trailing_words_after_a_complete_statement_are_refused()
    {
        Assert.Contains("unexpected", Refuse("""
            host connect loopback address 01
            host expect link up within 5s please
            """).Reason);
    }
}

public class ScriptLocationTests
{
    [Fact]
    public void Every_step_remembers_the_line_it_was_written_on()
    {
        ScriptDocument document = ScriptParser.Parse("""
            host connect loopback address 01
            host expect link up
            host send lp 1F
            """, "counting.sas");

        ScriptTest test = document.Tests[0];
        Assert.Equal(1, test.Steps[0].Location.Line);
        Assert.Equal(2, test.Steps[1].Location.Line);
        Assert.Equal(3, test.Steps[2].Location.Line);
        Assert.Equal("counting.sas", test.Steps[0].Location.File);
    }

    [Fact]
    public void A_link_state_assertion_reads_all_three_states()
    {
        ScriptDocument document = ScriptParser.Parse("""
            host connect loopback address 01
            host expect link up
            host expect link down
            host expect link synchronising
            """, "states.sas");

        Assert.Equal(LinkState.Up, State(document, 1));
        Assert.Equal(LinkState.Down, State(document, 2));
        Assert.Equal(LinkState.Synchronising, State(document, 3));
    }

    private static LinkState State(ScriptDocument document, int index) =>
        Assert.IsType<ExpectLinkStep>(document.Tests[0].Steps[index]).Expected;
}

public class SasNamesTests
{
    [Theory]
    [InlineData("USD20BillAccepted", "usd_20_bill_accepted")]
    [InlineData("SlotDoorOpened", "slot_door_opened")]
    [InlineData("TotalCoinInCredits", "total_coin_in_credits")]
    public void Library_identifiers_become_names_a_person_would_type(string symbol, string expected)
    {
        Assert.Equal(expected, SasNames.Underscore(symbol));
    }

    [Theory]
    [InlineData("$4A", 0x4A)]
    [InlineData("0x4A", 0x4A)]
    [InlineData("4A", 0x4A)]
    [InlineData("11", 0x11)]
    public void Codes_are_read_in_the_forms_the_protocol_is_written_in(string text, int expected)
    {
        Assert.True(SasNames.TryParseCode(text, out byte code));
        Assert.Equal(expected, code);
    }

    [Fact]
    public void An_exception_not_in_the_catalogue_is_refused_even_when_it_parses_as_a_code()
    {
        Assert.False(SasNames.TryResolveException("$FE", out _));
    }
}

/// <summary>
/// What a case declares about the machine it assumes, which is a different thing from what it
/// requires of the environment and a different thing again from what it does to the machine.
/// </summary>
public class MachineDeclarationTests
{
    [Fact]
    public void A_denomination_can_be_written_in_cents()
    {
        ScriptTest test = ScriptParser.Parse("machine accounting denomination 10c").Tests[0];

        Assert.NotNull(test.Machine);
        Assert.Equal(10, test.Machine!.Cents);
        Assert.Equal(0x03, test.Machine.Code);
        Assert.Equal("10c", test.Machine.Assumed);
    }

    [Fact]
    public void A_denomination_can_be_written_as_a_table_code()
    {
        ScriptTest test = ScriptParser.Parse("machine accounting denomination code 03").Tests[0];

        Assert.Equal(10, test.Machine!.Cents);
        Assert.Equal(0x03, test.Machine.Code);
    }

    // A bare number is ambiguous - '3' is ten cents as a code and three cents as an amount - and
    // the two readings differ by more than a factor of three on every credit meter in the machine.
    [Fact]
    public void A_bare_number_is_refused_rather_than_guessed_at()
    {
        ScriptParseException problem = Assert.Throws<ScriptParseException>(
            () => ScriptParser.Parse("machine accounting denomination 3"));

        Assert.Contains("code", problem.Reason, StringComparison.Ordinal);
    }

    // Code $1B is one of the five sub-cent codes, which exist in Table C-4 with a value of zero:
    // a build that accepted one would divide by zero rather than refuse. Seven cents is simply not
    // in the table - three cents is, at code $18, which is exactly why a script says which of the
    // two things a bare number means.
    [Theory]
    [InlineData("machine accounting denomination code 1B")]
    [InlineData("machine accounting denomination 7c")]
    [InlineData("egm set accounting denomination 7c")]
    public void A_unit_no_machine_can_be_in_force_at_is_refused(string line)
    {
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse(line));
    }

    // Every backend but the software machine refuses it, because no real machine can change the
    // unit it counts in without a memory clear.
    [Fact]
    public void Only_the_software_machine_offers_the_setter()
    {
        Assert.True(CapabilityProbe.ForStimulus(StimulusBackend.Software)
            .ContainsKey("stimulus.accounting_denomination"));

        foreach (StimulusBackend backend in new[]
                 { StimulusBackend.Manual, StimulusBackend.Qpm, StimulusBackend.None })
        {
            Assert.False(CapabilityProbe.ForStimulus(backend)
                .ContainsKey("stimulus.accounting_denomination"));
        }
    }

    // And a backend that does not offer it refuses the step with the reason, so the case skips -
    // the same treatment a door nobody has wired gets.
    [Fact]
    public void A_backend_that_cannot_reconfigure_refuses_the_setter_with_a_reason()
    {
        var step = (StimulusStep)ScriptParser
            .Parse("egm set accounting denomination 10c").Tests[0].Steps[0];

        StimulusUnavailableException refused = Assert.Throws<StimulusUnavailableException>(
            () => new NoStimulusBackend().Apply(step));

        Assert.Contains("stimulus.accounting_denomination", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_declaration_is_a_contradiction()
    {
        ScriptParseException problem = Assert.Throws<ScriptParseException>(
            () => ScriptParser.Parse("""
                machine accounting denomination 10c
                machine accounting denomination 5c
                """));

        Assert.Contains("one unit at a time", problem.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Player_denominations_join_the_declaration()
    {
        ScriptTest test = ScriptParser.Parse("""
            machine accounting denomination 10c
            machine player denominations 10c 25c 50c
            """).Tests[0];

        Assert.Equal([10, 25, 50], test.Machine!.PlayerDenominationsInCents);
    }

    // On their own they declare nothing a machine can be built or checked against.
    [Fact]
    public void Player_denominations_alone_are_refused()
    {
        Assert.Throws<ScriptParseException>(
            () => ScriptParser.Parse("machine player denominations 10c 25c"));
    }

    [Fact]
    public void The_setter_is_a_stimulus_step()
    {
        ScriptTest test = ScriptParser.Parse("egm set accounting denomination 10c").Tests[0];

        var step = Assert.IsType<SetDenominationStep>(Assert.Single(test.Steps));
        Assert.Equal(0x03, step.Code);
        Assert.Equal("stimulus.accounting_denomination", step.RequiredCapability);
        Assert.Null(test.Machine);
    }

    [Fact]
    public void The_asset_number_setter_is_a_stimulus_step()
    {
        ScriptTest test = ScriptParser.Parse("egm set asset number 12345").Tests[0];

        var step = Assert.IsType<SetAssetNumberStep>(Assert.Single(test.Steps));
        Assert.Equal(12345u, step.Asset);
        Assert.Equal("stimulus.asset_number", step.RequiredCapability);
    }

    [Fact]
    public void An_asset_number_that_is_not_a_decimal_is_refused()
    {
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse("egm set asset number 3039h"));
    }

    // The declaration is checked by the same path as a capability the machine answers for, so an
    // unmet one is a skip with a reason rather than a failure.
    [Fact]
    public void The_declaration_becomes_a_requirement_the_machine_answers()
    {
        ScriptTest test = ScriptParser.Parse("machine accounting denomination 10c").Tests[0];
        Requirement requirement = test.Machine!.AsRequirement();

        Assert.True(CapabilityProbe.IsAskedOfTheMachine(requirement.Key));
        Assert.Equal("10c", requirement.ExpectedValue);
    }

    [Fact]
    public void A_declaration_written_in_the_other_form_reads_the_same()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".yaml");
        File.WriteAllText(path, """
            name: ten centavos
            steps:
              - machine accounting denomination 10c
              - host connect loopback address 01
            """);

        try
        {
            ScriptTest test = YamlScriptReader.ParseFile(path).Tests[0];
            Assert.Equal(10, test.Machine!.Cents);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_hand_pay_is_a_stimulus_in_three_forms()
    {
        ScriptTest test = Assert.Single(ScriptParser.Parse("""
            egm handpay 2000
            egm handpay jackpot 150000
            egm handpay reset
            """, "test.sas").Tests);

        HandpayStep cashout = Assert.IsType<HandpayStep>(test.Steps[0]);
        Assert.Equal((HandpayKind.CancelledCredit, 2000L), (cashout.Kind, cashout.AmountCents));
        Assert.Equal("stimulus.handpay", cashout.RequiredCapability);

        HandpayStep jackpot = Assert.IsType<HandpayStep>(test.Steps[1]);
        Assert.Equal("stimulus.handpay.jackpot", jackpot.RequiredCapability);

        Assert.Equal("stimulus.handpay.reset", Assert.IsType<HandpayResetStep>(test.Steps[2]).RequiredCapability);
    }

    // Power cycles, memory faults and memory clears are stimuli like a door: a backend that cannot
    // do them makes the case skip with the reason.
    [Fact]
    public void A_power_cycle_a_memory_fault_and_a_memory_clear_are_machine_stimuli()
    {
        ScriptTest test = Assert.Single(ScriptParser.Parse("""
            egm power cycle
            egm memory fault cmos-lost
            egm memory fault clear
            egm nvram clear
            """, "test.sas").Tests);

        Assert.Equal("machine.power_cycle", Assert.IsType<PowerCycleStep>(test.Steps[0]).RequiredCapability);

        MemoryFaultStep lost = Assert.IsType<MemoryFaultStep>(test.Steps[1]);
        Assert.Equal((MemoryFaultKind.CmosLost, "machine.memory_fault"), (lost.Kind, lost.RequiredCapability));
        Assert.Equal(MemoryFaultKind.Clear, Assert.IsType<MemoryFaultStep>(test.Steps[2]).Kind);
        Assert.Equal("machine.nvram_clear", Assert.IsType<NvramClearStep>(test.Steps[3]).RequiredCapability);
    }

    // A fault some machines never report needs a capability of its own, so a case needing it skips there.
    [Theory]
    [InlineData("cmos-lost", "machine.memory_fault")]
    [InlineData("eprom-changed", "machine.memory_fault")]
    [InlineData("clear", "machine.memory_fault")]
    [InlineData("cmos-recovered", "machine.memory_fault.cmos_recovered")]
    [InlineData("cmos-bad-device", "machine.memory_fault.cmos_bad_device")]
    [InlineData("eeprom-data", "machine.memory_fault.eeprom")]
    [InlineData("eeprom-bad-device", "machine.memory_fault.eeprom")]
    [InlineData("eprom-bad-checksum", "machine.memory_fault.eprom_checksum")]
    [InlineData("partitioned-eprom-changed", "machine.memory_fault.partitioned_eprom")]
    [InlineData("partitioned-eprom-bad-checksum", "machine.memory_fault.partitioned_eprom")]
    public void Each_memory_fault_needs_the_capability_of_the_code_it_raises(string kind, string capability)
    {
        ScriptTest test = Assert.Single(ScriptParser.Parse($"egm memory fault {kind}", "test.sas").Tests);

        Assert.Equal(capability, Assert.IsType<MemoryFaultStep>(test.Steps[0]).RequiredCapability);
    }

    [Theory]
    [InlineData("egm power")]
    [InlineData("egm power off")]
    [InlineData("egm memory fault")]
    [InlineData("egm memory fault cmos")]
    [InlineData("egm memory error cmos-lost")]
    [InlineData("egm nvram")]
    [InlineData("egm nvram reset")]
    public void A_power_cycle_memory_fault_or_clear_written_wrongly_is_a_mistake_in_the_script(string line)
    {
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse(line));
    }

    // A person can do all three, and the software machine models them; the peripheral driver cannot
    // cut power or break a memory device, so claiming them there would turn a skip into a case that
    // waits on nothing. The faults the platform never reports are offered by a person alone.
    [Fact]
    public void A_person_and_the_software_machine_offer_power_cycles_memory_faults_and_clears()
    {
        foreach (string key in new[] { "machine.power_cycle", "machine.memory_fault", "machine.nvram_clear" })
        {
            Assert.True(CapabilityProbe.ForStimulus(StimulusBackend.Manual).ContainsKey(key));
            Assert.True(CapabilityProbe.ForStimulus(StimulusBackend.Software).ContainsKey(key));
            Assert.False(CapabilityProbe.ForStimulus(StimulusBackend.Qpm).ContainsKey(key));
            Assert.False(CapabilityProbe.ForStimulus(StimulusBackend.None).ContainsKey(key));
        }

        Assert.True(CapabilityProbe.ForStimulus(StimulusBackend.Manual).ContainsKey("machine.memory_fault.eeprom"));
        Assert.False(CapabilityProbe.ForStimulus(StimulusBackend.Software).ContainsKey("machine.memory_fault.eeprom"));
    }

    [Theory]
    [InlineData("egm handpay")]
    [InlineData("egm handpay 0")]
    [InlineData("egm handpay twenty")]
    [InlineData("egm handpay jackpot")]
    [InlineData("egm handpay reset now")]
    public void A_hand_pay_without_an_amount_is_a_mistake_in_the_script(string line)
    {
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse(line, "bad.sas"));
    }

    /// <summary>
    /// Address 00 reaches every machine, so it is accepted only for a long poll the protocol lets a
    /// host broadcast - $80 here - and still refused for one it does not, such as $1F.
    /// </summary>
    [Fact]
    public void Only_a_broadcast_long_poll_may_be_sent_to_00()
    {
        ScriptTest test = Assert.Single(ScriptParser.Parse("""
            host send lp 80 with progressive_group=1 level=1 amount=1000 to 00
            """, "test.sas").Tests);

        SendLongPollStep send = Assert.IsType<SendLongPollStep>(Assert.Single(test.Steps));
        Assert.True(send.To is { IsSync: true });

        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse("host send lp 1F to 00", "bad.sas"));
    }
}
