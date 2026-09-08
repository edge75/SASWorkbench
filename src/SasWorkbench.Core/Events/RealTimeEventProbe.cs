using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Events;

/// <summary>What one exception's payload came to over a measurement.</summary>
/// <param name="Code">The exception code.</param>
/// <param name="Seen">How many times the machine reported it in the event format.</param>
/// <param name="First">The payload bytes of the first one, or null when none arrived.</param>
/// <param name="Fits">Whether the established payload read those bytes, when there are any.</param>
/// <param name="Failure">Why the payload did not fit, when it did not.</param>
public sealed record EventFinding(
    byte Code, int Seen, byte[]? First, bool? Fits, string? Failure)
{
    /// <summary>The plan's entry for this exception, when it carries a payload.</summary>
    public EventProbeEntry? Planned => RealTimeEventPlan.For(Code);

    /// <summary>The payload this build has established, when it has one.</summary>
    public SasEventLayout? Layout => SasEventLayouts.For(Code);

    /// <summary>The bytes, as they would be written down.</summary>
    public string Hex => First is null ? string.Empty : Convert.ToHexString(First);
}

/// <summary>
/// Turns real-time event reporting on, watches what the machine reports, and turns it off again.
/// </summary>
/// <remarks>
/// <para>
/// A deliberate measurement rather than something an ordinary run does. Long poll <c>$0E</c>
/// changes how the machine answers every general poll after it, which is exactly why the catalogue
/// sweep withholds it: a run that turned it on would have changed the meaning of everything it
/// measured afterwards. So this is asked for by name, it says what it is about to send before it
/// sends anything, and it puts the machine back.
/// </para>
/// <para>
/// It asserts nothing. What it produces is bytes, and the layouts are still read out of the
/// library's own builders — the bytes are what turns a reading into an established payload, the
/// same discipline the catalogue sweep uses.
/// </para>
/// </remarks>
public sealed class RealTimeEventProbe : IDisposable
{
    /// <summary>The command that turns reporting on and off.</summary>
    public const byte EnableCommand = 0x0E;

    /// <summary>The command that makes the machine play rounds by itself.</summary>
    public const byte AutoRebetCommand = 0xAA;

    private readonly PollEngine _engine;
    private readonly List<ExceptionReport> _reports = [];
    private readonly Lock _gate = new();
    private bool _watching;

    public RealTimeEventProbe(PollEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _engine.ExceptionReported += Record;
        _watching = true;
    }

    /// <summary>Every exception the machine reported while this was watching, in order.</summary>
    public IReadOnlyList<ExceptionReport> Reported
    {
        get
        {
            lock (_gate)
                return [.. _reports];
        }
    }

    /// <summary>Turns real-time event reporting on.</summary>
    public LongPollResult Enable() => Set(on: true);

    /// <summary>Turns real-time event reporting off, putting the machine back as it was found.</summary>
    public LongPollResult Disable() => Set(on: false);

    /// <summary>
    /// Tells the machine to take notes.
    /// </summary>
    /// <remarks>
    /// Sent because a machine whose host has disabled its note acceptor drops a note with nothing
    /// on the wire to say so, which is indistinguishable here from an event this build cannot
    /// read. It is left enabled afterwards rather than put back: which of the two states the
    /// machine was found in is not knowable — no long poll reports it — so restoring it would mean
    /// guessing, and a note acceptor a host has enabled is the ordinary state of a machine.
    /// </remarks>
    public LongPollResult EnableNoteAcceptor() => _engine.Execute(
        0x06, default, purpose: "deliberately sent: enable the note acceptor");

    /// <summary>Turns the machine's own play-by-itself mode on or off.</summary>
    public LongPollResult AutoRebet(bool on) => _engine.Execute(
        AutoRebetCommand, [on ? (byte)0x01 : (byte)0x00],
        purpose: $"deliberately sent: auto rebet {(on ? "on" : "off")}");

    /// <summary>
    /// What every payload-carrying exception came to, whether or not one arrived.
    /// </summary>
    /// <remarks>
    /// Every entry in the plan is reported, including the ones nothing produced. An event this
    /// measurement never saw is a payload established from source alone, and leaving it out of the
    /// table would make the measurement look more complete than it is.
    /// </remarks>
    public IReadOnlyList<EventFinding> Findings()
    {
        IReadOnlyList<ExceptionReport> reports = Reported;
        var findings = new List<EventFinding>();

        foreach (EventProbeEntry entry in RealTimeEventPlan.All)
            findings.Add(Finding(entry.Code, reports));

        // An exception the plan does not carry, reported with bytes after its code, is a payload
        // nobody here knew about. Worth more than any of the rows above, so it is not dropped.
        foreach (byte code in reports
                     .Where(r => r.Data is { Length: > 0 } && RealTimeEventPlan.For(r.Code) is null)
                     .Select(r => r.Code)
                     .Distinct()
                     .Order())
        {
            findings.Add(Finding(code, reports));
        }

        return findings;
    }

    /// <summary>Every exception that arrived carrying nothing, which is most of them.</summary>
    public IReadOnlyList<byte> CarriedNothing() => Reported
        .Where(r => r.Data is { Length: 0 })
        .Select(r => r.Code)
        .Distinct()
        .Order()
        .ToList();

    /// <summary>
    /// Exceptions that arrived as bare codes while reporting was meant to be on.
    /// </summary>
    /// <remarks>
    /// Should be empty after <see cref="Enable"/> has been acknowledged. One that is not is a
    /// finding about the machine rather than about the measurement, so it is counted rather than
    /// filtered out.
    /// </remarks>
    public IReadOnlyList<byte> ArrivedAsBareCodes() => Reported
        .Where(r => r.Data is null)
        .Select(r => r.Code)
        .Distinct()
        .Order()
        .ToList();

    /// <summary>Stops watching, so anything after this belongs to whoever comes next.</summary>
    public void Dispose()
    {
        if (!_watching)
            return;

        _engine.ExceptionReported -= Record;
        _watching = false;
    }

    private EventFinding Finding(byte code, IReadOnlyList<ExceptionReport> reports)
    {
        ExceptionReport? first = reports.FirstOrDefault(r => r.Code == code && r.Data is not null);
        int seen = reports.Count(r => r.Code == code && r.Data is not null);

        if (first?.Data is not { } payload)
            return new EventFinding(code, 0, null, null, null);

        if (SasEventLayouts.For(code) is not { } layout)
        {
            return new EventFinding(code, seen, payload, false,
                "this build has established no payload for it, so its bytes are recorded and "
                + "nothing is claimed about them");
        }

        byte[] frame = new byte[SasEventLayouts.HeaderLength + payload.Length + 2];
        frame[1] = SasEventLayouts.Marker;
        frame[2] = code;
        payload.CopyTo(frame, SasEventLayouts.HeaderLength);

        SasDecoded decoded = SasMessageDecoder.DecodeEvent(layout, frame);
        bool complete = decoded.Succeeded && decoded["unmodelled_trailer"] is null;
        return new EventFinding(code, seen, payload, complete,
            decoded.Failure ?? (complete
                ? null
                : "the payload read, and there are bytes after it the layout does not describe"));
    }

    private LongPollResult Set(bool on) => _engine.Execute(
        EnableCommand, [on ? (byte)0x01 : (byte)0x00],
        purpose: $"deliberately sent: real-time event reporting {(on ? "on" : "off")}");

    private void Record(ExceptionReport report)
    {
        lock (_gate)
            _reports.Add(report);
    }
}
