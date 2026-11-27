using System.Diagnostics;
using SasWorkbench.Core.Tickets;

namespace SasWorkbench.Core.Scripting;

/// <summary>
/// The wallet one run reads its printed tickets from: a folder of its own for the software
/// machine, the shared one for anything a real machine or the peripheral simulator writes into.
/// </summary>
/// <remarks>
/// A private folder keeps a run from reading another run's tickets or wading through old ones, and is deleted when the run ends.
/// </remarks>
public sealed class RunWallet : IDisposable
{
    private RunWallet(string directory, bool isPrivate)
    {
        Directory = directory;
        IsPrivate = isPrivate;
    }

    /// <summary>The folder the run's machine prints into and the host reads.</summary>
    public string Directory { get; }

    /// <summary>Whether the folder belongs to this run alone and is deleted when it ends.</summary>
    public bool IsPrivate { get; }

    /// <summary>The wallet a run with this stimulus backend should use.</summary>
    public static RunWallet For(StimulusBackend backend) =>
        backend == StimulusBackend.Software
            ? new RunWallet(
                Path.Combine(Path.GetTempPath(), "saswb-wallet-" + Guid.NewGuid().ToString("N")),
                isPrivate: true)
            : new RunWallet(TicketWallet.DefaultDirectory, isPrivate: false);

    /// <summary>Deletes a private folder and everything in it; leaves the shared one alone.</summary>
    public void Dispose()
    {
        if (!IsPrivate)
            return;

        try
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The run's verdict stands; only a temporary folder is left behind, so say where.
            Trace.TraceWarning($"could not delete the run's wallet {Directory}: {e.Message}");
        }
    }
}
