using System.Diagnostics;

namespace SasWorkbench.Core.Session;

/// <summary>
/// The one clock a session is measured on.
/// </summary>
/// <remarks>
/// Two things in this application keep their own time and neither of them is the session: a poll
/// engine measures from the moment it started, and a test measures from the moment it began. A
/// wire record at 5927 ms and a step at 12 ms may be the same instant, so putting them in one
/// ordered stream means converting both onto a clock that outlives either — otherwise "within five
/// seconds" is a claim about two different zeroes.
/// </remarks>
public sealed class SessionClock
{
    private readonly long _origin = Stopwatch.GetTimestamp();

    /// <summary>When the session began, for a report header.</summary>
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>How long the session has been running.</summary>
    public TimeSpan Now => Stopwatch.GetElapsedTime(_origin);

    /// <summary>
    /// The conversion for a source starting now, such as a poll engine or a test.
    /// </summary>
    public ClockBase Mark() => new(Now);

    /// <summary>The conversion for a source that started at a known session time.</summary>
    public ClockBase MarkAt(TimeSpan sessionTime) => new(sessionTime);
}

/// <summary>
/// Converts one source's own elapsed time onto the session clock.
/// </summary>
/// <param name="Offset">The session time at which that source's zero fell.</param>
public readonly record struct ClockBase(TimeSpan Offset)
{
    /// <summary>The base for a source whose zero is the session's own zero.</summary>
    public static ClockBase Session => new(TimeSpan.Zero);

    /// <summary>Puts a source's elapsed time on the session clock.</summary>
    public TimeSpan Reconcile(TimeSpan sourceElapsed) => Offset + sourceElapsed;
}
