using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Transport;

/// <summary>
/// Joins a host and a gaming machine inside one process, over a pipe that carries all nine bits.
/// </summary>
/// <remarks>
/// The point of a loopback link is that the wakeup bit survives it, so a unit test exercises the
/// same framing rules as a real link. It exists for speed, not for coverage: a pass here is
/// evidence about our own code, not about a gaming machine.
/// </remarks>
public sealed class LoopbackBus : IDisposable
{
    private readonly SymbolPipe _toEgm;
    private readonly SymbolPipe _toHost;

    public LoopbackBus(int capacity = 4096)
    {
        _toEgm = new SymbolPipe(capacity);
        _toHost = new SymbolPipe(capacity);
        Host = new LoopbackTransport("loopback (host end)", _toEgm, _toHost, this);
        Egm = new LoopbackTransport("loopback (machine end)", _toHost, _toEgm, this);
    }

    /// <summary>The end a poll engine drives.</summary>
    public ISasTransport Host { get; }

    /// <summary>The end a gaming machine answers on.</summary>
    public ISasTransport Egm { get; }

    /// <summary>True once either end has been closed.</summary>
    public bool IsClosed { get; private set; }

    /// <summary>Symbols lost to a reader that fell behind, summed over both directions.</summary>
    public long Dropped => _toEgm.Dropped + _toHost.Dropped;

    public void Dispose()
    {
        IsClosed = true;
        _toEgm.Close();
        _toHost.Close();
    }
}

/// <summary>One end of a <see cref="LoopbackBus"/>.</summary>
internal sealed class LoopbackTransport : ISasTransport
{
    private static readonly TransportCapabilities Caps = new(
        TransportKind.Loopback,
        CarriesWakeupBit: true,
        TransportFidelity.Proven,
        "In-process link: proves our own framing, not a gaming machine's.");

    private readonly SymbolPipe _outbound;
    private readonly SymbolPipe _inbound;
    private readonly LoopbackBus _bus;

    internal LoopbackTransport(string description, SymbolPipe outbound, SymbolPipe inbound, LoopbackBus bus)
    {
        Description = description;
        _outbound = outbound;
        _inbound = inbound;
        _bus = bus;
    }

    public string Description { get; }

    public TransportCapabilities Capabilities => Caps;

    public bool IsConnected => !_bus.IsClosed;

    public void Open()
    {
    }

    public void Close() => _bus.Dispose();

    public void Write(ReadOnlySpan<SasSymbol> symbols) => _outbound.Write(symbols);

    public int Read(Span<SasSymbol> destination, TimeSpan timeout) => _inbound.Read(destination, timeout);

    public void Dispose() => Close();
}
