using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Session;

/// <summary>
/// One time-ordered record of everything a session did, on one clock.
/// </summary>
/// <remarks>
/// <para>
/// This is the reporting surface, not the hot path. The poll engine still records the wire into
/// its own pre-allocated ring, which allocates nothing; the session log is fed from the observer
/// side and allocates one event per record, which is what lets it carry an assertion and a poll in
/// the same list.
/// </para>
/// <para>
/// One log per session whatever the channel count. Each event carries its channel, so two hosts
/// interleave here rather than producing two logs that would have to be aligned afterwards.
/// </para>
/// </remarks>
public sealed class SessionLog(SessionClock? clock = null, int capacity = 20000)
{
    private readonly Lock _gate = new();
    private readonly Queue<SessionEvent> _events = new(Math.Min(capacity, 4096));
    private long _sequence;
    private long _dropped;
    private string? _test;

    /// <summary>The clock every event in this log is measured on.</summary>
    public SessionClock Clock { get; } = clock ?? new SessionClock();

    /// <summary>How many events the log holds before it starts forgetting the oldest.</summary>
    public int Capacity { get; } = capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity));

    /// <summary>How many events have been recorded since the log was created.</summary>
    public long Recorded
    {
        get
        {
            lock (_gate)
                return _sequence;
        }
    }

    /// <summary>How many events were forgotten before anyone read them.</summary>
    public long Dropped
    {
        get
        {
            lock (_gate)
                return _dropped;
        }
    }

    /// <summary>Raised for each event, outside the lock. Handlers run on the recording thread.</summary>
    public event Action<SessionEvent>? Appended;

    /// <summary>Records a message that crossed the wire.</summary>
    public SessionEvent Wire(
        WireDirection direction,
        SasChannel channel,
        ReadOnlySpan<SasSymbol> symbols,
        TimeSpan sourceElapsed,
        ClockBase clockBase,
        string? note = null)
    {
        SasSymbol[] copy = symbols.ToArray();
        return Append(new SessionEvent(
            0,
            direction == WireDirection.Transmit ? SessionLane.Transmit : SessionLane.Receive,
            channel,
            clockBase.Reconcile(sourceElapsed),
            string.Join(' ', copy.Select(s => s.ToString())))
        {
            Symbols = copy,
            Note = note,
        });
    }

    /// <summary>Records an exception the machine reported.</summary>
    public SessionEvent Exception(ExceptionReport report, ClockBase clockBase, string? meaning = null) =>
        Append(new SessionEvent(
            0,
            SessionLane.Exception,
            report.Channel,
            clockBase.Reconcile(report.Elapsed),
            $"exception ${report.Code:X2}")
        {
            Detail = meaning,
        });

    /// <summary>Records a change of link state.</summary>
    public SessionEvent Link(LinkState state, SasChannel channel, ClockBase clockBase, TimeSpan at) =>
        Append(new SessionEvent(
            0,
            SessionLane.Link,
            channel,
            clockBase.Reconcile(at),
            $"link {state.ToString().ToLowerInvariant()}"));

    /// <summary>Records that a test began, which is what "since test start" measures from.</summary>
    /// <param name="at">
    /// When it began. Omit it for now, which is what a live run wants; supply one when replaying a
    /// recorded session, where the times come from the recording rather than from this clock.
    /// </param>
    public SessionEvent TestStarted(
        string name, SourceLocation where, SasChannel channel, TimeSpan? at = null) =>
        Append(
            new SessionEvent(0, SessionLane.TestStarted, channel, at ?? default, name)
            {
                Where = where,
            },
            now: at is null,
            opens: name);

    /// <summary>Records a test's verdict, and closes the test.</summary>
    public SessionEvent TestFinished(TestOutcome outcome, SasChannel channel)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return Append(
            new SessionEvent(0, SessionLane.Verdict, channel, default, outcome.Name)
            {
                Verdict = outcome.Verdict,
                Where = outcome.Location,
                Detail = outcome.SkipReason
                         ?? $"{outcome.Assertions} assert(s) in {outcome.Duration.TotalSeconds:F1}s",
            },
            now: true,
            closes: true);
    }

    /// <summary>
    /// Records one step of a test, on the session clock rather than the test's own.
    /// </summary>
    /// <remarks>
    /// A step that made a claim lands in the assertion lane whatever its verdict — including a
    /// claim that held. A view that shows only failures cannot tell an assertion that passed from
    /// an assertion that was never made.
    /// </remarks>
    /// <param name="at">
    /// When the step finished. Omit it for now, which is what a live run wants; supply one when
    /// replaying a recording, whose times come from the recording rather than from this clock.
    /// </param>
    public SessionEvent Step(
        StepResult step, SasChannel channel, ClockBase clockBase, TimeSpan? at = null)
    {
        ArgumentNullException.ThrowIfNull(step);

        SessionLane lane = step switch
        {
            { IsAssertion: true } => SessionLane.Assertion,
            _ => SessionLane.Stimulus,
        };

        return Append(
            new SessionEvent(0, lane, channel, at ?? default, step.Description)
            {
                Started = clockBase.Reconcile(step.At),
                Verdict = step.Verdict,
                Where = step.Location,
                Detail = step.Reason ?? (step.Expected is null
                    ? null
                    : $"expected {step.Expected}, got {step.Actual}"),
            },
            now: at is null);
    }

    /// <summary>Records that a person was asked to do something.</summary>
    public SessionEvent Prompt(
        string request, SasChannel channel, string? outcome = null, TimeSpan? at = null) =>
        Append(
            new SessionEvent(0, SessionLane.Prompt, channel, at ?? default, request)
            {
                Detail = outcome,
            },
            now: at is null);

    /// <summary>Copies out everything currently held, in the order it happened.</summary>
    /// <remarks>
    /// Ordered by the clock, not by arrival. Two threads write here — the poll thread records the
    /// wire the instant a frame moves, and the runner thread records a step once its outcome is
    /// known — so a frame that happened first can be appended second. <see cref="SessionEvent.Sequence"/>
    /// stays the arrival order, which is what tells a reader whether anything was dropped.
    /// </remarks>
    public IReadOnlyList<SessionEvent> Snapshot()
    {
        lock (_gate)
            return Ordered(_events);
    }

    /// <summary>Copies out the events in the given lanes, in the order they happened.</summary>
    public IReadOnlyList<SessionEvent> Snapshot(IReadOnlySet<SessionLane> lanes)
    {
        ArgumentNullException.ThrowIfNull(lanes);

        lock (_gate)
            return Ordered(_events.Where(e => lanes.Contains(e.Lane)));
    }

    /// <summary>
    /// Where an event belongs in a list already ordered by the clock, for a live view appending
    /// as events arrive. Searching from the end costs nothing in the usual case, where the event
    /// is simply the latest.
    /// </summary>
    public static int PlaceOf(IReadOnlyList<SessionEvent> ordered, SessionEvent record)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(record);

        int index = ordered.Count;
        while (index > 0 && ordered[index - 1].At > record.At)
            index--;

        return index;
    }

    private static int LastIndexOf(IReadOnlyList<SessionEvent> events, SessionLane lane, string text)
    {
        for (int i = events.Count - 1; i >= 0; i--)
        {
            if (events[i].Lane == lane && events[i].Text == text)
                return i;
        }

        return -1;
    }

    private static SessionEvent[] Ordered(IEnumerable<SessionEvent> events) =>
        events.OrderBy(e => e.At).ThenBy(e => e.Sequence).ToArray();

    /// <summary>
    /// When a test ran, so a view can scope the stream to it rather than making the reader scroll.
    /// </summary>
    /// <remarks>
    /// The most recent run of that name, because a suite re-run should scope to what just happened
    /// rather than to the first time the test was seen.
    /// </remarks>
    public (TimeSpan From, TimeSpan To)? WindowOf(string test)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(test);

        IReadOnlyList<SessionEvent> events = Snapshot();

        int started = LastIndexOf(events, SessionLane.TestStarted, test);
        if (started < 0)
            return null;

        int finished = events
            .Select((e, i) => (Event: e, Index: i))
            .Where(x => x.Index > started
                        && x.Event.Lane == SessionLane.Verdict
                        && x.Event.Text == test)
            .Select(x => (int?)x.Index)
            .FirstOrDefault() ?? events.Count - 1;

        return (events[started].At, events[finished].At);
    }

    /// <summary>Forgets everything held, without resetting the sequence numbering.</summary>
    public void Clear()
    {
        lock (_gate)
            _events.Clear();
    }

    /// <summary>
    /// Records something happening now. The clock is read inside the lock, so an event stamped
    /// this way can never be ordered before one already in the log.
    /// </summary>
    private SessionEvent AppendNow(SessionEvent record) => Append(record, now: true);

    /// <param name="opens">The test this event begins, when it begins one.</param>
    /// <param name="closes">Whether this event ends the test that was running.</param>
    /// <remarks>
    /// Opening and closing a test happen inside the same lock as the append. Setting the current
    /// test outside it would let a frame recorded on the poll thread be labelled with a test whose
    /// own start marker had not been written yet.
    /// </remarks>
    private SessionEvent Append(
        SessionEvent record, bool now = false, string? opens = null, bool closes = false)
    {
        SessionEvent stored;
        lock (_gate)
        {
            if (opens is not null)
                _test = opens;

            stored = record with
            {
                Sequence = _sequence++,
                At = now ? Clock.Now : record.At,
                Test = record.Test ?? _test,
            };
            _events.Enqueue(stored);

            while (_events.Count > Capacity)
            {
                _events.Dequeue();
                _dropped++;
            }

            if (closes)
                _test = null;
        }

        Appended?.Invoke(stored);
        return stored;
    }
}
