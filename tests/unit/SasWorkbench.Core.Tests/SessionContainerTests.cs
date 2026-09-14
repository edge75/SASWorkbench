using System.Text;
using SasWorkbench.Core.Reporting;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// The container: a readable header, blocks that decompress on their own, and an index that makes
/// the file seekable by time.
/// </summary>
/// <remarks>
/// The property the whole format rests on is the first fact here — what comes out is what went in.
/// A compression layer that is not exactly reversible is not a framing of the canonical record,
/// it is a second record, and then there are two things that have to be right instead of one.
/// </remarks>
public class SessionContainerTests
{
    private static SessionContainer.ContainerHeader Header() => new(
        Codec: "brotli",
        WrittenAt: new DateTimeOffset(2026, 9, 13, 21, 0, 0, TimeSpan.Zero),
        StartedAt: new DateTimeOffset(2026, 9, 13, 20, 0, 0, TimeSpan.Zero),
        Provenance: ["multi-drop loopback, 100 machine(s), proven, stimulus none"],
        Channels: "channels 1@01 general_control=1",
        Round: "01-64",
        CatalogueVersion: "2026-09-12");

    /// <summary>A run of wire records, the shape a floor session is almost entirely made of.</summary>
    private static List<string> Lines(int count, double everyMs = 31)
    {
        var lines = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            int machine = (i % 100) + 1;
            lines.Add(
                $$"""{"kind":"wire","test":"a floor","sequence":{{i}},"direction":"{{(i % 3 == 2 ? "Receive" : "Transmit")}}","channel":1,"address":{{machine}},"atMs":{{(i * everyMs).ToString(System.Globalization.CultureInfo.InvariantCulture)}},"symbols":"1.{{machine:X2}}","note":null}""");
        }

        return lines;
    }

    private static MemoryStream Packed(
        IReadOnlyList<string> lines, int blockBytes, out SessionContainer.Directory written)
    {
        var file = new MemoryStream();
        written = SessionContainer.Write(lines, file, Header(), blockBytes);
        file.Position = 0;
        return file;
    }

    // ---- the load-bearing property ------------------------------------------------------------

    /// <summary>
    /// What comes out of a container is exactly what went in.
    /// </summary>
    /// <remarks>
    /// JSON lines stays the canonical record and this is a framing over it. If unpacking produced
    /// anything but the same lines, the container would be a second recording — and then there
    /// would be two artefacts that have to be right rather than one.
    /// </remarks>
    [Fact]
    public void WhatComesOutOfAContainerIsExactlyWhatWentIn()
    {
        List<string> lines = Lines(5000);

        using MemoryStream file = Packed(lines, 8192, out _);
        SessionContainer.Directory directory = SessionContainer.ReadDirectory(file);

        Assert.Equal(lines, SessionContainer.ReadAll(file, directory).ToList());
    }

    /// <summary>
    /// Unpacked to a file, a container is the same bytes as the file it was packed from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The test above compares the lines, which is not the same claim and cannot catch the way
    /// this actually broke: the records were right and the file was not, because the writer
    /// followed the platform's newline and turned every \n into \r\n on Windows. A run packed on
    /// one machine and unpacked on another came back with a different hash, which is exactly the
    /// check the format's whole claim rests on.
    /// </para>
    /// <para>
    /// Compared as bytes rather than as text on purpose. Reading both back as strings would
    /// normalise the very thing that was wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public void UnpackedToAFileAContainerIsTheSameBytesItWasPackedFrom()
    {
        List<string> lines = Lines(2000);

        string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            string original = Path.Combine(directory, "run.jsonl");
            string unpacked = Path.Combine(directory, "again.jsonl");

            SessionContainer.WriteLines(original, lines);

            using MemoryStream file = Packed(lines, 8192, out _);
            SessionContainer.Directory index = SessionContainer.ReadDirectory(file);
            SessionContainer.WriteLines(unpacked, SessionContainer.ReadAll(file, index));

            Assert.Equal(File.ReadAllBytes(original), File.ReadAllBytes(unpacked));

            // And the line ending is the one the canonical record uses, on every platform, rather
            // than whichever one this platform happens to prefer.
            Assert.DoesNotContain((byte)13, File.ReadAllBytes(unpacked));
        }
        finally
        {
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The header is readable without any of the body being decompressed.</summary>
    [Fact]
    public void TheHeaderIsReadableWithoutTouchingTheBody()
    {
        using MemoryStream file = Packed(Lines(5000), 8192, out _);

        SessionContainer.ContainerHeader header = SessionContainer.ReadDirectory(file).Header;

        Assert.Equal("brotli", header.Codec);
        Assert.Equal("channels 1@01 general_control=1", header.Channels);
        Assert.Equal("01-64", header.Round);
        Assert.Equal("2026-09-12", header.CatalogueVersion);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 20, 0, 0, TimeSpan.Zero), header.StartedAt);
    }

    /// <summary>
    /// The header is plain text at the front of the file, so a tool that knows nothing about this
    /// format can still read what the run was worth.
    /// </summary>
    [Fact]
    public void TheHeaderIsPlainTextAtTheFrontOfTheFile()
    {
        using MemoryStream file = Packed(Lines(1000), 8192, out _);

        string front = Encoding.UTF8.GetString(file.ToArray().AsSpan(0, 400));

        Assert.StartsWith("SASWB1\n", front, StringComparison.Ordinal);
        Assert.Contains("\"Round\":\"01-64\"", front, StringComparison.Ordinal);
    }

    // ---- blocks that stand alone ---------------------------------------------------------------

    /// <summary>
    /// A block decompresses on its own, with nothing before it having been read.
    /// </summary>
    /// <remarks>
    /// This is what "independently decompressible" has to mean to be worth anything. A single
    /// compressed stream over the whole body would compress better and would make the middle of an
    /// hour unreachable without decompressing the half-hour in front of it.
    /// </remarks>
    [Fact]
    public void AnyBlockDecompressesOnItsOwn()
    {
        List<string> lines = Lines(5000);
        using MemoryStream file = Packed(lines, 8192, out SessionContainer.Directory written);

        Assert.True(written.Blocks.Count >= 3,
            $"the test needs several blocks and got {written.Blocks.Count}");

        SessionContainer.Directory directory = SessionContainer.ReadDirectory(file);
        SessionContainer.BlockIndex last = directory.Blocks[^1];

        IReadOnlyList<string> read = SessionContainer.ReadBlock(file, last);

        Assert.Equal(last.Lines, read.Count);
        Assert.Equal(lines[^1], read[^1]);
    }

    /// <summary>Every record is in exactly one block, and the index counts them all.</summary>
    [Fact]
    public void TheIndexAccountsForEveryRecord()
    {
        List<string> lines = Lines(5000);
        using MemoryStream file = Packed(lines, 8192, out _);

        SessionContainer.Directory directory = SessionContainer.ReadDirectory(file);

        Assert.Equal(lines.Count, directory.Lines);
        Assert.Equal(
            lines.Sum(l => Encoding.UTF8.GetByteCount(l) + 1),
            directory.UncompressedBytes);
    }

    /// <summary>Each block's index says the span of time the block actually covers.</summary>
    [Fact]
    public void EachBlockReportsTheSpanOfTimeItCovers()
    {
        using MemoryStream file = Packed(Lines(5000), 8192, out _);
        SessionContainer.Directory directory = SessionContainer.ReadDirectory(file);

        foreach (SessionContainer.BlockIndex block in directory.Blocks)
        {
            IReadOnlyList<string> read = SessionContainer.ReadBlock(file, block);
            double[] times = [.. read.Select(SessionContainer.TimeOf).OfType<double>()];

            Assert.Equal(times.Min(), block.FirstMs);
            Assert.Equal(times.Max(), block.LastMs);
        }

        // And the blocks run forward, so a reader can stop once it is past the span it wants.
        for (int i = 1; i < directory.Blocks.Count; i++)
            Assert.True(directory.Blocks[i].FirstMs >= directory.Blocks[i - 1].LastMs);
    }

    // ---- seeking by time ------------------------------------------------------------------------

    /// <summary>
    /// A span of time is read by decompressing only the blocks that overlap it.
    /// </summary>
    /// <remarks>
    /// The reason the footer carries timestamps at all. An hour of floor traffic with the
    /// interesting thirty seconds in the middle is the case this format was written for.
    /// </remarks>
    [Fact]
    public void ASpanOfTimeReadsOnlyTheBlocksThatOverlapIt()
    {
        List<string> lines = Lines(5000);
        using MemoryStream file = Packed(lines, 8192, out _);
        SessionContainer.Directory directory = SessionContainer.ReadDirectory(file);

        // A window near the end, far from where a reader starting at the front would be.
        double from = 4800 * 31;
        double to = 4850 * 31;

        IReadOnlyList<string> read = SessionContainer.ReadRange(
            file, directory, from, to, out int blocksRead);

        Assert.Equal(lines.Skip(4800).Take(51), read);
        Assert.True(blocksRead < directory.Blocks.Count,
            $"every one of {directory.Blocks.Count} block(s) was read for a window inside one");
    }

    /// <summary>A span with nothing in it comes back empty, having read nothing.</summary>
    [Fact]
    public void ASpanWithNothingInItReadsNoBlocks()
    {
        using MemoryStream file = Packed(Lines(5000), 8192, out _);
        SessionContainer.Directory directory = SessionContainer.ReadDirectory(file);

        IReadOnlyList<string> read = SessionContainer.ReadRange(
            file, directory, 10_000_000, 20_000_000, out int blocksRead);

        Assert.Empty(read);
        Assert.Equal(0, blocksRead);
    }

    // ---- what it refuses -------------------------------------------------------------------------

    /// <summary>A file that is not a container says so rather than producing bytes.</summary>
    [Fact]
    public void AFileThatIsNotAContainerIsRefused()
    {
        using var plain = new MemoryStream(Encoding.UTF8.GetBytes("{\"kind\":\"run\"}\n"));

        InvalidDataException refused = Assert.Throws<InvalidDataException>(
            () => SessionContainer.ReadDirectory(plain));

        Assert.Contains("not a session container", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A container cut short while it was being written says so, and does not guess at its index.
    /// </summary>
    /// <remarks>
    /// The failure a long floor session actually has: the machine is stopped, or the disk fills,
    /// halfway through an hour. The header is still readable and is still true; the body is not
    /// indexed, and inventing an index would hand somebody records nothing wrote.
    /// </remarks>
    [Fact]
    public void AContainerCutShortSaysSoRatherThanGuessing()
    {
        using MemoryStream whole = Packed(Lines(5000), 8192, out _);
        byte[] bytes = whole.ToArray();

        using var truncated = new MemoryStream(bytes[..(bytes.Length / 2)]);

        InvalidDataException refused = Assert.Throws<InvalidDataException>(
            () => SessionContainer.ReadDirectory(truncated));

        Assert.Contains("truncated", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A body compressed with a codec this build does not read is refused by name.</summary>
    [Fact]
    public void ABodyInACodecThisBuildCannotReadIsRefusedByName()
    {
        using MemoryStream file = Packed(Lines(100), 8192, out _);
        byte[] bytes = file.ToArray();

        string text = Encoding.UTF8.GetString(bytes);
        int at = text.IndexOf("\"brotli\"", StringComparison.Ordinal);
        Assert.True(at > 0);

        // Same length, so every offset in the index still points where it did.
        Encoding.UTF8.GetBytes("\"zstdxx\"").CopyTo(bytes, at);

        using var swapped = new MemoryStream(bytes);
        InvalidDataException refused = Assert.Throws<InvalidDataException>(
            () => SessionContainer.ReadDirectory(swapped));

        Assert.Contains("zstdxx", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>An empty run still makes a readable container with a readable header.</summary>
    [Fact]
    public void ARunWithNoRecordsIsStillAReadableContainer()
    {
        using MemoryStream file = Packed([], 8192, out _);

        SessionContainer.Directory directory = SessionContainer.ReadDirectory(file);

        Assert.Empty(directory.Blocks);
        Assert.Equal(0, directory.Lines);
        Assert.Equal("01-64", directory.Header.Round);
    }

    // ---- read like any other run ---------------------------------------------------------------

    /// <summary>
    /// A container is read by the ordinary run reader, without anything being told it is one.
    /// </summary>
    /// <remarks>
    /// Decided here rather than in each command, so every reader of a run — the extended log, a
    /// replay, a recording turned into a script — takes a container without knowing the format
    /// exists. That is what keeps it a framing over the canonical record rather than a second kind
    /// of run with its own set of tools.
    /// </remarks>
    [Fact]
    public void AContainerIsReadByTheOrdinaryRunReader()
    {
        string[] run =
        [
            """{"kind":"run","startedAt":"2026-09-13T20:00:00+00:00","durationMs":1000,"provenance":[{"transport":"multi-drop loopback","fidelity":"Proven","stimulus":"None","channels":"channels 1@01","channelsRead":true}],"capabilities":[]}""",
            """{"kind":"test","name":"a floor","file":"floor.sas","line":1,"verdict":"Pass","durationMs":900}""",
            """{"kind":"wire","test":"a floor","sequence":0,"direction":"Transmit","channel":1,"address":7,"atMs":10,"symbols":"1.87"}""",
        ];

        string path = Path.Combine(Path.GetTempPath(), $"container-{Guid.NewGuid():N}.saslog");
        try
        {
            using (FileStream file = File.Create(path))
                SessionContainer.Write(run, file, Header(), 1024);

            Assert.True(JsonLinesReader.IsContainer(path));

            JsonLinesReader.Result read = JsonLinesReader.ReadFile(path);

            Assert.Empty(read.Problems);
            Assert.Equal("a floor", read.Report.Tests[0].Name);
            Assert.True(read.Report.Provenance[0].ChannelsRead);
            Assert.Equal(
                SasWorkbench.Core.Wire.SasAddress.Of(7),
                read.Report.Tests[0].Wire[0].Address);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Plain JSON lines are still plain JSON lines, and are not taken for a container.</summary>
    [Fact]
    public void APlainRunIsNotTakenForAContainer()
    {
        string path = Path.Combine(Path.GetTempPath(), $"plain-{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllText(path,
                """{"kind":"test","name":"t","file":"t.sas","line":1,"verdict":"Pass","durationMs":1}"""
                + Environment.NewLine);

            Assert.False(JsonLinesReader.IsContainer(path));
            Assert.Equal("t", JsonLinesReader.ReadFile(path).Report.Tests[0].Name);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
