using System.Collections.Frozen;
using System.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Tickets;
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
public sealed partial class FakeEgm : IDisposable
{
    private readonly FakeEgmOptions _options;
    private readonly List<MachineLink> _links = [];
    private readonly HashSet<string> _openDoors = new(StringComparer.OrdinalIgnoreCase);
    private readonly TicketRedemption _redemption = new();
    private readonly Lock _gate = new();

    private volatile bool _running;
    private bool _enabled = true;

    // Whether the host has asked for exceptions in the real-time event format. Off until long
    // poll $0E says otherwise, which is how a machine comes up.
    private bool _rteEnabled;

    // Every denomination, until a host narrows it with long poll $08. Permissive to begin with
    // because the machine this fixture stands in for was measured accepting a note without ever
    // being sent that long poll — what it needs first is its own configuration, not the host's.
    private uint _enabledNotes = uint.MaxValue;

    private long _generalPolls;
    private long _longPolls;
    private long _rejections;

    // Polls this machine does not implement and therefore ignored. Counted because silence on
    // the wire is the only correct answer but is not a useful diagnostic on its own.
    private long _ignored;
    private readonly HashSet<byte> _ignoredCommands = [];

    /// <summary>
    /// The commands this fixture has no machinery for: progressives, legacy bonus, cards, reels.
    /// </summary>
    /// <remarks>
    /// Stated here rather than left to the "could not build the values" fallback, because what
    /// this machine is built without is a property of the machine and belongs where a reader
    /// looks for one. The machine library behaves the same way and was read rather than assumed:
    /// the legacy bonus polls open with a test of the legacy-bonus feature bit and return without
    /// sending when it is clear, and none of these nine ever sends a refusal — a real machine
    /// answers or says nothing.
    /// <para>
    /// Kept apart from <see cref="FakeEgmOptions.UnansweredCommands"/>, which a test sets to build
    /// some other machine: if these lived there, a test that set that option for its own reasons
    /// would silently give this machine a progressive it does not have.
    /// </para>
    /// </remarks>
    private static readonly FrozenSet<byte> WithoutMachinery = new byte[]
    {
        0x83, 0x84, 0x85, 0x86, 0x87, 0x8E, 0x8F, 0x90, 0x9A,
    }.ToFrozenSet();

    // Kept apart from the credit meters on purpose: the bill-value meter is counted in whole
    // currency units and does not move when the accounting denomination does.
    private long _billValueInDollars;
    private long _ticketsPrinted;

    // The most recently printed ticket, which is what the cashout ticket information poll
    // answers with. One record rather than the library's buffer of thirty-two: this fixture
    // reads at the write index the way the library's handler does, and everything the buffer
    // exists for - the unread-record count, the fifteen second reissue, the read index the
    // enhanced styles advance - belongs to a style this machine does not perform.
    private ulong _lastValidationNumber;
    private long _lastTicketAmountCents;

    // The unit in force, which a script can change part-way through a session. Guarded because a
    // stimulus arrives on the runner's thread while the poll loop is answering on its own.
    private byte _denominationCode;
    private int _accountingCents;

    // What the last note taken was. Zero and zero until one is, which is what the library answers
    // on a machine that has taken none: its handler reads the stored denomination code and looks
    // up that denomination's own count, and code zero is the one-dollar note.
    private byte _lastBillCountryCode;
    private byte _lastBillDenominationCode;

    // Games played since the machine came up and since the slot door was last shut. Kept here
    // rather than as meters because the protocol keeps no meter code for either.

    // What a host set with the secure enhanced validation identifier command, which a machine
    // performing that validation echoes back. Held only by a machine configured for it.
    private uint _secureValidationId;
    private uint _secureSequenceNumber;

    public FakeEgm(ISasTransport transport, FakeEgmOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);

        if (!transport.Capabilities.CarriesWakeupBit)
            throw new TransportRefusedException(
                $"{transport.Description} cannot carry the wakeup bit, so this machine could not "
                + "tell an address byte from a data byte. Refusing rather than guessing.");

        _options = options ?? new FakeEgmOptions();
        (_denominationCode, _accountingCents, DenominationRefusal) = ResolveDenomination(_options);

        //  The channel arrangement decides which host is answered what, so it is settled before
        //  any link opens. A configuration no host could serve is refused here rather than
        //  half-served: the refusal is the machine's, and it is reported the way a machine's own
        //  menu would report it.
        Channels = _options.Channels ?? SasChannelPlan.Only(_options.Channel, _options.Address);
        ChannelRefusal = _options.ChannelRefusal;

        _links.Add(new MachineLink(transport, _options.Channel, _options.Address));
    }

    /// <summary>How this machine is divided between hosts.</summary>
    public SasChannelPlan Channels { get; }

    /// <summary>
    /// Why the arrangement asked for was refused, when one was and this machine fell back.
    /// </summary>
    /// <remarks>
    /// Carried rather than thrown so that a run can report a machine that would not take the
    /// configuration it was given, which is a finding, without the run itself falling over.
    /// </remarks>
    public string? ChannelRefusal { get; }

    /// <summary>
    /// Opens another host link on a second channel, at the address that channel answers at.
    /// </summary>
    /// <remarks>
    /// Refused rather than opened where the arrangement gives that channel no poll address: a
    /// channel with no address is one the machine never opens, so a host polling it would be
    /// talking to something this machine does not have.
    /// </remarks>
    /// <exception cref="TransportRefusedException">That channel has no poll address.</exception>
    public SasAddress Attach(ISasTransport transport, SasChannel channel)
    {
        ArgumentNullException.ThrowIfNull(transport);

        if (Channels.AddressOf(channel) is not { } address)
        {
            throw new TransportRefusedException(
                $"channel {(int)channel} has no poll address in this machine's configuration, so "
                + "it is not a channel this machine opens. " + Channels.Describe());
        }

        if (_links.Any(link => link.Channel == channel))
        {
            throw new TransportRefusedException(
                $"channel {(int)channel} is already open on this machine.");
        }

        var opened = new MachineLink(transport, channel, address);
        lock (_gate)
            _links.Add(opened);

        if (_running)
            StartLink(opened);

        return address;
    }

    /// <summary>The meters this machine keeps.</summary>
    public MeterStore Meters { get; } = new();

    /// <summary>The Table C-4 code this machine publishes as its accounting denomination.</summary>
    public byte DenominationCode
    {
        get
        {
            lock (_gate)
                return _denominationCode;
        }
    }

    /// <summary>Which validation style this machine performs when it cashes out.</summary>
    public SasCashoutValidation CashoutValidation => _options.CashoutValidation;

    /// <summary>What one accounting unit is worth, in cents.</summary>
    public int AccountingCents
    {
        get
        {
            lock (_gate)
                return _accountingCents;
        }
    }

    /// <summary>
    /// Changes the unit this machine counts in, part-way through a session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No real machine can do this - section 16 makes the accounting denomination a property a
    /// memory clear establishes - so this exists only because a software machine is one this build
    /// made and a script has to be able to reach it. Every other backend refuses the step.
    /// </para>
    /// <para>
    /// A value section 16 does not allow is refused outright rather than replaced with one cent.
    /// Falling back would leave the machine counting in a unit the script never asked for while
    /// every assertion after it looked healthy, which is the silent degrade this project refuses;
    /// the caller gets the reason and the case skips.
    /// </para>
    /// <para>
    /// Values already counted are left in the unit they were counted in. Rescaling them would
    /// either invent money or truncate it away, and neither is something a machine does.
    /// </para>
    /// </remarks>
    /// <returns>Why the change was refused, or null when it was made.</returns>
    public string? SetAccountingDenomination(byte code)
    {
        if (SasDenomination.CentsOf(code) is not { } cents)
        {
            return $"${code:X2} is not a denomination the protocol can express as an accounting "
                   + "unit, so this machine will not count in it";
        }

        if (_options.PlayerDenominationsInCents.Count > 0
            && !SasDenomination.DividesEvenly(cents, _options.PlayerDenominationsInCents))
        {
            return $"{cents} cents does not divide every denomination offered to the player, so "
                   + "section 16 does not allow this machine to count in it";
        }

        lock (_gate)
        {
            _denominationCode = code;
            _accountingCents = cents;
        }

        return null;
    }

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

    /// <summary>The address this machine answers to on its first channel.</summary>
    public SasAddress Address => _options.Address;

    /// <summary>Which channels are open on this machine, in order.</summary>
    public IReadOnlyList<SasChannel> OpenChannels
    {
        get
        {
            lock (_gate)
                return [.. _links.Select(link => link.Channel).Order()];
        }
    }

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

    /// <summary>How many polls this machine ignored because it does not implement them.</summary>
    public long Ignored => Interlocked.Read(ref _ignored);

    /// <summary>Which commands those were, so a run can say what it asked for in vain.</summary>
    public IReadOnlyCollection<byte> IgnoredCommands
    {
        get
        {
            lock (_ignoredCommands)
                return _ignoredCommands.Order().ToArray();
        }
    }

    /// <summary>How many exceptions are queued but not yet reported, over every channel.</summary>
    public int PendingExceptions
    {
        get
        {
            lock (_gate)
                return _links.Sum(link => link.Exceptions.Count);
        }
    }

    /// <summary>How many exceptions one channel is holding.</summary>
    public int PendingExceptionsOn(SasChannel channel)
    {
        lock (_gate)
            return _links.FirstOrDefault(link => link.Channel == channel)?.Exceptions.Count ?? 0;
    }

    /// <summary>Raised for every long poll that arrived, whether or not it could be answered.</summary>
    public event Action<byte>? LongPollReceived;

    /// <summary>Starts answering.</summary>
    public void Start()
    {
        if (_running)
            return;

        _running = true;
        foreach (MachineLink link in Links())
            StartLink(link);
    }

    private void StartLink(MachineLink link)
    {
        link.Transport.Open();
        link.Thread = new Thread(() => Loop(link))
        {
            IsBackground = true,
            Name = $"fake-egm-{link.Address}-ch{(int)link.Channel}",
        };
        link.Thread.Start();
    }

    private List<MachineLink> Links()
    {
        lock (_gate)
            return [.. _links];
    }

    /// <summary>Stops answering.</summary>
    public void Stop()
    {
        _running = false;
        foreach (MachineLink link in Links())
        {
            link.Thread?.Join(TimeSpan.FromSeconds(1));
            link.Thread = null;
        }
    }

    public void Dispose()
    {
        Stop();
        foreach (MachineLink link in Links())
            link.Transport.Dispose();
    }

    // ---- Stimulus ------------------------------------------------------------------------

    /// <summary>Queues an exception for the next poll that can carry it.</summary>
    public void RaiseException(SasExceptionCode code) => RaiseException((byte)code);

    /// <summary>Queues an exception code for the next poll that can carry it.</summary>
    public void RaiseException(byte code) => RaiseEvent(code, []);

    /// <summary>
    /// Queues an exception together with the data it carries.
    /// </summary>
    /// <remarks>
    /// The payload is queued whether or not the host has asked for real-time event reporting,
    /// which is what the machine library does: its builders fill the data in every time and the
    /// transmit path decides what to do with it when the exception leaves. So turning reporting on
    /// changes how an event is sent and never whether it happened.
    /// </remarks>
    public void RaiseEvent(byte code, params byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (code == 0x00)
            throw new ArgumentOutOfRangeException(nameof(code), code,
                "Zero means the queue is empty and cannot be queued as an exception.");

        //  One buffer per channel, and a channel that does not own the exception's category
        //  never hears it. Both come out of the library: it walks every open channel, skips one
        //  whose category does not match, and queues a copy in the rest. So two hosts are each
        //  told about a printed ticket, and neither consumes the other's copy.
        lock (_gate)
        {
            foreach (MachineLink link in _links)
            {
                if (Channels.Hears(link.Channel, code))
                    link.Exceptions.Add(new PendingEvent(code, data));
            }
        }
    }

    /// <summary>True when the host has asked for exceptions in the real-time event format.</summary>
    public bool RealTimeEventsEnabled
    {
        get
        {
            lock (_gate)
                return _rteEnabled;
        }
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
        (SasMeterCode countMeter, int denomination) = cents switch
        {
            100 => (SasMeterCode.TotalNumberOfUSD1BillsAccepted, 0),
            200 => (SasMeterCode.TotalNumberOfUSD2BillsAccepted, 1),
            500 => (SasMeterCode.TotalNumberOfUSD5BillsAccepted, 2),
            1000 => (SasMeterCode.TotalNumberOfUSD10BillsAccepted, 3),
            2000 => (SasMeterCode.TotalNumberOfUSD20BillsAccepted, 4),
            5000 => (SasMeterCode.TotalNumberOfUSD50BillsAccepted, 6),
            10000 => (SasMeterCode.TotalNumberOfUSD100BillsAccepted, 7),
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

        // A note taken goes into the stacker, and nothing here ever empties one: this machine has
        // no drop. So these two only ever rise, which is true of it and would not be true of a
        // machine somebody collects from.
        Meters.Increment(SasMeterCode.NumberOfBillsCurrentlyInTheStacker, 1);
        Meters.Increment(SasMeterCode.TotalValueOfBillsCurrentlyInTheStacker_Crd, credits);
        Meters.SetCurrentCredits(Meters.Read(SasMeterCode.CurrentCredits) + credits);

        lock (_gate)
        {
            _lastBillCountryCode = _options.BillCountryCode;
            _lastBillDenominationCode = (byte)denomination;
        }

        // Queued as the note exception carrying what the note was, which is what the machine
        // library does; the per-denomination code the host sees when reporting is off is worked
        // out on the way out. Nothing about the exception a host already sees changes — the same
        // code leaves on the same poll — and a host that has asked for reporting now gets the
        // country, the denomination and the count as well.
        RaiseEvent((byte)SasExceptionCode.BillAccepted,
        [
            Bcd(_options.BillCountryCode),
            Bcd((byte)denomination),
            .. BcdBytes(Meters.Read(countMeter), 4),
        ]);
    }

    /// <summary>One byte of binary coded decimal, the way the machine library writes one.</summary>
    private static byte Bcd(byte value) => (byte)((value % 10) | ((value / 10) << 4));

    /// <summary>A number as binary coded decimal, most significant byte first.</summary>
    private static byte[] BcdBytes(long value, int bytes)
    {
        byte[] digits = new byte[bytes];
        for (int i = bytes - 1; i >= 0; i--)
        {
            digits[i] = (byte)((value % 10) | (((value / 10) % 10) << 4));
            value /= 100;
        }

        return digits;
    }

    /// <summary>
    /// Cashes the machine out: every credit on it becomes one printed ticket, filed in the wallet
    /// the way a real machine's printer files one.
    /// </summary>
    /// <remarks>
    /// A cashout takes the whole credit meter rather than the last note put in, which is what a
    /// collect button does on a real machine and what the same script measured against one.
    /// The ticket is written into the same directory the host reads, so the two peers of D7 are
    /// answering the same question rather than two similar ones.
    /// </remarks>
    /// <param name="walletDirectory">Where the printed ticket is filed.</param>
    /// <returns>The ticket printed, or null when the machine had no credits to cash out.</returns>
    public Ticket? Cashout(string walletDirectory)
    {
        long credits = Meters.Read(SasMeterCode.CurrentCredits);
        if (credits <= 0)
            return null;

        long cents = credits * AccountingCents;
        Meters.SetCurrentCredits(0);
        Meters.Increment(SasMeterCode.TotalCashableTicketOut_Cents, cents);
        Meters.Increment(SasMeterCode.TotalSASCashableTicketOut_Qty, 1);

        // The same ticket counted again in the family the validation meter poll reads. A machine
        // moves both, and a fixture that moved only one would answer that poll with a zero its own
        // cashout had just disproved.
        Meters.Increment(SasMeterCode.RegularCashableTicketOut_Qty, 1);
        Meters.Increment(SasMeterCode.RegularCashableTicketOut_Cents, cents);

        // The same money in the other unit. A machine keeps both totals, and it is the pair that
        // makes the accounting denomination visible at all: the cents meter reads the same at
        // every denomination while this one moves with the unit.
        Meters.Increment(SasMeterCode.TotalTicketOut_Crd, credits);

        DateTime printedAt = DateTime.Now;
        uint number = (uint)Interlocked.Increment(ref _ticketsPrinted);
        var ticket = new Ticket
        {
            // Not a secure enhanced validation number, and not pretending to be one: this machine
            // has no validation system, so the number is its own and says so by being short.
            ValidationNumber = (ulong)(printedAt.Ticks % 100_000_000),
            ValidationType = TicketValidationType.CashableFromCashout,
            AmountCents = cents,
            TicketNumber = number,
            PrintedAt = printedAt,
            Note = "printed by the software machine",
        };

        Directory.CreateDirectory(walletDirectory);
        string path = Path.Combine(walletDirectory,
            $"{number:D6}{TicketFile.ExtensionFor(TicketEncoding.Json)}");

        // Two tickets can carry the same number - this machine is rebuilt for every test - so a
        // name already taken gets a suffix rather than overwriting what is there.
        int suffix = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(walletDirectory,
                $"{number:D6}-{suffix++}{TicketFile.ExtensionFor(TicketEncoding.Json)}");
        }

        TicketFile.Write(path, ticket, TicketEncoding.Json);

        //  What the cashout ticket information poll will answer with, recorded before the
        //  exception goes out so a host that reads the poll the instant it hears $3D is told
        //  about this ticket rather than the one before it.
        lock (_gate)
        {
            _lastValidationNumber = ticket.ValidationNumber;
            _lastTicketAmountCents = ticket.AmountCents;
        }

        RaiseException(SasExceptionCode.CashoutTicketPrinted);
        return ticket;
    }

    /// <summary>Where this machine's ticket redemption has got to, for a test that asks directly.</summary>
    public TicketRedemption Redemption => _redemption;

    /// <summary>
    /// Feeds a ticket into the acceptor, which the machine holds until a host says what it is.
    /// </summary>
    /// <remarks>
    /// The machine takes no money here and moves no meter. All it does is report that a document
    /// is in escrow; the credits arrive only when the host answers the redemption long poll, which
    /// is the whole point of the exchange and the reason a ticket cannot be tested at one end.
    /// </remarks>
    /// <returns>False when a ticket is already in flight, so this one is turned away.</returns>
    public bool InsertTicket(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        bool taken;
        lock (_gate)
            taken = _redemption.Insert(ticket);

        if (taken)
            RaiseException(SasExceptionCode.TicketInserted);

        return taken;
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

        // Both are meters, and the library reads them by code in its own handler for long poll
        // $18: games since the last reset, and games since the slot door last closed.
        Meters.Increment(SasMeterCode.GamesSinceLastReset, games);
        Meters.Increment(SasMeterCode.GamesSinceSlotDoorClosure, games);
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

        // A game start and a game end for each round, the way the platform above the machine
        // library raises them: unconditionally, whether or not the host has asked for reporting.
        // With reporting off a host sees the two bare codes it has always seen; with it on the
        // same two events carry what was staked and what was won.
        for (int round = 0; round < games; round++)
        {
            bool last = round == games - 1;
            RaiseEvent((byte)SasExceptionCode.GameStarted,
            [
                .. BcdBytes(Math.Min(betCredits, 9999), 2),
                .. BcdBytes(Meters.Read(SasMeterCode.TotalCoinInCredits), 4),
                WagerType(betCredits),
                _options.ProgressiveGroup,
            ]);

            // The whole win is attributed to the last round, because this fixture is told one
            // total rather than one figure per round. Stated here rather than divided evenly,
            // which would invent a distribution nobody asked for.
            RaiseEvent((byte)SasExceptionCode.GameEnded,
                BcdBytes(last ? winCredits * _options.CentsPerGameCredit : 0, 4));
        }
    }

    /// <summary>
    /// What kind of wager the game start event reports: the denomination a credit is worth, with
    /// a bit for a multi-game machine and a bit for a bet at the machine's limit.
    /// </summary>
    /// <remarks>
    /// The two bits are the platform's own construction rather than the machine library's. This
    /// fixture has one game, so the multi-game bit is always clear here, and that is a fact about
    /// the fixture rather than about the protocol.
    /// </remarks>
    private byte WagerType(long betCredits)
    {
        byte code = SasDenomination.CodeFor(_options.CentsPerGameCredit) ?? 0;
        return betCredits >= _options.MaxBet ? (byte)(code | 0x80) : code;
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

        // The one door the protocol keeps a count for, and the one whose closing starts the
        // games-since-door-closure figure again. Both are moved by this machine's own stimulus,
        // so this machine maintains them.
        if (door.Opened == SasExceptionCode.SlotDoorOpened)
        {
            if (open)
            {
                Meters.Increment(SasMeterCode.NumberSlotDoorOpened, 1);
            }
            else
            {
                Meters.Restart(SasMeterCode.GamesSinceSlotDoorClosure);
            }
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

    /// <summary>
    /// One host link: a transport, the channel it is, the address it answers at, and the queue of
    /// exceptions waiting to go out on it.
    /// </summary>
    /// <remarks>
    /// A machine serves up to four of these and they are genuinely independent. Each has its own
    /// exception buffer, which is the library's own arrangement and the reason two hosts can be
    /// told about the same thing without either of them consuming the other's copy — and the
    /// reason an exception belonging to a category a channel does not own never reaches it at all.
    /// The scratch buffers are per link because two links answer on two threads.
    /// </remarks>
    private sealed class MachineLink(ISasTransport transport, SasChannel channel, SasAddress address)
    {
        public ISasTransport Transport { get; } = transport;

        public SasChannel Channel { get; } = channel;

        public SasAddress Address { get; } = address;

        public ExceptionQueue Exceptions { get; } = new();

        /// <summary>When this link last saw a message, as a stopwatch timestamp; zero before the first.</summary>
        public long LastHeard { get; set; }

        /// <summary>True after a loop break, until a message to another address resynchronises it.</summary>
        public bool AwaitingSync { get; set; }

        /// <summary>True when the last message this link acted on was a general poll to itself.</summary>
        public bool LastWasOwnGeneralPoll { get; set; }

        public SasSymbol[] Incoming { get; } = new SasSymbol[HostMessage.MaxSymbols];

        public List<SasSymbol> Message { get; } = new(HostMessage.MaxSymbols);

        public SasSymbol[] Outbound { get; } = new SasSymbol[HostMessage.MaxSymbols];

        public byte[] Frame { get; } = new byte[HostMessage.MaxSymbols];

        public Thread? Thread { get; set; }
    }

    private void Loop(MachineLink link)
    {
        while (_running)
        {
            int read = link.Transport.Read(link.Incoming, TimeSpan.FromMilliseconds(10));
            if (read == 0)
            {
                Dispatch(link);
                continue;
            }

            for (int i = 0; i < read; i++)
            {
                SasSymbol symbol = link.Incoming[i];

                // The wakeup bit is the start of a message, which is the one framing signal SAS
                // gives. Splitting on it means two host messages arriving together are still
                // read as two.
                if (symbol.Wakeup)
                    Dispatch(link);
                else if (link.Message.Count == 0)
                    continue;

                link.Message.Add(symbol);
            }

            // A read takes everything the line was holding, so a read that did not fill the
            // buffer ends the message. Answering on that rather than on a timed silence keeps
            // this machine's turnaround off the clock: a timed wait is the one thing a loaded
            // machine stretches, and stretching it here makes a host look at fault.
            if (read < link.Incoming.Length)
                Dispatch(link);
        }

        Dispatch(link);
    }

    private void Dispatch(MachineLink link)
    {
        if (link.Message.Count == 0)
            return;

        byte first = link.Message[0].Data;
        int count = link.Message.Count;
        for (int i = 0; i < count; i++)
            link.Frame[i] = link.Message[i].Data;
        link.Message.Clear();

        bool own = count == 1
            ? first == (link.Address.Value | HostMessage.GeneralPollFlag)
            : first == link.Address.Value;
        if (!Admit(link, own))
            return;

        bool ownGeneralPoll = count == 1 && own;
        bool repeat = ownGeneralPoll && link.LastWasOwnGeneralPoll;
        link.LastWasOwnGeneralPoll = ownGeneralPoll;
        if (!ownGeneralPoll)
        {
            lock (_gate)
                link.Exceptions.Acknowledge();
        }

        if (_options.ResponseDelay > TimeSpan.Zero)
            Thread.Sleep(_options.ResponseDelay);

        if (count == 1)
        {
            HandlePoll(link, first, repeat);
            return;
        }

        if (first != link.Address.Value && first != SasAddress.Sync.Value)
            return;

        HandleLongPoll(link, link.Frame.AsSpan(0, count));
    }

    /// <summary>
    /// Applies the loop break: after a silence longer than the configured limit the exception last
    /// sent is refused, and polls to this machine are ignored until one to another address arrives.
    /// </summary>
    private bool Admit(MachineLink link, bool own)
    {
        long now = Stopwatch.GetTimestamp();
        bool lost = _options.LinkLossAfter > TimeSpan.Zero
                    && link.LastHeard != 0
                    && Stopwatch.GetElapsedTime(link.LastHeard, now) > _options.LinkLossAfter;
        link.LastHeard = now;

        if (lost)
        {
            lock (_gate)
                link.Exceptions.Refuse();
            link.AwaitingSync = true;
            link.LastWasOwnGeneralPoll = false;
        }

        if (!link.AwaitingSync)
            return true;
        if (own)
            return false;

        link.AwaitingSync = false;
        return true;
    }

    private void HandlePoll(MachineLink link, byte first, bool repeat)
    {
        if ((first & HostMessage.GeneralPollFlag) == 0)
            return;

        byte polled = (byte)(first & ~HostMessage.GeneralPollFlag);
        if (polled == 0 || polled != link.Address.Value)
            return;

        Interlocked.Increment(ref _generalPolls);

        bool realTime;
        PendingEvent? queued;
        lock (_gate)
        {
            realTime = _rteEnabled;
            queued = realTime ? link.Exceptions.Take() : link.Exceptions.NextForGeneralPoll(repeat);
        }

        if (realTime)
        {
            SendRealTimeEvent(link, queued);
            return;
        }

        if (queued is not { } event_)
        {
            Send(link, [SasSymbol.Data8(0x00)]);
            return;
        }

        // Four exceptions the machine library only ever sends in the event format. With reporting
        // off it dequeues one and then sends nothing at all — not the code, and not the
        // no-activity byte either — so this poll goes unanswered. Modelled rather than smoothed
        // over: a host that treats silence as "nothing queued" is being told something false, and
        // that is a property of the machine a test should be able to see.
        if (SasEventLayouts.RealTimeOnly.Contains(event_.Code))
        {
            lock (_gate)
                link.Exceptions.Acknowledge();
            return;
        }

        Send(link, [SasSymbol.Data8(WithoutReporting(event_))]);
    }

    /// <summary>
    /// What code a payload-carrying exception is reduced to when reporting is off.
    /// </summary>
    /// <remarks>
    /// Only the note exception changes: the library queues one <c>$4F</c> carrying the
    /// denomination and, on the way out, turns it into the exception for that denomination. Every
    /// other code goes out as itself with its payload dropped, which is why a host that never
    /// enables reporting cannot see what a game staked.
    /// </remarks>
    private static byte WithoutReporting(PendingEvent queued)
    {
        if (queued.Code != (byte)SasExceptionCode.BillAccepted || queued.Data.Length < 2)
            return queued.Code;

        // The denomination is carried as binary coded decimal, so it is read back as a number
        // before it selects the code, the way the library reads it.
        int denomination = ((queued.Data[1] >> 4) * 10) + (queued.Data[1] & 0x0F);
        return denomination switch
        {
            0 => (byte)SasExceptionCode.USD1BillAccepted,
            1 => (byte)SasExceptionCode.USD2BillAccepted,
            2 => (byte)SasExceptionCode.USD5BillAccepted,
            3 => (byte)SasExceptionCode.USD10BillAccepted,
            4 => (byte)SasExceptionCode.USD20BillAccepted,
            6 => (byte)SasExceptionCode.USD50BillAccepted,
            7 => (byte)SasExceptionCode.USD100BillAccepted,
            8 => (byte)SasExceptionCode.USD200BillAccepted,
            10 => (byte)SasExceptionCode.USD500BillAccepted,
            _ => (byte)SasExceptionCode.BillAccepted,
        };
    }

    /// <summary>
    /// Answers a general poll in the real-time event format: address, marker, code, payload, check
    /// sequence.
    /// </summary>
    /// <remarks>
    /// A machine with nothing queued still answers, and answers in the same format — the code is
    /// zero. That is what the library does, and it matters: a host that had to tell a bare
    /// <c>$00</c> from an event would be reading two formats on the same link.
    /// </remarks>
    private void SendRealTimeEvent(MachineLink link, PendingEvent? queued)
    {
        link.Frame[0] = link.Address.Value;
        link.Frame[1] = SasEventLayouts.Marker;
        link.Frame[2] = queued?.Code ?? 0x00;

        int length = SasEventLayouts.HeaderLength;
        if (queued is { } event_)
        {
            foreach (byte value in event_.Data)
                link.Frame[length++] = value;
        }

        ushort crc = SasCrc.Compute(link.Frame.AsSpan(0, length));
        link.Frame[length++] = SasCrc.LowByte(crc);
        link.Frame[length++] = SasCrc.HighByte(crc);

        for (int i = 0; i < length; i++)
            link.Outbound[i] = SasSymbol.Data8(link.Frame[i]);

        Send(link, link.Outbound.AsSpan(0, length));
    }

    private void HandleLongPoll(MachineLink link, ReadOnlySpan<byte> frame)
    {
        byte command = frame[1];
        Interlocked.Increment(ref _longPolls);
        LongPollReceived?.Invoke(command);

        // Two separate refusals, because a real machine has two. Any message longer than two
        // bytes is checked on arrival, and one that fails is refused whatever the command.
        if (frame.Length > 2 && !SasCrc.Verify(frame))
        {
            Reject(link);
            return;
        }

        // And a command whose handler checks the sequence itself refuses a bare two-byte poll,
        // because on a message that short the check reads the address and the command byte as
        // the sequence and cannot pass. Deciding this from the length instead was a defect here
        // that this host shared, so the two peers agreed and both were wrong.
        if (frame.Length <= 2 && SasCommandFraming.RequiresCheckSequence(command))
        {
            Reject(link);
            return;
        }

        //  What the library does at libsas_threads.cpp:3333 — while reporting is on and an
        //  exception is queued for this link, an addressed long poll is answered with the event
        //  and the command is never dispatched. Sited after the tests above so a malformed poll is
        //  still refused, and before the gate below so an event never leaves on a channel that
        //  owns no category. A broadcast poll is not addressed to this machine and is left alone.
        if (frame[0] == link.Address.Value)
        {
            PendingEvent? preempting;
            lock (_gate)
            {
                preempting = _rteEnabled ? link.Exceptions.Take() : null;
            }

            if (preempting is not null)
            {
                SendRealTimeEvent(link, preempting);
                return;
            }
        }

        //  The channel category gate, which is what makes two hosts on one machine safe. A
        //  channel that was not given a category behaves as though the category did not exist,
        //  and the library implements that by returning without sending anything — so this is
        //  silence and not a refusal, and the difference matters: a refusal would tell the host
        //  the command was understood and declined.
        if (!Channels.Answers(link.Channel, command))
            return;

        //  A command this machine has been built without. Silence rather than a rejection, for the
        //  same reason as below: a rejection says the command was understood and declined.
        if (_options.UnansweredCommands.Contains(command) || WithoutMachinery.Contains(command))
        {
            Ignore(link, command);
            return;
        }

        //  Section 15.5, as the library reads it: a machine performing secure enhanced or system
        //  validation does not answer the cashout ticket information poll at all. Silence rather
        //  than a rejection, because the two say different things - a rejection would tell the
        //  host the command was understood and refused, and this machine has not understood it.
        if (command == (byte)SasLongPollCode.LP_SENDCASHOUTTICKETINFO
            && _options.CashoutValidation != SasCashoutValidation.Standard)
        {
            return;
        }

        SasMessageLayout? layout = SasLayouts.For(command);
        if (layout is null)
        {
            // Known to the protocol but not modelled here, or not a command at all. Either way an
            // unsupported poll is ignored rather than refused: a refusal says the command was
            // understood and declined, and this machine has not understood it. Counted so that
            // our own gap stays visible in the run record without being announced on the wire.
            Ignore(link, command);
            return;
        }

        switch (layout.Shape)
        {
            case SasResponseShape.Acknowledgement:
                if (!ApplyAcknowledgedCommand(command, frame))
                {
                    Reject(link);
                    return;
                }

                Send(link, [SasSymbol.Data8(link.Address.Value)]);
                return;

            case SasResponseShape.None:
                return;

            case SasResponseShape.Data:
                SendData(link, layout, frame);
                return;

            default:
                // Unreachable: the three shapes above are the whole enum. Kept as silence rather
                // than a refusal so that a shape added later follows the same rule as any other
                // command this machine does not implement.
                Ignore(link, command);
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
                // Real-time event reporting takes one data byte, which must be zero or one. A
                // value that is neither is refused rather than rounded to true, because a machine
                // that accepted it would start answering in a format the host did not ask for.
                if (frame.Length < 3 || frame[2] is not (0x00 or 0x01))
                    return false;

                lock (_gate)
                    _rteEnabled = frame[2] == 0x01;

                return true;

            default:
                return false;
        }
    }

    private void SendData(MachineLink link, SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        Dictionary<string, object>? values = ValuesFor(layout, request);
        if (values is null)
        {
            // The layout says this command is answered with data and no values could be built for
            // it. That is this fixture's own gap, not a machine behaviour — what this machine is
            // deliberately built without is declared in WithoutMachinery and never reaches here —
            // so it stays a refusal, which is loud, rather than silence, which would look like a
            // machine that simply does not have the feature.
            Reject(link);
            return;
        }

        // A command this machine answers with silence rather than with a reply, because the
        // machine library does. The two are not interchangeable: a refusal says the command was
        // understood and declined, silence says it was never acted on.
        if (ReferenceEquals(values, NoAnswer))
            return;

        //  The enabled-features reply describes the machine but is answered on a channel, so what
        //  goes out is narrowed to the categories this channel owns. Done to the values rather
        //  than to the finished bytes because the fields here are named, and done at all because
        //  the library does it: a channel with no validation must not tell its host the machine
        //  redeems tickets.
        if (layout.Command == (byte)SasLongPollCode.LP_SENDENABLEFEATURES)
            MaskFeaturesForChannel(link.Channel, values);

        int length;
        try
        {
            length = SasMessageEncoder.WriteResponse(layout, link.Address, values, link.Frame);
        }
        catch (InvalidOperationException)
        {
            // A value that will not fit the field is this fixture's fault, not the host's, but
            // answering a malformed frame would blame the host for it.
            Reject(link);
            return;
        }

        for (int i = 0; i < length; i++)
            link.Outbound[i] = SasSymbol.Data8(link.Frame[i]);

        Send(link, link.Outbound.AsSpan(0, length));
    }

    /// <summary>
    /// Clears the enabled-feature bits this channel must not report.
    /// </summary>
    /// <remarks>
    /// The library masks three feature bytes. Two of them are single fields here; the first is a
    /// byte whose top four bits are named fields of their own, so it is composed, masked and taken
    /// apart again rather than masked field by field — which is what keeps the bits this build
    /// clears exactly the bits the library clears.
    /// </remarks>
    private void MaskFeaturesForChannel(SasChannel channel, Dictionary<string, object> values)
    {
        Span<byte> features =
        [
            (byte)(Byte(values, "feature_codes_1")
                   | (byte)(Byte(values, "validation_style") << 5)
                   | (byte)(Byte(values, "validation_extensions") << 4)
                   | (byte)(Byte(values, "ticket_redemption") << 7)),
            Byte(values, "feature_codes_2"),
            Byte(values, "feature_codes_3"),
        ];

        Channels.MaskFeatures(channel, features);

        values["feature_codes_1"] = (byte)(features[0] & 0x0F);
        values["validation_style"] = (byte)((features[0] >> 5) & 0x03);
        values["validation_extensions"] = (byte)((features[0] >> 4) & 0x01);
        values["ticket_redemption"] = (byte)((features[0] >> 7) & 0x01);
        values["feature_codes_2"] = features[1];
        values["feature_codes_3"] = features[2];

        static byte Byte(Dictionary<string, object> values, string name) =>
            values.TryGetValue(name, out object? value) ? Convert.ToByte(value) : (byte)0;
    }

    private void Reject(MachineLink link)
    {
        Interlocked.Increment(ref _rejections);
        Send(link, [SasSymbol.Data8((byte)(link.Address.Value | HostMessage.GeneralPollFlag))]);
    }

    /// <summary>
    /// An unsupported poll: nothing goes out, and the command is remembered.
    /// </summary>
    /// <remarks>
    /// The test scripts are explicit that an unsupported poll is ignored, and the machine library
    /// does ignore one — an undefined command has no entry in its framing table, so its receiver
    /// reads a length byte that never arrives and drops the packet. Refusing instead would make
    /// this fixture the only one of the three that answers, and a case asserting the silence the
    /// documents ask for would fail against our own peer and pass against a real machine.
    /// <para>
    /// The gap is counted rather than announced, so "this machine was asked for something it does
    /// not implement" is still answerable from the run record — which is what the refusal was
    /// really there for.
    /// </para>
    /// </remarks>
    private void Ignore(MachineLink link, byte command)
    {
        Interlocked.Increment(ref _ignored);
        _ = link;
        lock (_ignoredCommands)
            _ignoredCommands.Add(command);
    }

    private static void Send(MachineLink link, ReadOnlySpan<SasSymbol> symbols) =>
        link.Transport.Write(symbols);
}
