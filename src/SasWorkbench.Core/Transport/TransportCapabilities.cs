namespace SasWorkbench.Core.Transport;

/// <summary>What sort of link a transport is.</summary>
public enum TransportKind
{
    /// <summary>In-process, for unit tests.</summary>
    Loopback,

    /// <summary>A TCP socket carrying nine-bit symbols.</summary>
    Tcp,

    /// <summary>A serial port.</summary>
    Serial,
}

/// <summary>
/// How much a green result on this transport is worth. A suite that passed over a link nobody
/// has measured must not read the same as one that passed over a link that has been.
/// </summary>
public enum TransportFidelity
{
    /// <summary>Exercised end to end against a real gaming machine library, and captured.</summary>
    Proven,

    /// <summary>Implemented to specification but never measured against hardware.</summary>
    Unverified,
}

/// <summary>
/// What a transport can and cannot do, stated plainly so a runner can skip with a reason
/// instead of failing, and a report can label the result honestly.
/// </summary>
/// <param name="Kind">The sort of link.</param>
/// <param name="CarriesWakeupBit">
/// False means the link cannot represent an address byte. Nothing may proceed on such a link:
/// dropping the wakeup bit leaves a broken link looking healthy.
/// </param>
/// <param name="Fidelity">How much weight a pass on this link carries.</param>
/// <param name="Caveat">One sentence a report can print next to the verdict, when there is one.</param>
public sealed record TransportCapabilities(
    TransportKind Kind,
    bool CarriesWakeupBit,
    TransportFidelity Fidelity,
    string? Caveat = null);
