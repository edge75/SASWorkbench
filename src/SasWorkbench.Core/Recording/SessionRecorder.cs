using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Session;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Recording;

/// <summary>
/// Watches a person drive a machine by hand, and keeps enough to write a script that does it again.
/// </summary>
/// <remarks>
/// <para>
/// Two streams go in and they are not interchangeable. The <b>actions</b> are what the person did:
/// exact, reproducible, and the only thing a script can be built out of. The <b>wire</b> is what
/// the machine answered: not reproducible at all, and useful only for deciding what may honestly
/// be asserted. Building a script out of the second is the mistake this class exists to avoid — a
/// script that replays a machine's answers asserts its way to a pass without ever doing anything.
/// </para>
/// <para>
/// It records rather than judges. What may be asserted, where a step's place in a transaction is
/// a guess, and which waits were a person thinking are all decided by
/// <see cref="ScriptEmitter"/>, once, over the finished recording — because most of those
/// questions cannot be answered until the session is over.
/// </para>
/// </remarks>
public sealed class SessionRecorder
{
    private readonly Lock _gate = new();
    private readonly List<RecordedAction> _actions = [];
    private readonly SessionLog _log;

    public SessionRecorder(SessionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <summary>When recording began, on the session clock.</summary>
    public TimeSpan StartedAt { get; private set; }

    /// <summary>Whether it is running.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>How many actions have been kept so far.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _actions.Count;
        }
    }

    /// <summary>
    /// Starts, forgetting anything recorded before.
    /// </summary>
    /// <param name="preamble">
    /// What was already true when recording began, as the steps that would make it true again.
    /// The link is the case in point: a person connects, looks at the machine for a while, and
    /// only then starts recording — and a script with no link in it cannot run at all. So the
    /// connection is not an action inside the recording, it is a fact about it, and it goes in
    /// here rather than being waited for.
    /// </param>
    public void Start(IEnumerable<RecordedAction>? preamble = null)
    {
        lock (_gate)
        {
            _actions.Clear();
            StartedAt = _log.Clock.Now;
            IsRecording = true;

            if (preamble is null)
                return;

            foreach (RecordedAction action in preamble)
                _actions.Add(action with { At = StartedAt, Preamble = true });
        }
    }

    /// <summary>
    /// Keeps one thing the person did.
    /// </summary>
    /// <remarks>
    /// Called by whatever the person is driving — a window, or a test standing in for one — rather
    /// than inferred from the wire. That is what makes the recording an account of intent.
    /// </remarks>
    public void Did(string what, ScriptStep? step = null, string? refused = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(what);

        lock (_gate)
        {
            if (!IsRecording)
                return;

            _actions.Add(new RecordedAction(_log.Clock.Now, Channel(step), what, step, refused));
        }
    }

    /// <summary>How long the machine has to go quiet before a recording is taken as finished.</summary>
    public static readonly TimeSpan DefaultQuiet = TimeSpan.FromMilliseconds(700);

    /// <summary>The longest a stop will wait for that quiet.</summary>
    public static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Stops, and hands back everything needed to write a script.
    /// </summary>
    /// <param name="quiet">
    /// How long the machine must say nothing before the recording is taken as finished.
    /// </param>
    /// <remarks>
    /// It waits, and the wait is the point. A machine reports one exception per poll, so what the
    /// last thing a person did caused is still inside the machine when they let go of the mouse —
    /// and a recording that stopped at that instant held the door being opened and not the machine
    /// saying so, which came out as a script that opened a door and asserted nothing about it. The
    /// wait is the same rule the runner's own 'forget exceptions' uses: quiet means the queue is
    /// empty.
    /// </remarks>
    public Recorded Stop(TimeSpan? quiet = null)
    {
        TimeSpan settle = quiet ?? DefaultQuiet;
        long deadline = Environment.TickCount64 + (long)LongestWait.TotalMilliseconds;
        long recorded = _log.Recorded;
        long lastChange = Environment.TickCount64;

        while (settle > TimeSpan.Zero && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(20);
            long now = _log.Recorded;
            if (now != recorded)
            {
                recorded = now;
                lastChange = Environment.TickCount64;
                continue;
            }

            if (Environment.TickCount64 - lastChange >= (long)settle.TotalMilliseconds)
                break;
        }

        lock (_gate)
        {
            IsRecording = false;
            return new Recorded
            {
                From = StartedAt,
                To = _log.Clock.Now,
                Actions = [.. _actions],
                Events = [.. _log.Snapshot().Where(e => e.At >= StartedAt)],
            };
        }
    }

    private static SasChannel Channel(ScriptStep? step) =>
        step switch
        {
            ConnectStep connect => connect.Channel,
            UseChannelStep use => use.Channel,
            _ => SasChannel.One,
        };
}

/// <summary>
/// A finished recording: what the person did, and everything the session saw while they did it.
/// </summary>
public sealed record Recorded
{
    public required TimeSpan From { get; init; }

    public required TimeSpan To { get; init; }

    /// <summary>What the person did, in order.</summary>
    public required IReadOnlyList<RecordedAction> Actions { get; init; }

    /// <summary>Everything the session recorded while it was running, in order.</summary>
    public required IReadOnlyList<SessionEvent> Events { get; init; }

    /// <summary>How long it ran.</summary>
    public TimeSpan Duration => To - From;

    /// <summary>Whether anything was done beyond what was already true when it began.</summary>
    public bool AnythingHappened => Actions.Any(action => !action.Preamble);

    /// <summary>The wire, as the splitter and the emitter read it.</summary>
    public IReadOnlyList<WireRecord> Wire =>
    [
        .. Events
            .Where(e => e.Lane is SessionLane.Transmit or SessionLane.Receive)
            .Select((e, index) => new WireRecord(
                index,
                e.Lane == SessionLane.Transmit ? WireDirection.Transmit : WireDirection.Receive,
                e.Channel,
                e.At,
                [.. e.Symbols],
                e.Note)),
    ];

    /// <summary>The exceptions the machine reported while it ran, in order.</summary>
    public IReadOnlyList<(TimeSpan At, SasChannel Channel, byte Code)> Exceptions =>
    [
        .. Events
            .Where(e => e.Lane == SessionLane.Exception)
            .Select(e => (e.At, e.Channel, Code(e.Text)))
            .Where(e => e.Item3 != 0),
    ];

    /// <summary>The link this recording was made over, from the connect the person made.</summary>
    public ConnectStep? Connect =>
        Actions.Select(a => a.Step).OfType<ConnectStep>().FirstOrDefault();

    // "exception $3D" is what the log writes; read back rather than carried separately so the
    // recording and the stream cannot disagree about what happened.
    private static byte Code(string text)
    {
        int dollar = text.IndexOf('$');
        return dollar >= 0
               && byte.TryParse(
                   text.AsSpan(dollar + 1, Math.Min(2, text.Length - dollar - 1)),
                   System.Globalization.NumberStyles.HexNumber,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out byte code)
            ? code
            : (byte)0;
    }
}
