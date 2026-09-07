using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;

namespace SasWorkbench.Core.Captures;

/// <summary>
/// Turns captures written by other tools into the record a run of our own produces.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the same record, built the same way: a capture becomes a
/// <see cref="RunReport"/>, and everything downstream — the machine-readable record, the extended
/// log, the coverage tally — is the code that already existed. That is what makes
/// <c>sasctl explain</c> work on imported traffic without being told about captures at all, and it
/// is why the two renderings of an import stay byte-identical for the same reason a run's do.
/// </para>
/// <para>
/// An imported capture asserts nothing, so it is a <see cref="Verdict.Skip"/> with the reason
/// spelled out. A run whose every case is skipped is already a skipped run with a non-zero exit
/// code, which is exactly the honest answer: reading somebody else's traffic proves nothing about
/// this build, and it must not be able to look like a pass.
/// </para>
/// </remarks>
public static class CaptureImporter
{
    /// <summary>Which reader a file goes to, decided by what the file is rather than by its name.</summary>
    public static CaptureFormat? Detect(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
            return null;

        // The binary log is the only one of the three that is not text, and it is recognised by
        // walking its first records rather than by a two-byte signature: the toolkit's own
        // configuration files start the same way, and reading one as a log produced pages of
        // warnings about records that were never records.
        byte[] head = new byte[4096];
        using (FileStream file = File.OpenRead(path))
        {
            int read = file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            if (GsaHostLogReader.Looks(head.AsSpan(0, read)))
                return CaptureFormat.GsaHostLog;
        }

        foreach (string line in File.ReadLines(path).Take(40))
        {
            if (line.StartsWith("Trace started at", StringComparison.Ordinal)
                || line.StartsWith("HOST ", StringComparison.Ordinal)
                || line.StartsWith("EGM  ", StringComparison.Ordinal))
                return CaptureFormat.GsaTrace;

            if (line.StartsWith("TX>= ", StringComparison.Ordinal)
                || line.StartsWith("RX<= ", StringComparison.Ordinal))
                return CaptureFormat.IgtSerialLog;
        }

        return null;
    }

    /// <summary>Reads one file, whichever of the three formats it is in.</summary>
    /// <exception cref="NotSupportedException">The file is in none of them.</exception>
    public static Capture ReadFile(string path, int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Detect(path) switch
        {
            CaptureFormat.GsaTrace => GsaTraceReader.Read(path),
            CaptureFormat.GsaHostLog => GsaHostLogReader.Read(path),
            CaptureFormat.IgtSerialLog => IgtSerialLogReader.Read(path, limit),
            _ => throw new NotSupportedException(
                $"'{path}' is not a capture in any format this build reads. It reads the "
                + "conformance toolkit's traces and session log, and the legacy tester's "
                + "serial log."),
        };
    }

    /// <summary>Every capture under a path, whether it names a file or a directory.</summary>
    public static IReadOnlyList<string> Discover(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (File.Exists(path))
            return [path];

        if (!Directory.Exists(path))
            return [];

        return
        [
            .. Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(file => Detect(file) is not null)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>Builds the record for a set of captures.</summary>
    public static RunReport ToReport(IReadOnlyList<Capture> captures)
    {
        ArgumentNullException.ThrowIfNull(captures);

        var tests = new List<TestOutcome>(captures.Count);
        foreach (Capture capture in captures)
        {
            RunProvenance provenance = ProvenanceFor(capture);
            tests.Add(new TestOutcome
            {
                Name = capture.Source,
                Location = new SourceLocation(capture.Source, 1),
                Verdict = Verdict.Skip,
                SkipReason = "imported traffic: this is a recording another tool made, so nothing "
                             + "here asked for any of it and nothing here asserted anything about "
                             + "it. It is read to find out what the protocol contains, not to "
                             + "produce a verdict.",
                Tags = [Tag(capture.Format)],
                StartedAt = capture.StartedAt ?? default,
                Duration = capture.Duration,
                Wire = capture.ToWire(),
                Provenance = provenance,
            });
        }

        return new RunReport
        {
            Tests = tests,
            StartedAt = captures.Select(c => c.StartedAt).OfType<DateTimeOffset>()
                .DefaultIfEmpty(DateTimeOffset.UtcNow).Min(),
            Duration = tests.Select(t => t.Duration).DefaultIfEmpty(TimeSpan.Zero).Max(),
            Provenance = [.. tests.Select(t => t.Provenance!).Distinct()],
            Capabilities = [],
        };
    }

    private static RunProvenance ProvenanceFor(Capture capture)
    {
        var caveat = new List<string>
        {
            "Recorded by " + Tool(capture.Format) + ", not by this build: nothing here drove this "
            + "link and no verdict was reached on it.",
        };

        if (!capture.WakeupBitCaptured)
            caveat.Add("The recording kept only eight data bits, so where the wakeup bit was is not "
                       + "in the file and is not claimed.");

        if (capture.TimingCaveat is { } timing)
            caveat.Add(timing);

        if (capture.Unreadable.Count > 0)
            caveat.Add($"{capture.Unreadable.Count} item(s) in the file could not be read; they are "
                       + "named where the import was run.");

        return new RunProvenance(
            $"imported {Tag(capture.Format)} {capture.Source}",
            TransportFidelity.Unverified,
            StimulusBackend.None,
            string.Join(" ", caveat))
        {
            WakeupBitCaptured = capture.WakeupBitCaptured,
        };
    }

    private static string Tag(CaptureFormat format) => format switch
    {
        CaptureFormat.GsaTrace => "@imported-toolkit-trace",
        CaptureFormat.GsaHostLog => "@imported-toolkit-log",
        CaptureFormat.IgtSerialLog => "@imported-legacy-serial-log",
        _ => "@imported",
    };

    private static string Tool(CaptureFormat format) => format switch
    {
        CaptureFormat.GsaTrace => "the conformance toolkit's tracer",
        CaptureFormat.GsaHostLog => "the conformance toolkit's session logger",
        CaptureFormat.IgtSerialLog => "the legacy tester",
        _ => "another tool",
    };
}
