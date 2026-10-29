using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Session;
using SasWorkbench.Core.Wire;
using SasWorkbench.Gui.Controls;

namespace SasWorkbench.Gui.ViewModels;

/// <summary>How a symbol's wakeup bit is drawn.</summary>
public enum WakeupDisplay
{
    /// <summary>The byte alone, with an address byte drawn in the wakeup colour.</summary>
    Colour,

    /// <summary>The flag, a dot, then the byte: <c>1.80</c>.</summary>
    FlagDigit,
}

/// <summary>
/// The spine: one time-ordered view of the session, filtered by lane.
/// </summary>
/// <remarks>
/// <para>
/// It is not a wire log with extra rows bolted on. Everything the session did goes into one list
/// on one clock, and a lane filter decides what is shown — which is why turning the assertion lane
/// on is a checkbox rather than a different view, and why a stimulus can sit immediately above the
/// exception it caused.
/// </para>
/// <para>
/// Events are buffered and flushed on a timer rather than posted one at a time: a two-hundred
/// millisecond poll loop produces five events a second per channel, and a hundred machines would
/// produce five hundred, which is layout churn rather than information.
/// </para>
/// </remarks>
public sealed partial class SessionStreamViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// How many rows the view keeps. The log itself holds more; this is what is on screen.
    /// </summary>
    private const int RowLimit = 4000;

    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>How far either side of a test's own window a narrowed stream reaches.</summary>
    private static readonly TimeSpan ScopeMargin = TimeSpan.FromMilliseconds(250);

    private readonly List<SessionEvent> _pending = [];
    private readonly List<SessionEvent> _shown = [];
    private readonly Lock _gate = new();
    private readonly DispatcherTimer _flush;
    private SessionLog? _log;
    private SessionStamper _stamper = new(TimestampMode.SinceTestStart);
    private long _highest = -1;

    public SessionStreamViewModel()
    {
        Lanes =
        [
            new LaneFilter(SessionLane.Transmit, "TX", true),
            new LaneFilter(SessionLane.Receive, "RX", true),
            new LaneFilter(SessionLane.Exception, "EXC", false),
            new LaneFilter(SessionLane.Stimulus, "EGM", false),
            new LaneFilter(SessionLane.Prompt, "ASK", false),
            new LaneFilter(SessionLane.Assertion, "TEST", false),
            new LaneFilter(SessionLane.Verdict, "VERDICT", false),
            new LaneFilter(SessionLane.TestStarted, "START", false),
            new LaneFilter(SessionLane.Link, "LINK", false),
        ];

        foreach (LaneFilter lane in Lanes)
            lane.PropertyChanged += (_, _) => Rebuild();

        _flush = new DispatcherTimer(FlushInterval, DispatcherPriority.Background, (_, _) => Flush());
        _flush.Start();
    }

    /// <summary>Which lanes are showing. TX and RX by default, so day one looks like a wire log.</summary>
    public IReadOnlyList<LaneFilter> Lanes { get; }

    /// <summary>The rows on screen.</summary>
    public ObservableCollection<SessionRow> Rows { get; } = [];

    /// <summary>Whether the view scrolls to the newest row.</summary>
    [ObservableProperty]
    public partial bool Follow { get; set; } = true;

    /// <summary>What the displayed timestamps are measured from.</summary>
    [ObservableProperty]
    public partial TimestampMode Timestamps { get; set; } = TimestampMode.SinceTestStart;

    /// <summary>How the wakeup bit is drawn.</summary>
    [ObservableProperty]
    public partial WakeupDisplay Wakeup { get; set; } = WakeupDisplay.Colour;

    /// <summary>How many events the log has forgotten, so a gap is never silent.</summary>
    [ObservableProperty]
    public partial string? Gap { get; set; }

    /// <summary>
    /// What the stream is narrowed to, when it is narrowed. Named rather than merely applied,
    /// because a filtered view that does not say it is filtered reads as an empty session.
    /// </summary>
    [ObservableProperty]
    public partial string? Scope { get; set; }

    /// <summary>The narrowing as the strip above the stream says it, or null when there is none.</summary>
    public string? ScopeLine =>
        Scope is null ? null : $"Narrowed to {Scope} ±{ScopeMargin.TotalMilliseconds:F0} ms";

    partial void OnScopeChanged(string? value) => OnPropertyChanged(nameof(ScopeLine));

    /// <summary>
    /// What to look for, written as one line: <c>lp 2F</c>, <c>exception 4A</c> or its name,
    /// <c>general</c>, <c>sync</c>, or any text to find in the symbols or the note.
    /// </summary>
    [ObservableProperty]
    public partial string? Find { get; set; }

    /// <summary>Whose messages the search looks at.</summary>
    [ObservableProperty]
    public partial TraceSide Side { get; set; } = TraceSide.Both;

    /// <summary>
    /// Whether the stream shows only what matched, or shows everything with the matches marked.
    /// </summary>
    /// <remarks>
    /// Highlighting is the default because a filtered stream loses what a message sat between,
    /// and in a protocol log the neighbours are usually the point.
    /// </remarks>
    [ObservableProperty]
    public partial bool HideOthers { get; set; }

    /// <summary>How many rows matched, so a search that found nothing says so.</summary>
    [ObservableProperty]
    public partial string? Matches { get; set; }

    private TimeSpan? _from;
    private TimeSpan? _to;
    private TraceQuery _query = new();

    public IReadOnlyList<TimestampMode> TimestampModes { get; } = Enum.GetValues<TimestampMode>();

    public IReadOnlyList<WakeupDisplay> WakeupDisplays { get; } = Enum.GetValues<WakeupDisplay>();

    /// <summary>Whose messages a search may be narrowed to.</summary>
    public IReadOnlyList<TraceSide> Sides { get; } = Enum.GetValues<TraceSide>();

    /// <summary>
    /// Points the spine at a log. The stream outlives any one link, so a run that tears the
    /// interactive session down and builds its own leaves a labelled seam rather than a hole.
    /// </summary>
    public void Watch(SessionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (_log is not null)
            _log.Appended -= OnAppended;

        _log = log;
        _log.Appended += OnAppended;

        lock (_gate)
        {
            // The handler is already attached, so an event recorded between attaching and taking
            // this snapshot arrives twice. Sequence numbers are what settle it: the flush keeps
            // only what it has not already seen.
            _pending.Clear();
            _pending.AddRange(log.Snapshot());
            _highest = -1;
        }

        // A new log is a new clock. Carrying the old one's "previous message" into it would
        // measure the first event of this session from the last event of the one before.
        _stamper = NewStamper();
        _shown.Clear();
        Rows.Clear();
        Flush();
    }

    /// <summary>
    /// Narrows the stream to a window of the session, such as the one test that failed.
    /// </summary>
    public void ScopeTo(TimeSpan from, TimeSpan to, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        // A margin either side, because the frame that explains a failure is usually the one just
        // before the window rather than inside it.
        _from = from - ScopeMargin;
        _to = to + ScopeMargin;
        Scope = label;
        Rebuild();
    }

    /// <summary>Widens the stream back to the whole session.</summary>
    [RelayCommand]
    public void ClearScope()
    {
        _from = null;
        _to = null;
        Scope = null;
        Rebuild();
    }

    /// <summary>Empties the view. The log keeps what it holds.</summary>
    [RelayCommand]
    public void Clear()
    {
        lock (_gate)
            _pending.Clear();

        _shown.Clear();
        Rows.Clear();
        Gap = null;
        _stamper.Reset();
    }

    public void Dispose()
    {
        _flush.Stop();
        if (_log is not null)
            _log.Appended -= OnAppended;
    }

    partial void OnFindChanged(string? value) => Research();

    partial void OnSideChanged(TraceSide value) => Research();

    partial void OnHideOthersChanged(bool value) => Rebuild();

    /// <summary>Re-reads the search and applies it.</summary>
    private void Research()
    {
        _query = TraceQuery.Parse(Find, Side);
        Rebuild();
    }

    partial void OnTimestampsChanged(TimestampMode value) => Rebuild();

    partial void OnWakeupChanged(WakeupDisplay value) => Rebuild();

    /// <summary>
    /// A stamper for the chosen mode, given the clock of whatever log is being watched.
    /// </summary>
    /// <remarks>
    /// Wall-clock time needs the instant the session began, and before a log arrives there is no
    /// such instant — and no events either. Stamping relatively until one arrives shows nothing
    /// wrongly, because there is nothing on screen to show; answering from this moment's clock
    /// would put every row of a session recorded yesterday at today's time.
    /// </remarks>
    private SessionStamper NewStamper()
    {
        DateTimeOffset? origin = _log?.Clock.StartedAt;
        return Timestamps == TimestampMode.WallClock && origin is null
            ? new SessionStamper(TimestampMode.SinceTestStart)
            : new SessionStamper(Timestamps, origin);
    }

    /// <summary>Called on whichever thread recorded the event, so it only buffers.</summary>
    private void OnAppended(SessionEvent record)
    {
        lock (_gate)
            _pending.Add(record);
    }

    private void Flush()
    {
        SessionEvent[] arrived;
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                RefreshGap();
                return;
            }

            arrived =
            [
                .. _pending
                    .Where(e => e.Sequence > _highest)
                    .DistinctBy(e => e.Sequence)
                    .OrderBy(e => e.At)
                    .ThenBy(e => e.Sequence),
            ];
            _pending.Clear();

            if (arrived.Length == 0)
            {
                RefreshGap();
                return;
            }

            _highest = arrived.Max(e => e.Sequence);
        }

        // An event that happened before the last one already shown changes what every later row is
        // measured from, so the cheap tail append is only correct when the batch lands at the end.
        TimeSpan tail = _shown.Count == 0 ? TimeSpan.MinValue : _shown[^1].At;
        bool outOfOrder = arrived[0].At < tail;

        foreach (SessionEvent record in arrived)
            _shown.Insert(SessionLog.PlaceOf(_shown, record), record);

        if (outOfOrder)
        {
            Rebuild();
        }
        else
        {
            foreach (SessionEvent record in arrived)
                Show(record);
        }

        Trim();
        RefreshGap();
    }

    private void Show(SessionEvent record)
    {
        TimeSpan offset = _stamper.Stamp(record);
        if (IsShowing(record))
            Rows.Add(Row(record, offset));
    }

    private void Rebuild()
    {
        _stamper = NewStamper();
        Rows.Clear();

        foreach (SessionEvent record in _shown)
        {
            // Every event advances the stamper, including one the filter hides: a poll period
            // measured across a hidden poll would be two periods reported as one.
            TimeSpan offset = _stamper.Stamp(record);
            if (IsShowing(record))
                Rows.Add(Row(record, offset));
        }

        CountMatches();
    }

    private bool IsShowing(SessionEvent record) =>
        Lanes.First(l => l.Lane == record.Lane).IsShown
        && (_from is null || record.At >= _from)
        && (_to is null || record.At <= _to)
        && (!HideOthers || _query.IsEmpty || IsMatch(record));

    /// <summary>
    /// Whether the search found this event. Only the two wire lanes carry symbols to search, so a
    /// search never matches a verdict or a prompt - and a search for text still reads their note.
    /// </summary>
    private bool IsMatch(SessionEvent record)
    {
        if (_query.IsEmpty)
            return false;

        WireDirection direction = record.Lane == SessionLane.Transmit
            ? WireDirection.Transmit
            : WireDirection.Receive;

        return _query.Matches(direction, [.. record.Symbols], record.Note ?? record.Detail);
    }

    /// <summary>Says how the search went, including when it went badly.</summary>
    private void CountMatches()
    {
        if (_query.IsEmpty)
        {
            Matches = null;
            return;
        }

        int found = Rows.Count(r => r.IsMatch);
        Matches = found == 0
            ? $"nothing matched {_query}"
            : $"{found} matched {_query}";
    }

    private void Trim()
    {
        while (_shown.Count > RowLimit)
            _shown.RemoveAt(0);

        while (Rows.Count > RowLimit)
            Rows.RemoveAt(0);
    }

    private void RefreshGap()
    {
        long dropped = _log?.Dropped ?? 0;
        Gap = dropped == 0 ? null : $"{dropped} earlier events were dropped";
    }

    private SessionRow Row(SessionEvent record, TimeSpan offset) => new(
        record,
        SessionStamper.Format(offset, _stamper.Mode),
        Label(record.Lane),
        record.Channel == SasChannel.One ? string.Empty : $"c{(int)record.Channel}",
        [.. record.Symbols.Select(s => Symbol(s, Wakeup))],
        record.Symbols.Count > 0 ? string.Empty : record.Text,
        record.Note ?? record.Detail ?? string.Empty,
        record.Verdict is { } verdict ? Chip(verdict) : null,
        record.Where?.ToString() ?? string.Empty,
        IsMatch(record));

    private static WireSymbol Symbol(SasSymbol symbol, WakeupDisplay display) => new(
        display == WakeupDisplay.FlagDigit ? symbol.ToString() : $"{symbol.Data:X2}",
        symbol.Wakeup);

    private static string Label(SessionLane lane) => lane switch
    {
        SessionLane.Transmit => "TX",
        SessionLane.Receive => "RX",
        SessionLane.Exception => "EXC",
        SessionLane.Stimulus => "EGM",
        SessionLane.Prompt => "ASK",
        SessionLane.Assertion => "TEST",
        SessionLane.Verdict => "=",
        SessionLane.TestStarted => ">",
        SessionLane.Link => "LNK",
        _ => "?",
    };

    private static ChipStatus Chip(Verdict verdict) => verdict switch
    {
        Verdict.Pass => ChipStatus.Pass,
        Verdict.Fail => ChipStatus.Fail,
        Verdict.Skip => ChipStatus.Skip,
        _ => ChipStatus.Error,
    };
}

/// <summary>One lane, and whether it is showing.</summary>
public sealed partial class LaneFilter(SessionLane lane, string name, bool shown) : ObservableObject
{
    public SessionLane Lane { get; } = lane;

    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool IsShown { get; set; } = shown;
}

/// <summary>One symbol, and whether it carries the wakeup bit.</summary>
/// <remarks>
/// A joined string cannot highlight bit 8, so the symbols travel as a list and the row template
/// styles each one. Built on the recording thread, which is where the event already exists.
/// </remarks>
public sealed record WireSymbol(string Text, bool IsWakeup);

/// <summary>One row of the session stream.</summary>
public sealed record SessionRow(
    SessionEvent Event,
    string At,
    string Lane,
    string Channel,
    IReadOnlyList<WireSymbol> Symbols,
    string Text,
    string Note,
    ChipStatus? Verdict,
    string Where,
    bool IsMatch = false)
{
    /// <summary>True for a message this host sent, which the row styling leans on.</summary>
    public bool IsTransmit => Event.Lane == SessionLane.Transmit;

    /// <summary>True when there is a channel badge to draw at all.</summary>
    public bool HasChannel => Channel.Length > 0;

    /// <summary>True for a wire row, which draws hex rather than words.</summary>
    public bool HasSymbols => Symbols.Count > 0;

    /// <summary>True for a row that draws words rather than hex.</summary>
    public bool HasText => Text.Length > 0;

    /// <summary>True when this row carries a verdict chip.</summary>
    public bool HasVerdict => Verdict is not null;
}
