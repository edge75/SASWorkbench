using SasWorkbench.Core.Session;
using SasWorkbench.Core.Transport;

namespace SasWorkbench.Egm;

/// <summary>
/// Puts the software gaming machine on the far end of a loopback link, for a session held open by
/// hand rather than by a script.
/// </summary>
/// <remarks>
/// The same fixture the fast tests use. It answers the protocol properly, and it is still not the
/// thing under test: what it proves is that our host and our machine agree.
/// </remarks>
public static class SoftwareMachinePeer
{
    /// <summary>
    /// Builds a loopback link with the software machine answering on the far end, and returns the
    /// host end together with everything the caller must dispose.
    /// </summary>
    public static Func<HostSessionSpec, (ISasTransport Host, IDisposable Owned)> For(
        FakeEgmOptions? options = null,
        Action<FakeEgm>? started = null) =>
        spec =>
        {
            var bus = new LoopbackBus();
            FakeEgmOptions settings = (options ?? new FakeEgmOptions()) with
            {
                Address = spec.Address,
                Channel = spec.Channel,
            };

            var machine = new FakeEgm(bus.Egm, settings);
            machine.Start();
            started?.Invoke(machine);

            return (bus.Host, new Both(machine, bus));
        };

    private sealed class Both(params IDisposable[] parts) : IDisposable
    {
        public void Dispose()
        {
            foreach (IDisposable part in parts)
                part.Dispose();
        }
    }
}
