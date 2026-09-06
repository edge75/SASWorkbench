namespace SasWorkbench.Core.Tickets;

/// <summary>
/// The acceptor: a directory a ticket is put into to feed it to the gaming machine.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="TicketWallet"/> and the same shape for the same reason. A ticket
/// the machine printed is filed in the wallet; a ticket offered to the machine is dropped here,
/// and the machine's peripheral manager takes it and reports a document in escrow. Three programs
/// share it — a person clicking a button in the peripheral simulator, a script step, and the
/// machine that consumes it — so it is a directory of small files rather than anything that needs
/// two of them to be running.
///
/// It is a queue, not a store: what is taken is gone. A ticket that has been offered and taken is
/// in the machine, and a directory still holding it would let a second insertion happen by
/// accident.
/// </remarks>
public sealed class TicketAcceptor
{
    private int _offered;

    public TicketAcceptor(string directory)
    {
        Directory = directory;
    }

    /// <summary>Where a ticket is offered when nothing says otherwise.</summary>
    /// <remarks>
    /// Beside the wallet, and it has to be: the machine's peripheral manager watches exactly this
    /// path, and a script writing somewhere else offers a ticket nobody is looking for and reports
    /// a machine that ignored it.
    /// </remarks>
    public static string DefaultDirectory =>
        OperatingSystem.IsWindows()
            ? @"C:\ProgramData\qpm_simulator\acceptor"
            : "/tmp/qpm_simulator/acceptor";

    public string Directory { get; }

    /// <summary>
    /// Offers a ticket to the machine, and returns the file it was written as.
    /// </summary>
    /// <remarks>
    /// Written under a temporary name and then moved into place, because the machine is watching
    /// the directory and would otherwise be able to read a ticket that is still being written.
    /// A rename within one directory is the only atomic file operation every platform here has.
    /// </remarks>
    public string Offer(Ticket ticket, TicketEncoding encoding = TicketEncoding.Json)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        System.IO.Directory.CreateDirectory(Directory);
        string name = NextName(TicketFile.ExtensionFor(encoding));
        string staging = Path.Combine(Directory, name + ".writing");
        string path = Path.Combine(Directory, name);

        TicketFile.Write(staging, ticket, encoding);
        File.Move(staging, path, overwrite: true);
        return path;
    }

    /// <summary>
    /// Offers a ticket that is already written somewhere, keeping the encoding it was written in.
    /// </summary>
    /// <remarks>
    /// It is read before it is offered, so a file that is not a ticket is refused here, where the
    /// script line that named it can be blamed, rather than being dropped into the directory for
    /// the machine to ignore in silence.
    /// </remarks>
    /// <exception cref="TicketFormatException">The file is not a ticket.</exception>
    public string Offer(string ticketPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketPath);

        Ticket ticket = TicketFile.Read(ticketPath);
        return Offer(ticket, TicketFile.EncodingOf(ticketPath));
    }

    /// <summary>Every ticket waiting to be taken, oldest first, without taking any.</summary>
    /// <remarks>
    /// Files that will not read are left where they are and reported, not skipped: a ticket
    /// written by hand with a typo in it is the ordinary case, and a person who is told nothing
    /// concludes the machine ignored a perfectly good ticket.
    /// </remarks>
    public IReadOnlyList<OfferedTicket> Waiting(out IReadOnlyList<string> problems)
    {
        var found = new List<OfferedTicket>();
        var bad = new List<string>();

        foreach (string path in Files())
        {
            try
            {
                found.Add(new OfferedTicket(TicketFile.Read(path), path));
            }
            catch (Exception problem) when (problem is TicketFormatException or IOException)
            {
                bad.Add($"{Path.GetFileName(path)}: {problem.Message}");
            }
        }

        problems = bad;
        return found;
    }

    /// <summary>
    /// Takes the ticket that has waited longest, removing it, or null when none is waiting.
    /// </summary>
    public OfferedTicket? Take()
    {
        foreach (string path in Files())
        {
            Ticket ticket;
            try
            {
                ticket = TicketFile.Read(path);
            }
            catch (Exception problem) when (problem is TicketFormatException or IOException)
            {
                continue;
            }

            File.Delete(path);
            return new OfferedTicket(ticket, path);
        }

        return null;
    }

    /// <summary>Throws away everything waiting, so a test starts with an empty acceptor.</summary>
    public void Clear()
    {
        foreach (string path in Files())
            File.Delete(path);
    }

    private string[] Files()
    {
        if (!System.IO.Directory.Exists(Directory))
            return [];

        // The staging name a half-written ticket carries is not a ticket yet, so it is not listed.
        string[] paths = System.IO.Directory.GetFiles(Directory)
            .Where(p => !p.EndsWith(".writing", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
        return paths;
    }

    /// <summary>
    /// A name that sorts in the order tickets were offered. The clock alone is not enough — two
    /// offers in the same millisecond would collide — so a counter follows it.
    /// </summary>
    private string NextName(string extension)
    {
        int sequence = Interlocked.Increment(ref _offered);
        return $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{sequence:D4}{extension}";
    }
}

/// <summary>A ticket waiting in the acceptor, and the file it came from.</summary>
public sealed record OfferedTicket(Ticket Ticket, string Path)
{
    /// <summary>The file's name alone, which is what a message names.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);
}
