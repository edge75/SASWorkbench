using System.Diagnostics;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Polling;

/// <summary>
/// Drives one SAS channel from a dedicated thread: synchronisation polls, general polls, and
/// long polls submitted from anywhere.
/// </summary>
/// <remarks>
/// <para>
/// The steady-state cycle allocates nothing. Buffers for the outgoing message, the incoming
/// reply and the wire history are all allocated once, because a poll loop that produces garbage
/// every 200 milliseconds eventually pauses for a collection, and a collection pause inside a
/// timing assertion is a test that fails for no reason anybody can reproduce.
/// </para>
/// <para>
/// A synchronisation poll goes out ahead of each general poll by default. See
/// <see cref="PollEngineOptions.SyncPollEvery"/> for why that is a requirement rather than a
/// courtesy.
/// </para>
/// </remarks>
public sealed class PollEngine : IDisposable
{
    private readonly SasLink _link;
    private readonly PollEngineOptions _options;
    private readonly Stopwatch _clock = new();
    private readonly SasSymbol[] _outbound = new SasSymbol[HostMessage.MaxSymbols];
    private readonly byte[] _inbound = new byte[HostMessage.MaxSymbols];
    private readonly byte[] _pendingData = new byte[255];
    private readonly SasSymbol[] _discardScratch = new SasSymbol[HostMessage.MaxSymbols];
    private readonly SemaphoreSlim _executeGate = new(1, 1);
    private readonly ManualResetEventSlim _pendingDone = new(false);
    private readonly Lock _stateGate = new();

    // Guards the handoff of one long poll from the caller to the poll thread and of its result
    // back again. Only taken when a long poll is submitted or dispatched, so the general poll
    // cycle never touches it.
    private readonly Lock _submitGate = new();

    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _pendingReady;
    private byte _pendingCommand;
    private int _pendingDataLength;
    private LongPollResult? _pendingResult;
    private long _pendingTicket;

    private long _syncPolls;
    private long _generalPolls;
    private long _longPolls;
    private long _exceptions;
    private long _silences;
    private long _badCrcs;
    private long _rejections;
    private long _wakeupsFromMachine;
    private long _symbolsDiscarded;
    private long _pollsSinceSync;
    private TimeSpan _lastHeard = TimeSpan.MinValue;
    private LinkState _state = LinkState.Down;

    public PollEngine(ISasTransport transport, PollEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);

        if (!transport.Capabilities.CarriesWakeupBit)
            throw new TransportRefusedException(
                $"{transport.Description} cannot carry the wakeup bit. "
                + "Refusing rather than sending eight-bit bytes a machine will never recognise "
                + $"as addresses.{FormatCaveat(transport.Capabilities.Caveat)}");

        _link = new SasLink(transport);
        _options = options;
        WireLog = new WireLog(options.WireLogCapacity);
    }

    /// <summary>Recent messages, both directions, for reports and the user interface.</summary>
    public WireLog WireLog { get; }

    /// <summary>The link this engine drives.</summary>
    public ISasTransport Transport => _link.Transport;

    /// <summary>Which channel this engine owns.</summary>
    public SasChannel Channel => _options.Channel;

    /// <summary>The machine being polled.</summary>
    public SasAddress Address => _options.Address;

    /// <summary>How often the machine is polled.</summary>
    public TimeSpan PollInterval => _options.PollInterval;

    /// <summary>True while the poll thread is running.</summary>
    public bool IsRunning => _running;

    /// <summary>Time since <see cref="Start"/>.</summary>
    public TimeSpan Elapsed => _clock.Elapsed;

    /// <summary>How the link is doing.</summary>
    public LinkState State
    {
        get
        {
            lock (_stateGate)
                return _state;
        }
    }

    /// <summary>Counters for the run so far.</summary>
    public PollStatistics Statistics => new(
        Interlocked.Read(ref _syncPolls),
        Interlocked.Read(ref _generalPolls),
        Interlocked.Read(ref _longPolls),
        Interlocked.Read(ref _exceptions),
        Interlocked.Read(ref _silences),
        Interlocked.Read(ref _badCrcs),
        Interlocked.Read(ref _rejections),
        Interlocked.Read(ref _wakeupsFromMachine),
        Interlocked.Read(ref _symbolsDiscarded));

    /// <summary>Every message, as it happens. Handlers run on the poll thread; keep them quick.</summary>
    public event WireObserver? Wire;

    /// <summary>Every non-zero exception a general poll returned.</summary>
    public event Action<ExceptionReport>? ExceptionReported;

    /// <summary>Raised when the link changes state.</summary>
    public event Action<LinkState>? LinkStateChanged;

    /// <summary>Starts polling.</summary>
    public void Start()
    {
        if (_running)
            return;

        Transport.Open();
        _clock.Restart();
        SetState(LinkState.Synchronising);
        _running = true;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = $"sas-poll-ch{(int)_options.Channel}",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    /// <summary>Stops polling and waits for the thread to finish.</summary>
    public void Stop()
    {
        if (!_running)
            return;

        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        SetState(LinkState.Down);
    }

    /// <summary>
    /// Sends one long poll in place of the next general poll, and waits for the reply.
    /// </summary>
    /// <remarks>
    /// SAS is strictly one transaction at a time, so callers are serialised here rather than
    /// pipelined. Waiting is the honest model of the protocol.
    /// </remarks>
    public LongPollResult Execute(byte command, ReadOnlySpan<byte> data = default, TimeSpan? timeout = null)
    {
        if (data.Length > _pendingData.Length)
            throw new ArgumentOutOfRangeException(nameof(data), data.Length,
                $"A long poll carries at most {_pendingData.Length} data bytes.");

        TimeSpan wait = timeout ?? _options.PollInterval + _options.ResponseTimeout + TimeSpan.FromSeconds(1);

        if (!_running)
            return new LongPollResult(command, _options.Channel, LongPollOutcome.NotSent,
                [], TimeSpan.Zero, "The poll engine is not running.");

        _executeGate.Wait();
        try
        {
            lock (_submitGate)
            {
                data.CopyTo(_pendingData);
                _pendingDataLength = data.Length;
                _pendingCommand = command;
                _pendingResult = null;
                _pendingTicket++;
                _pendingDone.Reset();
                _pendingReady = true;
            }

            if (!_pendingDone.Wait(wait))
            {
                // The transaction is abandoned, not cancelled: the poll thread may still be in the
                // middle of it. Retiring the ticket is what stops its answer being handed to
                // whoever asks next, which would report one command's reply under another's name.
                lock (_submitGate)
                {
                    _pendingTicket++;
                    _pendingReady = false;
                }

                return new LongPollResult(command, _options.Channel, LongPollOutcome.NotSent,
                    [], wait, "The poll engine did not reach this command in time.");
            }

            lock (_submitGate)
            {
                return _pendingResult ?? new LongPollResult(command, _options.Channel,
                    LongPollOutcome.NotSent, [], TimeSpan.Zero, "No result was recorded.");
            }
        }
        finally
        {
            _executeGate.Release();
        }
    }

    public void Dispose()
    {
        Stop();
        _pendingDone.Dispose();
        _executeGate.Dispose();
        Transport.Dispose();
    }

    private void Loop()
    {
        long nextPollAt = _clock.ElapsedMilliseconds;

        while (_running)
        {
            if (!Transport.IsConnected)
            {
                if (!_options.WaitForPeer)
                {
                    SetState(LinkState.Down);
                    return;
                }

                SetState(LinkState.Down);
                Thread.Sleep(20);
                nextPollAt = _clock.ElapsedMilliseconds;
                continue;
            }

            long now = _clock.ElapsedMilliseconds;
            long sleepFor = nextPollAt - now;
            if (sleepFor > 0)
            {
                Thread.Sleep((int)Math.Min(sleepFor, 50));
                continue;
            }

            nextPollAt = now + (long)_options.PollInterval.TotalMilliseconds;

            if (_pendingReady)
                RunPendingLongPoll();
            else
                RunPollCycle();

            CheckLinkTimeout();
        }
    }

    private void RunPollCycle()
    {
        Resynchronise();

        if (_options.SyncPollEvery > 0 && _pollsSinceSync % _options.SyncPollEvery == 0)
        {
            int length = HostMessage.WriteSyncPoll(_outbound);
            SendAndRecord(length, "sync poll - address zero");
            Interlocked.Increment(ref _syncPolls);

            // Let the machine see the synchronisation poll as a message of its own. Without a
            // gap the two polls arrive as one burst and the second is read as trailing bytes.
            Thread.Sleep((int)_options.QuietGap.TotalMilliseconds);
        }

        _pollsSinceSync++;

        int pollLength = HostMessage.WriteGeneralPoll(_outbound, _options.Address);
        SendAndRecord(pollLength, "general poll");
        Interlocked.Increment(ref _generalPolls);

        int received = _link.Receive(_inbound, _options.ResponseTimeout, _options.QuietGap, out bool wakeup);
        if (received == 0)
        {
            Interlocked.Increment(ref _silences);
            return;
        }

        RecordReceive(received, wakeup, null);
        MarkHeard();

        byte code = _inbound[0];
        if (code == 0x00)
            return;

        if (code == (byte)(_options.Address.Value | HostMessage.GeneralPollFlag))
        {
            Interlocked.Increment(ref _rejections);
            return;
        }

        long sequence = Interlocked.Increment(ref _exceptions);
        ExceptionReported?.Invoke(new ExceptionReport(code, _options.Channel, _clock.Elapsed, sequence));
    }

    private void RunPendingLongPoll()
    {
        byte command;
        int dataLength;
        long ticket;
        lock (_submitGate)
        {
            command = _pendingCommand;
            dataLength = _pendingDataLength;
            ticket = _pendingTicket;
            _pendingReady = false;
        }

        Resynchronise();

        // A machine that has not been resynchronised ignores an addressed poll, so keep sending
        // the synchronisation poll ahead of long polls too.
        if (_options.SyncPollEvery > 0)
        {
            int syncLength = HostMessage.WriteSyncPoll(_outbound);
            SendAndRecord(syncLength, "sync poll - address zero");
            Interlocked.Increment(ref _syncPolls);
            Thread.Sleep((int)_options.QuietGap.TotalMilliseconds);
        }

        int length = HostMessage.WriteLongPoll(
            _outbound, _options.Address, command, _pendingData.AsSpan(0, dataLength));

        long sentAt = _clock.ElapsedTicks;
        SendAndRecord(length, $"long poll ${command:X2}");
        Interlocked.Increment(ref _longPolls);

        int received = _link.Receive(_inbound, _options.ResponseTimeout, _options.QuietGap, out bool wakeup);
        var latency = TimeSpan.FromTicks(_clock.ElapsedTicks - sentAt);

        LongPollResult result = Interpret(command, received, wakeup, latency);
        if (received > 0)
        {
            RecordReceive(received, wakeup, result.Outcome.ToString());
            MarkHeard();
        }
        else
        {
            Interlocked.Increment(ref _silences);
        }

        lock (_submitGate)
        {
            // Whoever asked for this has already given up and may have asked for something else,
            // so the answer belongs to nobody. It stays on the wire log either way.
            if (_pendingTicket != ticket)
                return;

            _pendingResult = result;
        }

        _pendingDone.Set();
    }

    private LongPollResult Interpret(byte command, int received, bool wakeup, TimeSpan latency)
    {
        if (received == 0)
            return new LongPollResult(command, _options.Channel, LongPollOutcome.Silent,
                [], latency, "The machine did not answer.");

        byte[] frame = _inbound.AsSpan(0, received).ToArray();

        if (wakeup)
        {
            Interlocked.Increment(ref _wakeupsFromMachine);
            return new LongPollResult(command, _options.Channel, LongPollOutcome.BadCheckSequence,
                frame, latency,
                "A received symbol carried the wakeup bit. Only a host may set it, so this is "
                + "either a misbehaving machine or the host hearing its own transmission.");
        }

        if (received == 1)
        {
            if (frame[0] == _options.Address.Value)
                return new LongPollResult(command, _options.Channel, LongPollOutcome.Acknowledged,
                    frame, latency);

            if (frame[0] == (byte)(_options.Address.Value | HostMessage.GeneralPollFlag))
            {
                Interlocked.Increment(ref _rejections);
                return new LongPollResult(command, _options.Channel, LongPollOutcome.Rejected,
                    frame, latency, $"The machine rejected long poll ${command:X2}.");
            }

            return new LongPollResult(command, _options.Channel, LongPollOutcome.WrongAddress,
                frame, latency, $"A one-byte reply of ${frame[0]:X2} is neither an acknowledgement "
                                + $"nor a rejection from address {_options.Address}.");
        }

        if (frame[0] != _options.Address.Value)
            return new LongPollResult(command, _options.Channel, LongPollOutcome.WrongAddress,
                frame, latency,
                $"The reply is addressed to ${frame[0]:X2}, not to {_options.Address}.");

        if (received > 2 && !SasCrc.Verify(frame))
        {
            Interlocked.Increment(ref _badCrcs);
            return new LongPollResult(command, _options.Channel, LongPollOutcome.BadCheckSequence,
                frame, latency, "The frame check sequence did not match.");
        }

        return new LongPollResult(command, _options.Channel, LongPollOutcome.Answered, frame, latency);
    }

    private void SendAndRecord(int length, string note)
    {
        ReadOnlySpan<SasSymbol> message = _outbound.AsSpan(0, length);
        _link.Send(message);
        WireLog.Record(WireDirection.Transmit, _options.Channel, message, _clock.Elapsed, note);
        Wire?.Invoke(WireDirection.Transmit, _options.Channel, message, _clock.Elapsed, note);
    }

    private void RecordReceive(int received, bool wakeup, string? note)
    {
        // Reuse the outbound scratch to render received bytes as symbols, so recording the wire
        // costs no allocation. The send has already happened by this point.
        int length = Math.Min(received, _outbound.Length);
        for (int i = 0; i < length; i++)
            _outbound[i] = wakeup ? SasSymbol.Address(_inbound[i]) : SasSymbol.Data8(_inbound[i]);

        ReadOnlySpan<SasSymbol> message = _outbound.AsSpan(0, length);
        WireLog.Record(WireDirection.Receive, _options.Channel, message, _clock.Elapsed, note);
        Wire?.Invoke(WireDirection.Receive, _options.Channel, message, _clock.Elapsed, note);
    }

    /// <summary>
    /// Drops anything already on the line before a poll goes out. SAS ends a message with silence
    /// and nothing else, so a reply that arrives after the host gave up waiting cannot be told
    /// apart from the next one by looking at it; without this the engine reads every later reply
    /// one transaction behind for the rest of the session. What is dropped is counted and
    /// recorded, never swallowed quietly.
    /// </summary>
    private void Resynchronise()
    {
        int discarded = _link.Drain(_discardScratch, TimeSpan.Zero);
        if (discarded == 0)
            return;

        Interlocked.Add(ref _symbolsDiscarded, discarded);

        ReadOnlySpan<SasSymbol> kept = _discardScratch.AsSpan(0, Math.Min(discarded, _discardScratch.Length));
        string note = $"discarded {discarded} symbol{(discarded == 1 ? string.Empty : "s")} "
                      + "left on the line by an answer that came too late";
        WireLog.Record(WireDirection.Receive, _options.Channel, kept, _clock.Elapsed, note);
        Wire?.Invoke(WireDirection.Receive, _options.Channel, kept, _clock.Elapsed, note);
    }

    private void MarkHeard()
    {
        _lastHeard = _clock.Elapsed;
        SetState(LinkState.Up);
    }

    private void CheckLinkTimeout()
    {
        if (_lastHeard == TimeSpan.MinValue)
            return;

        if (_clock.Elapsed - _lastHeard > _options.LinkDownAfter)
            SetState(LinkState.Down);
    }

    private void SetState(LinkState state)
    {
        bool changed;
        lock (_stateGate)
        {
            changed = _state != state;
            _state = state;
        }

        if (changed)
            LinkStateChanged?.Invoke(state);
    }

    private static string FormatCaveat(string? caveat) => caveat is null ? string.Empty : $" {caveat}";
}
