using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Events;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Reporting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// The real-time event format: what a machine answers a general poll with once the host has asked
/// for reporting, and what each exception carries after its code.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of evidence, kept apart on purpose. The frame itself is confirmed against bytes
/// <c>QSP_DemoOpenGL</c> sent on 2026-09-08 — eleven distinct exceptions, copied verbatim with
/// their check sequences. The ten payloads are read out of the machine library's own builders and
/// no run here has produced one, so what is asserted about them is that this build reads the bytes
/// those builders write, and nothing more.
/// </para>
/// <para>
/// A payload case therefore builds its bytes the way the library builds them — the same
/// conversions, in the same order — rather than copying a machine's answer. That is a weaker
/// check than the sweep's and it is labelled as such wherever the coverage is reported.
/// </para>
/// </remarks>
public class RealTimeEventTests
{
    private static byte[] Bytes(string hex) => Convert.FromHexString(hex.Replace(" ", string.Empty));

    /// <summary>
    /// Every event the real machine sent while reporting was on, verbatim.
    /// </summary>
    /// <remarks>
    /// All eleven carry nothing after the code, which is what most exceptions do. What they
    /// establish is the frame: the machine's address, the marker, the code and a check sequence
    /// over the three.
    /// </remarks>
    public static TheoryData<string, byte> FromTheMachine => new()
    {
        { "01 FF 11 14 A4", 0x11 },
        { "01 FF 12 8F 96", 0x12 },
        { "01 FF 15 30 E2", 0x15 },
        { "01 FF 16 AB D0", 0x16 },
        { "01 FF 17 22 C1", 0x17 },
        { "01 FF 18 D5 39", 0x18 },
        { "01 FF 19 5C 28", 0x19 },
        { "01 FF 1A C7 1A", 0x1A },
        { "01 FF 3C F3 5E", 0x3C },
        { "01 FF 82 06 02", 0x82 },
        { "01 FF 83 8F 13", 0x83 },
    };

    [Theory]
    [MemberData(nameof(FromTheMachine))]
    public void An_event_the_machine_sent_checks_out_and_names_its_exception(string hex, byte code)
    {
        byte[] frame = Bytes(hex);

        Assert.True(SasCrc.Verify(frame));
        Assert.Equal(SasEventLayouts.Marker, frame[1]);
        Assert.Equal(code, frame[2]);
        Assert.Equal(SasEventLayouts.HeaderLength + 2, frame.Length);
    }

    [Theory]
    [MemberData(nameof(FromTheMachine))]
    public void An_event_carrying_nothing_is_explained_in_full(string hex, byte code)
    {
        PacketExplanation packet = Explain(Bytes(hex));

        Assert.Equal(PacketMeaning.Explained, packet.Meaning);
        Assert.True(packet.IsRealTimeEvent);
        Assert.Equal(code, packet.Exception);
        Assert.Contains($"${code:X2}", packet.Headline, StringComparison.Ordinal);
    }

    /// <summary>
    /// The bare code and the event carry the same exception, so a script written against one runs
    /// against the other.
    /// </summary>
    [Fact]
    public void The_same_exception_is_named_the_same_way_in_both_formats()
    {
        PacketExplanation asEvent = Explain(Bytes("01 FF 11 14 A4"));
        PacketExplanation asCode = Explain([0x11], address: SasAddress.Of(1));

        Assert.Equal(asCode.Exception, asEvent.Exception);
        Assert.Equal(PacketMeaning.Explained, asCode.Meaning);
        Assert.False(asCode.IsRealTimeEvent);
    }

    /// <summary>
    /// Zero is the machine saying it has nothing queued. It is not an exception and must not be
    /// counted as one, or a quiet machine would look like one reporting continuously.
    /// </summary>
    [Fact]
    public void No_activity_in_the_event_format_is_not_an_exception()
    {
        PacketExplanation packet = Explain(Framed(0x00));

        Assert.Equal(PacketMeaning.Explained, packet.Meaning);
        Assert.True(packet.IsRealTimeEvent);
        Assert.Null(packet.Exception);
        Assert.Contains("no exception", packet.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void A_note_event_reports_the_currency_the_denomination_and_the_count()
    {
        // Country 37 and denomination 4 as the library writes them: each converted to binary coded
        // decimal one byte at a time, then the meter as four bytes of the same.
        SasDecoded decoded = Decode(0x4F, "37 04 00 00 00 12");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(37, decoded.Number("country_code"));
        Assert.Equal(4, decoded.Number("denomination_code"));
        Assert.Equal(12, decoded.Number("bill_meter"));
    }

    /// <summary>
    /// The two units in one event that a validation case wanted this layout for: a wager is a
    /// number of game credits and the meter beside it is in accounting units, so reading either as
    /// the other is a mistake no test could otherwise catch.
    /// </summary>
    [Fact]
    public void A_game_start_reports_the_wager_and_the_meter_in_different_units()
    {
        SasDecoded decoded = Decode(0x7E, "00 05 00 00 04 00 02 00");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(5, decoded.Number("credits_wagered"));
        Assert.Equal(400, decoded.Number("total_coin_in"));
        Assert.Equal(2, decoded.Number("wager_type"));
        Assert.Equal(0, decoded.Number("progressive_group"));
    }

    [Fact]
    public void A_game_end_reports_what_the_game_paid()
    {
        SasDecoded decoded = Decode(0x7F, "00 00 12 50");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(1250, decoded.Number("win_amount"));
    }

    [Fact]
    public void A_reel_event_reports_which_reel_and_where_it_stopped()
    {
        SasDecoded decoded = Decode(0x88, "03 11");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(3, decoded.Number("reel_number"));
        Assert.Equal(0x11, decoded.Number("physical_stop"));
    }

    [Fact]
    public void A_legacy_bonus_reports_the_multiplied_win_and_the_bonus_apart()
    {
        SasDecoded decoded = Decode(0x7C, "02 00 00 10 00 01 00 00 05 00");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(2, decoded.Number("multiplier"));
        Assert.Equal(1000, decoded.Number("multiplied_win"));
        Assert.Equal(1, decoded.Number("tax_status"));
        Assert.Equal(500, decoded.Number("bonus_amount"));
    }

    /// <summary>
    /// The two the library writes differently from the other eight, recorded rather than smoothed
    /// over: their numbers are plain binary where every other builder converts to binary coded
    /// decimal first, and they begin with a byte holding the length of what follows.
    /// </summary>
    [Fact]
    public void A_session_start_is_binary_and_length_prefixed()
    {
        SasDecoded decoded = Decode(0x9E, "09 00 01 02 00 00 03 E8 00 07");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(9, decoded.Number("length"));
        Assert.Equal(1, decoded.Number("game_number"));
        Assert.Equal(2, decoded.Number("denomination"));
        Assert.Equal(1000, decoded.Number("wager"));
        Assert.Equal(7, decoded.Number("games_in_session"));
    }

    [Fact]
    public void A_session_end_is_binary_and_length_prefixed()
    {
        SasDecoded decoded = Decode(0x9F, "0E 00 0A 00 00 00 02 00 00 01 F4 00 00 00 64");

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(14, decoded.Number("length"));
        Assert.Equal(10, decoded.Number("games"));
        Assert.Equal(2, decoded.Number("sessions"));
        Assert.Equal(500, decoded.Number("session_wins"));
        Assert.Equal(100, decoded.Number("extra_coin_in"));
    }

    [Fact]
    public void Every_payload_says_where_it_was_read_from()
    {
        foreach (SasEventLayout layout in SasEventLayouts.All.Values)
        {
            Assert.False(string.IsNullOrWhiteSpace(layout.DerivedFrom));
            Assert.NotEmpty(layout.Payload);
        }
    }

    /// <summary>
    /// A payload the plan does not carry would be one this build reads and cannot say how to see,
    /// and a plan entry with no payload would be one it names and cannot read. Either way the two
    /// have drifted, and a coverage figure taken over one of them would be wrong.
    /// </summary>
    [Fact]
    public void The_plan_and_the_payloads_describe_the_same_exceptions()
    {
        Assert.Equal(
            SasEventLayouts.All.Keys.Order(),
            RealTimeEventPlan.All.Select(e => e.Code).Order());
    }

    /// <summary>
    /// No payload here has been seen on a link. Asserted rather than left to a comment, so the day
    /// a measurement produces one the flag has to be set deliberately and this test says so.
    /// </summary>
    [Fact]
    public void No_payload_is_yet_confirmed_against_a_machine()
    {
        Assert.Empty(SasEventLayouts.All.Values.Where(e => e.ConfirmedByCapture));
    }

    /// <summary>
    /// Two of the four exceptions the library will only send in the event format are ones nothing
    /// in it ever queues, so no machine running it can produce either and no payload for them can
    /// be established here at all.
    /// </summary>
    [Fact]
    public void The_two_exceptions_nothing_queues_have_no_payload()
    {
        foreach (byte code in SasEventLayouts.NeverQueued.Keys)
        {
            Assert.True(SasEventLayouts.RealTimeOnly.Contains(code));
            Assert.Null(SasEventLayouts.For(code));
            Assert.Null(RealTimeEventPlan.For(code));
        }
    }

    /// <summary>
    /// An exception carrying bytes this build cannot read is named and not decoded, and the bytes
    /// are reported rather than interpreted.
    /// </summary>
    [Fact]
    public void An_event_with_no_established_payload_is_named_and_not_read()
    {
        PacketExplanation packet = Explain(Framed(0x69, 0x01, 0x02, 0x03));

        Assert.Equal(PacketMeaning.NamedOnly, packet.Meaning);
        Assert.Equal<byte?>(0x69, packet.Exception);
        Assert.NotNull(packet.Limit);
    }

    /// <summary>
    /// A payload that is longer than the layout describes is reported as a trailer rather than
    /// silently ignored, the same rule a reply follows.
    /// </summary>
    [Fact]
    public void Bytes_beyond_the_payload_are_reported()
    {
        PacketExplanation packet = Explain(Framed(0x7F, 0x00, 0x00, 0x12, 0x50, 0xAB));

        Assert.Equal(PacketMeaning.Explained, packet.Meaning);
        Assert.Contains(packet.Findings, f => f.Contains("beyond", StringComparison.Ordinal));
    }

    /// <summary>
    /// A frame whose check sequence does not check out establishes nothing, the exception code
    /// included. The same rule a reply follows, for the same reason.
    /// </summary>
    [Fact]
    public void An_event_that_fails_its_check_sequence_is_not_read_as_one()
    {
        PacketExplanation packet = Explain(Bytes("01 FF 11 00 00"));

        Assert.Equal(PacketMeaning.NotAMessage, packet.Meaning);
        Assert.Null(packet.Exception);
    }

    /// <summary>The coverage tally counts events apart from long polls, and only real ones.</summary>
    [Fact]
    public void Events_are_counted_in_their_own_tally()
    {
        var coverage = PacketCoverage.Of(
        [
            Explain(Bytes("01 FF 11 14 A4")),
            Explain(Framed(0x69, 0x01, 0x02, 0x03)),
            Explain([0x11], address: SasAddress.Of(1)),
        ]);

        Assert.Equal(2, coverage.Events);
        Assert.Equal<byte>([0x11], coverage.EventsExplained);
        Assert.Equal<byte>([0x69], coverage.EventsNamedOnly);
        Assert.Equal(0, coverage.Commands);
    }

    private static SasDecoded Decode(byte code, string payload)
    {
        SasEventLayout? layout = SasEventLayouts.For(code);
        Assert.NotNull(layout);
        return SasMessageDecoder.DecodeEvent(layout, Framed(code, Bytes(payload)));
    }

    /// <summary>Wraps a payload the way a machine frames one, check sequence and all.</summary>
    private static byte[] Framed(byte code, params byte[] payload)
    {
        byte[] frame = new byte[SasEventLayouts.HeaderLength + payload.Length + 2];
        frame[0] = 0x01;
        frame[1] = SasEventLayouts.Marker;
        frame[2] = code;
        payload.CopyTo(frame, SasEventLayouts.HeaderLength);

        ushort crc = SasCrc.Compute(frame.AsSpan(0, frame.Length - 2));
        frame[^2] = SasCrc.LowByte(crc);
        frame[^1] = SasCrc.HighByte(crc);
        return frame;
    }

    private static PacketExplanation Explain(byte[] bytes, SasAddress? address = null)
    {
        var record = new WireRecord(
            1, WireDirection.Receive, SasChannel.One, TimeSpan.Zero,
            [.. bytes.Select(SasSymbol.Data8)], null);

        return new PacketExplainer(address ?? SasAddress.Of(1)).Explain(record);
    }
}
