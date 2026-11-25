using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>
/// Drives the software gaming machine peer directly, in the same process.
/// </summary>
/// <remarks>
/// Only the stimuli the peer actually implements are offered. A coin, for instance, needs a coin
/// acceptor the platform does not have, so asking for one here produces a skip naming the missing
/// capability rather than a quiet no-op followed by an assertion that passes for the wrong reason.
/// Doors are modelled by the protocol door rather than by a physical switch, which is what lets
/// one script run against both this peer and a real machine.
/// </remarks>
public sealed class SoftwareMachineStimulus(FakeEgm machine, string walletDirectory) : IStimulusBackend
{
    public StimulusBackend Kind => StimulusBackend.Software;

    public IReadOnlyDictionary<string, string?> Capabilities =>
        CapabilityProbe.ForStimulus(StimulusBackend.Software);

    //  The named machine is ignored, and that is safe rather than sloppy: this backend drives one
    //  machine, and 'host machine' will not accept an address outside the host's round, so every
    //  address a case can name here is one of this machine's own channel addresses.
    public void Apply(StimulusStep step, SasAddress? named = null)
    {
        switch (step)
        {
            case InsertBillStep bill:
                try
                {
                    machine.InsertBill(bill.Cents);
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw new StimulusUnavailableException(
                        $"the protocol has no bill-accepted exception for {bill.Cents} cents");
                }

                return;

            //  Handed straight to the machine rather than written into the acceptor directory.
            //  There is no peripheral manager here to watch one, and a directory in the middle
            //  would only add a way for the step to look done while nothing happened.
            case InsertTicketStep insert:
                if (!machine.InsertTicket(insert.Ticket))
                    throw new StimulusUnavailableException(
                        "the machine is already holding a ticket, so this one cannot go in until "
                        + "the host has finished with that one");

                return;

            case PlayGamesStep play:
                try
                {
                    machine.PlayGames(play.Games, play.BetCredits, play.WinCredits);
                }
                catch (InvalidOperationException notEnoughCredit)
                {
                    throw new StimulusUnavailableException(notEnoughCredit.Message);
                }

                return;

            // Settling shuts whatever is open and clears every peripheral fault. On a machine that
            // starts shut and healthy this does nothing at all, which is the honest outcome rather
            // than a reason to leave the step unsupported.
            case SettleStep:
                machine.Settle();
                return;

            //  A collect on an empty machine prints nothing,
            //  which is what a real one does too, so it is not an error - the assertion waiting
            //  for a ticket is what reports it, and reports it as the machine having no money on
            //  it rather than as a button that does not exist.
            case PressButtonStep press when press.Button is "collect":
                machine.Cashout(walletDirectory);
                return;

            case SlowCashoutStep slow:
                Refusable(slow, () => machine.CashoutLasting(walletDirectory, slow.Lasting));
                return;

            case StartGameStep start:
                Refusable(start, () => machine.StartGame(start.BetCredits));
                return;

            case EndGameStep end:
                Refusable(end, () => machine.EndGame(end.WinCredits));
                return;

            case ComponentChangeStep:
                if (machine.ChangeComponent() is { } noComponents)
                    throw new StimulusUnavailableException($"the software machine cannot do '{step.Describe()}': {noComponents}");

                return;

            case SelectGameStep select:
                Unless(select, machine.SelectGame(select.Game));
                return;

            case SelectDenominationStep select:
                Unless(select, machine.SelectDenomination(select.Cents));
                return;

            case SetGameEnabledStep game:
                Unless(game, machine.SetGameEnabled(game.Game, game.Enabled));
                return;

            case MenuStep menu:
                Unless(menu, machine.SetMenu(menu.Menu, menu.Enter));
                return;

            case SetPollAddressStep address:
                Unless(address, machine.SetPollAddress(address.Address));
                return;

            case SetJackpotLimitStep jackpot:
                machine.SetJackpotLimit(jackpot.Cents);
                return;

            case PressButtonStep press when press.Button is "service":
                machine.PressService();
                return;

            case PeripheralFaultStep fault:
                string? why = fault.Device == PeripheralDevice.Note
                    ? machine.NoteFault(fault.Fault)
                    : machine.PrinterFault(fault.Fault);
                if (why is not null)
                    throw new StimulusUnavailableException($"the software machine cannot do '{fault.Describe()}': {why}");

                return;

            case HandpayStep handpay:
                try
                {
                    machine.RaiseHandpay(handpay.Kind, handpay.AmountCents);
                }
                catch (Exception refused) when (refused is InvalidOperationException
                                                    or ArgumentOutOfRangeException)
                {
                    throw new StimulusUnavailableException(
                        $"the software machine cannot owe '{handpay.Describe()}': {refused.Message.Split('(')[0].Trim()}");
                }

                return;

            // The software machine keeps what the library and the platform keep across a restart, and nothing else.
            case PowerCycleStep:
                machine.PowerCycle();
                return;

            case MemoryFaultStep memory:
                if (machine.MemoryFault(memory.Kind) is { } noReport)
                    throw new StimulusUnavailableException($"the software machine cannot do '{memory.Describe()}': {noReport}");

                return;

            case NvramClearStep:
                machine.ClearMemory();
                return;

            case ProgressiveWinStep win:
                try
                {
                    machine.WinProgressive(win.Levels, win.BetCredits, win.WinCredits);
                }
                catch (InvalidOperationException refused)
                {
                    throw new StimulusUnavailableException($"the software machine cannot do '{win.Describe()}': {refused.Message}");
                }

                return;

            case HandpayResetStep:
                if (!machine.ResetHandpay())
                    throw new StimulusUnavailableException(
                        "the software machine holds no hand pay, so there is nothing to reset");

                return;

            // Refused by every other backend, and honoured here only because this machine is one
            // this build made. Values already counted stay in the unit they were counted in.
            case SetDenominationStep set:
                if (machine.SetAccountingDenomination(set.Code) is { } refusal)
                    throw new StimulusUnavailableException(refusal);

                return;

            case SetAssetNumberStep asset:
                machine.SetAssetNumber(asset.Asset);
                return;

            case SetAftBonusStep bonus:
                machine.SetAftBonus(bonus.On);
                return;

            case SetAftStep aft:
                machine.SetAft(aft.On);
                return;

            case SetCreditLimitStep limit:
                machine.SetCreditLimit(limit.Cents);
                return;

            case SetLegacyBonusStep legacy:
                machine.SetLegacyBonus(legacy.On);
                return;

            case AftRegisterStep:
                if (machine.AttendantRegister() is { } menuRefusal)
                    throw new StimulusUnavailableException(menuRefusal);

                return;

            case DoorStep door:
                try
                {
                    machine.SetDoor(door.Door, door.Open);
                }
                catch (ArgumentOutOfRangeException unknown)
                {
                    throw new StimulusUnavailableException(
                        $"the software machine has no door called '{door.Door}'. "
                        + unknown.Message.Split('(')[0].Trim());
                }

                return;

            default:
                throw new StimulusUnavailableException(
                    $"the software machine cannot do '{step.Describe()}' "
                    + $"(needs {step.RequiredCapability})");
        }
    }

    /// <summary>Reports a refusal the machine gave as a reason, when it gave one.</summary>
    private static void Unless(StimulusStep step, string? refusal)
    {
        if (refusal is not null)
            throw new StimulusUnavailableException($"the software machine cannot do '{step.Describe()}': {refusal}");
    }

    /// <summary>Runs a stimulus the machine may refuse in its present state, reporting the refusal as one.</summary>
    private static void Refusable(StimulusStep step, Action act)
    {
        try
        {
            act();
        }
        catch (InvalidOperationException refused)
        {
            throw new StimulusUnavailableException($"the software machine cannot do '{step.Describe()}': {refused.Message}");
        }
    }
}

/// <summary>
/// Drives one named machine of a software floor.
/// </summary>
/// <remarks>
/// <para>
/// What W3 refused to do, now that a case can say which machine it means. A floor has no "the
/// machine", so a step that names none is skipped rather than applied to the head of the round —
/// a case that passed having stimulated a machine it never named is the worst outcome available,
/// because nothing in the report would look wrong.
/// </para>
/// <para>
/// The wallet is the run's and the whole floor prints into it, because the assertions watch one
/// directory. That is stated rather than hidden: a case that cashes out two machines and then asks
/// for "a ticket" gets whichever was filed first, and must tell them apart by what is on them.
/// </para>
/// </remarks>
public sealed class SoftwareFloorStimulus : IStimulusBackend
{
    private readonly Dictionary<SasAddress, SoftwareMachineStimulus> _machines = [];
    private readonly IReadOnlyList<SasAddress> _round;

    public SoftwareFloorStimulus(SoftwareFloor floor, string walletDirectory)
    {
        ArgumentNullException.ThrowIfNull(floor);

        foreach (FakeEgm machine in floor.Machines)
        {
            var driver = new SoftwareMachineStimulus(machine, walletDirectory);

            //  One machine answers at one address per channel, and all of them reach the same
            //  machine, so every address it opens is a way to name it.
            foreach (SasChannel channel in machine.Channels.Open)
            {
                if (machine.Channels.AddressOf(channel) is { } address)
                    _machines[address] = driver;
            }
        }

        _round = floor.Addresses;
    }

    public StimulusBackend Kind => StimulusBackend.Software;

    public IReadOnlyDictionary<string, string?> Capabilities =>
        CapabilityProbe.ForStimulus(StimulusBackend.Software);

    public void Apply(StimulusStep step, SasAddress? machine = null)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (machine is not { } address)
        {
            throw new StimulusUnavailableException(
                $"this host polls {_round.Count} machines and the case has not said which one "
                + $"'{step.Describe()}' is for. Add 'host machine <hh>' first: "
                + $"the round is {SasAddressRound.Describe(_round)}.");
        }

        if (!_machines.TryGetValue(address, out SoftwareMachineStimulus? driver))
        {
            throw new StimulusUnavailableException(
                $"no machine on this floor answers at ${address}. "
                + $"The round is {SasAddressRound.Describe(_round)}.");
        }

        driver.Apply(step, address);
    }
}

/// <summary>
/// Builds an environment in which the machine under test is the software peer in this repository.
/// </summary>
/// <remarks>
/// Useful for fast tests and for trying a script out, and honest about what it proves: both ends
/// are our own code, so a pass says the two agree and nothing more. A link named as anything
/// other than loopback is left alone and opened for real, because a script that asks for a
/// machine over TCP means the machine over TCP.
/// </remarks>
public static class SoftwareMachineEnvironment
{
    /// <summary>An environment factory that answers loopback links with the software machine.</summary>
    public static EnvironmentFactory For(FakeEgmOptions? machineOptions = null) =>
        (step, options, declared, channels, validation, progressive, authentication, multipliedJackpot, handpay, games) =>
        {
            if (step.Transport != ScriptTransport.Loopback)
                return DefaultEnvironment.Create(
                    step, options, declared, channels, validation, progressive, authentication, multipliedJackpot, handpay, games);

            //  A case that names a round gets a floor of machines on one line rather than one
            //  machine. It is the other axis, so what it can do is different and is not smoothed
            //  over: see Floor below for what a floor deliberately does not provide.
            if (step.Round.Count > 1)
                return Floor(step, options, channels);

            var bus = new LoopbackBus();
            FakeEgmOptions settings = (machineOptions ?? new FakeEgmOptions()) with
            {
                Address = step.Address,
                Channel = step.Channel,

                // Receipts go beside the wallet rather than in it, so no ticket reader mistakes one for a ticket.
                ReceiptDirectory = Path.Combine(options.WalletDirectory, "receipts"),
                WalletDirectory = options.WalletDirectory,
            };

            //  A case that says how the machine is divided between hosts gets a machine divided
            //  that way. An arrangement no machine could hold is not silently repaired: the
            //  machine is built on its one channel instead and carries the refusal, so the case
            //  reports what the machine would not accept rather than passing against a machine in
            //  a configuration nobody asked for.
            if (channels is not null)
            {
                if (channels.TryResolve(out SasChannelPlan plan, out string? refusal))
                    settings = settings with { Channels = plan };
                else
                    settings = settings with { ChannelRefusal = refusal };
            }

            // A case that says what unit it assumes gets a machine in that unit. Nothing is
            // asserted here about whether the machine accepted it - section 16 may refuse the
            // value, and if it does the machine publishes what it fell back to and the runner
            // reads that off the wire like it would from any other machine.
            if (declared is not null)
            {
                settings = settings with
                {
                    AccountingDenomination = declared.Code,
                    PlayerDenominationsInCents = declared.PlayerDenominationsInCents,
                };
            }

            // A case that says which validation style it assumes gets a machine performing it, as an attendant would set it before the test.
            if (validation is not null)
                settings = settings with { CashoutValidation = validation.Style };

            // A case that says which progressive levels it assumes gets a machine with them, as the operator and the game would set them.
            if (progressive is not null)
            {
                settings = settings with
                {
                    ProgressiveGroup = progressive.Group,
                    SasProgressiveLevels = progressive.SasLevels,
                    NonSasProgressiveLevels = progressive.NonSasLevels,
                };
            }

            // A case that says the machine takes part in component authentication gets one that does, as the platform would be started.
            if (authentication is not null)
                settings = settings with { ComponentAuthentication = true };

            // A case that says the machine multiplies jackpots gets one that does.
            if (multipliedJackpot is not null)
                settings = settings with { MultipliedJackpot = true };

            // A case that says how the machine reports and validates hand pays gets one that does, as set at a memory reset.
            if (handpay is not null)
                settings = settings with { LegacyHandpayReporting = handpay.Legacy, HandpayValidation = handpay.Validation };

            // A case that says which games the machine holds gets them; its meters count in cents, as the platform's do,
            // unless the case says otherwise (QSP SasNetwork.cpp:1258).
            if (games is not null)
            {
                settings = settings with { GameDenominationsInCents = games.DenominationsInCents };
                if (declared is null)
                    settings = settings with { AccountingDenomination = 0x01 };
                else if (declared.PlayerDenominationsInCents.Count == 0)
                    settings = settings with { PlayerDenominationsInCents = games.DenominationsInCents };
            }

            var machine = new FakeEgm(bus.Egm, settings);
            machine.Start();

            //  The wallet comes from the run rather than from here, so the directory this machine
            //  prints into is by construction the one the host reads.
            var stimulus = new SoftwareMachineStimulus(machine, options.WalletDirectory);

            //  What this machine is configured to do is deliberately not published here. It
            //  answers the enabled-features poll like any machine, and the runner asks it there —
            //  so the same question reaches both peers the same way, and a disagreement between
            //  them is a finding rather than an artefact of one being asked and the other told.
            //  A second host on this machine is a second loopback bus attached to the same
            //  machine — not a second machine, which is what building the environment twice would
            //  produce and which would make a two-channel case meaningless.
            var extra = new List<LoopbackBus>();

            return new ScriptEnvironment(
                bus.Host,
                stimulus,
                stimulus.Capabilities,
                new Composite(machine, bus, new Closing(extra)))
            {
                Channels = machine.Channels,
                ChannelRefusal = machine.ChannelRefusal,
                //  One capability per channel this machine actually opens, because this
                //  environment built the machine and so can answer it. It is reported rather than
                //  required: what decides a two-channel case is whether the second link opens.
                ExtraCapabilities = stimulus.Capabilities
                    .Concat(machine.Channels.Open.Select(channel =>
                        new KeyValuePair<string, string?>(
                            MachineChannelDeclaration.CapabilityFor(channel), "yes")))
                    .Concat(NonSasProgressives(progressive))
                    .Concat(Handpays(settings))
                    .ToDictionary(pair => pair.Key, pair => pair.Value),
                OpenChannel = another =>
                {
                    var second = new LoopbackBus();
                    extra.Add(second);
                    machine.Attach(second.Egm, another.Channel);
                    return second.Host;
                },
            };
        };

    /// <summary>
    /// How this machine reports and validates hand pays, which it claims because it was built that way: no poll
    /// reports either. The queue holds sixteen, as the library's does (HANDPAY_BUFFER_LENGTH, libsas_db.h:72).
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string?>> Handpays(FakeEgmOptions settings)
    {
        if (settings.LegacyHandpayReporting)
        {
            yield return new(MachineHandpayDeclaration.LegacyCapability, "yes");
        }
        else
        {
            yield return new(MachineHandpayDeclaration.QueueCapability, "yes");
            yield return new(MachineHandpayDeclaration.QueueSizeCapability,
                HandpayQueue.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (settings.HandpayValidation)
            yield return new(MachineHandpayDeclaration.ValidationCapability, "yes");
    }

    /// <summary>
    /// The levels this machine runs itself, which it claims because it was built with them: they
    /// report group zero on every poll, so no machine can be asked for them.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string?>> NonSasProgressives(MachineProgressiveDeclaration? progressive) =>
        progressive is { NonSasLevels: not 0 }
            ? [new(MachineProgressiveDeclaration.NonSasCapability, MachineProgressiveDeclaration.DescribeLevels(progressive.NonSasLevels))]
            : [];

    /// <summary>
    /// A floor of software machines, one multi-drop line per channel, for a case that names a
    /// round.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both axes at once. A round splits one host between machines and a channel splits one
    /// machine between hosts; the two meet as a line per channel, because a channel is a port in
    /// the machine and two hosts on one line would be two hosts transmitting on one pair.
    /// </para>
    /// <para>
    /// A case that names no arrangement gets a floor of one-channel machines, which is every case
    /// written before both axes existed.
    /// </para>
    /// </remarks>
    private static ScriptEnvironment Floor(
        ConnectStep step, ScriptRunnerOptions options, MachineChannelDeclaration? channels)
    {
        SoftwareFloor floor = channels is null
            ? SoftwareFloor.Of(step.Round.Count, step.Round[0])
            : FromDeclaration(step, channels);

        //  A host polling machines its line does not carry hears silence on every one of them, and
        //  a partial mismatch is worse: some machines answer and the floor reads as one with holes
        //  in it. Refused with both rounds named.
        IReadOnlyList<SasAddress> carried = floor.RoundOf(step.Channel);
        if (!carried.SequenceEqual(step.Round))
        {
            floor.Dispose();
            throw new TransportRefusedException(
                $"this host polls {SasAddressRound.Describe(step.Round)} on channel "
                + $"{(int)step.Channel}, and that line carries "
                + (carried.Count == 0
                    ? "no machines at all"
                    : SasAddressRound.Describe(carried))
                + ". A host polling machines its line does not carry hears silence from every one "
                + "of them.");
        }

        var stimulus = new SoftwareFloorStimulus(floor, options.WalletDirectory);

        return new ScriptEnvironment(
            floor.Host, stimulus, stimulus.Capabilities, floor)
        {
            //  Reported for the first machine, because that is the arrangement the host's
            //  provenance describes and every machine on this floor is alike in everything but
            //  its addresses.
            Channels = floor.Machines[0].Channels,

            ExtraCapabilities = stimulus.Capabilities
                .Concat(floor.Channels.Select(channel =>
                    new KeyValuePair<string, string?>(
                        MachineChannelDeclaration.CapabilityFor(channel), "yes")))
                .ToDictionary(pair => pair.Key, pair => pair.Value),

            //  A second host on a floor is the other line, not a second engine on this one. The
            //  line already exists — the machines were built with an end on it — so this hands
            //  back its host end rather than opening anything new, and the two hosts' rounds run
            //  at once on independent wires instead of contending for one.
            OpenChannel = another => Line(floor, another),
        };
    }

    /// <summary>
    /// The host end of the line a second host wants, checked against what that line carries.
    /// </summary>
    private static ISasTransport Line(SoftwareFloor floor, ConnectStep step)
    {
        if (floor.HostFor(step.Channel) is not { } host)
        {
            throw new TransportRefusedException(
                $"no machine on this floor opens channel {(int)step.Channel}, so there is no line "
                + "for a host to poll. This floor carries "
                + string.Join(", ", floor.Channels.Select(c => $"channel {(int)c}")) + ".");
        }

        IReadOnlyList<SasAddress> carried = floor.RoundOf(step.Channel);
        IReadOnlyList<SasAddress> wanted = step.Round.Count > 0 ? step.Round : [step.Address];
        if (!carried.SequenceEqual(wanted))
        {
            throw new TransportRefusedException(
                $"this host polls {SasAddressRound.Describe(wanted)} on channel "
                + $"{(int)step.Channel}, and that line carries "
                + SasAddressRound.Describe(carried)
                + ". A host polling machines its line does not carry hears silence from every one "
                + "of them.");
        }

        return host;
    }

    /// <summary>
    /// Builds the floor a case's arrangement describes: one machine per entry of the rounds, each
    /// with its own address on every channel.
    /// </summary>
    private static SoftwareFloor FromDeclaration(
        ConnectStep step, MachineChannelDeclaration channels)
    {
        if (!channels.TryCountMachines(out int count, out string? mismatch))
            throw new TransportRefusedException(mismatch!);

        if (count != step.Round.Count)
        {
            throw new TransportRefusedException(
                $"this host polls {step.Round.Count} machine(s) and the arrangement describes "
                + $"{count}. A floor is one set of machines, however many channels it is divided "
                + "between.");
        }

        var plans = new List<SasChannelPlan>(count);
        for (int index = 0; index < count; index++)
        {
            //  An arrangement no machine could hold is refused for the whole floor rather than
            //  built for some machines and not others: a floor half in a configuration nobody
            //  asked for is worse to read than one that would not come up.
            if (!channels.TryResolveFor(index, out SasChannelPlan plan, out string? refusal))
                throw new TransportRefusedException(refusal!);

            plans.Add(plan);
        }

        return SoftwareFloor.Of(plans);
    }

    /// <summary>Closes the extra buses a second and third host needed.</summary>
    private sealed class Closing(List<LoopbackBus> buses) : IDisposable
    {
        public void Dispose()
        {
            foreach (LoopbackBus bus in buses)
                bus.Dispose();
        }
    }

    private sealed class Composite(params IDisposable[] parts) : IDisposable
    {
        public void Dispose()
        {
            foreach (IDisposable part in parts)
                part.Dispose();
        }
    }
}
