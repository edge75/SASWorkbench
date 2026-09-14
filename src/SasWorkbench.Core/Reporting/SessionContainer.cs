using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace SasWorkbench.Core.Reporting;

/// <summary>
/// A session log as one file: a header anybody can read without decompressing anything, a body of
/// independently-decompressible blocks, and a footer saying where each block is and what span of
/// time it covers.
/// </summary>
/// <remarks>
/// <para>
/// JSON lines stays the canonical record. This is a framing and compression layer over exactly
/// those lines, and unpacking one produces the same bytes that went in — so nothing is readable
/// only through this format, and a file can always be turned back into text.
/// </para>
/// <para>
/// The three parts each earn their place. The header is uncompressed so a tool can say what a run
/// was worth — the link, the arrangement, the clock base — without paying for the body. The blocks
/// are independent so the file is seekable by time rather than only readable from the start: a
/// floor session is an hour of polls and the interesting thirty seconds are somewhere in the
/// middle. The footer carries each block's first and last timestamp, which is what makes finding
/// those thirty seconds a seek rather than a scan.
/// </para>
/// <para>
/// Compression is Brotli, which ships in the framework on both platforms, so the file needs no
/// native dependency to read. The codec is named in the header rather than assumed, so a file
/// written by a build that chose differently says so instead of decompressing into nonsense.
/// </para>
/// </remarks>
public static class SessionContainer
{
    /// <summary>The file's first bytes, and its last, so it is recognisable from either end.</summary>
    public static ReadOnlySpan<byte> Magic => "SASWB1\n"u8;

    /// <summary>The extension these files carry.</summary>
    public const string Extension = ".saslog";

    /// <summary>How much uncompressed text goes into one block, unless told otherwise.</summary>
    public const int DefaultBlockBytes = 1 << 20;

    private const string Codec = "brotli";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>What a block holds, as the footer index records it.</summary>
    /// <param name="Offset">Where the block starts in the file.</param>
    /// <param name="CompressedBytes">How long the block is on disk.</param>
    /// <param name="UncompressedBytes">How much text it decompresses to.</param>
    /// <param name="Lines">How many records it holds.</param>
    /// <param name="FirstMs">The first record's time, in milliseconds, or null where none said.</param>
    /// <param name="LastMs">The last record's time, in milliseconds, or null where none said.</param>
    public sealed record BlockIndex(
        long Offset,
        int CompressedBytes,
        int UncompressedBytes,
        int Lines,
        double? FirstMs,
        double? LastMs);

    /// <summary>What the uncompressed header says, without the body having been touched.</summary>
    /// <param name="Codec">How the blocks are compressed.</param>
    /// <param name="WrittenAt">When the container was written.</param>
    /// <param name="StartedAt">The run's clock base, so a relative time can be put on a watch.</param>
    /// <param name="Provenance">What the run was carried over, one line each.</param>
    /// <param name="Channels">How the machine was divided between hosts, when the run said.</param>
    /// <param name="Round">The machines the host polled, when the run said.</param>
    /// <param name="CatalogueVersion">Which protocol catalogue the run was read against.</param>
    public sealed record ContainerHeader(
        string Codec,
        DateTimeOffset WrittenAt,
        DateTimeOffset? StartedAt,
        IReadOnlyList<string> Provenance,
        string? Channels,
        string? Round,
        string? CatalogueVersion);

    /// <summary>A container opened for reading: its header, and where everything in it is.</summary>
    public sealed record Directory(ContainerHeader Header, IReadOnlyList<BlockIndex> Blocks)
    {
        /// <summary>How many records the whole file holds.</summary>
        public long Lines => Blocks.Sum(b => (long)b.Lines);

        /// <summary>How much text the body decompresses to.</summary>
        public long UncompressedBytes => Blocks.Sum(b => (long)b.UncompressedBytes);

        /// <summary>How much of the file the body occupies.</summary>
        public long CompressedBytes => Blocks.Sum(b => (long)b.CompressedBytes);

        /// <summary>The earliest time any block reports, or null where none does.</summary>
        public double? FirstMs => Blocks.Select(b => b.FirstMs).OfType<double>().DefaultMin();

        /// <summary>The latest time any block reports, or null where none does.</summary>
        public double? LastMs => Blocks.Select(b => b.LastMs).OfType<double>().DefaultMax();
    }

    /// <summary>
    /// Writes JSON lines into a container.
    /// </summary>
    /// <param name="lines">The records, one per line, exactly as the JSON-lines writer wrote them.</param>
    /// <param name="destination">Where to write. Left open.</param>
    /// <param name="header">What the uncompressed header should say.</param>
    /// <param name="blockBytes">How much uncompressed text goes into one block.</param>
    /// <returns>What went into the file, block by block.</returns>
    public static Directory Write(
        IEnumerable<string> lines,
        Stream destination,
        ContainerHeader header,
        int blockBytes = DefaultBlockBytes)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockBytes, 1024);

        destination.Write(Magic);
        WriteLine(destination, JsonSerializer.Serialize(header with { Codec = Codec }, Json));

        var blocks = new List<BlockIndex>();
        var pending = new StringBuilder();
        int pendingLines = 0;
        double? first = null;
        double? last = null;

        foreach (string line in lines)
        {
            if (line.Length == 0)
                continue;

            pending.Append(line).Append('\n');
            pendingLines++;

            if (TimeOf(line) is { } at)
            {
                first ??= at;
                last = at;
            }

            if (pending.Length < blockBytes)
                continue;

            blocks.Add(Flush(destination, pending, pendingLines, first, last));
            pending.Clear();
            pendingLines = 0;
            first = null;
            last = null;
        }

        if (pendingLines > 0)
            blocks.Add(Flush(destination, pending, pendingLines, first, last));

        long footerAt = destination.Position;
        WriteLine(destination, JsonSerializer.Serialize(blocks, Json));

        // The footer's own offset, then the magic again: a reader that has only the end of the
        // file can still find the index without scanning forward through the body.
        Span<byte> trailer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(trailer, footerAt);
        destination.Write(trailer);
        destination.Write(Magic);

        return new Directory(header with { Codec = Codec }, blocks);
    }

    /// <summary>
    /// Reads the header and the block index without decompressing any of the body.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a container, or its index is gone.</exception>
    public static Directory ReadDirectory(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.CanSeek)
            throw new InvalidDataException("A container has to be read from a seekable stream: "
                                           + "its index is at the end.");

        source.Position = 0;
        Span<byte> magic = stackalloc byte[Magic.Length];
        source.ReadExactly(magic);
        if (!magic.SequenceEqual(Magic))
        {
            throw new InvalidDataException(
                "This is not a session container: it does not start with the format's own mark. "
                + "A plain JSON-lines run is read with the ordinary reader instead.");
        }

        string headerLine = ReadLine(source)
                            ?? throw new InvalidDataException(
                                "The container's header is missing, so nothing can be said about "
                                + "what this run was worth.");

        ContainerHeader header = JsonSerializer.Deserialize<ContainerHeader>(headerLine, Json)
                                 ?? throw new InvalidDataException(
                                     "The container's header is not readable.");

        if (!string.Equals(header.Codec, Codec, StringComparison.OrdinalIgnoreCase))
        {
            // Named rather than assumed. Decompressing with the wrong codec produces bytes, and
            // bytes that are not the run are worse than a refusal.
            throw new InvalidDataException(
                $"This container's body is compressed with '{header.Codec}', which this build "
                + $"cannot read; it reads '{Codec}'.");
        }

        if (source.Length < Magic.Length + 8)
            throw new InvalidDataException("The container is too short to hold an index.");

        source.Position = source.Length - Magic.Length;
        Span<byte> tail = stackalloc byte[Magic.Length];
        source.ReadExactly(tail);
        if (!tail.SequenceEqual(Magic))
        {
            throw new InvalidDataException(
                "The container does not end with the format's own mark, so it was truncated "
                + "while being written. The header above it is still readable; the body is not "
                + "indexed and nothing here will guess at where the blocks are.");
        }

        source.Position = source.Length - Magic.Length - 8;
        Span<byte> offset = stackalloc byte[8];
        source.ReadExactly(offset);
        long footerAt = BinaryPrimitives.ReadInt64LittleEndian(offset);

        if (footerAt < Magic.Length || footerAt >= source.Length)
            throw new InvalidDataException("The container's index points outside the file.");

        source.Position = footerAt;
        string footerLine = ReadLine(source)
                            ?? throw new InvalidDataException("The container's index is missing.");

        IReadOnlyList<BlockIndex> blocks =
            JsonSerializer.Deserialize<List<BlockIndex>>(footerLine, Json)
            ?? throw new InvalidDataException("The container's index is not readable.");

        return new Directory(header, blocks);
    }

    /// <summary>Decompresses one block on its own, without touching any other.</summary>
    public static IReadOnlyList<string> ReadBlock(Stream source, BlockIndex block)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(block);

        var compressed = new byte[block.CompressedBytes];
        source.Position = block.Offset;
        source.ReadExactly(compressed);

        using var input = new MemoryStream(compressed, writable: false);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(brotli, Encoding.UTF8);

        var lines = new List<string>(block.Lines);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
                lines.Add(line);
        }

        return lines;
    }

    /// <summary>Reads the whole body back as the JSON lines that went in.</summary>
    public static IEnumerable<string> ReadAll(Stream source, Directory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        foreach (BlockIndex block in directory.Blocks)
        {
            foreach (string line in ReadBlock(source, block))
                yield return line;
        }
    }

    /// <summary>
    /// Reads only the records inside a span of time, decompressing only the blocks that overlap it.
    /// </summary>
    /// <remarks>
    /// The property the footer index exists for. A block whose last timestamp is before the span
    /// begins, or whose first is after it ends, is never read off disk at all — which is what makes
    /// an hour of floor traffic searchable rather than merely stored.
    /// </remarks>
    /// <param name="fromMs">The start of the span, in the run's own milliseconds.</param>
    /// <param name="toMs">The end of the span, inclusive.</param>
    /// <param name="blocksRead">How many blocks had to be decompressed.</param>
    public static IReadOnlyList<string> ReadRange(
        Stream source, Directory directory, double fromMs, double toMs, out int blocksRead)
    {
        ArgumentNullException.ThrowIfNull(directory);

        blocksRead = 0;
        var lines = new List<string>();

        foreach (BlockIndex block in directory.Blocks)
        {
            // A block that reports no times at all is read, because nothing in the index rules it
            // out and skipping it would silently drop records the caller asked for.
            bool overlaps = block.FirstMs is not { } first || block.LastMs is not { } last
                            || (last >= fromMs && first <= toMs);

            if (!overlaps)
                continue;

            blocksRead++;
            foreach (string line in ReadBlock(source, block))
            {
                if (TimeOf(line) is not { } at || (at >= fromMs && at <= toMs))
                    lines.Add(line);
            }
        }

        return lines;
    }

    private static BlockIndex Flush(
        Stream destination, StringBuilder pending, int lines, double? first, double? last)
    {
        byte[] text = Encoding.UTF8.GetBytes(pending.ToString());
        long offset = destination.Position;

        // Each block is a complete Brotli stream of its own. That is the whole point: a reader can
        // seek to one and decompress it without having decompressed anything before it.
        using (var buffer = new MemoryStream())
        {
            using (var brotli = new BrotliStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
                brotli.Write(text);

            byte[] compressed = buffer.ToArray();
            destination.Write(compressed);
            return new BlockIndex(offset, compressed.Length, text.Length, lines, first, last);
        }
    }

    /// <summary>
    /// The time a record carries, in milliseconds, or null where it carries none.
    /// </summary>
    /// <remarks>
    /// Public because it is the rule the index is built on, and a reader checking that a block's
    /// reported span matches the records inside it has to apply the same rule.
    /// </remarks>
    /// <remarks>
    /// Read off the text rather than by parsing the whole record into an object: the writer handles
    /// hundreds of thousands of lines and a JSON document per line to find one number is the
    /// difference between a container that can be written during a run and one that cannot.
    /// </remarks>
    /// <summary>
    /// Writes records back out as JSON lines, exactly as they were packed.
    /// </summary>
    /// <remarks>
    /// The line ending is pinned rather than left to the platform, which is the same rule a ticket
    /// file follows and for a stronger reason: what comes out of a container has to be what went
    /// in, byte for byte, or the container is a second record rather than a framing over the first.
    /// A writer that follows the platform's newline makes the same run a different file on Windows
    /// than on Linux, and the check that would have caught it — a hash of the two — is exactly the
    /// check the format's whole claim rests on.
    /// </remarks>
    public static void WriteLines(string path, IEnumerable<string> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        using var writer = new StreamWriter(path) { NewLine = LineEnding };
        foreach (string record in records)
            writer.WriteLine(record);
    }

    /// <summary>The one line ending a container's records are written with, on every platform.</summary>
    public const string LineEnding = "\n";

    public static double? TimeOf(string line)
    {
        const string key = "\"atMs\":";
        int at = line.IndexOf(key, StringComparison.Ordinal);
        if (at < 0)
            return null;

        int start = at + key.Length;
        int end = start;
        while (end < line.Length && (char.IsAsciiDigit(line[end]) || line[end] is '.' or '-' or '+'
                                     || line[end] is 'e' or 'E'))
        {
            end++;
        }

        return double.TryParse(line[start..end],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double value)
            ? value
            : null;
    }

    private static void WriteLine(Stream destination, string text)
    {
        destination.Write(Encoding.UTF8.GetBytes(text));
        destination.WriteByte((byte)'\n');
    }

    /// <summary>Reads one newline-terminated line from where the stream is.</summary>
    private static string? ReadLine(Stream source)
    {
        var bytes = new List<byte>(4096);
        while (true)
        {
            int read = source.ReadByte();
            if (read < 0)
                return bytes.Count == 0 ? null : Encoding.UTF8.GetString([.. bytes]);
            if (read == '\n')
                return Encoding.UTF8.GetString([.. bytes]);

            bytes.Add((byte)read);
        }
    }

    private static double? DefaultMin(this IEnumerable<double> values)
    {
        double? best = null;
        foreach (double value in values)
            best = best is { } held ? Math.Min(held, value) : value;
        return best;
    }

    private static double? DefaultMax(this IEnumerable<double> values)
    {
        double? best = null;
        foreach (double value in values)
            best = best is { } held ? Math.Max(held, value) : value;
        return best;
    }
}
