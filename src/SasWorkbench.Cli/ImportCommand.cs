using System.Globalization;
using SasWorkbench.Core.Captures;
using SasWorkbench.Core.Reporting;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Cli;

/// <summary>
/// Reads traffic another tool recorded into the record a run of our own produces.
/// </summary>
/// <remarks>
/// <para>
/// Our own suite can only exchange what it was taught to exchange, so counting coverage over it
/// measures the script and not the protocol. This command lets the count be taken over traffic
/// nobody here authored, which does not flatter anything.
/// </para>
/// <para>
/// It writes the same machine-readable record a run writes, so <c>sasctl explain</c> reads an
/// import without knowing that captures exist, and the tally it prints is the same tally.
/// </para>
/// </remarks>
internal static class ImportCommand
{
    public static int Execute(CommandLine line)
    {
        if (line.Arguments.Count == 0)
        {
            Console.Error.WriteLine(
                "error: which capture? Expected: sasctl import <file-or-directory>... "
                + "[--out <dir>] [--limit <n>] [--report jsonl,extended]");
            return 2;
        }

        if (!TryReadLimit(line, out int? limit))
            return 2;

        if (!Reports.TryParse(line.Value("report") ?? "jsonl,extended",
                out ReportFormat[] formats, out string? unknown))
        {
            Console.Error.WriteLine(
                $"error: '{unknown}' is not a report this build writes. Available: {Reports.Names}");
            return 2;
        }

        if (!TryDiscover(line.Arguments, out List<string> paths))
            return 2;

        var captures = new List<Capture>(paths.Count);
        int unreadable = 0;
        foreach (string path in paths)
        {
            Capture capture = CaptureImporter.ReadFile(path, limit);
            captures.Add(capture);
            unreadable += capture.Unreadable.Count;
            Console.WriteLine($"read {capture.Messages.Count,9:N0} message(s)  "
                              + $"{Describe(capture.Format)}  {path}");

            // Named, never dropped: a reader that skipped what it did not understand would make a
            // partial import look like a whole one.
            foreach (string problem in capture.Unreadable.Take(10))
                Console.Error.WriteLine($"warning: {problem}");

            if (capture.Unreadable.Count > 10)
                Console.Error.WriteLine($"warning: and {capture.Unreadable.Count - 10} more "
                                        + $"item(s) in {path} that could not be read");
        }

        RunReport report = CaptureImporter.ToReport(captures);
        string directory = line.Value("out") ?? Path.Combine("runs", Stamp() + "-import");

        foreach (string written in Reports.Write(report, directory, formats))
            Console.WriteLine($"wrote {written}");

        Console.WriteLine();
        Tally(report);

        // An import proves nothing about this build, so it cannot come back as a success. Three is
        // the code a run that tested nothing already uses.
        return unreadable == 0 ? report.ExitCode : 2;
    }

    private static bool TryReadLimit(CommandLine line, out int? limit)
    {
        limit = null;
        if (line.Value("limit") is not { } asked)
            return true;

        if (!int.TryParse(asked, CultureInfo.InvariantCulture, out int count) || count <= 0)
        {
            Console.Error.WriteLine($"error: '--limit {asked}' is not a number of messages.");
            return false;
        }

        limit = count;
        return true;
    }

    private static bool TryDiscover(IReadOnlyList<string> arguments, out List<string> paths)
    {
        paths = [];
        foreach (string argument in arguments)
        {
            IReadOnlyList<string> found = CaptureImporter.Discover(argument);
            if (found.Count == 0)
            {
                Console.Error.WriteLine(
                    $"error: '{argument}' holds no capture this build reads. It reads the "
                    + "conformance toolkit's traces and its session log, and the legacy tester's "
                    + "serial log.");
                return false;
            }

            paths.AddRange(found);
        }

        paths = [.. paths.Distinct(StringComparer.OrdinalIgnoreCase)];
        return true;
    }

    /// <summary>
    /// The same count the extended log prints, said here so the number is on the screen that
    /// produced it. The second list is the one that means something: the first is dominated by the
    /// poll loop and reads as near-total coverage however little of the protocol is understood.
    /// </summary>
    private static void Tally(RunReport report)
    {
        var packets = new List<PacketExplanation>();
        foreach (TestOutcome test in report.Tests)
        {
            packets.AddRange(PacketExplainer.ExplainAll(test.Wire, address: null,
                wakeupBitCaptured: test.Provenance?.WakeupBitCaptured ?? true));
        }

        PacketCoverage coverage = PacketCoverage.Of(packets);
        Console.WriteLine("WHAT THE IMPORTED TRAFFIC EXPLAINED, COUNTED");
        Console.WriteLine($"  packets      {coverage.Packets,8:N0}");
        Line("explained in full", coverage.Explained, coverage.Packets);
        Line("known but not decoded", coverage.NamedOnly, coverage.Packets);
        Line("layout did not fit", coverage.LayoutDidNotFit, coverage.Packets);
        Line("not a message at all", coverage.NotAMessage, coverage.Packets);
        Line("not named at all", coverage.Unknown, coverage.Packets);
        Console.WriteLine();
        Console.WriteLine($"  long polls   {coverage.Commands,8:N0} distinct command(s)");
        Commands("explained in full", coverage.CommandsExplained, coverage.Commands);
        Commands("known but not decoded", coverage.CommandsNamedOnly, coverage.Commands);
        Commands("not named at all", coverage.CommandsUnknown, coverage.Commands);
        Console.WriteLine();
        Console.WriteLine(
            "  Each command listed as known but not decoded is a layout this build has not");
        Console.WriteLine(
            "  established. That list is what this traffic leaves unexplained.");
    }

    private static void Line(string label, int count, int total) =>
        Console.WriteLine($"    {label,-24}{count,8:N0}  ({Percent(count, total)})");

    private static void Commands(string label, IReadOnlyList<byte> commands, int total)
    {
        Console.WriteLine(
            $"    {label,-24}{commands.Count,8:N0}  ({Percent(commands.Count, total)})");
        if (commands.Count > 0)
            Console.WriteLine("      " + string.Join(' ', commands.Select(c => $"${c:X2}")));
    }

    private static string Percent(int count, int total) =>
        total == 0 ? "0%" : $"{count * 100.0 / total:F0}%";

    private static string Stamp() =>
        DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    private static string Describe(CaptureFormat format) => format switch
    {
        CaptureFormat.GsaTrace => "toolkit trace      ",
        CaptureFormat.GsaHostLog => "toolkit session log",
        CaptureFormat.IgtSerialLog => "legacy serial log  ",
        _ => "capture            ",
    };
}
