using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Polling;

/// <summary>How the poll loop should behave.</summary>
public sealed record PollEngineOptions
{
    /// <summary>Which machine to poll, or the first of them when a round is configured.</summary>
    public required SasAddress Address { get; init; }

    /// <summary>
    /// Every machine this host polls, in the order a round visits them. Empty means the one
    /// machine in <see cref="Address"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second axis. A channel splits one machine between hosts; this splits one host between
    /// machines, and the two are independent — a floor host on channel two polling forty machines
    /// is both at once.
    /// </para>
    /// <para>
    /// One engine walking a round rather than one engine per machine, because a multi-drop line
    /// carries one transaction at a time: two machines answering together collide. So a round
    /// costs the sum of its machines and not the longest of them, and an arrangement that reported
    /// otherwise would be measuring several lines rather than one.
    /// </para>
    /// </remarks>
    public IReadOnlyList<SasAddress> Addresses { get; init; } = [];

    /// <summary>The round this engine actually walks, which is never empty.</summary>
    public IReadOnlyList<SasAddress> Round => Addresses.Count > 0 ? Addresses : [Address];

    /// <summary>Which of the four channels this engine drives.</summary>
    public SasChannel Channel { get; init; } = SasChannel.One;

    /// <summary>How often to poll. Legacy hosts use 200 milliseconds.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>How long to wait for the first byte of a reply before calling it silence.</summary>
    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How long a quiet line means a reply has ended. SAS gives no end-of-message marker, so a
    /// gap is the only thing that separates one reply from the next.
    /// </summary>
    public TimeSpan QuietGap { get; init; } = TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// How many general polls to send between synchronisation polls. One means every general
    /// poll is preceded by one.
    /// </summary>
    /// <remarks>
    /// This is not optional politeness. After power-up and after every loop break a machine
    /// ignores polls addressed to itself until it has seen either a poll for a different machine
    /// or a general poll of address zero, so a host that only ever polls one machine at its own
    /// address is answered by silence — correctly.
    /// </remarks>
    public int SyncPollEvery { get; init; } = 1;

    /// <summary>How long without a reply before the link is called down.</summary>
    public TimeSpan LinkDownAfter { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How many messages of wire history to keep for reports.</summary>
    public int WireLogCapacity { get; init; } = 8192;

    /// <summary>
    /// Whether to keep polling when the transport has no peer attached. On, the engine waits
    /// quietly; off, it stops and the link is reported down.
    /// </summary>
    public bool WaitForPeer { get; init; } = true;
}
