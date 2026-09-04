using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SasWorkbench.Core.Scripting;
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

    private readonly List<SessionEvent> _pending = [];
    private readonly List<SessionEvent> _shown = [];
    private readonly Lock _gate = new();
    private readonly DispatcherTimer _flush;
    private SessionLog? _log;
    private SessionStamper _stamper = new(TimestampMode.SinceTestStart);

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

    public IReadOnlyList<TimestampMode> TimestampModes { get; } = Enum.GetValues<TimestampMode>();

    public IReadOnlyList<WakeupDisplay> WakeupDisplays { get; } = Enum.GetValues<WakeupDisplay>();

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
            _pending.Clear();
            _pending.AddRange(log.Snapshot());
        }

        _shown.Clear();
        Rows.Clear();
        Flush();
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
    }

    public void Dispose()
    {
        _flush.Stop();
        if (_log is not null)
            _log.Appended -= OnAppended;
    }

    partial void OnTimestampsChanged(TimestampMode value) => Rebuild();

    partial void OnWakeupChanged(WakeupDisplay value) => Rebuild();

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

            arrived = [.. _pending.OrderBy(e => e.At).ThenBy(e => e.Sequence)];
            _pending.Clear();
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
        if (!IsShowing(record.Lane))
            return;

        Rows.Add(Row(record, _stamper.Stamp(record)));
    }

    private void Rebuild()
    {
        _stamper = new SessionStamper(Timestamps);
        Rows.Clear();

        foreach (SessionEvent record in _shown)
        {
            // Every event advances the stamper, including one the filter hides: a poll period
            // measured across a hidden poll would be two periods reported as one.
            TimeSpan offset = _stamper.Stamp(record);
            if (IsShowing(record.Lane))
                Rows.Add(Row(record, offset));
        }
    }

    private bool IsShowing(SessionLane lane) =>
        Lanes.First(l => l.Lane == lane).IsShown;

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
        $"{offset.TotalMilliseconds,9:F1}",
        Label(record.Lane),
        record.Channel == SasChannel.One ? string.Empty : $"c{(int)record.Channel}",
        [.. record.Symbols.Select(s => Symbol(s, Wakeup))],
        record.Symbols.Count > 0 ? string.Empty : record.Text,
        record.Note ?? record.Detail ?? string.Empty,
        record.Verdict is { } verdict ? Chip(verdict) : null,
        record.Where?.ToString() ?? string.Empty);

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
    string Where)
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
