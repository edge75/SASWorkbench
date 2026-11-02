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

    // Taken by the loop to start a timed cycle and by BeginSequence to stop the next one, so a
    // cycle is either already running when the sequence begins or never starts.
    private readonly Lock _cycleGate = new();
    private readonly ManualResetEventSlim _cycleIdle = new(true);

    // Guards the handoff of one long poll from the caller to the poll thread and of its result
    // back again. Only taken when a long poll is submitted or dispatched, so the general poll
    // cycle never touches it.
    private readonly Lock _submitGate = new();

    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _pendingReady;
    private volatile bool _sequenced;
    private volatile string? _linkFault;
    private PendingKind _pendingKind = PendingKind.LongPoll;
    private GeneralPollResult? _pendingPollResult;
    private byte _pendingCommand;
    private SasAddress _pendingAddress;
    private string? _pendingPurpose;
    private bool _pendingCheckSequence;
    private int _pendingDataLength;
    private LongPollResult? _pendingResult;
    private long _pendingTicket;

    // The machine the transaction in flight is for. On a host bound to one machine it never
    // changes; on a round it is the address the cycle has reached, and every address check in the
    // transaction — the poll, the reply, the rejection, the event — is against this and not
    // against whichever machine the options happen to name first.
    private SasAddress _polling;

    // Which machine the record being written belongs to, which is not always the one being polled.
    // A synchronisation poll is addressed to nobody, and symbols drained off the line before a poll
    // answered some earlier transaction — attributing either to the machine about to be polled
    // would put another machine's traffic under its name, which is the one thing a floor log must
    // not do. Null means not known, and a reader must read it that way.
    private SasAddress? _attributed;

    private int _next;
    private long _rounds;

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
        _polling = options.Round[0];
        WireLog = new WireLog(options.WireLogCapacity);
    }

    /// <summary>Recent messages, both directions, for reports and the user interface.</summary>
    public WireLog WireLog { get; }

    /// <summary>The link this engine drives.</summary>
    public ISasTransport Transport => _link.Transport;

    /// <summary>Which channel this engine owns.</summary>
    public SasChannel Channel => _options.Channel;

    /// <summary>The machine being polled, or the one the round has reached.</summary>
    public SasAddress Address => _polling;

    /// <summary>Every machine this host polls, in the order the round visits them.</summary>
    public IReadOnlyList<SasAddress> Round => _options.Round;

    /// <summary>How many complete rounds have been walked.</summary>
    public long RoundsCompleted => Interlocked.Read(ref _rounds);

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

    /// <summary>
    /// Why polling stopped, because the link stopped taking messages or something else failed on
    /// the poll thread; null while it has not.
    /// </summary>
    public string? LinkFault => _linkFault;

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
    /// <param name="to">
    /// Which machine to send it to. Left null on a host bound to one machine it is that machine;
    /// on a host that polls a round it is <b>refused</b>, because a command that went to whichever
    /// machine the round had reached would be answered correctly by the wrong machine.
    /// </param>
    /// <summary>True while the host sends only what a caller names, and nothing on a timer.</summary>
    public bool IsSequenced => _sequenced;

    /// <summary>
    /// Stops the round and the synchronisation poll, so the line carries exactly the messages a
    /// caller submits, in the order they are submitted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A machine learns that the host received its last reply from the next poll it sees, so what
    /// is on the line between two messages is not incidental — it is the protocol. A host that
    /// runs a round on a timer and prefixes a broadcast to every message cannot present a machine
    /// with the sequence an implied-acknowledgement case describes, because the broadcast is
    /// itself an acknowledgement (section 3.19).
    /// </para>
    /// <para>
    /// The link timeout still runs. A sequence that sends nothing the machine answers for longer
    /// than the timeout takes the link down, and that is left alone deliberately: a host that kept
    /// the link marked up while hearing nothing would be reporting something it had not measured.
    /// </para>
    /// </remarks>
    /// <returns>
    /// False when the cycle already on the line had not finished when the wait for it ran out.
    /// The sequence has begun anyway, but that cycle's general poll can still draw what the caller
    /// sends for, so the caller has to report the sequence as broken.
    /// </returns>
    public bool BeginSequence()
    {
        lock (_cycleGate)
            _sequenced = true;

        // The cycle already on the line finishes first, so the next message is the caller's.
        if (Thread.CurrentThread == _thread)
            return true;

        return _cycleIdle.Wait(_options.PollInterval + _options.ResponseTimeout + TimeSpan.FromSeconds(1));
    }

    /// <summary>Gives the line back to the timer, and resumes the round and the synchronisation poll.</summary>
    public void EndSequence() => _sequenced = false;

    /// <summary>
    /// Sends one general poll and says what it drew.
    /// </summary>
    /// <param name="to">
    /// Which machine to poll. Left null it is the machine this host is bound to, and on a host
    /// polling a round it is refused for the same reason a long poll without a name is.
    /// </param>
    /// <param name="timeout">How long to wait for the engine to reach it.</param>
    /// <param name="purpose">Why it is being sent, for the wire record.</param>
    public GeneralPollResult PollGenerally(
        SasAddress? to = null, TimeSpan? timeout = null, string? purpose = null)
    {
        if (!TryTarget(to, out SasAddress target, out string? refusal))
        {
            return new GeneralPollResult(to ?? _polling, _options.Channel,
                GeneralPollOutcome.NotSent, 0x00, [], TimeSpan.Zero, refusal);
        }

        return Submit(PendingKind.GeneralPoll, target, purpose, timeout);
    }

    /// <summary>
    /// Sends the broadcast to address 00 — the synchronisation poll — as a message of its own.
    /// </summary>
    /// <remarks>
    /// Available only in the sequenced mode. Outside it the engine sends this on its own schedule,
    /// and a second one placed by hand would be a message the case did not account for.
    /// </remarks>
    public GeneralPollResult Synchronise(TimeSpan? timeout = null, string? purpose = null)
    {
        if (!_sequenced)
        {
            return new GeneralPollResult(SasAddress.Sync, _options.Channel,
                GeneralPollOutcome.NotSent, 0x00, [], TimeSpan.Zero,
                "The host is polling on a timer and sends the synchronisation poll itself. Put it "
                + "into a sequence before placing one by hand.");
        }

        return Submit(PendingKind.SyncPoll, SasAddress.Sync, purpose, timeout);
    }

    /// <summary>Puts one general or synchronisation poll on the engine's thread and waits for it.</summary>
    private GeneralPollResult Submit(
        PendingKind kind, SasAddress target, string? purpose, TimeSpan? timeout)
    {
        TimeSpan wait = timeout
                        ?? _options.PollInterval + _options.ResponseTimeout + TimeSpan.FromSeconds(1);

        if (!_running)
        {
            return new GeneralPollResult(target, _options.Channel, GeneralPollOutcome.NotSent,
                0x00, [], TimeSpan.Zero, "The poll engine is not running.");
        }

        _executeGate.Wait();
        try
        {
            lock (_submitGate)
            {
                if (_linkFault is { } fault)
                {
                    return new GeneralPollResult(target, _options.Channel, GeneralPollOutcome.NotSent,
                        0x00, [], TimeSpan.Zero, fault);
                }

                _pendingKind = kind;
                _pendingAddress = target;
                _pendingPurpose = purpose;
                _pendingPollResult = null;
                _pendingTicket++;
                _pendingDone.Reset();
                _pendingReady = true;
            }

            if (!_pendingDone.Wait(wait))
            {
                lock (_submitGate)
                {
                    _pendingTicket++;
                    _pendingReady = false;
                }

                return new GeneralPollResult(target, _options.Channel, GeneralPollOutcome.NotSent,
                    0x00, [], wait, "The poll engine did not reach this poll in time.");
            }

            lock (_submitGate)
            {
                return _pendingPollResult ?? new GeneralPollResult(target, _options.Channel,
                    GeneralPollOutcome.NotSent, 0x00, [], TimeSpan.Zero, "No result was recorded.");
            }
        }
        finally
        {
            _executeGate.Release();
        }
    }

    public LongPollResult Execute(
        byte command,
        ReadOnlySpan<byte> data = default,
        TimeSpan? timeout = null,
        string? purpose = null,
        bool? checkSequence = null,
        SasAddress? to = null)
    {
        if (data.Length > _pendingData.Length)
            throw new ArgumentOutOfRangeException(nameof(data), data.Length,
                $"A long poll carries at most {_pendingData.Length} data bytes.");

        TimeSpan wait = timeout ?? _options.PollInterval + _options.ResponseTimeout + TimeSpan.FromSeconds(1);

        if (!_running)
            return new LongPollResult(command, _options.Channel, LongPollOutcome.NotSent,
                [], TimeSpan.Zero, "The poll engine is not running.");

        if (!TryTarget(to, out SasAddress target, out string? refusal))
        {
            return new LongPollResult(command, _options.Channel, LongPollOutcome.NotSent,
                [], TimeSpan.Zero, refusal);
        }

        _executeGate.Wait();
        try
        {
            lock (_submitGate)
            {
                if (_linkFault is { } fault)
                {
                    return new LongPollResult(command, _options.Channel, LongPollOutcome.NotSent,
                        [], TimeSpan.Zero, fault);
                }

                _pendingKind = PendingKind.LongPoll;
                _pendingAddress = target;
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

    /// <summary>
    /// Settles which machine a long poll is for, or says why it cannot be settled.
    /// </summary>
    /// <remarks>
    /// Refused rather than guessed. On a round, "the machine being polled" is whichever one the
    /// cycle happened to reach, so a command sent without a name would land on a different machine
    /// each time and be answered properly by all of them — a test that passes while addressing the
    /// wrong machine is worse than one that does not run.
    /// </remarks>
    private bool TryTarget(SasAddress? asked, out SasAddress target, out string? refusal)
    {
        IReadOnlyList<SasAddress> round = _options.Round;
        refusal = null;

        if (asked is { } named)
        {
            target = named;
            if (round.Contains(named))
                return true;

            // A sequence may poll a stranger on purpose, and several cases turn on it: a poll to
            // another machine acknowledges this one without being answered, so the address has to
            // be one no machine here holds. The refusal below guards against a typo becoming a
            // poll nobody reads, which a case that named the address deliberately is not doing.
            if (_sequenced)
                return true;

            refusal = $"this host does not poll ${named}; it polls "
                      + string.Join(", ", round.Select(a => $"${a}")) + ".";
            return false;
        }

        target = round[0];
        if (round.Count == 1)
            return true;

        refusal = $"this host polls {round.Count} machines, so a long poll has to name one of "
                  + "them rather than be sent to whichever the round had reached.";
        return false;
    }

    public void Dispose()
    {
        // _pendingDone and _cycleIdle are left undisposed: the poll thread can outlive Stop's wait
        // and still set them, and neither holds a kernel handle.
        Stop();
        _executeGate.Dispose();
        Transport.Dispose();
    }

    private void Loop()
    {
        try
        {
            Poll();
        }
        catch (LinkFailedException failed)
        {
            Fail(failed.Message);
        }
        catch (Exception unexpected)
        {
            // Nothing thrown on the poll thread may end the process: it ends the polling, with the reason.
            Trace.TraceError(unexpected.ToString());
            Fail($"polling stopped after an unexpected {unexpected.GetType().Name}{Where(unexpected)}: "
                 + unexpected.Message);
        }
    }

    /// <summary>The first method of ours the exception passed through, as " in Type.Method", or nothing.</summary>
    private static string Where(Exception exception)
    {
        try
        {
            foreach (StackFrame? frame in new StackTrace(exception).GetFrames())
            {
                if (frame?.GetMethod() is not { DeclaringType: { } type } method
                    || type.Namespace?.StartsWith("SasWorkbench", StringComparison.Ordinal) != true)
                {
                    continue;
                }

                // A lambda compiles into a nested type with a generated name, so name the type it was written in.
                while (type.DeclaringType is { } outer && type.Name.Contains('<'))
                    type = outer;

                return method.Name.Contains('<') ? $" in {type.Name}" : $" in {type.Name}.{method.Name}";
            }
        }
        catch (Exception)
        {
            // Only the location is lost: the reason still carries the exception's type and message.
        }

        return string.Empty;
    }

    private void Poll()
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

            // In the sequenced mode nothing goes out on a timer: the line carries what a caller
            // named and nothing else, which is the only way to present a machine with an exact
            // sequence of polls. So a submitted message runs at once rather than at the next tick.
            if (_sequenced)
            {
                if (!_pendingReady)
                {
                    Thread.Sleep(1);
                    continue;
                }

                RunPending();
                nextPollAt = _clock.ElapsedMilliseconds
                             + (long)_options.PollInterval.TotalMilliseconds;
                CheckLinkTimeout();
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

            lock (_cycleGate)
            {
                if (_sequenced)
                    continue;

                _cycleIdle.Reset();
            }

            try
            {
                if (_pendingReady)
                    RunPending();
                else
                    RunPollCycle();

                CheckLinkTimeout();
            }
            finally
            {
                _cycleIdle.Set();
            }
        }
    }

    private void RunPollCycle()
    {
        _attributed = null;

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

        // The round advances before the poll goes out, so the address the poll carries and every
        // check against the answer are one value read once. A round of one never moves.
        IReadOnlyList<SasAddress> round = _options.Round;
        _polling = round[_next];
        _next++;
        if (_next >= round.Count)
        {
            _next = 0;
            Interlocked.Increment(ref _rounds);
        }

        SendGeneralPollAndRead(null);
    }

    /// <summary>
    /// Sends a general poll to the address currently being polled and reads what it drew.
    /// </summary>
    /// <remarks>
    /// Shared by the timer-driven round, which discards the result and lets the exception stream
    /// carry it, and by the sequenced mode, where a case names the poll and has to be told what
    /// that one poll drew rather than watching a stream.
    /// </remarks>
    private GeneralPollResult SendGeneralPollAndRead(string? purpose)
    {
        int pollLength = HostMessage.WriteGeneralPoll(_outbound, _polling);
        long sentAt = _clock.ElapsedTicks;
        Resynchronise();
        _attributed = _polling;
        SendAndRecord(pollLength, WirePurpose.Label("general poll", purpose)!);
        Interlocked.Increment(ref _generalPolls);

        int received = ReceiveAnswer(GeneralPoll, out bool wakeup, out _);
        var latency = TimeSpan.FromTicks(_clock.ElapsedTicks - sentAt);

        if (received == 0)
        {
            Interlocked.Increment(ref _silences);
            return new GeneralPollResult(_polling, _options.Channel, GeneralPollOutcome.Silent,
                0x00, [], latency, "The machine did not answer the general poll.");
        }

        byte[] frame = _inbound.AsSpan(0, received).ToArray();

        // A reply of more than one byte to a general poll is the real-time event format: the
        // machine's address, the event marker, the exception code and whatever that exception
        // carries. Read as a whole message or not at all — taking the first byte for an exception
        // code would invent a machine event out of a message about another one.
        if (received > 1)
        {
            byte? reported = ReportRealTimeEvent(received, wakeup);
            if (reported is not { } code_)
            {
                return new GeneralPollResult(_polling, _options.Channel,
                    GeneralPollOutcome.Unreadable, 0x00, frame, latency,
                    "The message could not be read as a real-time event.")
                {
                    IsRealTimeEvent = true,
                };
            }

            return new GeneralPollResult(_polling, _options.Channel,
                code_ == 0x00 ? GeneralPollOutcome.NoActivity : GeneralPollOutcome.Exception,
                code_, frame, latency)
            {
                IsRealTimeEvent = true,
                Data = frame[SasEventLayouts.HeaderLength..^2],
            };
        }

        RecordReceive(received, wakeup, null);
        MarkHeard();

        byte code = _inbound[0];
        if (code == 0x00)
            return new GeneralPollResult(_polling, _options.Channel,
                GeneralPollOutcome.NoActivity, 0x00, frame, latency);

        if (code == (byte)(_polling.Value | HostMessage.GeneralPollFlag))
        {
            Interlocked.Increment(ref _rejections);
            return new GeneralPollResult(_polling, _options.Channel, GeneralPollOutcome.Rejected,
                code, frame, latency, $"The machine refused a general poll to {_polling}.");
        }

        long sequence = Interlocked.Increment(ref _exceptions);
        ExceptionReported?.Invoke(
            new ExceptionReport(code, _options.Channel, _clock.Elapsed, sequence)
            {
                Address = _polling,
            });

        return new GeneralPollResult(_polling, _options.Channel, GeneralPollOutcome.Exception,
            code, frame, latency);
    }

    /// <summary>
    /// Reads a general poll answered in the real-time event format, and reports the exception it
    /// carries along with the bytes that came after the code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The payload travels with the report and is not decoded here. What the bytes mean is a
    /// question for the layout of that exception, and the engine's job is to say which exception
    /// arrived and with what — so an exception whose payload nothing has established still reaches
    /// a script as the exception it is, with its bytes intact for whoever can read them later.
    /// </para>
    /// <para>
    /// A message that fails its check sequence, carries the wrong marker or is addressed elsewhere
    /// is recorded and dropped rather than read. There is no assurance about any byte of a frame
    /// that fails the check, the exception code included.
    /// </para>
    /// </remarks>
    private byte? ReportRealTimeEvent(int received, bool wakeup)
    {
        ReadOnlySpan<byte> frame = _inbound.AsSpan(0, received);

        if (frame[1] != SasEventLayouts.Marker)
        {
            RecordReceive(received, wakeup,
                "not a real-time event: a general poll answered with more than one byte and "
                + $"${frame[1]:X2} where the event marker belongs");
            MarkHeard();
            return null;
        }

        if (received < SasEventLayouts.HeaderLength + 2 || !SasCrc.Verify(frame))
        {
            RecordReceive(received, wakeup,
                "a real-time event whose check sequence does not check out, so no byte of it is "
                + "established");
            return null;
        }

        if (frame[0] != _polling.Value)
        {
            RecordReceive(received, wakeup,
                $"a real-time event addressed ${frame[0]:X2}, and the machine being polled is "
                + $"${_polling.Value:X2}");
            MarkHeard();
            return null;
        }

        byte code = frame[2];
        byte[] data = frame[SasEventLayouts.HeaderLength..^2].ToArray();
        RecordReceive(received, wakeup, code == 0x00
            ? "no exception, in the real-time event format"
            : $"real-time event ${code:X2}");
        MarkHeard();

        // Zero is the machine saying it has nothing queued, in the same format as everything else.
        // It is not an exception and must not be reported as one.
        if (code == 0x00)
            return code;

        long sequence = Interlocked.Increment(ref _exceptions);
        ExceptionReported?.Invoke(
            new ExceptionReport(code, _options.Channel, _clock.Elapsed, sequence, data)
            {
                Address = _polling,
            });

        return code;
    }

    /// <summary>
    /// Reports the exception carried by an event that arrived in a long poll's window.
    /// </summary>
    /// <remarks>
    /// The frame has already been checked and recorded by the long-poll path, so this only reads
    /// it. A code of zero is the machine saying it has nothing queued and is never an exception,
    /// the same rule the general-poll path keeps.
    /// </remarks>
    private void ReportEventFromLongPoll(byte[] frame)
    {
        byte code = frame[2];
        if (code == 0x00)
            return;

        byte[] data = frame[SasEventLayouts.HeaderLength..^2];
        long sequence = Interlocked.Increment(ref _exceptions);
        ExceptionReported?.Invoke(
            new ExceptionReport(code, _options.Channel, _clock.Elapsed, sequence, data)
            {
                Address = _polling,
            });
    }

    /// <summary>Which kind of message the caller submitted.</summary>
    private enum PendingKind
    {
        LongPoll,
        GeneralPoll,
        SyncPoll,
    }

    /// <summary>Runs whatever a caller submitted, whichever kind it is.</summary>
    private void RunPending()
    {
        switch (_pendingKind)
        {
            case PendingKind.GeneralPoll:
                RunPendingGeneralPoll();
                return;

            case PendingKind.SyncPoll:
                RunPendingSyncPoll();
                return;

            default:
                RunPendingLongPoll();
                return;
        }
    }

    /// <summary>Sends one general poll a caller named, and hands back what it drew.</summary>
    private void RunPendingGeneralPoll()
    {
        long ticket;
        string? purpose;
        lock (_submitGate)
        {
            ticket = _pendingTicket;
            purpose = _pendingPurpose;
            _polling = _pendingAddress;
            _pendingReady = false;
        }

        _attributed = null;
        GeneralPollResult result = SendGeneralPollAndRead(purpose);
        Finish(ticket, result);
    }

    /// <summary>
    /// Sends the broadcast to address 00 a caller named, and listens for the answer no machine
    /// should give.
    /// </summary>
    /// <remarks>
    /// Section 3.19 makes this a global acknowledgement, which is the whole reason a case needs to
    /// place one by hand. The line is listened to afterwards rather than assumed quiet: a machine
    /// that answered a broadcast would be a finding, and one this build would otherwise never see.
    /// </remarks>
    private void RunPendingSyncPoll()
    {
        long ticket;
        string? purpose;
        lock (_submitGate)
        {
            ticket = _pendingTicket;
            purpose = _pendingPurpose;
            _pendingReady = false;
        }

        _attributed = null;
        int length = HostMessage.WriteSyncPoll(_outbound);
        long sentAt = _clock.ElapsedTicks;
        Resynchronise();
        SendAndRecord(length, WirePurpose.Label("sync poll - address zero", purpose)!);
        Interlocked.Increment(ref _syncPolls);

        int received = _link.Receive(
            _inbound, _options.ResponseTimeout, _options.QuietGap, out bool wakeup);
        var latency = TimeSpan.FromTicks(_clock.ElapsedTicks - sentAt);

        if (received == 0)
        {
            Finish(ticket, new GeneralPollResult(SasAddress.Sync, _options.Channel,
                GeneralPollOutcome.Broadcast, 0x00, [], latency));
            return;
        }

        RecordReceive(received, wakeup,
            "a machine answered a broadcast to address 00, which no machine may answer");
        Finish(ticket, new GeneralPollResult(SasAddress.Sync, _options.Channel,
            GeneralPollOutcome.Unreadable, 0x00, _inbound.AsSpan(0, received).ToArray(), latency,
            "A machine answered the broadcast to address 00. No machine may answer one."));
    }

    /// <summary>Hands a poll's answer back to whoever asked, when they are still waiting for it.</summary>
    private void Finish(long ticket, GeneralPollResult result)
    {
        lock (_submitGate)
        {
            // Whoever asked has already given up and may have asked for something else, so the
            // answer belongs to nobody. It stays on the wire log either way.
            if (_pendingTicket != ticket)
                return;

            _pendingPollResult = result;
        }

        _pendingDone.Set();
    }

    /// <summary>
    /// Stops polling after a failure on the poll thread, and hands the reason to whoever is
    /// waiting rather than letting the failure end the process.
    /// </summary>
    /// <remarks>
    /// A caller whose answer was already handed back keeps it: only a transaction nobody has
    /// answered yet is told the link failed.
    /// </remarks>
    private void Fail(string fault)
    {
        // Down before the fault is visible, and the waiter released before any handler runs.
        bool changed = ChangeState(LinkState.Down);

        lock (_submitGate)
        {
            _linkFault = fault;
            _pendingReady = false;
            if (!_pendingDone.IsSet)
            {
                _pendingResult = new LongPollResult(_pendingCommand, _options.Channel,
                    LongPollOutcome.NotSent, [], TimeSpan.Zero, fault);
                _pendingPollResult = new GeneralPollResult(_pendingAddress, _options.Channel,
                    GeneralPollOutcome.NotSent, 0x00, [], TimeSpan.Zero, fault);
            }
        }

        _pendingDone.Set();

        if (!changed || LinkStateChanged is not { } handlers)
            return;

        // Each handler is told on its own, so one that fails does not keep the others from hearing.
        foreach (Action<LinkState> handler in handlers.GetInvocationList().Cast<Action<LinkState>>())
        {
            try
            {
                handler(LinkState.Down);
            }
            catch (Exception failed)
            {
                // The fault is already recorded, so a handler that fails on hearing it adds to the reason.
                lock (_submitGate)
                {
                    _linkFault += $" Reporting it then failed with {failed.GetType().Name}: {failed.Message}";
                }
            }
        }
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
            _polling = _pendingAddress;
            _pendingReady = false;
        }

        _attributed = null;

        // A machine that has not been resynchronised ignores an addressed poll, so keep sending
        // the synchronisation poll ahead of long polls too — except in the sequenced mode, where
        // the case owns every message on the line and a broadcast is itself an acknowledgement.
        if (_options.SyncPollEvery > 0 && !_sequenced)
        {
            int syncLength = HostMessage.WriteSyncPoll(_outbound);
            SendAndRecord(syncLength, "sync poll - address zero");
            Interlocked.Increment(ref _syncPolls);
            Thread.Sleep((int)_options.QuietGap.TotalMilliseconds);
        }

        int length = HostMessage.WriteLongPoll(
            _outbound, _polling, command, _pendingData.AsSpan(0, dataLength), checkSequence);

        long sentAt = _clock.ElapsedTicks;
        Resynchronise();
        _attributed = _polling;
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

        // The event reached the stream it would have reached on a general poll. Reported here
        // rather than in Interpret, which decides what a reply was and raises nothing.
        if (result.Outcome == LongPollOutcome.AnsweredWithEvent)
            ReportEventFromLongPoll(result.Frame);

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
            if (frame[0] == _polling.Value)
                return new LongPollResult(command, _options.Channel, LongPollOutcome.Acknowledged,
                    frame, latency);

            if (frame[0] == (byte)(_polling.Value | HostMessage.GeneralPollFlag))
            {
                Interlocked.Increment(ref _rejections);
                return new LongPollResult(command, _options.Channel, LongPollOutcome.Rejected,
                    frame, latency, $"The machine rejected long poll ${command:X2}.");
            }

            return new LongPollResult(command, _options.Channel, LongPollOutcome.WrongAddress,
                frame, latency, $"A one-byte reply of ${frame[0]:X2} is neither an acknowledgement "
                                + $"nor a rejection from address {_polling}.");
        }

        if (frame[0] != _polling.Value)
            return new LongPollResult(command, _options.Channel, LongPollOutcome.WrongAddress,
                frame, latency,
                $"The reply is addressed to ${frame[0]:X2}, not to {_polling}.");

        if (received > 2 && !SasCrc.Verify(frame))
        {
            Interlocked.Increment(ref _badCrcs);
            return new LongPollResult(command, _options.Channel, LongPollOutcome.BadCheckSequence,
                frame, latency, "The frame check sequence did not match.");
        }

        if (IsEventInsteadOfReply(command, frame))
        {
            return new LongPollResult(command, _options.Channel, LongPollOutcome.AnsweredWithEvent,
                frame, latency,
                $"The machine answered with real-time event ${frame[2]:X2} rather than with the "
                + $"reply to long poll ${command:X2}, which is what it does while event reporting "
                + "is on and an exception is queued. The event was kept and reported. Drain the "
                + "queue before sending this poll.");
        }

        return new LongPollResult(command, _options.Channel, LongPollOutcome.Answered, frame, latency);
    }

    /// <summary>
    /// Whether what came back in a long poll's window is a real-time event rather than the reply.
    /// </summary>
    /// <remarks>
    /// Called only once the frame has passed the address and check-sequence tests, so the marker
    /// and the code it carries are established bytes rather than bytes that happen to be there.
    /// </remarks>
    private static bool IsEventInsteadOfReply(int command, ReadOnlySpan<byte> frame) =>
        command != HostMessage.RealTimeEventIdentifier
        && frame.Length >= SasEventLayouts.HeaderLength + 2
        && frame[1] == SasEventLayouts.Marker;

    private void SendAndRecord(int length, string note)
    {
        ReadOnlySpan<SasSymbol> message = _outbound.AsSpan(0, length);
        try
        {
            _link.Send(message);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException
                                       or UnauthorizedAccessException)
        {
            throw new LinkFailedException($"{Transport.Description} stopped taking messages: {ex.Message}", ex);
        }

        WireLog.Record(WireDirection.Transmit, _options.Channel, _attributed, message, _clock.Elapsed, note);
        Wire?.Invoke(WireDirection.Transmit, _options.Channel, _attributed, message, _clock.Elapsed, note);
    }

    private void RecordReceive(int received, bool wakeup, string? note)
    {
        // Reuse the outbound scratch to render received bytes as symbols, so recording the wire
        // costs no allocation. The send has already happened by this point.
        int length = Math.Min(received, _outbound.Length);
        for (int i = 0; i < length; i++)
            _outbound[i] = wakeup ? SasSymbol.Address(_inbound[i]) : SasSymbol.Data8(_inbound[i]);

        ReadOnlySpan<SasSymbol> message = _outbound.AsSpan(0, length);
        WireLog.Record(WireDirection.Receive, _options.Channel, _attributed, message, _clock.Elapsed, note);
        Wire?.Invoke(WireDirection.Receive, _options.Channel, _attributed, message, _clock.Elapsed, note);
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
        byte address = _polling.Value;

        if (command == GeneralPoll)
            return frame.Length == 1
                || (frame[0] == address && frame[1] == HostMessage.RealTimeEventIdentifier);

        if (frame.Length == 1)
            return frame[0] == address || frame[0] == (byte)(address | HostMessage.GeneralPollFlag);

        if (frame[0] != address)
            return false;

        // The machine answers a long poll with a queued real-time event instead of the poll's own
        // reply while reporting is on, so the event format belongs in this window too. A poll of
        // $FF itself is left alone: there the marker and an echoed command are the same byte.
        if (command != HostMessage.RealTimeEventIdentifier
            && frame[1] == HostMessage.RealTimeEventIdentifier)
        {
            return true;
        }

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
        WireLog.Record(WireDirection.Receive, _options.Channel, _attributed, kept, _clock.Elapsed, note);
        Wire?.Invoke(WireDirection.Receive, _options.Channel, _attributed, kept, _clock.Elapsed, note);
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
        if (ChangeState(state))
            LinkStateChanged?.Invoke(state);
    }

    /// <summary>Records the state without telling anyone, and says whether it changed.</summary>
    private bool ChangeState(LinkState state)
    {
        lock (_stateGate)
        {
            bool changed = _state != state;
            _state = state;
            return changed;
        }
    }

    private static string FormatCaveat(string? caveat) => caveat is null ? string.Empty : $" {caveat}";

    /// <summary>The link refused a message, carried from the write to the poll loop.</summary>
    private sealed class LinkFailedException(string message, Exception inner) : Exception(message, inner);
}
