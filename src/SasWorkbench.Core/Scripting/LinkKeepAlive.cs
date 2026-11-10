using SasWorkbench.Core.Polling;

namespace SasWorkbench.Core.Scripting;

/// <summary>
/// Sends a synchronisation poll every second while a sequenced case waits on a person, so the
/// machine never goes five seconds without an address byte and enters a loop break.
/// </summary>
/// <remarks>
/// A synchronisation poll carries no exception off the queue, so what the case polls for after
/// the person has acted is still there. Disposing waits for a poll in flight to finish, so the
/// next message on the line is the case's own.
/// </remarks>
public sealed class LinkKeepAlive : IDisposable
{
    /// <summary>Well inside the five seconds after which a machine declares a loop break.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly PollEngine _engine;
    private readonly ManualResetEventSlim _stop = new();
    private readonly Thread _thread;
    private readonly TimeSpan _interval;

    private LinkKeepAlive(PollEngine engine, TimeSpan interval)
    {
        _engine = engine;
        _interval = interval;
        _thread = new Thread(Run) { IsBackground = true, Name = "link keep-alive" };
        _thread.Start();
    }

    /// <summary>Starts keeping the link alive, or returns null when the host polls on its own timer.</summary>
    public static LinkKeepAlive? Start(PollEngine engine) => Start(engine, Interval);

    /// <summary>As <see cref="Start(PollEngine)"/>, at the interval given.</summary>
    public static LinkKeepAlive? Start(PollEngine engine, TimeSpan interval) =>
        engine.IsSequenced ? new LinkKeepAlive(engine, interval) : null;

    public void Dispose()
    {
        _stop.Set();
        _thread.Join();
        _stop.Dispose();
    }

    private void Run()
    {
        while (!_stop.Wait(_interval))
            _engine.Synchronise(purpose: "keep-alive while a person acts");
    }
}
