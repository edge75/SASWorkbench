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
            host expect field amount of lp 72 == 100
            """, "future.sas");

        var step = Assert.IsType<ExpectFieldStep>(document.Tests[0].Steps[1]);
        Assert.Equal(0x72, step.Command);
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

public class MeterIndexTests
{
    /// <summary>
    /// The index is built from the layouts, so a dedicated single-meter command must win over the
    /// bulk one. A reply carrying one number is far easier to explain when it disagrees.
    /// </summary>
    [Fact]
    public void A_dedicated_command_is_preferred_over_a_bulk_one()
    {
        Assert.True(MeterIndex.TryResolve("total_coin_in", out MeterSource source));

        Assert.Equal(0x11, source.Command);
    }

    [Fact]
    public void A_meter_only_a_bulk_command_returns_is_still_readable()
    {
        Assert.True(MeterIndex.TryResolve("bills_20", out MeterSource source));

        Assert.Equal(0x1E, source.Command);
        Assert.Equal("bills_20", source.Field);
    }

    [Fact]
    public void Friendly_names_resolve_to_a_real_field()
    {
        Assert.True(MeterIndex.TryResolve("total_bill_in", out MeterSource source));

        Assert.Equal("total_bill_value", source.Field);
        Assert.Equal(0x20, source.Command);
    }

    [Fact]
    public void A_meter_no_layout_returns_is_not_readable()
    {
        Assert.False(MeterIndex.TryResolve("total_widgets", out _));
    }
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
