using SasWorkbench.Core.Tickets;

namespace SasWorkbench.Core.Tests;

public class TicketAcceptorTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "saswb-acceptor-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A temporary directory that outlives one test run is not worth failing a suite over.
        }

        GC.SuppressFinalize(this);
    }

    private static Ticket Cashout(ulong validation, long cents) => new()
    {
        ValidationNumber = validation,
        AmountCents = cents,
        ValidationType = TicketValidationType.CashableFromCashout,
    };

    [Fact]
    public void AnOfferedTicketComesBackUnchanged()
    {
        var acceptor = new TicketAcceptor(_directory);
        Ticket offered = Cashout(123456789012345678, 2000);

        acceptor.Offer(offered);

        OfferedTicket? taken = acceptor.Take();
        Assert.NotNull(taken);
        Assert.Equal(offered, taken.Ticket);
    }

    // The acceptor is a queue, not a store. A ticket that is still there after the machine has
    // taken it is a ticket that can be inserted twice by accident.
    [Fact]
    public void TakingATicketRemovesIt()
    {
        var acceptor = new TicketAcceptor(_directory);
        acceptor.Offer(Cashout(1, 100));

        Assert.NotNull(acceptor.Take());
        Assert.Null(acceptor.Take());
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void TicketsAreTakenInTheOrderTheyWereOffered()
    {
        var acceptor = new TicketAcceptor(_directory);
        acceptor.Offer(Cashout(111, 100));
        acceptor.Offer(Cashout(222, 200));
        acceptor.Offer(Cashout(333, 300));

        Assert.Equal(111ul, acceptor.Take()!.Ticket.ValidationNumber);
        Assert.Equal(222ul, acceptor.Take()!.Ticket.ValidationNumber);
        Assert.Equal(333ul, acceptor.Take()!.Ticket.ValidationNumber);
    }

    // A ticket is written under a temporary name and moved into place, because the machine is
    // watching this directory and must never see a file whose bytes are still arriving. What can
    // be checked from here is the consequence: nothing is left behind under the staging name.
    [Fact]
    public void NothingIsLeftHalfWritten()
    {
        var acceptor = new TicketAcceptor(_directory);
        acceptor.Offer(Cashout(1, 100));

        Assert.All(Directory.GetFiles(_directory), path =>
            Assert.DoesNotContain(".writing", path, StringComparison.Ordinal));
    }

    [Fact]
    public void ATicketOfferedByFileKeepsItsEncoding()
    {
        Directory.CreateDirectory(_directory);
        string source = Path.Combine(_directory, "by-hand.ticket");
        TicketFile.Write(source, Cashout(999, 4200), TicketEncoding.KeyValue);

        var acceptor = new TicketAcceptor(Path.Combine(_directory, "in"));
        string offered = acceptor.Offer(source);

        Assert.EndsWith(".ticket", offered, StringComparison.Ordinal);
        Assert.Equal(4200, acceptor.Take()!.Ticket.AmountCents);
    }

    // Refused where the caller can be told, rather than dropped into the directory for the machine
    // to ignore in silence.
    [Fact]
    public void AFileThatIsNotATicketIsRefusedOnTheWayIn()
    {
        Directory.CreateDirectory(_directory);
        string source = Path.Combine(_directory, "not-a-ticket.json");
        File.WriteAllText(source, "{ \"nothing\": true }");

        var acceptor = new TicketAcceptor(Path.Combine(_directory, "in"));

        Assert.Throws<TicketFormatException>(() => acceptor.Offer(source));
    }

    // A hand-written ticket with a typo in it is the ordinary case. It is reported by name so the
    // person who wrote it hears about it, rather than concluding the machine ignored a good one.
    [Fact]
    public void AFileThatWillNotReadIsReportedRatherThanSkipped()
    {
        var acceptor = new TicketAcceptor(_directory);
        acceptor.Offer(Cashout(1, 100));
        File.WriteAllText(Path.Combine(_directory, "broken.json"), "{");

        IReadOnlyList<OfferedTicket> waiting = acceptor.Waiting(out IReadOnlyList<string> problems);

        Assert.Single(waiting);
        Assert.Single(problems);
        Assert.Contains("broken.json", problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AnAcceptorWithNoDirectoryYetIsSimplyEmpty()
    {
        var acceptor = new TicketAcceptor(Path.Combine(_directory, "never-created"));

        Assert.Null(acceptor.Take());
        Assert.Empty(acceptor.Waiting(out _));
    }

    [Fact]
    public void ClearingLeavesNothingWaiting()
    {
        var acceptor = new TicketAcceptor(_directory);
        acceptor.Offer(Cashout(1, 100));
        acceptor.Offer(Cashout(2, 200));

        acceptor.Clear();

        Assert.Empty(acceptor.Waiting(out _));
    }
}
