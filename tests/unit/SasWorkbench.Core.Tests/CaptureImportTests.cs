using System.Buffers.Binary;
using SasWorkbench.Core.Captures;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Reporting;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// Reading traffic nobody here authored.
/// </summary>
/// <remarks>
/// The fixtures are written inline rather than taken from the corpora on the author's machine, so
/// these run everywhere and a missing corpus cannot turn into a test that passes by doing nothing.
/// The shapes they use were verified against the real files; where a count is quoted in a comment,
/// it was measured over the whole corpus.
/// </remarks>
public class CaptureImportTests
{
    private static readonly string[] Trace =
    [
        "Trace started at 07/20/26 10:01:32",
        string.Empty,
        "Timestamp is relative to end of previous poll",
        string.Empty,
        "HOST   -------   [01] 51 ",
        "                 Send total number of games implemented: ",
        "EGM       23.0   01 51 00 09 45 58 ",
        "                 Send total number of games implemented: Number of Games = 0009; ",
        "----------",
        "HOST      42.0   [81] ",
        "                 General Poll",
        "EGM       25.1   6A ",
        "                 AFT request for host cashout",
    ];

    /// <summary>
    /// The bracket is the ninth bit. Measured over the thirty-two traces the toolkit ships: a
    /// bracket appears only ever on a line's first token, on all 2,005 host lines and on 13 of 945
    /// machine lines — which a marker for the start of a printed line could not be.
    /// </summary>
    [Fact]
    public void A_traces_brackets_are_the_ninth_bit()
    {
        Capture capture = GsaTraceReader.Parse(Trace, "trace");

        Assert.True(capture.WakeupBitCaptured);
        Assert.Equal("1.01 0.51", Hex(capture.Messages[0]));
        Assert.Equal("0.01 0.51 0.00 0.09 0.45 0.58", Hex(capture.Messages[1]));
        Assert.Equal("1.81", Hex(capture.Messages[2]));
    }

    [Fact]
    public void A_traces_directions_are_the_host_and_the_machine()
    {
        Capture capture = GsaTraceReader.Parse(Trace, "trace");

        Assert.Equal(
            [WireDirection.Transmit, WireDirection.Receive, WireDirection.Transmit,
                WireDirection.Receive],
            capture.Messages.Select(m => m.Direction));
    }

    /// <summary>
    /// A trace's intervals are each measured from the end of the previous poll, so a message's
    /// place in the capture is their running total. Reading each one as a time from the start would
    /// pile every message onto the first second.
    /// </summary>
    [Fact]
    public void A_traces_intervals_accumulate()
    {
        Capture capture = GsaTraceReader.Parse(Trace, "trace");

        Assert.Equal(
            [0.0, 23.0, 65.0, 90.1],
            capture.Messages.Select(m => Math.Round(m.At.TotalMilliseconds, 1)));
    }

    /// <summary>
    /// Every capture format carries the other tool's own description of each message, and none of
    /// it is taken. The words this product ships are its own, and a description written elsewhere
    /// would be indistinguishable from one of ours once it was in the record.
    /// </summary>
    [Fact]
    public void The_other_tools_own_prose_is_not_taken_into_the_record()
    {
        Capture capture = GsaTraceReader.Parse(Trace, "trace");

        Assert.Equal(4, capture.Messages.Count);
        Assert.All(capture.ToWire(), record => Assert.Null(record.Note));
    }

    /// <summary>
    /// A line this build cannot read is named, not skipped. A reader that dropped what it did not
    /// understand would make a partial import look like a whole one.
    /// </summary>
    [Fact]
    public void A_line_that_cannot_be_read_is_named_rather_than_dropped()
    {
        Capture capture = GsaTraceReader.Parse(
            [.. Trace, "HOST      10.0   [01] ZZ "], "trace");

        Assert.Equal(4, capture.Messages.Count);
        string problem = Assert.Single(capture.Unreadable);
        Assert.Contains("trace:14", problem, StringComparison.Ordinal);
        Assert.Contains("hexadecimal", problem, StringComparison.Ordinal);
    }

    /// <summary>
    /// Direction one is the host and two is the machine, corrected against the bytes: over the log
    /// the toolkit ships, 723 records carry one, 645 carry two and none carries the zero an earlier
    /// note on this format gave for the machine.
    /// </summary>
    [Fact]
    public void A_session_logs_directions_are_one_for_the_host_and_two_for_the_machine()
    {
        byte[] log = [.. Message(index: 1, ticks: 1000, direction: 1, [0x01, 0x1F]),
                      .. Message(index: 2, ticks: 1025, direction: 2, [0x00])];

        Capture capture = GsaHostLogReader.Parse(log, "log");

        Assert.Empty(capture.Unreadable);
        Assert.Equal(
            [WireDirection.Transmit, WireDirection.Receive],
            capture.Messages.Select(m => m.Direction));
        Assert.Equal([0.0, 25.0], capture.Messages.Select(m => m.At.TotalMilliseconds));
    }

    [Fact]
    public void A_session_log_direction_this_build_has_not_seen_is_refused_rather_than_guessed()
    {
        byte[] log = [.. Message(index: 1, ticks: 1000, direction: 1, [0x01, 0x1F]),
                      .. Message(index: 2, ticks: 1025, direction: 0, [0x00])];

        Capture capture = GsaHostLogReader.Parse(log, "log");

        Assert.Single(capture.Messages);
        Assert.Contains("neither the host nor the machine",
            Assert.Single(capture.Unreadable), StringComparison.Ordinal);
    }

    /// <summary>
    /// A version word of one and a length is not a signature: the toolkit's own configuration files
    /// begin that way, and reading one as a log produced pages of warnings about records that were
    /// never records.
    /// </summary>
    [Fact]
    public void A_file_that_merely_starts_like_a_session_log_is_not_one()
    {
        // Version one, then a length, then bytes that are not a record kind — the shape of the
        // configuration files that used to be picked up.
        byte[] notALog = [0x01, 0x00, 0x07, 0x00, 0x00, 0x00, 0x01, 0x00, 0x07];

        Assert.False(GsaHostLogReader.Looks(notALog));
    }

    [Fact]
    public void A_real_session_logs_first_records_are_recognised()
    {
        byte[] log = [.. Message(index: 1, ticks: 1000, direction: 1, [0x01, 0x1F]),
                      .. Message(index: 2, ticks: 1025, direction: 2, [0x00]),
                      .. Message(index: 3, ticks: 1200, direction: 1, [0x81])];

        Assert.True(GsaHostLogReader.Looks(log));
    }

    /// <summary>
    /// The legacy log's clock is whole seconds, so every message inside one second shares a time.
    /// Filling that in would invent the intervals a poll loop is judged by.
    /// </summary>
    [Fact]
    public void The_legacy_logs_whole_second_clock_is_carried_as_it_is()
    {
        Capture capture = IgtSerialLogReader.Parse(
            [
                "Tue Feb 25 09:44:31 2025",
                "TX>= 01 1F",
                "RX<= 01 1F 51 58",
                "Tue Feb 25 09:44:33 2025",
                "TX>= 81",
            ],
            "legacy");

        Assert.False(capture.WakeupBitCaptured);
        Assert.Equal([0.0, 0.0, 2000.0], capture.Messages.Select(m => m.At.TotalMilliseconds));
        Assert.Contains("whole seconds", capture.TimingCaveat, StringComparison.Ordinal);
    }

    [Fact]
    public void A_limit_is_recorded_so_part_of_a_file_cannot_pass_for_all_of_it()
    {
        Capture capture = IgtSerialLogReader.Parse(
            ["Tue Feb 25 09:44:31 2025", "TX>= 81", "TX>= 80", "TX>= 81"], "legacy", limit: 2);

        Assert.Equal(2, capture.Messages.Count);
        Assert.Contains("not all of it", capture.TimingCaveat, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reading somebody else's traffic proves nothing about this build, so an import must not be
    /// able to look like a pass. A run in which nothing was asserted already carries its own
    /// non-zero exit code.
    /// </summary>
    [Fact]
    public void An_import_is_a_skipped_run_and_never_a_pass()
    {
        RunReport report = CaptureImporter.ToReport([GsaTraceReader.Parse(Trace, "trace")]);

        Assert.Equal(Verdict.Skip, report.Verdict);
        Assert.NotEqual(0, report.ExitCode);
        Assert.Equal(0, report.Passed);
        Assert.Contains("nothing here asserted anything",
            Assert.Single(report.Tests).SkipReason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where a recording kept only the eight data bits, the record says so and the explanation
    /// reports nothing about the wakeup bit either way — otherwise every host message in such a
    /// capture reads as one no machine would have answered, which is a finding about the recorder
    /// dressed up as a finding about the machine.
    /// </summary>
    [Fact]
    public void A_capture_without_the_ninth_bit_is_not_read_as_a_link_that_lost_it()
    {
        byte[] log = Message(index: 1, ticks: 1000, direction: 1, [0x01, 0x1F]);
        RunReport report = CaptureImporter.ToReport([GsaHostLogReader.Parse(log, "log")]);

        Assert.False(Assert.Single(report.Tests).Provenance!.WakeupBitCaptured);

        string explained = new ExtendedLogWriter().Render(report);

        Assert.DoesNotContain("does not carry the wakeup bit", explained, StringComparison.Ordinal);
        Assert.Contains("only eight data bits", explained, StringComparison.Ordinal);
    }

    /// <summary>
    /// A run's two renderings are asserted byte-identical because the extended log is a rendering
    /// of the record and not a second capture. An import goes through the same record, so the same
    /// equality has to hold for it — and a field the importer sets but the record does not carry
    /// fails here rather than in six months.
    /// </summary>
    [Fact]
    public void An_imports_two_renderings_are_byte_identical()
    {
        RunReport imported = CaptureImporter.ToReport(
        [
            GsaTraceReader.Parse(Trace, "trace"),
            GsaHostLogReader.Parse(Message(1, 1000, 1, [0x01, 0x1F]), "log"),
        ]);

        string live = new ExtendedLogWriter().Render(imported);
        JsonLinesReader.Result read = JsonLinesReader.Read(
            new JsonLinesReportWriter().Render(imported)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries));

        Assert.Empty(read.Problems);
        Assert.Equal(live, new ExtendedLogWriter().Render(read.Report));
    }

    private static string Hex(CapturedMessage message) =>
        string.Join(' ', message.Symbols.Select(s => s.ToString()));

    /// <summary>One message record of the session log, built to the layout the real files use.</summary>
    private static byte[] Message(ushort index, uint ticks, byte direction, byte[] bytes)
    {
        var payload = new byte[10 + (bytes.Length * 3)];
        payload[0] = 0x44;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1), index);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(3), ticks);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(7), (ushort)bytes.Length);
        payload[9] = direction;
        for (int i = 0; i < bytes.Length; i++)
        {
            payload[10 + (i * 3)] = bytes[i];
            BinaryPrimitives.WriteUInt16LittleEndian(
                payload.AsSpan(11 + (i * 3)), (ushort)(ticks + i));
        }

        var record = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0), 0x0001);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(2),
            (uint)new DateTimeOffset(2026, 7, 20, 10, 1, 32, TimeSpan.Zero).ToUnixTimeSeconds());
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6), (ushort)payload.Length);
        payload.CopyTo(record, 8);
        return record;
    }
}
