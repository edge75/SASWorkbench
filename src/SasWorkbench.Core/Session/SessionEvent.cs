using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Session;

/// <summary>
/// Which kind of thing happened. A view filters on these, so they are what a lane checkbox names.
/// </summary>
public enum SessionLane
{
    /// <summary>The host sent something.</summary>
    Transmit,

    /// <summary>The machine answered.</summary>
    Receive,

    /// <summary>The machine reported an exception.</summary>
    Exception,

    /// <summary>Something acted on the machine.</summary>
    Stimulus,

    /// <summary>A person was asked to do something.</summary>
    Prompt,

    /// <summary>A claim was made about the machine, and held or did not.</summary>
    Assertion,

    /// <summary>A test or a run reached a verdict.</summary>
    Verdict,

    /// <summary>The link changed state.</summary>
    Link,

    /// <summary>A test began.</summary>
    TestStarted,
}

/// <summary>
/// One thing that happened in a session, on the session's own clock.
/// </summary>
/// <remarks>
/// One stream rather than several, because the ordering between a poll and the assertion that
/// reads it is the thing a reader is trying to establish, and two lists cannot be interleaved
/// afterwards without a shared clock. One stream per session whatever the channel count: the lane
/// carries the channel, so two hosts interleave here rather than producing two logs to align later.
/// </remarks>
/// <param name="Sequence">Monotonic, so a reader can tell whether anything was dropped.</param>
/// <param name="Lane">What kind of thing this is.</param>
/// <param name="Channel">Which channel it belongs to.</param>
/// <param name="At">When, on the session clock.</param>
/// <param name="Text">The line a person reads.</param>
public sealed record SessionEvent(
    long Sequence,
    SessionLane Lane,
    SasChannel Channel,
    TimeSpan At,
    string Text)
{
    /// <summary>The symbols, wakeup bits included, for a wire event.</summary>
    public IReadOnlyList<SasSymbol> Symbols { get; init; } = [];

    /// <summary>What the engine called this message, when it knew.</summary>
    public string? Note { get; init; }

    /// <summary>How it went, for an assertion or a verdict.</summary>
    public Verdict? Verdict { get; init; }

    /// <summary>The line of script this came from, when it came from one.</summary>
    public SourceLocation? Where { get; init; }

    /// <summary>A sentence that explains the event, when one helps.</summary>
    public string? Detail { get; init; }

    /// <summary>The test this happened inside, when a test was running.</summary>
    public string? Test { get; init; }

    /// <summary>
    /// When a step that took time began, for a step whose <see cref="At"/> is its completion.
    /// </summary>
    /// <remarks>
    /// A long poll is an interval, not an instant: it starts, the wire carries a request and a
    /// reply, and thirty milliseconds later it has an outcome. The stream orders it by the moment
    /// its outcome was known, so that the frames it produced sit above it rather than below it,
    /// and keeps the start here rather than discarding it.
    /// </remarks>
    public TimeSpan? Started { get; init; }

    /// <summary>True when this is the host talking, which is what a poll period is measured on.</summary>
    public bool IsHostPoll => Lane == SessionLane.Transmit;

    /// <summary>Renders the symbols as the captures do: <c>1.01 0.1F</c>.</summary>
    public string ToHex() => string.Join(' ', Symbols.Select(s => s.ToString()));

    public override string ToString() =>
        $"[{At.TotalMilliseconds,9:F2}ms] {Lane,-11} {Text}";
}
