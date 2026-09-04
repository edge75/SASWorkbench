using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Polling;

/// <summary>How the poll loop should behave.</summary>
public sealed record PollEngineOptions
{
    /// <summary>Which machine to poll.</summary>
    public required SasAddress Address { get; init; }

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
