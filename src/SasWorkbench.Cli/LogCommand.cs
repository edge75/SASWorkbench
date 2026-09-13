using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;
using SasWorkbench.Core.Reporting;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Cli;

/// <summary>
/// Packs a run into a session container, reads one back, and reads a span of time out of the
/// middle of one without decompressing the rest.
/// </summary>
/// <remarks>
/// The commands exist in that order for a reason. Packing is only worth doing if unpacking gives
/// back exactly what went in, and the format is only worth having over a plain compressed file if
/// a span of time can be read out of the middle. Each of the three is the thing that justifies the
/// one before it.
/// </remarks>
internal static class LogCommand
{
    public static int Execute(CommandLine line)
    {
        string? action = line.Arguments.Count > 0 ? line.Arguments[0] : null;

        return action switch
        {
            "pack" => Pack(line),
            "unpack" => Unpack(line),
            "header" => Describe(line),
            "slice" => Slice(line),
            null => Usage("which action?"),
            _ => Usage($"'{action}' is not a log action"),
        };
    }

    /// <summary>The file an action names, which is the word after the action.</summary>
    private static string? Target(CommandLine line) =>
        line.Arguments.Count > 1 ? line.Arguments[1] : null;

    private static int Usage(string problem)
    {
        Console.Error.WriteLine(
            $"error: {problem}. Expected: sasctl log pack|unpack|header|slice <file> [options]");
        return 2;
    }

    /// <summary>Turns a run's JSON lines into one container file.</summary>
    private static int Pack(CommandLine line)
    {
        if (Target(line) is not { } source)
            return Usage("which run?");

        if (!File.Exists(source))
        {
            Console.Error.WriteLine($"error: there is no file at '{source}'.");
            return 2;
        }

        string target = line.Value("out")
                        ?? Path.ChangeExtension(source, SessionContainer.Extension);

        int blockBytes = SessionContainer.DefaultBlockBytes;
        if (line.Value("block") is { } size)
        {
            if (!int.TryParse(size, out blockBytes) || blockBytes < 1024)
            {
                Console.Error.WriteLine(
                    $"error: --block expects a whole number of bytes, at least 1024, and '{size}' "
                    + "is not one.");
                return 2;
            }
        }

        //  The header is built from the run itself rather than from the command line, so a
        //  container says what its own run said and cannot be labelled with something else.
        JsonLinesReader.Result read = JsonLinesReader.ReadFile(source);
        RunProvenance? first = read.Report.Provenance.FirstOrDefault();

        var header = new SessionContainer.ContainerHeader(
            Codec: "brotli",
            WrittenAt: DateTimeOffset.UtcNow,
            StartedAt: read.Report.StartedAt == default ? null : read.Report.StartedAt,
            Provenance: [.. read.Report.Provenance.Select(p => p.ToString())],
            Channels: first?.Channels,

            //  The round is read back off the run's own records rather than taken on trust: the
            //  machines that appear in it are the machines it polled, and a header saying anything
            //  else would describe a run somebody else made.
            Round: Round(read),
            CatalogueVersion: CatalogueVersion.Current);

        var source_info = new FileInfo(source);
        using FileStream destination = File.Create(target);
        SessionContainer.Directory written = SessionContainer.Write(
            File.ReadLines(source), destination, header, blockBytes);

        long packed = destination.Length;
        destination.Flush();

        Console.WriteLine($"packed {source} into {target}");
        Console.WriteLine();
        Console.WriteLine($"  records            {written.Lines,12:N0}");
        Console.WriteLine($"  blocks             {written.Blocks.Count,12:N0}");
        Console.WriteLine($"  plain text         {source_info.Length,12:N0} bytes");
        Console.WriteLine($"  container          {packed,12:N0} bytes");
        Console.WriteLine(
            $"  ratio              {source_info.Length / (double)packed,12:F2}× smaller");

        if (read.Problems.Count > 0)
        {
            // The run is packed either way — the container is a framing of whatever the file held,
            // not a judgement on it — but a file this build could not fully read is said so.
            Console.WriteLine();
            Console.WriteLine(
                $"  {read.Problems.Count} line(s) of the run were not fully readable by this "
                + "build. They are packed unchanged; nothing was dropped.");
        }

        return 0;
    }

    /// <summary>The machines a run's own records name, written the short way.</summary>
    private static string? Round(JsonLinesReader.Result read)
    {
        List<SasAddress> machines =
        [
            .. read.Report.Tests
                .SelectMany(test => test.Wire)
                .Select(record => record.Address)
                .OfType<SasAddress>()
                .Distinct()
                .OrderBy(address => address.Value),
        ];

        return machines.Count == 0 ? null : SasAddressRound.Describe(machines);
    }

    /// <summary>Turns a container back into the JSON lines that went into it.</summary>
    private static int Unpack(CommandLine line)
    {
        if (Target(line) is not { } source)
            return Usage("which container?");

        using FileStream file = File.OpenRead(source);
        SessionContainer.Directory directory = SessionContainer.ReadDirectory(file);

        string? target = line.Value("out");
        if (target is null)
        {
            foreach (string record in SessionContainer.ReadAll(file, directory))
                Console.WriteLine(record);

            return 0;
        }

        using (StreamWriter writer = File.CreateText(target))
        {
            foreach (string record in SessionContainer.ReadAll(file, directory))
                writer.WriteLine(record);
        }

        Console.WriteLine($"wrote {directory.Lines:N0} record(s) to {target}");
        return 0;
    }

    /// <summary>Says what a container holds, without decompressing any of its body.</summary>
    private static int Describe(CommandLine line)
    {
        if (Target(line) is not { } source)
            return Usage("which container?");

        using FileStream file = File.OpenRead(source);
        SessionContainer.Directory directory = SessionContainer.ReadDirectory(file);
        SessionContainer.ContainerHeader header = directory.Header;

        Console.WriteLine($"{source}");
        Console.WriteLine();
        Console.WriteLine("WHAT THIS RUN WAS, READ WITHOUT DECOMPRESSING ANY OF IT");
        Console.WriteLine($"  written            {header.WrittenAt:u}");
        Console.WriteLine($"  run started        "
                          + (header.StartedAt is { } began ? $"{began:u}" : "the record does not say"));
        Console.WriteLine($"  catalogue          {header.CatalogueVersion ?? "not recorded"}");

        if (header.Channels is not null)
            Console.WriteLine($"  arrangement        {header.Channels}");

        if (header.Round is not null)
            Console.WriteLine($"  round              {header.Round}");

        foreach (string provenance in header.Provenance)
            Console.WriteLine($"  carried over       {provenance}");

        Console.WriteLine();
        Console.WriteLine("WHAT IS IN IT");
        Console.WriteLine($"  records            {directory.Lines,12:N0}");
        Console.WriteLine($"  blocks             {directory.Blocks.Count,12:N0}");
        Console.WriteLine($"  plain text         {directory.UncompressedBytes,12:N0} bytes");
        Console.WriteLine($"  compressed body    {directory.CompressedBytes,12:N0} bytes");
        Console.WriteLine($"  file               {file.Length,12:N0} bytes");

        if (directory.UncompressedBytes > 0)
        {
            Console.WriteLine(
                $"  ratio              {directory.UncompressedBytes / (double)file.Length,12:F2}× smaller");
        }

        if (directory.FirstMs is { } from && directory.LastMs is { } to)
        {
            Console.WriteLine(
                $"  covers             {from / 1000:F1}s to {to / 1000:F1}s of the run");
        }

        return 0;
    }

    /// <summary>
    /// Reads a span of time out of a container, and says how much of the file it had to touch.
    /// </summary>
    /// <remarks>
    /// The count of blocks read is printed because it is the claim the format makes. A slice that
    /// decompressed every block would be a file that is merely compressed, not one that is
    /// seekable, and the number is what tells those two apart.
    /// </remarks>
    private static int Slice(CommandLine line)
    {
        if (Target(line) is not { } source)
            return Usage("which container?");

        if (!TrySeconds(line, "from", 0, out double from)
            || !TrySeconds(line, "to", double.MaxValue / 2, out double to))
        {
            return 2;
        }

        if (to < from)
        {
            Console.Error.WriteLine("error: --to is before --from.");
            return 2;
        }

        using FileStream file = File.OpenRead(source);
        SessionContainer.Directory directory = SessionContainer.ReadDirectory(file);

        IReadOnlyList<string> records = SessionContainer.ReadRange(
            file, directory, from * 1000, to * 1000, out int blocksRead);

        string? target = line.Value("out");
        if (target is not null)
        {
            File.WriteAllLines(target, records);
            Console.WriteLine($"wrote {records.Count:N0} record(s) to {target}");
        }
        else
        {
            foreach (string record in records)
                Console.WriteLine(record);
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine(
            $"  {records.Count:N0} record(s) from {blocksRead} of {directory.Blocks.Count} "
            + "block(s). The rest of the file was never decompressed.");

        return 0;
    }

    private static bool TrySeconds(CommandLine line, string name, double fallback, out double value)
    {
        value = fallback;
        if (line.Value(name) is not { } text)
            return true;

        if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        Console.Error.WriteLine(
            $"error: --{name} expects a number of seconds into the run, and '{text}' is not one.");
        return false;
    }
}
