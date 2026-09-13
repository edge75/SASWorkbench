using SasWorkbench.Core.Captures;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Reporting;

namespace SasWorkbench.Cli;

/// <summary>One recorded session: what it was called, and the packets in it.</summary>
/// <param name="Name">The test it came from, or the file it came from.</param>
/// <param name="Records">The packets, in the order they were recorded.</param>
internal sealed record RecordedSession(string Name, IReadOnlyList<WireRecord> Records);

/// <summary>
/// Reads a file as either a run of our own or a capture somebody else wrote.
/// </summary>
/// <remarks>
/// One reader, because three commands now want the same two inputs and a reader that only handled
/// our own recordings would be no use for the traffic these are worth doing on. A run of a suite
/// comes back as one session per case, which is what lets a caller work on one case at a time —
/// a suite reconfigures the machine between cases, so read as a single recording it describes a
/// machine that never existed.
/// </remarks>
internal static class RecordedSessions
{
    public static bool TryRead(
        string path,
        int? limit,
        out IReadOnlyList<RecordedSession> sessions,
        out string source)
    {
        sessions = [];
        source = string.Empty;

        if (CaptureImporter.Detect(path) is { } format)
        {
            Capture capture = CaptureImporter.ReadFile(path, limit);
            source = format switch
            {
                CaptureFormat.GsaTrace => "the conformance toolkit's trace",
                CaptureFormat.GsaHostLog => "the conformance toolkit's session log",
                _ => "the legacy tester's serial log",
            };

            sessions = [new RecordedSession(Path.GetFileName(path), Cap(capture.ToWire(), limit))];
            return true;
        }

        JsonLinesReader.Result read = JsonLinesReader.ReadFile(path);
        foreach (string problem in read.Problems)
            Console.Error.WriteLine($"warning: {problem}");

        if (read.IsEmpty)
        {
            Console.Error.WriteLine(
                $"error: '{path}' is neither a capture this build reads nor a run written with "
                + "--report jsonl.");
            return false;
        }

        source = "a run written by this build";
        sessions =
        [
            .. read.Report.Tests
                .Where(test => test.Wire.Count > 0)
                .Select(test => new RecordedSession(test.Name, Cap(test.Wire, limit))),
        ];

        if (sessions.Count == 0)
        {
            Console.Error.WriteLine(
                $"error: '{path}' describes a run that recorded no packets, so there is nothing "
                + "to read.");
            return false;
        }

        return true;
    }

    private static IReadOnlyList<WireRecord> Cap(IReadOnlyList<WireRecord> records, int? limit) =>
        limit is { } count && records.Count > count ? [.. records.Take(count)] : records;
}
