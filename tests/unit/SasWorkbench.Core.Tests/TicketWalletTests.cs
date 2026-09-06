using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Tickets;

namespace SasWorkbench.Core.Tests;

public class TicketWalletTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "saswb-wallet-" + Guid.NewGuid().ToString("N"));

    public TicketWalletTests() => Directory.CreateDirectory(_directory);

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

    private string Write(string name, Ticket ticket)
    {
        string path = Path.Combine(_directory, name);
        TicketFile.Write(path, ticket);
        return path;
    }

    private static Ticket Cashout(uint number, long cents) => new()
    {
        ValidationNumber = 1234567890123456,
        TicketNumber = number,
        AmountCents = cents,
    };

    [Fact]
    public void ATicketAlreadyThereIsNotThisTestsDoing()
    {
        Write("0001.json", Cashout(1, 500));

        var wallet = new TicketWallet(_directory);
        wallet.Baseline();

        Assert.Empty(wallet.Taken());
    }

    [Fact]
    public void ATicketFiledAfterTheBaselineIsFound()
    {
        Write("0001.json", Cashout(1, 500));

        var wallet = new TicketWallet(_directory);
        wallet.Baseline();
        Write("0002.json", Cashout(2, 2000));

        PrintedTicket found = Assert.Single(wallet.Taken());
        Assert.Equal(2000, found.Ticket.AmountCents);
        Assert.Equal("0002.json", found.FileName);
    }

    [Fact]
    public void ATicketIsOnlyReportedOnce()
    {
        var wallet = new TicketWallet(_directory);
        wallet.Baseline();
        Write("0001.json", Cashout(1, 500));

        Assert.Single(wallet.Taken());
        Assert.Empty(wallet.Taken());
    }

    // The wallet holding one fewer ticket than the directory does is how a test comes to pass for
    // the wrong reason, so a file that will not read is named rather than passed over.
    [Fact]
    public void AFileThatWillNotReadIsNamedRatherThanSkipped()
    {
        var wallet = new TicketWallet(_directory);
        wallet.Baseline();
        File.WriteAllText(Path.Combine(_directory, "rubbish.json"), "this is not a ticket");

        Assert.Empty(wallet.Taken());
        Assert.Contains(wallet.Problems, p => p.Contains("rubbish.json", StringComparison.Ordinal));
    }

    // Both encodings are tickets wherever a ticket is accepted, so the wallet reads a hand-written
    // key/value file beside a machine-written JSON one.
    [Fact]
    public void BothEncodingsAreRead()
    {
        var wallet = new TicketWallet(_directory);
        wallet.Baseline();
        Write("0001.json", Cashout(1, 500));
        Write("0002.ticket", Cashout(2, 2000));

        Assert.Equal(2, wallet.Taken().Count);
    }

    [Fact]
    public void ANonexistentDirectoryIsEmptyRatherThanAnError()
    {
        var wallet = new TicketWallet(Path.Combine(_directory, "not-created-yet"));
        wallet.Baseline();

        Assert.Empty(wallet.Taken());
    }

    // One cashout must not satisfy every ticket assertion in a file, so a ticket taken by one
    // assertion is gone from the next.
    [Fact]
    public void EachAssertionTakesADifferentTicket()
    {
        var printed = new PrintedTickets(_directory);
        printed.Baseline();
        Write("0001.json", Cashout(1, 500));
        Write("0002.json", Cashout(2, 2000));

        PrintedTicket? first = printed.Take(TimeSpan.FromSeconds(1), CancellationToken.None);
        PrintedTicket? second = printed.Take(TimeSpan.FromSeconds(1), CancellationToken.None);
        PrintedTicket? third = printed.Take(TimeSpan.FromMilliseconds(100), CancellationToken.None);

        Assert.Equal(500, first?.Ticket.AmountCents);
        Assert.Equal(2000, second?.Ticket.AmountCents);
        Assert.Null(third);
    }

    [Fact]
    public void WaitingReturnsNothingWhenTheMachinePrintsNothing()
    {
        var printed = new PrintedTickets(_directory);
        printed.Baseline();

        Assert.Null(printed.Take(TimeSpan.FromMilliseconds(120), CancellationToken.None));
        Assert.Null(printed.Current);
    }
}
