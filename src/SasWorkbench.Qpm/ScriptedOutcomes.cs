using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SasWorkbench.Qpm;

/// <summary>
/// The host's half of the demo's scripted outcomes (D64): the marker a scripted build leaves, the queue the host writes
/// and the log of every game draw the build reads it from.
/// </summary>
/// <remarks>
/// The demo's half is <c>tools/qxcom-tcp/src/saswb_scripted_rng.cpp</c>, built only by the scripted preset. Nothing here
/// trusts that a value was used because it was queued: a step is confirmed by the build's own log line for its outcome.
/// </remarks>
public sealed class ScriptedOutcomes(string? directory = null)
{
    /// <summary>Where the scripted preset's build reads its queue unless it was configured otherwise.</summary>
    public const string DefaultDirectory = @"C:\ProgramData\saswb\outcomes";

    /// <summary>How a result from a scripted build is labelled.</summary>
    public const string Label = "scripted outcomes (dev build)";

    private const string QueueFile = "queue.txt";
    private const string LogFile = "draws.log";
    private const string MarkerFile = "scripted-outcomes.marker";

    private static int s_next;

    public string Directory { get; } = Path.GetFullPath(directory ?? DefaultDirectory);

    /// <summary>Why the machine running is not a scripted build, or null when a live one left its marker.</summary>
    /// <param name="declaredExecutable">The executable the run named, which the marker must then be from; null when none.</param>
    public string? WhyNotScripted(string? declaredExecutable = null)
    {
        string marker = Path.Combine(Directory, MarkerFile);
        Dictionary<string, string> fields;
        try
        {
            fields = File.ReadAllLines(marker)
                .Select(line => line.Split('=', 2))
                .Where(pair => pair.Length == 2)
                .ToDictionary(pair => pair[0].Trim(), pair => pair[1].Trim(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            return $"the machine is not a scripted-outcomes build: there is no marker at {marker} (only the "
                   + "SasWorkbenchM0-Scripted preset leaves one, and only while it runs)";
        }

        if (!fields.TryGetValue("pid", out string? pidText)
            || !int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
        {
            return $"the scripted-outcomes marker at {marker} names no process";
        }

        if (Alive(pid, fields.GetValueOrDefault("created")) is { } gone)
            return $"the scripted-outcomes marker at {marker} was left by process {pid}, which {gone}";

        if (declaredExecutable is not null
            && !string.Equals(fields.GetValueOrDefault("exe"), Path.GetFullPath(declaredExecutable), StringComparison.OrdinalIgnoreCase))
        {
            return $"the scripted build running is {fields.GetValueOrDefault("exe")}, not the declared {declaredExecutable}";
        }

        return null;
    }

    /// <summary>How many lines the draw log holds now, so a step reads only what follows.</summary>
    public int LogLineCount() => ReadLog().Length;

    /// <summary>Queues one outcome for the next game, replacing anything left over, and returns its id.</summary>
    public string Queue(IReadOnlyList<OutcomeAlternative> alternatives)
    {
        ArgumentNullException.ThrowIfNull(alternatives);
        if (alternatives.Count == 0)
            throw new ArgumentException("an outcome needs at least one alternative", nameof(alternatives));

        string id = string.Create(CultureInfo.InvariantCulture,
            $"h{Environment.ProcessId}-{Interlocked.Increment(ref s_next)}");
        string line = id + " " + string.Join(' ', alternatives.Select(a => a.ToQueueText()));

        System.IO.Directory.CreateDirectory(Directory);
        string written = Path.Combine(Directory, QueueFile + ".new");
        File.WriteAllText(written, line + "\n", Encoding.ASCII);
        File.Move(written, Path.Combine(Directory, QueueFile), overwrite: true);
        return id;
    }

    /// <summary>Takes back an outcome no game drew, so a later game cannot draw it.</summary>
    public void Withdraw()
    {
        try
        {
            File.Delete(Path.Combine(Directory, QueueFile));
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            // The demo may hold it for the moment it takes; what it then draws is in the log.
        }
    }

    /// <summary>
    /// Waits for the log to say the outcome was drawn in full; null when it was, otherwise why not.
    /// </summary>
    public string? WaitForDrawn(string id, int skip, TimeSpan within)
    {
        long deadline = Environment.TickCount64 + (long)within.TotalMilliseconds;
        do
        {
            foreach (string line in ReadLog().Skip(skip))
            {
                string text = Text(line);
                if (text.StartsWith($"done {id} ", StringComparison.Ordinal))
                    return null;
                if (text.StartsWith($"refused {id}:", StringComparison.Ordinal))
                    return $"the machine refused the scripted outcome: {text}";
            }

            Thread.Sleep(50);
        }
        while (Environment.TickCount64 < deadline);

        return $"no game drew the scripted outcome within {within.TotalSeconds:F0}s (its log is {Path.Combine(Directory, LogFile)})";
    }

    /// <summary>A log line without its time.</summary>
    private static string Text(string line)
    {
        int space = line.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? line : line[(space + 1)..];
    }

    private string[] ReadLog()
    {
        string path = Path.Combine(Directory, LogFile);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Null when the process runs and is the one that wrote the marker; otherwise what is wrong.</summary>
    private static string? Alive(int pid, string? created)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            if (process.HasExited)
                return "has exited";

            // Zero where the build could not read its creation time; the process id alone then has to do.
            if (long.TryParse(created, NumberStyles.None, CultureInfo.InvariantCulture, out long fileTime) && fileTime != 0
                && Math.Abs(process.StartTime.ToFileTimeUtc() - fileTime) > TimeSpan.TicksPerSecond)
            {
                return "has exited (the id now belongs to another process)";
            }

            return null;
        }
        catch (ArgumentException)
        {
            return "is no longer running";
        }
        catch (Exception problem) when (problem is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return $"cannot be checked: {problem.Message}";
        }
    }
}
