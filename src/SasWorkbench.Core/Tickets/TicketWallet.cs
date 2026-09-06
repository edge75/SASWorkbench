namespace SasWorkbench.Core.Tickets;

/// <summary>
/// The wallet: a directory holding one ticket per file, read from the host's side of it.
/// </summary>
/// <remarks>
/// Three programs share this directory. The gaming machine's printer writes a ticket into it, the
/// peripheral simulator shows what is in it and lets a person add one by hand, and this is the
/// third: the test host, which reads it to assert on what was printed and writes into it to stage
/// a ticket for insertion. A directory of small files is the only form all three agree on without
/// one of them having to be running.
///
/// A file that will not read is reported by name rather than skipped, because a wallet quietly
/// holding one fewer ticket than the directory does is how a test comes to pass for the wrong
/// reason.
/// </remarks>
public sealed class TicketWallet
{
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public TicketWallet(string directory)
    {
        Directory = directory;
    }

    /// <summary>The wallet the machine and the simulator use when nothing says otherwise.</summary>
    /// <remarks>
    /// This is the same path the simulator's own wallet resolves to, and the two have to agree:
    /// a host reading one directory while the machine writes another finds nothing and reports it
    /// as a machine that did not print.
    /// </remarks>
    public static string DefaultDirectory =>
        OperatingSystem.IsWindows() ? @"C:\ProgramData\qpm_simulator\wallet" : "/tmp/qpm_simulator/wallet";

    public string Directory { get; }

    /// <summary>Files in the directory that could not be read as tickets, each with its reason.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = [];

    /// <summary>
    /// Takes everything in the directory as already known, so what appears afterwards is what this
    /// test caused. Tickets survive a run, and a machine whose memory has been cleared starts its
    /// numbering again, so identity cannot be a ticket number and has to be the file.
    /// </summary>
    public void Baseline()
    {
        _seen.Clear();
        foreach (string path in Files())
            _seen.Add(path);
    }

    /// <summary>
    /// Every ticket filed since <see cref="Baseline"/>, oldest file first, with the unreadable
    /// ones recorded in <see cref="Problems"/> rather than passed over.
    /// </summary>
    /// <remarks>
    /// A file is only reported once it can be read, and stays unknown until then, because a
    /// printer writing one is not atomic: the host can list a name whose bytes are still being
    /// written. Failing to read is therefore not yet a problem — it becomes one only when the
    /// step that was waiting gives up, and the reasons are what it reports.
    /// </remarks>
    public IReadOnlyList<PrintedTicket> Taken()
    {
        var found = new List<PrintedTicket>();
        var problems = new List<string>();

        foreach (string path in Files())
        {
            if (_seen.Contains(path))
                continue;

            Ticket ticket;
            try
            {
                ticket = TicketFile.Read(path);
            }
            catch (Exception problem) when (problem is TicketFormatException or IOException)
            {
                problems.Add($"{Path.GetFileName(path)}: {problem.Message}");
                continue;
            }

            _seen.Add(path);
            found.Add(new PrintedTicket(ticket, path));
        }

        Problems = problems;
        return found;
    }

    private string[] Files()
    {
        if (!System.IO.Directory.Exists(Directory))
            return [];

        string[] paths = System.IO.Directory.GetFiles(Directory);
        Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
        return paths;
    }
}

/// <summary>A ticket found in the wallet, and the file it came from.</summary>
public sealed record PrintedTicket(Ticket Ticket, string Path)
{
    /// <summary>The file's name alone, which is what an assertion's message names.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);
}
