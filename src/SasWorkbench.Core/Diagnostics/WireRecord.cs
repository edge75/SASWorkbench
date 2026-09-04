using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Diagnostics;

/// <summary>Which way a message went.</summary>
public enum WireDirection
{
    /// <summary>Host to gaming machine.</summary>
    Transmit,

    /// <summary>Gaming machine to host.</summary>
    Receive,
}

/// <summary>
/// Observes every message as it happens. The delegate is allocated once and handed spans, so
/// watching the wire costs nothing per message.
/// </summary>
public delegate void WireObserver(
    WireDirection direction,
    SasChannel channel,
    ReadOnlySpan<SasSymbol> symbols,
    TimeSpan elapsed,
    string? note);

/// <summary>
/// One message, copied out of the ring for a report or a user interface.
/// </summary>
/// <param name="Sequence">Monotonic index, so a reader can tell whether it missed any.</param>
/// <param name="Direction">Which way it went.</param>
/// <param name="Channel">Which of the four channels carried it.</param>
/// <param name="Elapsed">Time since the engine started.</param>
/// <param name="Symbols">The symbols, wakeup bits included.</param>
/// <param name="Note">A short human-readable label, when the engine knew one.</param>
public sealed record WireRecord(
    long Sequence,
    WireDirection Direction,
    SasChannel Channel,
    TimeSpan Elapsed,
    SasSymbol[] Symbols,
    string? Note)
{
    /// <summary>Renders as the captures do: <c>1.01 0.1F</c>.</summary>
    public string ToHex() => string.Join(' ', Symbols.Select(s => s.ToString()));

    public override string ToString()
    {
        string arrow = Direction == WireDirection.Transmit ? "TX" : "RX";
        string suffix = Note is null ? string.Empty : $"   {Note}";
        return $"[{Elapsed.TotalMilliseconds,9:F2}ms] {arrow} {ToHex()}{suffix}";
    }
}
