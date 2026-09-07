using System.Net;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Session;

/// <summary>What to connect to, and how.</summary>
/// <param name="Transport">Which kind of link.</param>
/// <param name="Target">An endpoint or a port name; ignored for a loopback link.</param>
/// <param name="Address">The machine's poll address.</param>
/// <param name="Channel">Which channel to drive.</param>
/// <param name="PollInterval">How often to poll.</param>
public sealed record HostSessionSpec(
    ScriptTransport Transport,
    string Target,
    SasAddress Address,
    SasChannel Channel = SasChannel.One,
    TimeSpan? PollInterval = null,
    TimeSpan? ResponseTimeout = null)
{
    /// <summary>The interval to use when none was given.</summary>
    public TimeSpan Interval => PollInterval ?? TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How long the host waits for a machine to answer. A script run already says this; a session
    /// could not, so it took the engine's own default whatever it was talking to.
    /// </summary>
    public TimeSpan Patience => ResponseTimeout ?? TimeSpan.FromMilliseconds(200);
}

/// <summary>One meter, as read from the machine.</summary>
/// <param name="Name">The name a script would use.</param>
/// <param name="Command">The long poll it came from.</param>
/// <param name="Unit">What the number means.</param>
/// <param name="Value">The value, or null when it could not be read.</param>
/// <param name="Problem">Why it could not be read, when it could not.</param>
public sealed record MeterReading(
    string Name,
    byte Command,
    SasUnit Unit,
    long? Value,
    string? Problem = null)
{
    public override string ToString() => Value is { } value
        ? $"{Name} = {value} {Unit}".TrimEnd()
        : $"{Name} = ? ({Problem})";
}

/// <summary>
/// A live conversation with a gaming machine, held open while somebody looks at it.
/// </summary>
/// <remarks>
/// The same engine a script run uses, without the script. It exists so that whatever can be done
/// by hand in the interface is the same thing a test does, and so the two cannot drift: a meter
/// read here goes through the same layout, the same decoder and the same index a script does.
/// </remarks>
public sealed class HostSession : IDisposable
{
    private readonly PollEngine _engine;
    private readonly IDisposable? _owned;
    private readonly ClockBase _base;

    private HostSession(PollEngine engine, IDisposable? owned, SessionLog log)
    {
        _engine = engine;
        _owned = owned;
        Log = log;

        // The engine measures from its own start, so its zero is marked on the session clock here
        // and every record it produces is converted before it joins the stream.
        _base = log.Clock.Mark();

        _engine.Wire += (direction, channel, symbols, elapsed, note) =>
        {
            Log.Wire(direction, channel, symbols, elapsed, _base, note);
            Wire?.Invoke(direction, channel, symbols, elapsed, note);
        };

        _engine.ExceptionReported += report =>
        {
            Log.Exception(report, _base, SasNames.ExceptionName(report.Code));
            ExceptionReported?.Invoke(report);
        };

        _engine.LinkStateChanged += state =>
        {
            Log.Link(state, _engine.Channel, _base, _engine.Elapsed);
            LinkStateChanged?.Invoke(state);
        };
    }

    /// <summary>Everything this session did, in order, on one clock.</summary>
    public SessionLog Log { get; }

    /// <summary>Every message, as it happens. Handlers run on the poll thread.</summary>
    public event WireObserver? Wire;

    /// <summary>Every exception the machine reports.</summary>
    public event Action<ExceptionReport>? ExceptionReported;

    /// <summary>Raised when the link changes state.</summary>
    public event Action<LinkState>? LinkStateChanged;

    /// <summary>How the link is doing.</summary>
    public LinkState State => _engine.State;

    /// <summary>Counters for the session so far.</summary>
    public PollStatistics Statistics => _engine.Statistics;

    /// <summary>Recent messages, for a log view.</summary>
    public WireLog WireLog => _engine.WireLog;

    /// <summary>Something a person can read, such as the endpoint.</summary>
    public string Description => _engine.Transport.Description;

    /// <summary>How much this link is worth, and any caveat that goes with it.</summary>
    public TransportCapabilities Capabilities => _engine.Transport.Capabilities;

    /// <summary>The machine being polled.</summary>
    public SasAddress Address => _engine.Address;

    /// <summary>Which channel is being driven.</summary>
    public SasChannel Channel => _engine.Channel;

    /// <summary>
    /// Opens a link and starts polling.
    /// </summary>
    /// <param name="spec">What to connect to.</param>
    /// <param name="peer">
    /// Builds a peer for a loopback link and hands back both ends. Without one, a loopback link is
    /// refused: there would be nothing on the other end of it.
    /// </param>
    /// <exception cref="TransportRefusedException">The link cannot be opened or cannot carry SAS.</exception>
    /// <param name="log">
    /// The stream to record into. Supplying one lets several links share a session, which is how
    /// two channels end up interleaved in a single log rather than in two that must be aligned.
    /// </param>
    public static HostSession Open(
        HostSessionSpec spec,
        Func<HostSessionSpec, (ISasTransport Host, IDisposable Owned)>? peer = null,
        SessionLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(spec);

        ISasTransport transport;
        IDisposable? owned = null;

        switch (spec.Transport)
        {
            case ScriptTransport.Tcp:
                if (!TcpTransportOptions.TryParseEndpoint(spec.Target, out IPEndPoint endpoint))
                    throw new TransportRefusedException(
                        $"'{spec.Target}' is not an endpoint. Expected something like 127.0.0.1:7200.");
                transport = new TcpTransport(new TcpTransportOptions(endpoint));
                break;

            case ScriptTransport.Serial:
                if (string.IsNullOrWhiteSpace(spec.Target))
                    throw new TransportRefusedException("A serial link needs a port name.");
                transport = new SerialTransport(new SerialTransportOptions(spec.Target));
                break;

            case ScriptTransport.Loopback:
                if (peer is null)
                    throw new TransportRefusedException(
                        "A loopback link needs a peer on the other end, and none was supplied.");
                (transport, owned) = peer(spec);
                break;

            default:
                throw new TransportRefusedException($"'{spec.Transport}' is not a link this build opens.");
        }

        var engine = new PollEngine(transport, new PollEngineOptions
        {
            Address = spec.Address,
            Channel = spec.Channel,
            PollInterval = spec.Interval,
            ResponseTimeout = spec.Patience,
        });

        var session = new HostSession(engine, owned, log ?? new SessionLog());
        try
        {
            engine.Start();
        }
        catch
        {
            session.Dispose();
            throw;
        }

        return session;
    }

    /// <summary>Sends one long poll and waits for the reply.</summary>
    public LongPollResult Send(byte command, byte[]? data = null) =>
        _engine.Execute(command, data ?? []);

    /// <summary>
    /// Sends one long poll and decodes the reply, when this build has a layout for it.
    /// </summary>
    public (LongPollResult Result, SasDecoded? Decoded) SendAndDecode(byte command, byte[]? data = null)
    {
        LongPollResult result = Send(command, data);
        SasMessageLayout? layout = SasLayouts.For(command);
        if (layout is null || result.Outcome != LongPollOutcome.Answered)
            return (result, null);

        return (result, SasMessageDecoder.DecodeResponse(layout, result.Frame));
    }

    /// <summary>
    /// Reads every meter this build can read by name. A meter that cannot be read is reported with
    /// the reason rather than left out, so the list is the same length every time.
    /// </summary>
    public IReadOnlyList<MeterReading> ReadMeters(IEnumerable<string>? names = null)
    {
        var readings = new List<MeterReading>();

        foreach (MeterSource source in Sources(names))
        {
            SasMessageLayout? layout = SasLayouts.For(source.Command);
            if (layout is null)
            {
                readings.Add(new MeterReading(source.Name, source.Command, source.Unit, null,
                    "this build has no layout for the reply"));
                continue;
            }

            LongPollResult reply = Send(source.Command);
            if (!reply.Succeeded)
            {
                readings.Add(new MeterReading(source.Name, source.Command, source.Unit, null,
                    reply.Detail ?? reply.Outcome.ToString()));
                continue;
            }

            SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, reply.Frame);
            if (!decoded.Succeeded)
            {
                readings.Add(new MeterReading(source.Name, source.Command, source.Unit, null,
                    decoded.Failure));
                continue;
            }

            long? value = decoded.Number(source.Field);
            readings.Add(value is null
                ? new MeterReading(source.Name, source.Command, source.Unit, null,
                    $"the reply held no '{source.Field}'")
                : new MeterReading(source.Name, source.Command, source.Unit, value));
        }

        return readings;
    }

    public void Dispose()
    {
        _engine.Dispose();
        _owned?.Dispose();
    }

    private static IEnumerable<MeterSource> Sources(IEnumerable<string>? names)
    {
        if (names is null)
            return MeterIndex.All.OrderBy(m => m.Command).ThenBy(m => m.Name);

        var chosen = new List<MeterSource>();
        foreach (string name in names)
        {
            if (MeterIndex.TryResolve(name, out MeterSource source))
                chosen.Add(source);
        }

        return chosen;
    }
}
