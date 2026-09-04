using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Transport;

/// <summary>
/// Turns a symbol pipe into a message pipe. Everything above this class deals in SAS messages.
/// </summary>
/// <remarks>
/// SAS marks the start of a message with the wakeup bit and marks the end of one with nothing
/// at all, so the only way to know a reply has finished is that the line went quiet. That makes
/// the quiet gap a protocol parameter, not a tuning knob: too short and one reply is read as
/// two, too long and a slow machine looks the same as a fast one.
/// </remarks>
public sealed class SasLink(ISasTransport transport, int maxMessageBytes = HostMessage.MaxSymbols)
{
    private readonly SasSymbol[] _scratch = new SasSymbol[maxMessageBytes];

    /// <summary>The underlying pipe.</summary>
    public ISasTransport Transport { get; } = transport;

    /// <summary>Sends one message, wakeup bits as given.</summary>
    public void Send(ReadOnlySpan<SasSymbol> message) => Transport.Write(message);

    /// <summary>
    /// Reads one message. Waits up to <paramref name="firstSymbolTimeout"/> for it to start, then
    /// until <paramref name="quietGap"/> passes with nothing further.
    /// </summary>
    /// <param name="destination">Receives the data bytes, wakeup bits stripped.</param>
    /// <param name="firstSymbolTimeout">How long to wait for the reply to begin.</param>
    /// <param name="quietGap">How long a silence marks the end of the reply.</param>
    /// <param name="wakeupSeen">
    /// True when any received symbol carried the wakeup bit. A gaming machine never sets it, so
    /// this is always a fault — either a misbehaving machine or a host hearing its own echo.
    /// </param>
    /// <returns>How many bytes were written to <paramref name="destination"/>.</returns>
    public int Receive(
        Span<byte> destination,
        TimeSpan firstSymbolTimeout,
        TimeSpan quietGap,
        out bool wakeupSeen)
    {
        wakeupSeen = false;
        if (destination.IsEmpty)
            return 0;

        int produced = 0;
        TimeSpan wait = firstSymbolTimeout;

        while (true)
        {
            int room = Math.Min(_scratch.Length, destination.Length - produced);
            if (room == 0)
                return produced;

            int read = Transport.Read(_scratch.AsSpan(0, room), wait);
            if (read == 0)
                return produced;

            for (int i = 0; i < read; i++)
            {
                wakeupSeen |= _scratch[i].Wakeup;
                destination[produced++] = _scratch[i].Data;
            }

            // The reply has started; from here on, silence is the terminator.
            wait = quietGap;
        }
    }

    /// <summary>Reads and discards anything still on the line, so a fresh poll starts clean.</summary>
    public int Drain(TimeSpan window)
    {
        int discarded = 0;
        while (true)
        {
            int read = Transport.Read(_scratch, window);
            if (read == 0)
                return discarded;
            discarded += read;
            window = TimeSpan.Zero;
        }
    }
}
