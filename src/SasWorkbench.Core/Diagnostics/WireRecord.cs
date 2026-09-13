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
    SasAddress? address,
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
    /// <summary>
    /// Which machine this message was to or from, when the host that recorded it knew.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recorded rather than derived, because for the traffic that matters most it cannot be
    /// derived. A host's own message carries the address in its first byte, and so does a reply to
    /// a long poll — but a reply to a general poll is a bare exception code and nothing else, one
    /// byte with no address in it. Across a round of a hundred machines that is most of the
    /// traffic, and which machine sent it is answerable only from the poll it answered.
    /// </para>
    /// <para>
    /// Null where nothing established it: an imported capture whose recorder kept no address, or a
    /// record written by a build that did not carry one. Null means <em>not known</em> and never
    /// <em>machine one</em>, because a reader that filled it in would attribute a floor's traffic
    /// to whichever machine happened to be first.
    /// </para>
    /// </remarks>
    public SasAddress? Address { get; init; }

    /// <summary>Renders as the captures do: <c>1.01 0.1F</c>.</summary>
    public string ToHex() => string.Join(' ', Symbols.Select(s => s.ToString()));

    public override string ToString()
    {
        string arrow = Direction == WireDirection.Transmit ? "TX" : "RX";
        string machine = Address is { } at ? $" ${at}" : string.Empty;
        string suffix = Note is null ? string.Empty : $"   {Note}";
        return $"[{Elapsed.TotalMilliseconds,9:F2}ms] {arrow}{machine} {ToHex()}{suffix}";
    }
}
