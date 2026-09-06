using SasWorkbench.Core.Tickets;

namespace SasWorkbench.Core.Scripting;

/// <summary>
/// What the machine's printer has produced during one test, and which of those an assertion is
/// currently about.
/// </summary>
/// <remarks>
/// A printed ticket is claimed the way an exception is: once matched it belongs to the assertion
/// that matched it, so two assertions for a ticket need two tickets. Without that, one cashout
/// would satisfy every ticket assertion in the file.
/// </remarks>
public sealed class PrintedTickets
{
    private readonly TicketWallet _wallet;
    private readonly Queue<PrintedTicket> _waiting = new();

    public PrintedTickets(string directory)
    {
        _wallet = new TicketWallet(directory);
    }

    /// <summary>The directory being watched, for a message that has to name it.</summary>
    public string Directory => _wallet.Directory;

    /// <summary>The ticket the most recent assertion took, which the field assertions are about.</summary>
    public PrintedTicket? Current { get; private set; }

    /// <summary>Files that are in the directory but could not be read as tickets.</summary>
    public IReadOnlyList<string> Problems => _wallet.Problems;

    /// <summary>
    /// Treats everything already in the wallet as not this test's doing. Called once as the test
    /// starts, because tickets outlive a run and a machine whose memory was cleared numbers its
    /// tickets from the beginning again.
    /// </summary>
    public void Baseline() => _wallet.Baseline();

    /// <summary>
    /// Takes the next ticket the printer has filed, waiting up to <paramref name="within"/> for
    /// one to appear, or null if none does.
    /// </summary>
    public PrintedTicket? Take(TimeSpan within, CancellationToken cancellation)
    {
        var deadline = DateTime.UtcNow + within;
        while (true)
        {
            foreach (PrintedTicket found in _wallet.Taken())
                _waiting.Enqueue(found);

            if (_waiting.Count > 0)
            {
                Current = _waiting.Dequeue();
                return Current;
            }

            if (DateTime.UtcNow >= deadline || cancellation.IsCancellationRequested)
                return null;

            // The wallet is a directory, so noticing a new file means looking. A printer produces
            // one ticket per cashout rather than a stream, so this costs a listing every 50 ms of
            // a step that is waiting anyway.
            Thread.Sleep(50);
        }
    }
}
