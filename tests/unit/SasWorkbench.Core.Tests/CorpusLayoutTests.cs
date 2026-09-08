using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// Every layout established for M4's backlog, checked against bytes taken out of the imported
/// corpus rather than against reasoning about the specification.
/// </summary>
/// <remarks>
/// <para>
/// Each case names the capture the bytes came from. That matters more than usual here: these
/// layouts were read out of the gaming machine library's handlers, and a handler read wrongly
/// produces a layout that decodes plausibly. Real traffic is the only thing that says whether the
/// offsets are where the reading put them.
/// </para>
/// <para>
/// The bytes are copied verbatim, check sequence included, so a layout that shifts a field by one
/// fails here rather than in a report somebody has to read carefully.
/// </para>
/// </remarks>
public class CorpusLayoutTests
{
    private static byte[] Bytes(string hex) => Convert.FromHexString(hex.Replace(" ", string.Empty));

    // ---- $2F, selected meters with no size byte -----------------------------------------------

    /// <summary>
    /// From the legacy tester's own capture beside <c>sastest.exe</c>, which carries eight of
    /// these exchanges.
    /// </summary>
    [Fact]
    public void Long_poll_2F_asks_for_meters_by_a_one_byte_code()
    {
        SasDecoded decoded = SasMessageDecoder.DecodeRequest(
            SasLayouts.For(0x2F)!, Bytes("01 2F 03 00 00 05 26 7C"));

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(3, decoded.Number("length"));
        Assert.Equal(0, decoded.Number("game_number"));
        Assert.Equal(1, decoded.Number("requested.count"));
        Assert.Equal(0x05, decoded.Number("requested[0].code"));
    }

    /// <summary>
    /// The reply to that request. Unlike <c>$6F</c> there is no size byte, so the four bytes are
    /// found only because the library's own table says meter <c>$05</c> is four bytes wide.
    /// </summary>
    [Fact]
    public void Long_poll_2F_answers_a_meter_whose_width_only_its_code_establishes()
    {
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(
            SasLayouts.For(0x2F)!, Bytes("01 2F 07 00 00 05 00 00 00 26 58 B3"));

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(7, decoded.Number("length"));
        Assert.Equal(1, decoded.Number("meters.count"));
        Assert.Equal(0x05, decoded.Number("meters[0].code"));
        Assert.Equal(26, decoded.Number("meters[0].value"));
    }

    /// <summary>
    /// A meter the library counts in cents is five bytes wide in this reply and four in no reply
    /// at all, so getting the table wrong shifts every later meter. Built here rather than taken
    /// from the corpus, because the corpus only ever asks for one meter at a time.
    /// </summary>
    [Fact]
    public void A_cents_meter_is_five_bytes_wide_and_the_next_meter_still_lands_right()
    {
        byte[] frame = new byte[2 + 1 + 2 + 6 + 5 + 2];
        Bytes("01 2F 0E 00 00 0D 00 00 01 23 45 05 00 00 00 26").CopyTo(frame, 0);
        Core.Wire.SasCrc.Append(frame, 16);

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x2F)!, frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(2, decoded.Number("meters.count"));
        Assert.Equal(0x0D, decoded.Number("meters[0].code"));
        Assert.Equal(12345, decoded.Number("meters[0].value"));
        Assert.Equal(0x05, decoded.Number("meters[1].code"));
        Assert.Equal(26, decoded.Number("meters[1].value"));
    }

    [Theory]
    [InlineData(0x0000, SasMeterWidths.Default)]
    [InlineData(0x000D, SasMeterWidths.Wide)]
    [InlineData(0x0010, SasMeterWidths.Wide)]
    [InlineData(0x0011, SasMeterWidths.Default)]
    [InlineData(0x0080, SasMeterWidths.Wide)]
    [InlineData(0x0081, SasMeterWidths.Default)]
    [InlineData(0x00BC, SasMeterWidths.Wide)]
    [InlineData(0x00BE, SasMeterWidths.Default)]
    public void The_width_table_is_the_librarys_own_list(int code, int width)
    {
        Assert.Equal(width, SasMeterWidths.Of((ushort)code));
    }

    // ---- $35, $36, $37, the dedicated bill counts ----------------------------------------------

    /// <summary>From the legacy tester's capture, where all three were asked in one run.</summary>
    [Theory]
    [InlineData(0x35, "01 35 00 00 00 00 AE F6")]
    [InlineData(0x36, "01 36 00 00 00 00 62 EB")]
    [InlineData(0x37, "01 37 00 00 00 00 26 E0")]
    public void A_dedicated_bill_count_is_four_bytes_of_binary_coded_decimal(int command, string hex)
    {
        SasMessageLayout layout = SasLayouts.For((byte)command)!;
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, Bytes(hex));

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(8, layout.FixedResponseLength);
        Assert.Equal(0, decoded.Values.Single().Number);
    }

    /// <summary>
    /// The counts these commands answer are not offered under a meter name, and the reason is that
    /// the library does not answer them the same way long poll <c>$1E</c> answers the same six
    /// meters: this one is divided by the accounting denomination under two of its meter modes.
    /// A script that has always read the meter from <c>$1E</c> keeps reading it from <c>$1E</c>.
    /// </summary>
    [Fact]
    public void The_bill_count_commands_do_not_take_over_the_bill_meters()
    {
        Assert.True(MeterIndex.TryResolve("bills_20", out MeterSource source));
        Assert.Equal(0x1E, source.Command);
    }

    // ---- $4C, the secure enhanced validation identifier ---------------------------------------

    /// <summary>
    /// From the toolkit's 10.2.1 traces. Both numbers are plain binary least significant byte
    /// first, which the handler assembles by hand and says so — read big-endian, the identifier
    /// would come out as <c>$011000</c> and still look like a number somebody chose.
    /// </summary>
    [Fact]
    public void Long_poll_4C_carries_two_three_byte_numbers_least_significant_byte_first()
    {
        SasDecoded request = SasMessageDecoder.DecodeRequest(
            SasLayouts.For(0x4C)!, Bytes("01 4C 01 00 10 01 00 00 75 6A"));

        Assert.True(request.Succeeded, request.Failure);
        Assert.Equal(0x100001, request.Number("validation_id"));
        Assert.Equal(1, request.Number("sequence_number"));

        SasDecoded reply = SasMessageDecoder.DecodeResponse(
            SasLayouts.For(0x4C)!, Bytes("01 4C 01 00 10 99 00 00 CE 25"));

        Assert.True(reply.Succeeded, reply.Failure);
        Assert.Equal(0x100001, reply.Number("validation_id"));
        Assert.Equal(0x99, reply.Number("sequence_number"));
    }

    // ---- $4D, one entry of the validation buffer -----------------------------------------------

    /// <summary>
    /// From the toolkit's 10.1.3 step 8 trace. The date and time are what fix the offsets: the
    /// trace's own header says the recording started on 07/20/26, and the reply reads
    /// 07/20/2026 17:57:38. A layout one byte out would put the year where the day is and still
    /// decode into plausible numbers.
    /// </summary>
    [Fact]
    public void Long_poll_4D_answers_a_validation_entry_whose_clock_matches_the_capture()
    {
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x4D)!, Bytes(
            "01 4D 00 01 07 20 20 26 17 57 38 01 69 37 04 85 31 50 32 "
            + "00 00 10 00 00 01 00 00 00 00 00 30 00 00 7A E0"));

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0, decoded.Number("validation_type"));
        Assert.Equal(1, decoded.Number("index"));
        Assert.Equal(7, decoded.Number("month"));
        Assert.Equal(20, decoded.Number("day"));
        Assert.Equal(2026, decoded.Number("year"));
        Assert.Equal(17, decoded.Number("hour"));
        Assert.Equal(57, decoded.Number("minute"));
        Assert.Equal(38, decoded.Number("second"));
        Assert.Equal(169370485315032, decoded.Number("validation_number"));
        Assert.Equal(100000, decoded.Number("amount"));

        // Two bytes of plain binary, least significant first: 01 00 is ticket one.
        Assert.Equal(1, decoded.Number("ticket_number"));
        Assert.Equal(0, decoded.Number("pool_id"));
    }

    /// <summary>
    /// The same command with nothing to report, which the library fills with zeros rather than
    /// refusing. Five of these are in the corpus and they are the reason every date field permits
    /// zero as well as a real month or day.
    /// </summary>
    [Fact]
    public void An_empty_validation_entry_decodes_without_a_constraint_being_broken()
    {
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x4D)!, Bytes(
            "01 4D 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 "
            + "00 00 00 00 00 00 00 00 00 00 00 00 00 00 F6 70"));

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.All(decoded.Values, value => Assert.Null(value.Violation));
    }

    // ---- $51, the number of games --------------------------------------------------------------

    /// <summary>
    /// The most-repeated reply in the whole corpus — a hundred and sixty-one copies of it in the
    /// toolkit's traces alone.
    /// </summary>
    [Fact]
    public void Long_poll_51_answers_two_bytes_of_binary_coded_decimal()
    {
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(
            SasLayouts.For(0x51)!, Bytes("01 51 00 09 45 58"));

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(9, decoded.Number("games"));
        Assert.Equal(6, SasLayouts.For(0x51)!.FixedResponseLength);
    }

    // ---- $86, the multiple level progressive broadcast -----------------------------------------

    /// <summary>
    /// From the toolkit's session log. The length byte counts the group byte plus six bytes per
    /// level, so two levels read <c>$0D</c> — and a reader that took it as a count of levels would
    /// walk off the end of the message.
    /// </summary>
    [Fact]
    public void Long_poll_86_broadcasts_one_amount_per_level()
    {
        SasDecoded decoded = SasMessageDecoder.DecodeRequest(SasLayouts.For(0x86)!,
            Bytes("00 86 0D 01 01 00 00 00 04 14 02 00 00 00 04 14 A2 90"));

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0x0D, decoded.Number("length"));
        Assert.Equal(1, decoded.Number("progressive_group"));
        Assert.Equal(2, decoded.Number("levels.count"));
        Assert.Equal(1, decoded.Number("levels[0].level"));
        Assert.Equal(414, decoded.Number("levels[0].amount"));
        Assert.Equal(2, decoded.Number("levels[1].level"));
        Assert.Equal(414, decoded.Number("levels[1].amount"));
    }

    /// <summary>
    /// It is a broadcast, and a broadcast is answered by nothing at all. Sent to one machine's own
    /// address the library acknowledges it, which is the shape recorded here.
    /// </summary>
    [Fact]
    public void The_progressive_broadcast_is_acknowledged_rather_than_answered_with_data()
    {
        SasMessageLayout layout = SasLayouts.For(0x86)!;

        Assert.Equal(SasResponseShape.Acknowledgement, layout.Shape);
        Assert.True(layout.Broadcast);
    }

    // ---- what was left alone -------------------------------------------------------------------

    /// <summary>
    /// The four remaining dedicated bill counts shared <c>lpXX_SendMeter</c> with the three
    /// established here, so writing them from the shape of their neighbours would have been easy.
    /// No capture in this corpus carries one, and they stayed undecoded on that ground until a
    /// sweep of a real machine produced the bytes — see <see cref="SweepLayoutTests"/>. What this
    /// still holds is the reason: the corpus does not prove them, and it is the sweep and not this
    /// corpus that does.
    /// </summary>
    [Theory]
    [InlineData(0x31)]
    [InlineData(0x32)]
    [InlineData(0x33)]
    [InlineData(0x34)]
    public void A_bill_count_this_corpus_does_not_carry_is_proved_by_the_sweep_instead(int command)
    {
        Assert.DoesNotContain(
            (byte)command,
            Corpus().SelectMany(c => c).Distinct());

        Assert.False(SasLayouts.IsKnownButUndecoded((byte)command));
        Assert.True(SasLayouts.For((byte)command)!.ConfirmedByCapture);
    }

    /// <summary>Every command byte this file quotes bytes for, so the claim above is checkable.</summary>
    private static IEnumerable<byte[]> Corpus() =>
    [
        [0x2F], [0x35], [0x36], [0x37], [0x4C], [0x4D], [0x51], [0x86], [0x1F], [0x54],
    ];
}
