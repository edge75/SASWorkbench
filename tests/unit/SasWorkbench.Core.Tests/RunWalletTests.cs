using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Tickets;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// Which folder a run reads its printed tickets from. The software machine prints wherever the
/// run tells it to, so each run gets a folder of its own: a shared one fills with tickets from
/// every old run, and two runs at once read each other's. Anything a real machine or the
/// peripheral simulator writes still has to be read from the shared folder they both use.
/// </summary>
public class RunWalletTests
{
    [Fact]
    public void A_software_run_gets_a_wallet_of_its_own()
    {
        using RunWallet first = RunWallet.For(StimulusBackend.Software);
        using RunWallet second = RunWallet.For(StimulusBackend.Software);

        Assert.True(first.IsPrivate);
        Assert.NotEqual(TicketWallet.DefaultDirectory, first.Directory, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(first.Directory, second.Directory, StringComparer.OrdinalIgnoreCase);
        Assert.StartsWith(Path.GetTempPath(), first.Directory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_software_run_deletes_its_wallet_when_it_ends()
    {
        RunWallet wallet = RunWallet.For(StimulusBackend.Software);
        Directory.CreateDirectory(Path.Combine(wallet.Directory, "receipts"));
        File.WriteAllText(Path.Combine(wallet.Directory, "000001-ticket.json"), "{}");

        wallet.Dispose();

        Assert.False(Directory.Exists(wallet.Directory));
    }

    [Theory]
    [InlineData(StimulusBackend.Qpm)]
    [InlineData(StimulusBackend.Manual)]
    [InlineData(StimulusBackend.None)]
    public void Every_other_run_reads_the_shared_wallet_and_never_deletes_it(StimulusBackend backend)
    {
        RunWallet wallet = RunWallet.For(backend);

        Assert.False(wallet.IsPrivate);
        Assert.Equal(TicketWallet.DefaultDirectory, wallet.Directory);

        //  Disposing must leave the shared folder alone; the folder is only checked, never made,
        //  so a machine without the simulator installed is not given one by this test.
        bool existed = Directory.Exists(wallet.Directory);
        wallet.Dispose();
        Assert.Equal(existed, Directory.Exists(wallet.Directory));
    }
}
