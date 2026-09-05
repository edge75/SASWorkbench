using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Tests;

public class CatalogueTests
{
    [Fact]
    public void The_catalogue_covers_what_the_library_dispatches()
    {
        (int longPolls, int exceptions, int meters) = SasCatalogue.Counts;

        // Generated from the library's own enumerations. If these numbers move, the generator
        // has been re-run against a different revision, which is worth noticing in a diff.
        Assert.Equal(124, longPolls);
        Assert.Equal(122, exceptions);
        Assert.Equal(196, meters);
    }

    [Fact]
    public void Long_polls_are_described_by_the_library_own_handler_names()
    {
        SasLongPollInfo machineId = SasCatalogue.LongPolls[0x1F];

        Assert.Equal("LP_SENDMACHINEID_INFO", machineId.Symbol);
        Assert.Contains("gaming machine", machineId.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Exceptions_carry_the_meaning_the_library_documents()
    {
        Assert.Equal("SlotDoorOpened", SasCatalogue.Exceptions[0x11].Symbol);
        Assert.Contains("door", SasCatalogue.Exceptions[0x11].Description,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_exception_codes_seen_at_machine_start_up_are_all_in_the_catalogue()
    {
        // Every code the captured session reported while the simulated peripherals were
        // announcing open doors. A catalogue that cannot name these cannot explain a real run.
        byte[] observed = [0x11, 0x15, 0x17, 0x18, 0x98, 0x99];

        Assert.All(observed, code => Assert.True(SasCatalogue.Exceptions.ContainsKey(code),
            $"exception ${code:X2} is missing from the catalogue"));
    }

    [Fact]
    public void Every_meter_has_a_name_and_a_description()
    {
        Assert.All(SasCatalogue.Meters.Values, meter =>
        {
            Assert.False(string.IsNullOrWhiteSpace(meter.Symbol));
            Assert.False(string.IsNullOrWhiteSpace(meter.Description));
        });
    }
}

public class LayoutTests
{
    [Fact]
    public void Every_layout_names_the_handler_it_was_read_from()
    {
        Assert.All(SasLayouts.All.Values, layout =>
        {
            Assert.False(string.IsNullOrWhiteSpace(layout.DerivedFrom));
            Assert.False(string.IsNullOrWhiteSpace(layout.Description));
        });
    }

    [Fact]
    public void Every_layout_is_a_command_the_library_actually_has()
    {
        Assert.All(SasLayouts.All.Keys, command =>
            Assert.True(SasCatalogue.LongPolls.ContainsKey(command),
                $"layout for ${command:X2} has no matching command in the catalogue"));
    }

    /// <summary>
    /// The gap between what is catalogued and what can be decoded is the honest part of this
    /// milestone. A command in the catalogue with no layout must report as undecoded so the runner
    /// can skip with a reason, rather than be silently treated as unsupported or, worse, guessed at.
    /// </summary>
    [Fact]
    public void A_command_with_no_layout_is_reported_as_known_but_undecoded()
    {
        Assert.True(SasLayouts.IsKnownButUndecoded(0x72), "AFT transfer funds should be undecoded");
        Assert.True(SasLayouts.IsKnownButUndecoded(0x04), "sound on has not been modelled yet");
        Assert.False(SasLayouts.IsKnownButUndecoded(0x1F), "machine identification has a layout");
        Assert.False(SasLayouts.IsKnownButUndecoded(0x11), "total coin in has a layout");
    }

    [Fact]
    public void Layouts_can_be_found_by_the_name_a_script_would_write()
    {
        Assert.Equal(0x1F, SasLayouts.ByName["machine_id"].Command);
        Assert.Equal(0x15, SasLayouts.ByName["GAMES_PLAYED"].Command);
    }

    [Fact]
    public void A_fixed_length_reply_reports_its_length_and_a_variable_one_does_not()
    {
        // Address, command, six four-byte meters, check sequence.
        Assert.Equal(28, SasLayouts.For(0x0F)!.FixedResponseLength);
        Assert.Null(SasLayouts.For(0x54)!.FixedResponseLength);
    }
}

public class MessageDecoderTests
{
    /// <summary>
    /// The captured reply, decoded field by field. Every expected value here was read off the
    /// wire, so this test fails if the layout, the byte order, the encodings or the units are
    /// wrong — which is exactly the set of mistakes that produce a plausible wrong answer.
    /// </summary>
    [Fact]
    public void Decodes_the_captured_machine_identification_reply()
    {
        byte[] frame = Convert.FromHexString("011F5158303031" + "02" + "FF" + "00" + "0000"
                                             + "303031000000" + "39323130" + "863F");

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x1F)!, frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal("QX", decoded.Text("game_id"));
        Assert.Equal("001", decoded.Text("additional_id"));
        Assert.Equal(0x02, decoded.Number("denomination"));
        Assert.Equal(0xFF, decoded.Number("max_bet"));
        Assert.Equal(0x00, decoded.Number("progressive_group"));
        Assert.Equal(0x0000, decoded.Number("game_options"));
        Assert.Equal("001", decoded.Text("paytable_id"));
        Assert.Equal(9210, decoded.Number("base_percentage"));
        Assert.Empty(decoded.Violations);
    }

    [Fact]
    public void Reads_a_little_endian_field_least_significant_byte_first()
    {
        byte[] frame = Convert.FromHexString("011F5158303031" + "02" + "FF" + "00" + "3412"
                                             + "303031000000" + "39323130" + "0000");

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x1F)!, frame);

        // Bytes 34 12 on the wire, least significant first, are 0x1234 — not 0x3412.
        Assert.Equal(0x1234, decoded.Number("game_options"));
    }

    [Fact]
    public void Reads_a_meter_as_binary_coded_decimal()
    {
        byte[] frame = BuildFrame(0x11, [0x00, 0x01, 0x23, 0x45]);

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x11)!, frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(12345, decoded.Number("total_coin_in"));
        Assert.Equal(SasUnit.Credits, decoded["total_coin_in"]!.Field.Unit);
    }

    /// <summary>
    /// Bytes that are not valid decimal digits stop the decode rather than being reinterpreted.
    /// A meter read as binary when it is declared as decimal produces a number that is wrong by a
    /// factor nobody notices; a named failure at a named byte is recoverable.
    /// </summary>
    [Fact]
    public void Refuses_to_read_a_meter_whose_bytes_are_not_decimal_digits()
    {
        byte[] frame = BuildFrame(0x11, [0x00, 0x01, 0x2A, 0x45]);

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x11)!, frame);

        Assert.False(decoded.Succeeded);
        Assert.Contains("decimal", decoded.Failure!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("total_coin_in", decoded.Failure!);
    }

    [Fact]
    public void Names_the_field_and_the_shortfall_when_a_reply_is_truncated()
    {
        byte[] frame = BuildFrame(0x0F, [0x00, 0x00, 0x00, 0x01, 0x00, 0x00]);

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x0F)!, frame);

        // Six bytes cover the first meter and leave two, so the second is where it runs out.
        Assert.False(decoded.Succeeded);
        Assert.Contains("total_coin_in", decoded.Failure!);
        Assert.Contains("only 2", decoded.Failure!);
    }

    [Fact]
    public void Decodes_six_meters_from_one_reply()
    {
        byte[] payload = new byte[24];
        for (int meter = 0; meter < 6; meter++)
            payload[(meter * 4) + 3] = (byte)(meter + 1);

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(
            SasLayouts.For(0x0F)!, BuildFrame(0x0F, payload));

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(1, decoded.Number("total_cancelled_credits"));
        Assert.Equal(6, decoded.Number("games_played"));
    }

    [Fact]
    public void Reads_a_variable_length_reply_to_the_end()
    {
        byte[] payload = [0x09, .. "602"u8, .. "QX12345"u8];

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(
            SasLayouts.For(0x54)!, BuildFrame(0x54, payload));

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal("602", decoded.Text("sas_version"));
        Assert.Equal("QX12345", decoded.Text("serial_number"));
    }

    [Fact]
    public void Reports_a_constraint_breach_without_abandoning_the_reply()
    {
        // Month thirteen. The value decodes cleanly as a decimal number and is still impossible,
        // which is exactly the case a decoder must report rather than reject: the rest of the
        // clock is worth reading, and a test asserting on the hour should still get an answer.
        byte[] frame = [0x01, 0x7E, 0x13, 0x04, 0x20, 0x26, 0x09, 0x30, 0x00, 0x00, 0x00];

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x7E)!, frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(2026, decoded.Number("year"));
        Assert.Equal(9, decoded.Number("hour"));
        SasValue month = Assert.Single(decoded.Violations);
        Assert.Equal("month", month.Path);
        Assert.Contains("maximum", month.Violation!);
    }

    /// <summary>
    /// Extra bytes must be ignored rather than rejected, and reporting them is how a test can
    /// notice a machine sending fields this version does not model yet.
    /// </summary>
    [Fact]
    public void Trailing_bytes_beyond_the_layout_are_reported_not_rejected()
    {
        byte[] frame = BuildFrame(0x11, [0x00, 0x00, 0x00, 0x01, 0xAA, 0xBB]);

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x11)!, frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(1, decoded.Number("total_coin_in"));
        Assert.Equal("AABB", decoded["unmodelled_trailer"]!.ToHex());
    }

    [Fact]
    public void A_reply_echoing_the_wrong_command_is_refused()
    {
        byte[] frame = BuildFrame(0x12, [0x00, 0x00, 0x00, 0x01]);

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x11)!, frame);

        Assert.False(decoded.Succeeded);
        Assert.Contains("$12", decoded.Failure!);
    }

    [Fact]
    public void An_acknowledgement_only_command_has_no_reply_to_decode()
    {
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(
            SasLayouts.For(0x01)!, Convert.FromHexString("01"));

        Assert.False(decoded.Succeeded);
    }

    [Fact]
    public void Decodes_the_data_a_host_sent()
    {
        byte[] frame = Convert.FromHexString("010E01") ;
        byte[] withCrc = [.. frame, 0x00, 0x00];

        SasDecoded decoded = SasMessageDecoder.DecodeRequest(SasLayouts.For(0x0E)!, withCrc);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(1, decoded.Number("enabled"));
    }

    [Fact]
    public void A_request_field_outside_its_permitted_values_is_flagged()
    {
        byte[] withCrc = [0x01, 0x0E, 0x07, 0x00, 0x00];

        SasDecoded decoded = SasMessageDecoder.DecodeRequest(SasLayouts.For(0x0E)!, withCrc);

        SasValue enabled = Assert.Single(decoded.Violations);
        Assert.Equal("enabled", enabled.Path);
    }

    private static byte[] BuildFrame(byte command, ReadOnlySpan<byte> payload)
    {
        byte[] frame = new byte[2 + payload.Length + 2];
        frame[0] = 0x01;
        frame[1] = command;
        payload.CopyTo(frame.AsSpan(2));
        Core.Wire.SasCrc.Append(frame, 2 + payload.Length);
        return frame;
    }
}

public class RepeatingGroupTests
{
    private static readonly SasMessageLayout GameMeters = new()
    {
        Command = 0x52,
        Name = "game_n_meters",
        Description = "A run of meters, one per game.",
        Response =
        [
            new SasField
            {
                Name = "game_count",
                Length = 1,
                Encoding = SasFieldEncoding.Binary,
                Unit = SasUnit.Count,
                Description = "How many games follow.",
            },
            new SasField
            {
                Name = "games",
                Length = 0,
                Repeat = SasRepeat.CountedBy("game_count"),
                Description = "One entry per game.",
                Fields =
                [
                    new SasField
                    {
                        Name = "number",
                        Length = 1,
                        Encoding = SasFieldEncoding.Binary,
                        Unit = SasUnit.Index,
                        Description = "Game number.",
                    },
                    new SasField
                    {
                        Name = "coin_in",
                        Length = 2,
                        Encoding = SasFieldEncoding.Bcd,
                        Unit = SasUnit.Credits,
                        Description = "Credits wagered on this game.",
                    },
                ],
            },
        ],
        DerivedFrom = "test fixture, not a machine handler",
    };

    /// <summary>
    /// Arrays are one of the four things a display-only model cannot express, and the reason a
    /// model without them ends up with bespoke element types for the messages that need them.
    /// </summary>
    [Fact]
    public void A_group_repeats_as_many_times_as_an_earlier_field_says()
    {
        byte[] payload = [0x02, 0x01, 0x00, 0x10, 0x02, 0x00, 0x25];
        byte[] frame = [0x01, 0x52, .. payload, 0x00, 0x00];

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(GameMeters, frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(1, decoded.Number("games[0].number"));
        Assert.Equal(10, decoded.Number("games[0].coin_in"));
        Assert.Equal(2, decoded.Number("games[1].number"));
        Assert.Equal(25, decoded.Number("games[1].coin_in"));
        Assert.Equal(2, decoded.Number("games.count"));
    }

    [Fact]
    public void A_group_that_runs_past_the_end_of_the_reply_fails_by_name()
    {
        byte[] payload = [0x03, 0x01, 0x00, 0x10];
        byte[] frame = [0x01, 0x52, .. payload, 0x00, 0x00];

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(GameMeters, frame);

        Assert.False(decoded.Succeeded);
        Assert.Contains("games[1]", decoded.Failure!);
    }
}

public class ConditionalFieldTests
{
    private static readonly SasMessageLayout WithOptionalTail = new()
    {
        Command = 0x74,
        Name = "lock_and_status",
        Description = "Status, with details only when the machine is locked.",
        Response =
        [
            new SasField
            {
                Name = "status",
                Length = 1,
                Encoding = SasFieldEncoding.Binary,
                Unit = SasUnit.Code,
                Description = "Lock status.",
            },
            new SasField
            {
                Name = "lock_timeout",
                Length = 2,
                Encoding = SasFieldEncoding.Bcd,
                Unit = SasUnit.Milliseconds,
                Description = "How long the lock will be held.",
                PresentWhen = new SasCondition("status", SasComparison.Equal, 0x00),
            },
        ],
        DerivedFrom = "test fixture, not a machine handler",
    };

    [Fact]
    public void A_conditional_field_is_read_when_its_condition_holds()
    {
        byte[] frame = [0x01, 0x74, 0x00, 0x00, 0x30, 0x00, 0x00];

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(WithOptionalTail, frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(30, decoded.Number("lock_timeout"));
    }

    [Fact]
    public void A_conditional_field_is_absent_when_its_condition_does_not_hold()
    {
        byte[] frame = [0x01, 0x74, 0xFF, 0x00, 0x00];

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(WithOptionalTail, frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Null(decoded["lock_timeout"]);
        Assert.Equal(0xFF, decoded.Number("status"));
    }
}

/// <summary>
/// The selected-meter replies, which are the first messages in this build whose shape cannot be
/// written down as a table of offsets: each meter states its own width, and one the machine does
/// not support is left out of the reply entirely.
/// </summary>
public class SelectedMeterTests
{
    private static byte[] Reply(params byte[] payload)
    {
        Span<byte> frame = stackalloc byte[SasWorkbench.Core.Wire.HostMessage.MaxSymbols];
        frame[0] = 0x01;
        frame[1] = 0x6F;
        payload.CopyTo(frame[2..]);
        return frame[..SasWorkbench.Core.Wire.SasCrc.Append(frame, 2 + payload.Length)].ToArray();
    }

    /// <summary>
    /// The worked example from the specification: five hundred pesos of coin in, at an accounting
    /// denomination of ten centavos, reports 5000 rather than 50000.
    /// </summary>
    [Fact]
    public void A_meter_is_read_from_the_width_the_machine_chose_for_it()
    {
        byte[] frame = Reply(
            0x10,                                     // bytes following, check sequence excluded
            0x00, 0x00,                               // game number - the machine as a whole
            0x00, 0x00, 0x04, 0x00, 0x00, 0x50, 0x00, // meter 0000, four bytes, 5000
            0x0C, 0x00, 0x04, 0x00, 0x00, 0x01, 0x23);// meter 000C, four bytes, 123

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x6F)!, frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(2, decoded.Number("meters.count"));
        Assert.Equal(0x0000, decoded.Number("meters[0].code"));
        Assert.Equal(5000, decoded.Number("meters[0].value"));
        Assert.Equal(0x000C, decoded.Number("meters[1].code"));
        Assert.Equal(123, decoded.Number("meters[1].value"));
    }

    /// <summary>
    /// A meter code is binary and least significant byte first, so <c>A0 00</c> is meter 00A0.
    /// Reading the two bytes the other way round names meter A000 and decodes just as cleanly,
    /// which is why this is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void A_meter_code_is_read_least_significant_byte_first()
    {
        byte[] frame = Reply(
            0x0A,
            0x00, 0x00,
            0xA0, 0x00, 0x05, 0x00, 0x00, 0x00, 0x20, 0x00);

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x6F)!, frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0x00A0, decoded.Number("meters[0].code"));
        Assert.Equal(2000, decoded.Number("meters[0].value"));
    }

    /// <summary>
    /// The machine widens a meter as its value grows rather than letting it roll over, so two
    /// meters in one reply need not be the same size. A reader that assumed a fixed width would
    /// take the next meter's code as this one's last digits.
    /// </summary>
    [Fact]
    public void Meters_of_different_widths_in_one_reply_are_each_read_whole()
    {
        byte[] frame = Reply(
            0x11,
            0x00, 0x00,
            0x00, 0x00, 0x06, 0x12, 0x34, 0x56, 0x78, 0x90, 0x12, // six bytes
            0x01, 0x00, 0x04, 0x00, 0x00, 0x00, 0x99);            // four bytes

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x6F)!, frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(123456789012, decoded.Number("meters[0].value"));
        Assert.Equal(0x0001, decoded.Number("meters[1].code"));
        Assert.Equal(99, decoded.Number("meters[1].value"));
    }

    /// <summary>
    /// A meter the machine does not support is absent from the reply, code and all. That must read
    /// as "not supported" and must not shift every later meter onto the wrong code - which is what
    /// makes walking the reply mandatory and indexing it by position wrong.
    /// </summary>
    [Fact]
    public void A_meter_the_machine_does_not_support_is_absent_rather_than_zero()
    {
        // Three meters were asked for; the machine answers about the first and the third.
        byte[] frame = Reply(
            0x0E,
            0x00, 0x00,
            0x00, 0x00, 0x04, 0x00, 0x00, 0x11, 0x11,
            0x02, 0x00, 0x04, 0x00, 0x00, 0x22, 0x22);

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x6F)!, frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(2, decoded.Number("meters.count"));
        Assert.Equal(0x0000, decoded.Number("meters[0].code"));
        Assert.Equal(0x0002, decoded.Number("meters[1].code"));
        Assert.Null(decoded["meters[2].code"]);
    }

    /// <summary>Both commands answer with the same shape, which is why a sweep may alternate.</summary>
    [Fact]
    public void The_alternate_command_answers_with_the_same_shape()
    {
        Span<byte> frame = stackalloc byte[16];
        frame[0] = 0x01;
        frame[1] = 0xAF;
        byte[] payload = [0x07, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x50, 0x00];
        payload.CopyTo(frame[2..]);
        byte[] complete = frame[..SasWorkbench.Core.Wire.SasCrc.Append(frame, 2 + payload.Length)]
            .ToArray();

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0xAF)!, complete);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(5000, decoded.Number("meters[0].value"));
    }
}

/// <summary>
/// Which unit each meter is counted in, which is what decides whether the accounting denomination
/// applies to it. The expectations are the meter codes the machine-validation test plan names,
/// checked against a map derived from the library's own meter table — so the two have to agree.
/// </summary>
public class MeterUnitTests
{
    [Theory]
    // The credits meters the plan requires to scale: coin in and out, current credits, ticket in
    // and out, machine paid paytable win, won credits, hand paid credits.
    [InlineData(0x0000)]
    [InlineData(0x0001)]
    [InlineData(0x000C)]
    [InlineData(0x0015)]
    [InlineData(0x0016)]
    [InlineData(0x001C)]
    [InlineData(0x0022)]
    [InlineData(0x0023)]
    public void A_credits_meter_is_reported_in_the_accounting_denomination(int code)
    {
        Assert.Equal(SasMeterUnit.Accounting, SasMeterUnits.UnitOf((ushort)code));
    }

    [Theory]
    // Ticket and transfer amounts. A value wrongly divided here under-reports money to the host,
    // which is why the plan treats these as being as important as the ones that do scale.
    [InlineData(0x000D)]
    [InlineData(0x000E)]
    [InlineData(0x000F)]
    [InlineData(0x0010)]
    [InlineData(0x00A0)]
    [InlineData(0x00A2)]
    [InlineData(0x00B8)]
    public void A_cents_meter_is_not_touched_by_the_accounting_denomination(int code)
    {
        Assert.Equal(SasMeterUnit.Cents, SasMeterUnits.UnitOf((ushort)code));
    }

    [Theory]
    // Ticket quantities and the transfer counts. B9 in particular is a quantity rather than an
    // amount, which the plan calls out because it looks like one.
    [InlineData(0x0011)]
    [InlineData(0x0012)]
    [InlineData(0x0013)]
    [InlineData(0x00B9)]
    [InlineData(0x00BB)]
    [InlineData(0x00BD)]
    public void A_quantity_is_a_tally_and_scales_with_nothing(int code)
    {
        Assert.Equal(SasMeterUnit.Count, SasMeterUnits.UnitOf((ushort)code));
    }

    /// <summary>
    /// Games played and games won are tallies, but the library's table does not say so in a form
    /// this build can read: their names carry no unit at all. They are therefore unknown rather
    /// than assumed, and what matters for money is the guarantee below - an unknown meter is never
    /// scaled by the accounting denomination, so a tally cannot be quietly divided.
    /// </summary>
    [Theory]
    [InlineData(0x0005)]
    [InlineData(0x0006)]
    public void A_meter_whose_unit_the_table_does_not_state_is_never_scaled(int code)
    {
        Assert.False(SasMeterUnits.ScalesWithDenomination((ushort)code));
    }

    /// <summary>
    /// The map is derived, so it is worth knowing how much of the table it actually covers. A
    /// meter whose name carries no unit is reported as unknown rather than assumed to be a count.
    /// </summary>
    [Fact]
    public void Every_meter_is_either_classified_or_honestly_unknown()
    {
        IReadOnlyDictionary<SasMeterUnit, int> counts = SasMeterUnits.Counts;

        Assert.True(counts[SasMeterUnit.Accounting] > 0);
        Assert.True(counts[SasMeterUnit.Cents] > 0);
        Assert.True(counts[SasMeterUnit.Count] > 0);
        Assert.All(SasCatalogue.Meters.Keys, code =>
            Assert.True(Enum.IsDefined(SasMeterUnits.UnitOf(code))));
    }
}
