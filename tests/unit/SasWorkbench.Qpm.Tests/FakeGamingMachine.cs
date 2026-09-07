namespace SasWorkbench.Qpm.Tests;

/// <summary>
/// A stand-in for the gaming machine on the other end of the shared region.
/// </summary>
/// <remarks>
/// It does the two things the driver depends on the real machine for: it advances the heartbeat,
/// and it can be told to behave like the peripheral simulator's window, which rewrites all four
/// stimulus masks on every frame it draws. It ticks far faster than 125ms so a test that waits
/// for two heartbeats does not wait a quarter of a second.
/// </remarks>
public sealed class FakeGamingMachine : IDisposable
{
    private readonly QpmSharedRegion _region;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Thread _thread;

    private FakeGamingMachine(string path, bool clobbersTheMasks)
    {
        Path = path;
        ClobbersTheMasks = clobbersTheMasks;
        _region = QpmSharedRegion.Open(path);
        _thread = new Thread(Run) { IsBackground = true, Name = "fake gaming machine" };
        _thread.Start();
    }

    /// <summary>Where its shared region is.</summary>
    public string Path { get; }

    /// <summary>Whether it behaves like the simulator's window and erases the masks.</summary>
    public bool ClobbersTheMasks { get; set; }

    /// <summary>How fast the heartbeat advances, in milliseconds.</summary>
    public static int TickMilliseconds => 5;

    /// <summary>
    /// Timings for a test that expects this machine to be running. Both are far longer than this
    /// machine's five-millisecond tick needs, on purpose: what is being waited on is a thread
    /// being scheduled, and four test assemblies at once are exactly what stops one being.
    /// </summary>
    public static QpmTimings Timings { get; } =
        new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

    /// <summary>
    /// Timings for a test that expects <em>no</em> machine. Waiting longer cannot change the
    /// answer — a loaded box only makes a stopped heartbeat more certain — so these stay short and
    /// the test stays quick.
    /// </summary>
    public static QpmTimings ImpatientTimings { get; } =
        new(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200));

    /// <summary>Creates the region file and starts a machine reading it.</summary>
    public static FakeGamingMachine Start(string directory, bool clobbersTheMasks = false)
    {
        string path = System.IO.Path.Combine(directory, "lsm_QpmSharedMemory.shm");
        File.WriteAllBytes(path, new byte[QpmLayout.RegionBytes]);
        return new FakeGamingMachine(path, clobbersTheMasks);
    }

    /// <summary>Creates a region file that nothing is reading.</summary>
    public static string StartNothing(string directory, int bytes = QpmLayout.RegionBytes)
    {
        string path = System.IO.Path.Combine(directory, "lsm_QpmSharedMemory.shm");
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    /// <summary>What is in one of the masks right now.</summary>
    public uint Mask(int offset) => _region.Read(offset);

    public void Dispose()
    {
        _stopping.Cancel();
        _thread.Join(TimeSpan.FromSeconds(2));
        _region.Dispose();
        _stopping.Dispose();
    }

    private void Run()
    {
        while (!_stopping.IsCancellationRequested)
        {
            if (ClobbersTheMasks)
            {
                _region.Write(QpmLayout.Intrusion, 0);
                _region.Write(QpmLayout.Dio, 0);
                _region.Write(QpmLayout.Bna, 0);
                _region.Write(QpmLayout.Printer, 0);
            }

            _region.Write(QpmLayout.Heartbeat, _region.Read(QpmLayout.Heartbeat) + 1);
            Thread.Sleep(TickMilliseconds);
        }
    }
}

/// <summary>A temporary directory that cleans itself up.</summary>
public sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = Directory.CreateTempSubdirectory("saswb-qpm-").FullName;
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file still mapped by a test that failed is not worth failing a second time over.
        }
    }
}
