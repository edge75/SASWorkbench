using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// Every layout established from the catalogue sweep, checked against the bytes the real gaming
/// machine actually answered with.
/// </summary>
/// <remarks>
/// <para>
/// The difference from <see cref="CorpusLayoutTests"/> is whose bytes these are. Those came out of
/// recordings other tools made; these were produced by asking <c>QSP_DemoOpenGL</c> every command
/// the sweep is allowed to ask, on 2026-09-08, with the standard-validation memory image
/// installed. The sweep can be run again, which is the property that makes them worth asserting
/// on: a layout that stops fitting is a change in the machine or a mistake here, and either way
/// somebody should hear about it.
/// </para>
/// <para>
/// Copied verbatim, check sequence included, so a layout that shifts a field by one byte fails
/// here rather than in a report somebody has to read carefully.
/// </para>
/// </remarks>
public class SweepLayoutTests
{
    private static byte[] Bytes(string hex) => Convert.FromHexString(hex.Replace(" ", string.Empty));

    private static SasDecoded Decode(byte command, string hex)
    {
        SasMessageLayout? layout = SasLayouts.For(command);
        Assert.NotNull(layout);
        return SasMessageDecoder.DecodeResponse(layout, Bytes(hex));
    }

    /// <summary>
    /// The four dedicated bill counts the previous wave left undecoded on purpose, because no
    /// capture here carried one. The sweep carries all four, so they are decoded now — and the
    /// reason they were withheld then still stands: the shape came from the handler either way,
    /// and what changed is that a machine has been seen answering it.
    /// </summary>
    [Theory]
    [InlineData(0x31, "013100000000BEDB")]
    [InlineData(0x32, "01320000000072C6")]
    [InlineData(0x33, "01330000000036CD")]
    [InlineData(0x34, "013400000000EAFD")]
    public void The_bill_counts_no_capture_used_to_prove_now_decode(int command, string hex)
    {
        SasDecoded decoded = Decode((byte)command, hex);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.False(SasLayouts.IsKnownButUndecoded((byte)command));
    }

    [Theory]
    [InlineData(0x38, "013800000000DA8A")]
    [InlineData(0x39, "0139000000009E81")]
    [InlineData(0x3A, "013A00000000529C")]
    [InlineData(0x3B, "013B000000001697")]
    [InlineData(0x3C, "013C00000000CAA7")]
    [InlineData(0x3E, "013E0000000042B1")]
    [InlineData(0x3F, "013F0000000006BA")]
    [InlineData(0x40, "01400000000009C5")]
    [InlineData(0x41, "0141000000004DCE")]
    [InlineData(0x42, "01420000000081D3")]
    [InlineData(0x43, "014300000000C5D8")]
    [InlineData(0x44, "01440000000019E8")]
    [InlineData(0x45, "0145000000005DE3")]
    public void The_larger_bill_counts_decode(int command, string hex)
    {
        Assert.True(Decode((byte)command, hex).Succeeded);
    }

    [Theory]
    [InlineData(0x2A, "012A000000001228")]
    [InlineData(0x2B, "012B000000005623")]
    [InlineData(0x46, "01460000000091FE")]
    [InlineData(0x47, "014700000000D5F5")]
    [InlineData(0x49, "0149000000006D94")]
    [InlineData(0x4A, "014A00000000A189")]
    public void The_other_shared_meter_commands_decode(int command, string hex)
    {
        Assert.True(Decode((byte)command, hex).Succeeded);
    }

    /// <summary>
    /// Two two-byte counts rather than one four-byte meter, which the reply's total length is what
    /// settles: eight bytes, not ten.
    /// </summary>
    [Fact]
    public void Games_since_power_up_carries_two_two_byte_counts()
    {
        SasDecoded decoded = Decode(0x18, "0118000000004BEA");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0, decoded.Number("games_since_power_up"));
        Assert.Equal(0, decoded.Number("games_since_door_closure"));
    }

    [Fact]
    public void The_five_meter_and_eight_meter_replies_fill_their_whole_frames()
    {
        Assert.True(Decode(0x19,
            "01190000000000000000000000000000000000000000B343").Succeeded);
        Assert.True(Decode(0x1C,
            "011C0000000000000000000000000000000000000000000000000000002300000021D820").Succeeded);
    }

    /// <summary>
    /// The eight-meter reply's last two meters are door openings and power losses, and this
    /// machine had had both — which is what makes the offsets checkable rather than a row of
    /// zeroes that would fit any reading.
    /// </summary>
    [Fact]
    public void The_eight_meter_reply_puts_the_door_and_power_counts_where_the_handler_does()
    {
        SasDecoded decoded = Decode(0x1C,
            "011C0000000000000000000000000000000000000000000000000000002300000021D820");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(23, decoded.Number("slot_door_opened"));
        Assert.Equal(21, decoded.Number("power_reset"));
    }

    [Fact]
    public void The_game_keyed_meter_replies_echo_the_game_they_answer_for()
    {
        foreach ((byte command, string hex) in new (byte, string)[]
                 {
                     (0x2D, "012D00000000000073CC"),
                     (0x52, "0152000000000000000000000000000000000000E975"),
                     (0x83, "0183000000000000CCAF"),
                     (0x9A, "019A0000000000000000000000000000A7D2"),
                 })
        {
            SasDecoded decoded = Decode(command, hex);

            Assert.True(decoded.Succeeded, $"${command:X2}: {decoded.Failure}");
            Assert.Equal(0, decoded.Number("game_number"));
        }
    }

    [Fact]
    public void The_last_accepted_bill_reply_names_a_currency_and_a_denomination()
    {
        SasDecoded decoded = Decode(0x48, "01480000000000005598");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0, decoded.Number("country_code"));
        Assert.Equal(0, decoded.Number("denomination_code"));
    }

    /// <summary>
    /// The validation meters reply is one byte, a four-byte count and a five-byte amount, and it
    /// echoes the type it was asked about. Fourteen bytes, which is what fixes the amount at five.
    /// </summary>
    [Fact]
    public void The_validation_meters_reply_echoes_its_type_and_carries_a_five_byte_amount()
    {
        SasDecoded decoded = Decode(0x50, "0150000000000000000000001539");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0, decoded.Number("validation_type"));
        Assert.Equal(0, decoded.Number("quantity"));
        Assert.Equal(0, decoded.Number("amount"));
    }

    /// <summary>
    /// The game configuration reply carries five text fields and a little-endian number, and this
    /// capture has real values in every one of them — so a layout one byte out fails rather than
    /// decoding a row of zeroes.
    /// </summary>
    [Fact]
    public void The_game_configuration_reply_carries_the_maker_the_paytable_and_the_percentage()
    {
        SasDecoded decoded = Decode(0x53,
            "01530000515830303102FF0000003030310000003932313018B2");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0, decoded.Number("game_number"));
        Assert.Equal("QX", decoded.Text("game_id"));
        Assert.Equal("001", decoded.Text("additional_id"));
        Assert.Equal(0x02, decoded.Number("denomination"));
        Assert.Equal(0xFF, decoded.Number("max_bet"));
        Assert.Equal(0, decoded.Number("progressive_group"));
        Assert.Equal("9210", decoded.Text("base_percentage"));
    }

    [Fact]
    public void The_selected_game_and_token_denomination_replies_are_their_shortest_possible_forms()
    {
        Assert.True(Decode(0x55, "01550000E5A6").Succeeded);
        Assert.True(Decode(0xB3, "01B300DA4A").Succeeded);
    }

    /// <summary>
    /// The pending cashout reply's first byte says how the machine means to pay, and this machine
    /// answered <c>$80</c> — a value the layout reads as a code rather than as a number of cents.
    /// </summary>
    [Fact]
    public void The_pending_cashout_reply_names_how_the_machine_means_to_pay()
    {
        SasDecoded decoded = Decode(0x57, "0157800000000000CA31");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0x80, decoded.Number("cashout_type"));
        Assert.Equal(0, decoded.Number("amount"));
    }

    [Fact]
    public void The_progressive_win_replies_carry_a_group_a_level_and_five_bytes_of_cents()
    {
        Assert.True(Decode(0x84, "018400000000000000C190").Succeeded);
        Assert.True(Decode(0x85, "0185000000000000007E11").Succeeded);
    }

    [Fact]
    public void The_card_information_reply_carries_a_hand_type_and_five_cards()
    {
        Assert.True(Decode(0x8E, "018E0000000000008381").Succeeded);
    }

    /// <summary>
    /// Nine reel positions, measured. The overlay diff found the legacy tester describing this
    /// reply at seven bytes and the conformance toolkit at eleven, took it to the library's
    /// handler and closed it at eleven. This is the same answer off a real machine, which is the
    /// second half of settling a disagreement that neither of those two files could settle.
    /// </summary>
    [Fact]
    public void The_physical_reel_stop_reply_is_eleven_bytes_before_its_check_sequence()
    {
        byte[] frame = Bytes("018F000000000000000000BB5A");

        Assert.Equal(13, frame.Length);
        SasDecoded decoded = Decode(0x8F, "018F000000000000000000BB5A");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0, decoded.Number("reel_9"));
    }

    [Fact]
    public void The_legacy_bonus_win_reply_carries_two_amounts_and_a_tax_status()
    {
        Assert.True(Decode(0x90, "0190000000000000000000000B66").Succeeded);
    }

    /// <summary>
    /// The hopper reply's level is present only when the hopper can measure it, and this machine
    /// says it can — its own length byte counts the four bytes that follow. A machine that cannot
    /// answers a length of two and no level at all, which is the condition the layout carries.
    /// </summary>
    [Fact]
    public void The_hopper_status_reply_includes_a_level_only_because_the_hopper_can_measure_one()
    {
        SasDecoded decoded = Decode(0x4F, "014F0600000000000073C1");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(6, decoded.Number("length"));
        Assert.Equal(0, decoded.Number("percent_full"));
        Assert.Equal(0, decoded.Number("hopper_level"));
    }

    /// <summary>
    /// Four enabled games, each two bytes, counted by the byte before them. This is the reply
    /// where a reader that took the length byte for a count of games would walk off the end.
    /// </summary>
    [Fact]
    public void The_enabled_games_reply_lists_as_many_games_as_its_count_byte_says()
    {
        SasDecoded decoded = Decode(0x56, "0156090400010002000300049496");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(9, decoded.Number("length"));
        Assert.Equal(4, decoded.Number("count"));
        Assert.Equal(4, decoded.Number("games.count"));
        Assert.Equal(1, decoded.Number("games[0].game_number"));
        Assert.Equal(4, decoded.Number("games[3].game_number"));
    }

    [Fact]
    public void The_multiple_progressive_win_reply_carries_no_levels_when_none_were_won()
    {
        SasDecoded decoded = Decode(0x87, "0187020000B3C4");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(2, decoded.Number("length"));
        Assert.Equal(0, decoded.Number("count"));
        Assert.Equal(0, decoded.Number("levels.count"));
    }

    [Fact]
    public void The_enabled_denominations_reply_lists_the_four_this_machine_offers()
    {
        SasDecoded decoded = Decode(0xB2, "01B20504020305065812");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(4, decoded.Number("count"));
        Assert.Equal(4, decoded.Number("denominations.count"));
        Assert.Equal(0x02, decoded.Number("denominations[0].denomination"));
        Assert.Equal(0x06, decoded.Number("denominations[3].denomination"));
    }

    /// <summary>
    /// The wager category reply's coin-in meter is as wide as the machine says, and the payback
    /// percentage before it is four characters of text rather than a number — read as a number it
    /// would come out as 943,265,584.
    /// </summary>
    [Fact]
    public void The_wager_category_reply_sizes_its_own_meter_and_spells_its_percentage()
    {
        SasDecoded decoded = Decode(0xB4, "01B40D00000000393231300400000000F36B");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(13, decoded.Number("length"));
        Assert.Equal(0, decoded.Number("game_number"));
        Assert.Equal(0, decoded.Number("wager_category"));
        Assert.Equal("9210", decoded.Text("payback_percentage"));
        Assert.Equal(4, decoded.Number("meter_size"));
        Assert.Equal(0, decoded.Number("coin_in"));
    }

    /// <summary>
    /// Two length-prefixed strings in one reply, which is the whole reason its later fields cannot
    /// be at fixed offsets. The machine that answered called itself "Multigame Pack" running
    /// "PT_001", and those two names are what make the offsets checkable.
    /// </summary>
    [Fact]
    public void The_extended_game_information_reply_carries_two_length_prefixed_names()
    {
        SasDecoded decoded = Decode(0xB5,
            "01B5210000050000000000000E4D756C746967616D65205061636B0650545F30303100007E4D");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0x21, decoded.Number("length"));
        Assert.Equal(0, decoded.Number("game_number"));
        Assert.Equal(500, decoded.Number("max_bet"));
        Assert.Equal(0, decoded.Number("progressive_group"));
        Assert.Equal(14, decoded.Number("name_length"));
        Assert.Equal("Multigame Pack", decoded.Text("game_name"));
        Assert.Equal(6, decoded.Number("paytable_length"));
        Assert.Equal("PT_001", decoded.Text("paytable_name"));
        Assert.Equal(0, decoded.Number("wager_categories"));
    }

    /// <summary>
    /// The meter index did not move when these forty-seven layouts arrived, and it has since,
    /// without moving any name that was already in it.
    /// </summary>
    /// <remarks>
    /// The reason it waited is in <see cref="MeterIndexTests"/> and was discharged there rather
    /// than dropped: several of these commands report the same library meter as a command a name
    /// already came from, through a helper that scales it differently, so which command each name
    /// is read from had to be decided before any of them could be offered.
    /// </remarks>
    [Fact]
    public void Every_layout_this_sweep_added_that_carries_a_meter_says_which_meter_it_is()
    {
        foreach (byte command in (byte[])[0x31, 0x38, 0x45, 0x2A, 0x2B, 0x46, 0x47, 0x49, 0x4A])
        {
            SasField field = SasLayouts.For(command)!.Response.Single();
            Assert.NotNull(field.Meter);
            Assert.True(field.IsMeter);
        }
    }
}
