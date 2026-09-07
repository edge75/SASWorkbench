namespace SasWorkbench.Core.Session;

/// <summary>What the zero of a displayed timestamp is.</summary>
/// <remarks>
/// The distinction is the point. In a two-hundred-millisecond poll loop, <see cref="SincePreviousPoll"/>
/// reads about 200 ms and <see cref="SincePreviousMessage"/> reads about 30 ms, and the two answer
/// different questions: how the loop is running, and how quickly the machine turned a reply around.
/// A mode is chosen per view and never written into the record, so the log itself is unaffected.
/// </remarks>
public enum TimestampMode
{
    /// <summary>Zero is the start of the current test, or of the session before one begins.</summary>
    SinceTestStart,

    /// <summary>Zero is the previous host poll on the same channel — the poll period.</summary>
    SincePreviousPoll,

    /// <summary>Zero is the previous event of any kind — turnaround latency.</summary>
    SincePreviousMessage,

    /// <summary>Zero is midnight — when it happened in the world, in this machine's own zone.</summary>
    /// <remarks>
    /// The mode for matching a run against something outside it: another tool's log, a machine's
    /// own log, or somebody's account of what they were doing. It needs no field the record does
    /// not already hold — the session carries the instant it began and every event carries its
    /// elapsed time — which is why it is derived here like the other three rather than written
    /// into the log.
    /// </remarks>
    WallClock,
}

/// <summary>
/// Turns a session event's absolute time into the offset a view shows.
/// </summary>
/// <remarks>
/// Stateful and incremental: events are stamped in order as they arrive, so a live view costs one
/// subtraction per event rather than a re-scan of the log. Reset it when the mode changes or the
/// events are replayed from the start.
/// </remarks>
/// <param name="mode">Which zero to measure from.</param>
/// <param name="origin">
/// When the session began, which <see cref="TimestampMode.WallClock"/> needs and the other three
/// modes do not. Asking for wall-clock time without it is refused rather than answered from the
/// current time: a recorded session read back tomorrow would be stamped with tomorrow's clock and
/// nothing would say so.
/// </param>
public sealed class SessionStamper(TimestampMode mode, DateTimeOffset? origin = null)
{
    private readonly Dictionary<int, TimeSpan> _previousPoll = [];
    private TimeSpan _testStart;
    private TimeSpan _previousMessage;
    private bool _seenMessage;

    /// <summary>Which zero this stamper is measuring from.</summary>
    public TimestampMode Mode { get; } = mode == TimestampMode.WallClock && origin is null
        ? throw new ArgumentException(
            "Wall-clock time needs the instant the session began. Without it there is nothing to "
            + "add the elapsed times to.", nameof(origin))
        : mode;

    /// <summary>When the session began, for the one mode that measures from outside it.</summary>
    public DateTimeOffset? Origin { get; } = origin;

    /// <summary>Forgets everything, as if no event had been stamped.</summary>
    public void Reset()
    {
        _previousPoll.Clear();
        _testStart = TimeSpan.Zero;
        _previousMessage = TimeSpan.Zero;
        _seenMessage = false;
    }

    /// <summary>
    /// The offset to show for one event, and advances the state. Events must arrive in order.
    /// </summary>
    /// <remarks>
    /// The first event in each mode has no predecessor to measure from, so it reads as its own
    /// absolute time rather than as zero: a made-up zero would claim a gap that was never measured.
    /// </remarks>
    public TimeSpan Stamp(SessionEvent record)
    {
        ArgumentNullException.ThrowIfNull(record);

        TimeSpan offset = Mode switch
        {
            TimestampMode.SinceTestStart => record.At - _testStart,

            TimestampMode.SincePreviousPoll =>
                _previousPoll.TryGetValue((int)record.Channel, out TimeSpan poll)
                    ? record.At - poll
                    : record.At,

            TimestampMode.SincePreviousMessage => _seenMessage
                ? record.At - _previousMessage
                : record.At,

            // Local rather than universal, because the thing this is matched against is a log
            // somebody read off a screen in the room the machine was in.
            TimestampMode.WallClock => (Origin!.Value + record.At).ToLocalTime().TimeOfDay,

            _ => record.At,
        };

        if (record.Lane == SessionLane.TestStarted)
            _testStart = record.At;

        if (record.IsHostPoll)
            _previousPoll[(int)record.Channel] = record.At;

        _previousMessage = record.At;
        _seenMessage = true;

        return offset;
    }

    /// <summary>Stamps a whole sequence at once, for a report.</summary>
    public static IReadOnlyList<(SessionEvent Event, TimeSpan Offset)> StampAll(
        IEnumerable<SessionEvent> events, TimestampMode mode, DateTimeOffset? origin = null)
    {
        ArgumentNullException.ThrowIfNull(events);

        var stamper = new SessionStamper(mode, origin);
        return events.Select(e => (e, stamper.Stamp(e))).ToList();
    }

    /// <summary>
    /// Renders an offset the way its mode should read, so every view spells one the same way.
    /// </summary>
    /// <remarks>
    /// A time of day has to be written as one. The other three modes are durations and read as
    /// milliseconds, but 47,183,204.6 ms past midnight is a number nobody can compare with a
    /// timestamp in another program's log, which is the only reason that mode exists.
    /// </remarks>
    public static string Format(TimeSpan offset, TimestampMode mode) => mode == TimestampMode.WallClock
        ? offset.ToString(@"hh\:mm\:ss\.fff")
        : $"{offset.TotalMilliseconds,9:F1}";
}
