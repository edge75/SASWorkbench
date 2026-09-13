using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Transport;

/// <summary>
/// Joins one host to many gaming machines inside one process, the way a multi-drop line joins
/// them on a floor: everything the host sends reaches every machine, and whatever any machine
/// answers reaches the host.
/// </summary>
/// <remarks>
/// <para>
/// This is the second axis. <see cref="LoopbackBus"/> is one host and one machine, and several of
/// those are several floors; this is one floor. The distinction is not cosmetic — it is what makes
/// a round of addresses cost what a round of addresses costs. On one line only one machine may
/// speak at a time, so a host that polls a hundred machines pays for a hundred transactions in
/// sequence, and no arrangement inside the host changes that.
/// </para>
/// <para>
/// Address filtering is left to the machines, because that is where it lives on a real line. Every
/// machine sees every poll and answers only its own, which is what makes an address collision or a
/// machine answering for another one visible here rather than quietly prevented.
/// </para>
/// <para>
/// Collisions are not modelled. Two machines answering at once would interleave on a real line
/// into a frame that fails its check sequence; here their symbols interleave in the host's queue,
/// which is a different kind of wrong. It is not a problem for a host that polls one machine at a
/// time, and it is named so that nobody reads a clean round here as evidence about a line where
/// two machines share an address.
/// </para>
/// </remarks>
public sealed class MultidropLoopbackBus : IDisposable
{
    private readonly SymbolPipe _toHost;
    private readonly List<SymbolPipe> _toMachines = [];
    private readonly List<ISasTransport> _machines = [];
    private readonly Lock _gate = new();

    /// <summary>Builds a line with a host end and nothing on it yet.</summary>
    /// <param name="machines">How many machine ends to open.</param>
    /// <param name="capacity">Symbols each direction holds before the oldest is lost.</param>
    public MultidropLoopbackBus(int machines, int capacity = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(machines, 1);

        _toHost = new SymbolPipe(capacity);
        for (int i = 0; i < machines; i++)
        {
            var down = new SymbolPipe(capacity);
            _toMachines.Add(down);
            _machines.Add(new MultidropEnd($"multi-drop (machine {i + 1} end)", _toHost, down, this));
        }

        Host = new MultidropHostEnd(this, _toHost);
    }

    /// <summary>The one end a poll engine drives.</summary>
    public ISasTransport Host { get; }

    /// <summary>The machine ends, in the order they were opened.</summary>
    public IReadOnlyList<ISasTransport> Machines => _machines;

    /// <summary>True once the line has been closed.</summary>
    public bool IsClosed { get; private set; }

    /// <summary>Symbols lost to an end that fell behind, summed over the whole line.</summary>
    public long Dropped
    {
        get
        {
            lock (_gate)
                return _toHost.Dropped + _toMachines.Sum(pipe => pipe.Dropped);
        }
    }

    internal void Broadcast(ReadOnlySpan<SasSymbol> symbols)
    {
        // Copied out under no lock beyond the list's own stability: the machine list is built once
        // before the line is driven, so a write does not contend with an open.
        foreach (SymbolPipe pipe in _toMachines)
            pipe.Write(symbols);
    }

    public void Dispose()
    {
        IsClosed = true;
        _toHost.Close();
        foreach (SymbolPipe pipe in _toMachines)
            pipe.Close();
    }

    /// <summary>The host's end: writes reach every machine, reads take whatever any machine said.</summary>
    private sealed class MultidropHostEnd(MultidropLoopbackBus bus, SymbolPipe inbound) : ISasTransport
    {
        private static readonly TransportCapabilities Caps = new(
            TransportKind.Loopback,
            CarriesWakeupBit: true,
            TransportFidelity.Proven,
            "In-process multi-drop line: proves our own framing and a round's cost, "
            + "not a gaming machine's.");

        public string Description => $"multi-drop loopback, {bus.Machines.Count} machine(s)";

        public TransportCapabilities Capabilities => Caps;

        public bool IsConnected => !bus.IsClosed;

        public void Open()
        {
        }

        public void Close() => bus.Dispose();

        public void Write(ReadOnlySpan<SasSymbol> symbols) => bus.Broadcast(symbols);

        public int Read(Span<SasSymbol> destination, TimeSpan timeout) =>
            inbound.Read(destination, timeout);

        public void Dispose() => Close();
    }

    /// <summary>One machine's end of the line.</summary>
    private sealed class MultidropEnd(
        string description, SymbolPipe outbound, SymbolPipe inbound, MultidropLoopbackBus bus)
        : ISasTransport
    {
        private static readonly TransportCapabilities Caps = new(
            TransportKind.Loopback,
            CarriesWakeupBit: true,
            TransportFidelity.Proven,
            "In-process multi-drop line: proves our own framing, not a gaming machine's.");

        public string Description => description;

        public TransportCapabilities Capabilities => Caps;

        public bool IsConnected => !bus.IsClosed;

        public void Open()
        {
        }

        public void Close()
        {
        }

        public void Write(ReadOnlySpan<SasSymbol> symbols) => outbound.Write(symbols);

        public int Read(Span<SasSymbol> destination, TimeSpan timeout) =>
            inbound.Read(destination, timeout);

        public void Dispose()
        {
        }
    }
}
