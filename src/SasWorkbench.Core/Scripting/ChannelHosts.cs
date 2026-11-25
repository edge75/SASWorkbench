using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Session;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Scripting;

/// <summary>
/// The hosts a case has open on one machine — one per channel — and which of them the steps that
/// follow are speaking to.
/// </summary>
/// <remarks>
/// <para>
/// Two hosts on one machine is not two runs. They share the machine, the session log and the
/// case's verdict, and the ordering between what one host sent and what the other heard is the
/// thing a two-channel case is asserting — so the records go into one stream with a channel lane
/// rather than into two that would have to be aligned afterwards.
/// </para>
/// <para>
/// Each host keeps its own poll engine, its own clock and its own wire log, because each is a
/// separate link with its own state. The clocks are reconciled onto the session's when the records
/// are collected: two engines started a second apart would otherwise report the same instant as
/// two different times.
/// </para>
/// </remarks>
public sealed class ChannelHosts : IDisposable
{
    private readonly Dictionary<SasChannel, Host> _hosts = [];

    /// <summary>The channel the steps that follow are on.</summary>
    public SasChannel Current { get; private set; } = SasChannel.One;

    /// <summary>The engine driving the current channel.</summary>
    public PollEngine Engine => _hosts[Current].Engine;

    /// <summary>Every channel with a host on it, in order.</summary>
    public IReadOnlyList<SasChannel> Open => [.. _hosts.Keys.Order()];

    /// <summary>How many hosts are open.</summary>
    public int Count => _hosts.Count;

    /// <summary>Whether a channel has a host on it.</summary>
    public bool Has(SasChannel channel) => _hosts.ContainsKey(channel);

    /// <summary>
    /// Why every step from here on is a skip, once a channel could not be opened or moved to.
    /// </summary>
    /// <remarks>
    /// Sticky on purpose. The alternative is to leave the steps that follow on whichever channel
    /// the case was already on, which would run the second half of a two-channel case against the
    /// first channel and pass — the worst outcome available.
    /// </remarks>
    public string? Blocked { get; private set; }

    /// <summary>Records that the arrangement this case needs is not available.</summary>
    public void Block(string reason) => Blocked ??= reason;

    /// <summary>
    /// Which machine of the current host's round the steps that follow are talking to, or null
    /// while a round of more than one has not been told.
    /// </summary>
    /// <remarks>
    /// Null is not a default to be filled in. On a round, a command sent to whichever machine the
    /// cycle had reached would be answered correctly by the wrong machine, so the case is refused
    /// until it says which — the same rule the poll engine enforces, kept here too so a script
    /// hears it as a step that skipped rather than as a poll that was not sent.
    /// </remarks>
    public SasAddress? Machine { get; private set; }

    /// <summary>
    /// Moves to another machine in the current host's round, or says why it cannot.
    /// </summary>
    public bool TryUseMachine(SasAddress address, out string? refusal)
    {
        refusal = null;
        IReadOnlyList<SasAddress> round = Engine.Round;

        if (round.Contains(address))
        {
            Machine = address;
            return true;
        }

        refusal = $"this host does not poll ${address}; it polls "
                  + string.Join(", ", round.Select(a => $"${a}"))
                  + ". Name one of those, or widen the round on 'host connect'.";
        return false;
    }

    /// <summary>Points the current host, bound to one machine, at that machine's new address; null when done.</summary>
    public string? Follow(SasAddress address)
    {
        string? refusal = Engine.Readdress(address);
        if (refusal is null)
            Machine = address;
        return refusal;
    }

    /// <summary>Takes a host that has already been built, and makes it the current one.</summary>
    public void Add(SasChannel channel, PollEngine engine, ClockBase clock)
    {
        ArgumentNullException.ThrowIfNull(engine);

        _hosts[channel] = new Host(engine, clock);
        Current = channel;

        // A host on one machine has nothing to choose between, so the steps that follow talk to it
        // without saying so — which is every case written before rounds existed. A host on several
        // starts with none chosen, so the first step that needs one is refused rather than sent
        // somewhere arbitrary.
        Machine = engine.Round.Count == 1 ? engine.Round[0] : null;
    }

    /// <summary>
    /// Moves to another channel, or says why it cannot.
    /// </summary>
    /// <remarks>
    /// A channel with no host is refused with the channels that do have one named, rather than
    /// silently leaving the steps that follow on the channel they were already on — which would
    /// run the second half of a two-channel case against the first channel and pass.
    /// </remarks>
    public bool TryUse(SasChannel channel, out string? refusal)
    {
        refusal = null;
        if (_hosts.ContainsKey(channel))
        {
            Current = channel;

            // The machine is a property of the host, not of the case, so moving between hosts
            // moves to that host's own choice rather than carrying one host's machine to another.
            IReadOnlyList<SasAddress> round = _hosts[channel].Engine.Round;
            Machine = round.Count == 1 ? round[0] : null;
            return true;
        }

        refusal = $"no host is connected on channel {(int)channel}; this case has "
                  + string.Join(", ", Open.Select(open => $"channel {(int)open}"))
                  + ". Add 'host connect ... channel " + (int)channel + "' first.";
        return false;
    }

    /// <summary>
    /// Every message every host exchanged, on the session's clock, in order.
    /// </summary>
    /// <remarks>
    /// The sequence numbers are each engine's own, so they are renumbered over the merged list:
    /// a reader uses them to tell whether anything was dropped, and two independent sequences
    /// interleaved would look like a great deal was.
    /// </remarks>
    public IReadOnlyList<WireRecord> Wire() =>
    [
        .. _hosts.Values
            .SelectMany(host => host.Engine.WireLog.Snapshot()
                .Select(record => record with { Elapsed = host.Clock.Reconcile(record.Elapsed) }))
            .OrderBy(record => record.Elapsed)
            .Select((record, index) => record with { Sequence = index }),
    ];

    /// <summary>Stops every host, leaving their records readable.</summary>
    public void StopAll()
    {
        foreach (Host host in _hosts.Values)
            host.Engine.Stop();
    }

    public void Dispose()
    {
        foreach (Host host in _hosts.Values)
            host.Engine.Dispose();

        _hosts.Clear();
    }

    private sealed record Host(PollEngine Engine, ClockBase Clock);
}

/// <summary>
/// What a second channel needs in order to be opened: a transport on the same machine.
/// </summary>
/// <remarks>
/// A second link built the way the first one was would be a second machine, which is the opposite
/// of what a two-channel case is about. So the environment that built the machine is the thing
/// asked for the extra link, and one that cannot supply a second says so and the case skips.
/// </remarks>
public delegate ISasTransport ChannelOpener(ConnectStep step);
