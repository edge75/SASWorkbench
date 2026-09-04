using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Transport;

/// <summary>
/// A nine-bit symbol pipe. Everything above this interface deals in SAS messages; everything
/// below it deals in bytes and wakeup bits.
/// </summary>
public interface ISasTransport : IDisposable
{
    /// <summary>Something a human can read in a report, such as <c>tcp 127.0.0.1:7200</c>.</summary>
    string Description { get; }

    /// <summary>What this link can do, and how much a pass over it is worth.</summary>
    TransportCapabilities Capabilities { get; }

    /// <summary>
    /// True when a peer is attached. An open transport with no peer behaves like an open serial
    /// port with nothing plugged into it: writes succeed, reads return nothing.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>Acquires the link.</summary>
    /// <exception cref="TransportRefusedException">The link cannot carry SAS faithfully.</exception>
    void Open();

    /// <summary>Releases the link. Safe to call more than once.</summary>
    void Close();

    /// <summary>Sends symbols in order, wakeup bits included.</summary>
    void Write(ReadOnlySpan<SasSymbol> symbols);

    /// <summary>
    /// Reads whatever has arrived, waiting up to <paramref name="timeout"/> for the first symbol.
    /// </summary>
    /// <returns>How many symbols were written to <paramref name="destination"/>; zero on timeout.</returns>
    int Read(Span<SasSymbol> destination, TimeSpan timeout);
}
