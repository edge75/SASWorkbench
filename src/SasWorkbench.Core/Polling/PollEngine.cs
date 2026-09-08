using System.Diagnostics;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Protocol;
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
    /// <summary>Stands in for a command where a general poll is being answered. No command is negative.</summary>
    private const int GeneralPoll = -1;

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
    private string? _pendingPurpose;
    private bool _pendingCheckSequence;
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
    /// <param name="purpose">
    /// Why this poll is being sent, when the host is asking on its own behalf rather than because
    /// a step asked. It goes onto the record with the message, so a log can say which packets no
    /// step accounts for instead of leaving a reader to work it out from where they sit.
    /// </param>
    /// <param name="checkSequence">
    /// Whether to end the message in a check sequence, overriding what the command requires.
    /// Left null the command decides, which is what every ordinary caller wants. It is set only
    /// to measure the requirement itself — sending a command the wrong way on purpose, so that
    /// what a machine does about it is observed rather than reasoned about.
    /// </param>
    public LongPollResult Execute(
        byte command,
        ReadOnlySpan<byte> data = default,
        TimeSpan? timeout = null,
        string? purpose = null,
        bool? checkSequence = null)
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
                _pendingPurpose = purpose;
                _pendingCheckSequence = checkSequence
                    ?? SasCommandFraming.RequiresCheckSequence(command);
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
        Resynchronise();
        SendAndRecord(pollLength, "general poll");
        Interlocked.Increment(ref _generalPolls);

        int received = ReceiveAnswer(GeneralPoll, out bool wakeup, out _);
        if (received == 0)
        {
            Interlocked.Increment(ref _silences);
            return;
        }

        // A reply of more than one byte to a general poll is the real-time event format, which this
        // build does not decode. Naming it is the honest answer; reading its address byte as an
        // exception code would invent a machine event out of a message about another one.
        bool realTimeEvent = received > 1;
        RecordReceive(received, wakeup, realTimeEvent
            ? "known but not decoded: a general poll answered in the real-time event format"
            : null);
        MarkHeard();

        if (realTimeEvent)
            return;

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
        string? purpose;
        bool checkSequence;
        lock (_submitGate)
        {
            command = _pendingCommand;
            dataLength = _pendingDataLength;
            ticket = _pendingTicket;
            purpose = _pendingPurpose;
            checkSequence = _pendingCheckSequence;
            _pendingReady = false;
        }

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
            _outbound, _options.Address, command, _pendingData.AsSpan(0, dataLength), checkSequence);

        long sentAt = _clock.ElapsedTicks;
        Resynchronise();
        SendAndRecord(length, WirePurpose.Label($"long poll ${command:X2}", purpose)!);
        Interlocked.Increment(ref _longPolls);

        int received = ReceiveAnswer(command, out bool wakeup, out int discardedMessages);
        var latency = TimeSpan.FromTicks(_clock.ElapsedTicks - sentAt);

        LongPollResult result = Interpret(command, received, wakeup, latency, discardedMessages);
        if (received > 0)
        {
            RecordReceive(received, wakeup, WirePurpose.Label(result.Outcome.ToString(), purpose));
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

    private LongPollResult Interpret(
        byte command, int received, bool wakeup, TimeSpan latency, int discardedMessages)
    {
        if (received == 0)
            return new LongPollResult(command, _options.Channel, LongPollOutcome.Silent,
                [], latency, discardedMessages == 0
                    ? "The machine did not answer."
                    : $"The machine did not answer. {discardedMessages} message(s) that cannot be "
                      + "an answer to this poll arrived while waiting and were discarded.");

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
    /// Reads until a message arrives that could be the answer to the poll just sent, or the
    /// machine's time to answer runs out.
    /// </summary>
    /// <param name="command">
    /// The long poll being answered, or <see cref="GeneralPoll"/> for a general poll.
    /// </param>
    /// <remarks>
    /// <para>
    /// Draining the line before a poll goes out cannot catch an answer that arrives after the
    /// drain, and the drain is the only thing between the two: on a slow machine the overdue answer
    /// to the previous transaction lands inside this one's window and is read as its reply. Silence
    /// is SAS's only frame delimiter, so it cannot be told apart by when it arrived — but it can be
    /// told apart by what it says, and a message that cannot belong to this transaction is not
    /// this transaction's answer whatever else is true of it.
    /// </para>
    /// <para>
    /// What is discarded is counted and recorded, never swallowed. The machine's time to answer is
    /// not extended by a foreign message: the deadline is from the moment the poll was sent, so a
    /// line carrying noise cannot make the host wait longer than a quiet one.
    /// </para>
    /// </remarks>
    private int ReceiveAnswer(int command, out bool wakeup, out int discardedMessages)
    {
        TimeSpan deadline = _clock.Elapsed + _options.ResponseTimeout;
        discardedMessages = 0;
        while (true)
        {
            TimeSpan remaining = deadline - _clock.Elapsed;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;

            int received = _link.Receive(_inbound, remaining, _options.QuietGap, out wakeup);
            if (received == 0)
                return 0;

            // A received wakeup bit is always a fault, and the caller reports it as one. Discarding
            // it here would hide the very thing worth knowing about that message.
            if (wakeup || CouldAnswer(command, _inbound.AsSpan(0, received)))
                return received;

            Discard(received, command);
            discardedMessages++;
            if (_clock.Elapsed >= deadline)
                return 0;
        }
    }

    /// <summary>
    /// Whether a received message can be the answer to <paramref name="command"/> at all.
    /// </summary>
    /// <remarks>
    /// Read off the gaming machine library's own replies rather than reasoned about. A long poll is
    /// answered by an acknowledgement (the address alone), a rejection (the address with bit 7
    /// set), a busy response (the address and a zero, the one reply that echoes no command), or an
    /// ordinary reply whose second byte echoes the command. A general poll is answered by one byte,
    /// or by the real-time event format, which carries the address and a fixed identifier instead
    /// of a command. One ambiguity is left rather than guessed at: an exception code equal to the
    /// machine's own address is indistinguishable from an acknowledgement, and is read as one.
    /// </remarks>
    private bool CouldAnswer(int command, ReadOnlySpan<byte> frame)
    {
        byte address = _options.Address.Value;

        if (command == GeneralPoll)
            return frame.Length == 1
                || (frame[0] == address && frame[1] == HostMessage.RealTimeEventIdentifier);

        if (frame.Length == 1)
            return frame[0] == address || frame[0] == (byte)(address | HostMessage.GeneralPollFlag);

        if (frame[0] != address)
            return false;

        return frame[1] == (byte)command
               || (frame.Length == 2 && frame[1] == HostMessage.BusyResponseByte);
    }

    /// <summary>Counts and records a message that cannot be the answer to the poll it arrived under.</summary>
    private void Discard(int received, int command)
    {
        Interlocked.Add(ref _symbolsDiscarded, received);

        string asked = command == GeneralPoll ? "a general poll" : $"long poll ${command:X2}";
        RecordReceive(received, wakeup: false,
            $"discarded {received} symbol{(received == 1 ? string.Empty : "s")} - this cannot be "
            + $"an answer to {asked}, so it answers some earlier one");
    }

    /// <summary>
    /// Drops anything already on the line in the instant before a poll goes out. SAS ends a message
    /// with silence
    /// and nothing else, so a reply that arrives after the host gave up waiting cannot be told
    /// apart from the next one by looking at it; without this the engine reads every later reply
    /// one transaction behind for the rest of the session. What is dropped is counted and
    /// recorded, never swallowed quietly.
    /// </summary>
    /// <remarks>
    /// Called immediately before the transmission whose answer is about to be read, and not at the
    /// top of the cycle: a synchronisation poll and the quiet gap after it sit between the two, and
    /// a late answer landing in that gap is exactly the one that would otherwise be read as the
    /// reply to the poll that follows it.
    /// </remarks>
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
