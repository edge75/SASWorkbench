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
    private readonly HashSet<string> _openDoors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    private Thread? _thread;
    private volatile bool _running;
    private bool _enabled = true;

    // Every denomination, until a host narrows it with long poll $08. Permissive to begin with
    // because the machine this fixture stands in for was measured accepting a note without ever
    // being sent that long poll — what it needs first is its own configuration, not the host's.
    private uint _enabledNotes = uint.MaxValue;

    private long _generalPolls;
    private long _longPolls;
    private long _rejections;

    // Kept apart from the credit meters on purpose: the bill-value meter is counted in whole
    // currency units and does not move when the accounting denomination does.
    private long _billValueInDollars;

    public FakeEgm(ISasTransport transport, FakeEgmOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);

        if (!transport.Capabilities.CarriesWakeupBit)
            throw new TransportRefusedException(
                $"{transport.Description} cannot carry the wakeup bit, so this machine could not "
                + "tell an address byte from a data byte. Refusing rather than guessing.");

        _transport = transport;
        _options = options ?? new FakeEgmOptions();
        (DenominationCode, AccountingCents, DenominationRefusal) = ResolveDenomination(_options);
    }

    /// <summary>The meters this machine keeps.</summary>
    public MeterStore Meters { get; } = new();

    /// <summary>The Table C-4 code this machine publishes as its accounting denomination.</summary>
    public byte DenominationCode { get; }

    /// <summary>What one accounting unit is worth, in cents.</summary>
    public int AccountingCents { get; }

    /// <summary>
    /// Why a configured accounting denomination was refused, or null when none was.
    /// </summary>
    /// <remarks>
    /// A machine that quietly used a denomination it should have refused would report every credit
    /// meter in the wrong unit and look healthy doing it, so the refusal is readable rather than
    /// only traced.
    /// </remarks>
    public string? DenominationRefusal { get; }

    /// <summary>
    /// Works out the accounting denomination actually in force.
    /// </summary>
    /// <remarks>
    /// Unset, the machine keeps the behaviour it has always had: it publishes the identification
    /// byte it was given and counts money in whole credits. Set, section 16's two rules apply -
    /// the value has to be one the protocol can express, and every denomination a player may
    /// choose has to divide evenly by it. A value failing either is refused in favour of one cent,
    /// which is what a real machine does, rather than used and quietly wrong.
    /// </remarks>
    private static (byte Code, int Cents, string? Refusal) ResolveDenomination(FakeEgmOptions options)
    {
        // Unset, the machine counts in the unit it publishes. That is not a convenience: the two
        // disagreeing is a defect a test cannot see, because every credit meter is then wrong by
        // the same factor and nothing on the wire says so.
        if (options.AccountingDenomination is not { } configured)
        {
            int? published = SasDenomination.CentsOf(options.Denomination);
            return published is null
                ? ((byte)0x01, 1,
                    $"${options.Denomination:X2} is not a denomination the protocol can express as "
                    + "an accounting unit, so the machine reports in cents.")
                : (options.Denomination, published.Value, null);
        }

        int? cents = SasDenomination.CentsOf(configured);
        if (cents is null)
        {
            return (0x01, 1,
                $"${configured:X2} is not a denomination the protocol can express as an accounting "
                + "unit, so the machine reports in cents.");
        }

        if (options.PlayerDenominationsInCents.Count > 0
            && !SasDenomination.DividesEvenly(cents.Value, options.PlayerDenominationsInCents))
        {
            return (0x01, 1,
                $"{cents} cents does not divide every denomination offered to the player, so the "
                + "machine reports in cents.");
        }

        return (configured, cents.Value, null);
    }

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
    /// <exception cref="InvalidOperationException">The host has disabled that denomination.</exception>
    public void InsertBill(int cents)
    {
        (SasExceptionCode exception, SasMeterCode countMeter, int denomination) = cents switch
        {
            100 => (SasExceptionCode.USD1BillAccepted, SasMeterCode.TotalNumberOfUSD1BillsAccepted, 0),
            200 => (SasExceptionCode.USD2BillAccepted, SasMeterCode.TotalNumberOfUSD2BillsAccepted, 1),
            500 => (SasExceptionCode.USD5BillAccepted, SasMeterCode.TotalNumberOfUSD5BillsAccepted, 2),
            1000 => (SasExceptionCode.USD10BillAccepted, SasMeterCode.TotalNumberOfUSD10BillsAccepted, 3),
            2000 => (SasExceptionCode.USD20BillAccepted, SasMeterCode.TotalNumberOfUSD20BillsAccepted, 4),
            5000 => (SasExceptionCode.USD50BillAccepted, SasMeterCode.TotalNumberOfUSD50BillsAccepted, 6),
            10000 => (SasExceptionCode.USD100BillAccepted, SasMeterCode.TotalNumberOfUSD100BillsAccepted, 7),
            _ => throw new ArgumentOutOfRangeException(nameof(cents), cents,
                "The protocol has no bill-accepted exception for that value."),
        };

        lock (_gate)
        {
            if ((_enabledNotes & (1u << denomination)) == 0)
                throw new InvalidOperationException(
                    $"The host has disabled {cents}-cent notes, so this machine returns it.");
        }

        long credits = cents / AccountingCents;
        Interlocked.Add(ref _billValueInDollars, cents / 100);
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

        // A wager and a win are both numbers of game credits; the meters they feed are in
        // accounting units. The two are the same size only when a game credit happens to be worth
        // one accounting unit.
        long wagered = games * betCredits * _options.CentsPerGameCredit / AccountingCents;
        long won = winCredits * _options.CentsPerGameCredit / AccountingCents;
        long credits = Meters.Read(SasMeterCode.CurrentCredits);
        if (credits < wagered)
            throw new InvalidOperationException(
                $"The machine holds {credits} accounting unit(s) and cannot wager {wagered}.");

        Meters.Increment(SasMeterCode.GamesPlayed, games);
        Meters.Increment(SasMeterCode.TotalCoinInCredits, wagered);
        if (winCredits > 0)
        {
            Meters.Increment(SasMeterCode.GamesWon, 1);
            Meters.Increment(SasMeterCode.GamesLost, games - 1);
            Meters.Increment(SasMeterCode.TotalCoinOutCredits, won);
        }
        else
        {
            Meters.Increment(SasMeterCode.GamesLost, games);
        }

        Meters.SetCurrentCredits(credits - wagered + won);
    }

    /// <summary>Opens the slot door, raising the exception a real machine raises.</summary>
    public void OpenSlotDoor() => SetDoor("slot", open: true);

    /// <summary>Closes the slot door.</summary>
    public void CloseSlotDoor() => SetDoor("slot", open: false);

    /// <summary>
    /// Opens or closes a door, reporting it only when it actually moved.
    /// </summary>
    /// <remarks>
    /// A machine reports the change, not the state, so shutting a door that is already shut says
    /// nothing on the wire. Modelling that is what makes a test written against this fixture mean
    /// the same thing when it is run against a real machine.
    /// </remarks>
    /// <returns>True when the door moved and an exception was raised.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No door goes by that name.</exception>
    public bool SetDoor(string name, bool open)
    {
        if (!MachineDoors.TryFind(name, out MachineDoor door))
        {
            throw new ArgumentOutOfRangeException(nameof(name), name,
                $"The protocol reports these doors: {string.Join(", ", MachineDoors.Names)}.");
        }

        lock (_openDoors)
        {
            if (open ? !_openDoors.Add(door.Name) : !_openDoors.Remove(door.Name))
                return false;
        }

        RaiseException(open ? door.Opened : door.Closed);
        return true;
    }

    /// <summary>Shuts every door that is open, and says which ones those were.</summary>
    public IReadOnlyList<string> Settle()
    {
        string[] open;
        lock (_openDoors)
            open = [.. _openDoors];

        foreach (string door in open)
            SetDoor(door, open: false);

        return open;
    }

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
                SendData(layout, frame);
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

            case 0x08:
                // Four bytes of denomination bits, least significant first, then an action byte.
                // A real machine keeps this beside what its own menu allows and takes a note only
                // when both agree, so a host that never sends this gets no notes at all.
                if (frame.Length < 7)
                    return false;

                lock (_gate)
                {
                    _enabledNotes = (uint)(frame[2] | (frame[3] << 8) | (frame[4] << 16)
                                           | (frame[5] << 24));
                }

                return true;

            case 0x0E:
                // Real-time event reporting takes one data byte, which must be zero or one.
                return frame.Length >= 3 && frame[2] is 0x00 or 0x01;

            default:
                return false;
        }
    }

    private void SendData(SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        Dictionary<string, object>? values = ValuesFor(layout, request);
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

    private Dictionary<string, object>? ValuesFor(SasMessageLayout layout, ReadOnlySpan<byte> request) => layout.Command switch
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
            ["denomination"] = DenominationCode,
            ["max_bet"] = _options.MaxBet,
            ["progressive_group"] = _options.ProgressiveGroup,
            ["game_options"] = _options.GameOptions,
            ["paytable_id"] = _options.PaytableId,
            ["base_percentage"] = _options.BasePercentage,
        },
        0x20 => new Dictionary<string, object>
        {
            ["total_bill_value"] = Interlocked.Read(ref _billValueInDollars),
        },
        0x2C => new Dictionary<string, object> { ["hopper_level"] = (long)_options.HopperLevel },
        0x54 => new Dictionary<string, object>
        {
            ["length"] = _options.SasVersion.Length + _options.SerialNumber.Length,
            ["sas_version"] = _options.SasVersion,
            ["serial_number"] = _options.SerialNumber,
        },
        0x7E => DateAndTime(),
        0xB1 => new Dictionary<string, object> { ["denomination"] = DenominationCode },
        0x6F or 0xAF => SelectedMeters(request),

        // Everything else has a layout but no source of values here.
        _ => null,
    };

    /// <summary>
    /// Answers a selected-meter poll, in the shape a machine answers it: one entry per meter it
    /// supports, each stating its own width, and nothing at all for one it does not.
    /// </summary>
    /// <remarks>
    /// Meters are answered in the order they were asked for, and an unsupported code is left out
    /// entirely rather than answered with zero - a host that assumed a fixed number of entries
    /// would read the next meter's code as this one's value. The door and power counts are refused
    /// here on purpose and are read with the legacy meters command instead.
    /// </remarks>
    private Dictionary<string, object>? SelectedMeters(ReadOnlySpan<byte> request)
    {
        // Address, command, length, and two bytes of game number at the very least.
        if (request.Length < 5)
            return null;

        int declared = request[2];
        int codes = (declared - 2) / 2;
        int firstCode = 5;
        if (codes < 0 || firstCode + (codes * 2) > request.Length)
            return null;

        long game = ((request[3] >> 4) * 1000) + ((request[3] & 0x0F) * 100)
                    + ((request[4] >> 4) * 10) + (request[4] & 0x0F);

        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        int entries = 0;
        int payload = 2;

        for (int i = 0; i < codes; i++)
        {
            // Binary, least significant byte first.
            ushort code = (ushort)(request[firstCode + (i * 2)]
                                   | (request[firstCode + (i * 2) + 1] << 8));

            if (!IsReportableMeter(code))
                continue;

            long value = Meters.Read(code);
            int size = WidthOf(code, value);

            values[$"meters[{entries}].code"] = code;
            values[$"meters[{entries}].size"] = size;
            values[$"meters[{entries}].value"] = value;
            entries++;
            payload += 3 + size;
        }

        values["length"] = payload;
        values["game_number"] = game;
        values["meters.count"] = entries;
        return values;
    }

    /// <summary>
    /// Whether a selected-meter poll may answer about this meter at all. The slot door and power
    /// reset counts are excluded by the machine rather than merely absent from it.
    /// </summary>
    private static bool IsReportableMeter(ushort code) =>
        code != (ushort)SasMeterCode.NumberSlotDoorOpened
        && code != (ushort)SasMeterCode.NumberACPowerLost
        && SasCatalogue.Meters.ContainsKey(code);

    /// <summary>
    /// How many bytes this meter's value takes. Four is the floor, five for a meter counted in
    /// cents, and the field widens as the value grows so that it never rolls over silently the
    /// way the fixed-width polls do.
    /// </summary>
    private static int WidthOf(ushort code, long value)
    {
        int floor = SasMeterUnits.UnitOf(code) == SasMeterUnit.Cents ? 5 : 4;
        int digits = value <= 0 ? 1 : (int)Math.Floor(Math.Log10(value)) + 1;
        return Math.Max(floor, (digits + 1) / 2);
    }

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
