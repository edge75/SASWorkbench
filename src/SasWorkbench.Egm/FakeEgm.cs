using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>
/// A gaming machine written in C#, answering SAS over any transport.
/// </summary>
/// <remarks>
/// <para>
/// This is a fixture, not a model. It exists so tests of the host can run in milliseconds
/// without a build of the platform, and so the same script can be run against both peers and the
/// verdicts compared — a disagreement between them is a finding in one of the two, which is
/// worth more than either result alone.
/// </para>
/// <para>
/// What it is not: authority on how a gaming machine behaves. The real machine is the platform
/// library, and where the two differ the library is right by definition. Anything this fixture
/// cannot answer it rejects, rather than inventing a reply that a test would then depend on.
/// </para>
/// </remarks>
public sealed class FakeEgm : IDisposable
{
    private readonly ISasTransport _transport;
    private readonly FakeEgmOptions _options;
    private readonly SasSymbol[] _incoming = new SasSymbol[HostMessage.MaxSymbols];
    private readonly List<SasSymbol> _message = new(HostMessage.MaxSymbols);
    private readonly SasSymbol[] _outbound = new SasSymbol[HostMessage.MaxSymbols];
    private readonly byte[] _frame = new byte[HostMessage.MaxSymbols];
    private readonly Queue<byte> _exceptions = new();
    private readonly Lock _gate = new();

    private Thread? _thread;
    private volatile bool _running;
    private bool _enabled = true;
    private long _generalPolls;
    private long _longPolls;
    private long _rejections;

    public FakeEgm(ISasTransport transport, FakeEgmOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);

        if (!transport.Capabilities.CarriesWakeupBit)
            throw new TransportRefusedException(
                $"{transport.Description} cannot carry the wakeup bit, so this machine could not "
                + "tell an address byte from a data byte. Refusing rather than guessing.");

        _transport = transport;
        _options = options ?? new FakeEgmOptions();
    }

    /// <summary>The meters this machine keeps.</summary>
    public MeterStore Meters { get; } = new();

    /// <summary>The address this machine answers to.</summary>
    public SasAddress Address => _options.Address;

    /// <summary>True while play is enabled. Long poll shutdown clears it; startup sets it.</summary>
    public bool IsEnabled
    {
        get
        {
            lock (_gate)
                return _enabled;
        }
    }

    /// <summary>How many general polls of this machine's address have arrived.</summary>
    public long GeneralPolls => Interlocked.Read(ref _generalPolls);

    /// <summary>How many long polls addressed to this machine have arrived.</summary>
    public long LongPolls => Interlocked.Read(ref _longPolls);

    /// <summary>How many long polls this machine refused because it could not answer them.</summary>
    public long Rejections => Interlocked.Read(ref _rejections);

    /// <summary>How many exceptions are queued but not yet reported.</summary>
    public int PendingExceptions
    {
        get
        {
            lock (_gate)
                return _exceptions.Count;
        }
    }

    /// <summary>Raised for every long poll that arrived, whether or not it could be answered.</summary>
    public event Action<byte>? LongPollReceived;

    /// <summary>Starts answering.</summary>
    public void Start()
    {
        if (_running)
            return;

        _transport.Open();
        _running = true;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = $"fake-egm-{_options.Address}",
        };
        _thread.Start();
    }

    /// <summary>Stops answering.</summary>
    public void Stop()
    {
        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(1));
        _thread = null;
    }

    public void Dispose()
    {
        Stop();
        _transport.Dispose();
    }

    // ---- Stimulus ------------------------------------------------------------------------

    /// <summary>Queues an exception for the next general poll.</summary>
    public void RaiseException(SasExceptionCode code) => RaiseException((byte)code);

    /// <summary>Queues an exception code for the next general poll.</summary>
    public void RaiseException(byte code)
    {
        if (code == 0x00)
            throw new ArgumentOutOfRangeException(nameof(code), code,
                "Zero means the queue is empty and cannot be queued as an exception.");

        lock (_gate)
            _exceptions.Enqueue(code);
    }

    /// <summary>
    /// Accepts a note, raising the exception for that denomination and moving the meters a real
    /// machine would move.
    /// </summary>
    /// <param name="cents">Face value of the note, in cents.</param>
    /// <exception cref="ArgumentOutOfRangeException">No exception code exists for that value.</exception>
    public void InsertBill(int cents)
    {
        (SasExceptionCode exception, SasMeterCode countMeter) = cents switch
        {
            100 => (SasExceptionCode.USD1BillAccepted, SasMeterCode.TotalNumberOfUSD1BillsAccepted),
            200 => (SasExceptionCode.USD2BillAccepted, SasMeterCode.TotalNumberOfUSD2BillsAccepted),
            500 => (SasExceptionCode.USD5BillAccepted, SasMeterCode.TotalNumberOfUSD5BillsAccepted),
            1000 => (SasExceptionCode.USD10BillAccepted, SasMeterCode.TotalNumberOfUSD10BillsAccepted),
            2000 => (SasExceptionCode.USD20BillAccepted, SasMeterCode.TotalNumberOfUSD20BillsAccepted),
            5000 => (SasExceptionCode.USD50BillAccepted, SasMeterCode.TotalNumberOfUSD50BillsAccepted),
            10000 => (SasExceptionCode.USD100BillAccepted, SasMeterCode.TotalNumberOfUSD100BillsAccepted),
            _ => throw new ArgumentOutOfRangeException(nameof(cents), cents,
                "The protocol has no bill-accepted exception for that value."),
        };

        long credits = cents / Math.Max(1, _options.CentsPerCredit);
        Meters.Increment(countMeter, 1);
        Meters.Increment(SasMeterCode.TotalCreditsFromBillsAccepted, credits);
        Meters.Increment(SasMeterCode.TotalDropIn_Crd, credits);
        Meters.SetCurrentCredits(Meters.Read(SasMeterCode.CurrentCredits) + credits);
        RaiseException(exception);
    }

    /// <summary>
    /// Plays games, moving the wager and outcome meters. Whether each game is won is the caller's
    /// decision, because a fixture has no business inventing outcomes.
    /// </summary>
    /// <param name="games">How many games to play.</param>
    /// <param name="betCredits">Credits wagered per game.</param>
    /// <param name="winCredits">Credits won across all of them.</param>
    public void PlayGames(int games, long betCredits, long winCredits = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(games);
        ArgumentOutOfRangeException.ThrowIfNegative(betCredits);
        ArgumentOutOfRangeException.ThrowIfNegative(winCredits);

        long wagered = games * betCredits;
        long credits = Meters.Read(SasMeterCode.CurrentCredits);
        if (credits < wagered)
            throw new InvalidOperationException(
                $"The machine holds {credits} credit(s) and cannot wager {wagered}.");

        Meters.Increment(SasMeterCode.GamesPlayed, games);
        Meters.Increment(SasMeterCode.TotalCoinInCredits, wagered);
        if (winCredits > 0)
        {
            Meters.Increment(SasMeterCode.GamesWon, 1);
            Meters.Increment(SasMeterCode.GamesLost, games - 1);
            Meters.Increment(SasMeterCode.TotalCoinOutCredits, winCredits);
        }
        else
        {
            Meters.Increment(SasMeterCode.GamesLost, games);
        }

        Meters.SetCurrentCredits(credits - wagered + winCredits);
    }

    /// <summary>Opens the slot door, raising the exception a real machine raises.</summary>
    public void OpenSlotDoor() => RaiseException(SasExceptionCode.SlotDoorOpened);

    /// <summary>Closes the slot door.</summary>
    public void CloseSlotDoor() => RaiseException(SasExceptionCode.SlotDoorClosed);

    // ---- The wire ------------------------------------------------------------------------

    private void Loop()
    {
        while (_running)
        {
            int read = _transport.Read(_incoming, TimeSpan.FromMilliseconds(10));
            if (read == 0)
            {
                Dispatch();
                continue;
            }

            for (int i = 0; i < read; i++)
            {
                SasSymbol symbol = _incoming[i];

                // The wakeup bit is the start of a message, which is the one framing signal SAS
                // gives. Splitting on it means two host messages arriving together are still
                // read as two.
                if (symbol.Wakeup)
                    Dispatch();
                else if (_message.Count == 0)
                    continue;

                _message.Add(symbol);
            }
        }

        Dispatch();
    }

    private void Dispatch()
    {
        if (_message.Count == 0)
            return;

        byte first = _message[0].Data;
        int count = _message.Count;
        for (int i = 0; i < count; i++)
            _frame[i] = _message[i].Data;
        _message.Clear();

        if (_options.ResponseDelay > TimeSpan.Zero)
            Thread.Sleep(_options.ResponseDelay);

        if (count == 1)
        {
            HandlePoll(first);
            return;
        }

        if (first != _options.Address.Value && first != SasAddress.Sync.Value)
            return;

        HandleLongPoll(_frame.AsSpan(0, count));
    }

    private void HandlePoll(byte first)
    {
        if ((first & HostMessage.GeneralPollFlag) == 0)
            return;

        byte polled = (byte)(first & ~HostMessage.GeneralPollFlag);
        if (polled == 0 || polled != _options.Address.Value)
            return;

        Interlocked.Increment(ref _generalPolls);

        byte code;
        lock (_gate)
            code = _exceptions.Count > 0 ? _exceptions.Dequeue() : (byte)0x00;

        Send([SasSymbol.Data8(code)]);
    }

    private void HandleLongPoll(ReadOnlySpan<byte> frame)
    {
        byte command = frame[1];
        Interlocked.Increment(ref _longPolls);
        LongPollReceived?.Invoke(command);

        // A host message longer than two bytes carries a check sequence, and a machine that
        // answered one that failed would hide a broken link.
        if (frame.Length > 2 && !SasCrc.Verify(frame))
        {
            Reject();
            return;
        }

        SasMessageLayout? layout = SasLayouts.For(command);
        if (layout is null)
        {
            // Known to the protocol but not modelled here, or not a command at all. Either way
            // this machine says no rather than answering something a test would come to rely on.
            Reject();
            return;
        }

        switch (layout.Shape)
        {
            case SasResponseShape.Acknowledgement:
                if (!ApplyAcknowledgedCommand(command, frame))
                {
                    Reject();
                    return;
                }

                Send([SasSymbol.Data8(_options.Address.Value)]);
                return;

            case SasResponseShape.None:
                return;

            case SasResponseShape.Data:
                SendData(layout);
                return;

            default:
                Reject();
                return;
        }
    }

    private bool ApplyAcknowledgedCommand(byte command, ReadOnlySpan<byte> frame)
    {
        switch (command)
        {
            case 0x01:
                lock (_gate)
                    _enabled = false;
                return true;

            case 0x02:
                lock (_gate)
                    _enabled = true;
                return true;

            case 0x0E:
                // Real-time event reporting takes one data byte, which must be zero or one.
                return frame.Length >= 3 && frame[2] is 0x00 or 0x01;

            default:
                return false;
        }
    }

    private void SendData(SasMessageLayout layout)
    {
        Dictionary<string, object>? values = ValuesFor(layout);
        if (values is null)
        {
            Reject();
            return;
        }

        int length;
        try
        {
            length = SasMessageEncoder.WriteResponse(layout, _options.Address, values, _frame);
        }
        catch (InvalidOperationException)
        {
            // A value that will not fit the field is this fixture's fault, not the host's, but
            // answering a malformed frame would blame the host for it.
            Reject();
            return;
        }

        for (int i = 0; i < length; i++)
            _outbound[i] = SasSymbol.Data8(_frame[i]);

        Send(_outbound.AsSpan(0, length));
    }

    private Dictionary<string, object>? ValuesFor(SasMessageLayout layout) => layout.Command switch
    {
        0x0F => new Dictionary<string, object>
        {
            ["total_cancelled_credits"] = Meters.Read(SasMeterCode.TotalCancelledCredits),
            ["total_coin_in"] = Meters.Read(SasMeterCode.TotalCoinInCredits),
            ["total_coin_out"] = Meters.Read(SasMeterCode.TotalCoinOutCredits),
            ["total_drop"] = Meters.Read(SasMeterCode.TotalDropIn_Crd),
            ["total_jackpot"] = Meters.Read(SasMeterCode.TotalJackpotCredits),
            ["games_played"] = Meters.Read(SasMeterCode.GamesPlayed),
        },
        0x10 => Single("total_cancelled_credits", SasMeterCode.TotalCancelledCredits),
        0x11 => Single("total_coin_in", SasMeterCode.TotalCoinInCredits),
        0x12 => Single("total_coin_out", SasMeterCode.TotalCoinOutCredits),
        0x13 => Single("total_drop", SasMeterCode.TotalDropIn_Crd),
        0x14 => Single("total_jackpot", SasMeterCode.TotalJackpotCredits),
        0x15 => Single("games_played", SasMeterCode.GamesPlayed),
        0x16 => Single("games_won", SasMeterCode.GamesWon),
        0x17 => Single("games_lost", SasMeterCode.GamesLost),
        0x1A => Single("current_credits", SasMeterCode.CurrentCredits),
        0x1E => new Dictionary<string, object>
        {
            ["bills_1"] = Meters.Read(SasMeterCode.TotalNumberOfUSD1BillsAccepted),
            ["bills_5"] = Meters.Read(SasMeterCode.TotalNumberOfUSD5BillsAccepted),
            ["bills_10"] = Meters.Read(SasMeterCode.TotalNumberOfUSD10BillsAccepted),
            ["bills_20"] = Meters.Read(SasMeterCode.TotalNumberOfUSD20BillsAccepted),
            ["bills_50"] = Meters.Read(SasMeterCode.TotalNumberOfUSD50BillsAccepted),
            ["bills_100"] = Meters.Read(SasMeterCode.TotalNumberOfUSD100BillsAccepted),
        },
        0x1F => new Dictionary<string, object>
        {
            ["game_id"] = _options.GameId,
            ["additional_id"] = _options.AdditionalId,
            ["denomination"] = _options.Denomination,
            ["max_bet"] = _options.MaxBet,
            ["progressive_group"] = _options.ProgressiveGroup,
            ["game_options"] = _options.GameOptions,
            ["paytable_id"] = _options.PaytableId,
            ["base_percentage"] = _options.BasePercentage,
        },
        0x20 => Single("total_bill_value", SasMeterCode.TotalCreditsFromBillsAccepted),
        0x54 => new Dictionary<string, object>
        {
            ["length"] = _options.SasVersion.Length + _options.SerialNumber.Length,
            ["sas_version"] = _options.SasVersion,
            ["serial_number"] = _options.SerialNumber,
        },
        0x7E => DateAndTime(),
        0xB1 => new Dictionary<string, object> { ["denomination"] = _options.Denomination },

        // Everything else has a layout but no source of values here.
        _ => null,
    };

    private Dictionary<string, object> Single(string field, SasMeterCode meter) =>
        new() { [field] = Meters.Read(meter) };

    private static Dictionary<string, object> DateAndTime()
    {
        DateTime now = DateTime.Now;
        return new Dictionary<string, object>
        {
            ["month"] = now.Month,
            ["day"] = now.Day,
            ["year"] = now.Year,
            ["hour"] = now.Hour,
            ["minute"] = now.Minute,
            ["second"] = now.Second,
        };
    }

    private void Reject()
    {
        Interlocked.Increment(ref _rejections);
        Send([SasSymbol.Data8((byte)(_options.Address.Value | HostMessage.GeneralPollFlag))]);
    }

    private void Send(ReadOnlySpan<SasSymbol> symbols) => _transport.Write(symbols);
}
