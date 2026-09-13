using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>
/// Drives the software gaming machine peer directly, in the same process.
/// </summary>
/// <remarks>
/// Only the stimuli the peer actually implements are offered. A button press, for instance, needs
/// the peripheral simulator, so asking for one here produces a skip naming the missing
/// capability rather than a quiet no-op followed by an assertion that passes for the wrong reason.
/// Doors are modelled by the protocol door rather than by a physical switch, which is what lets
/// one script run against both this peer and a real machine.
/// </remarks>
public sealed class SoftwareMachineStimulus(FakeEgm machine, string walletDirectory) : IStimulusBackend
{
    public StimulusBackend Kind => StimulusBackend.Software;

    public IReadOnlyDictionary<string, string?> Capabilities =>
        CapabilityProbe.ForStimulus(StimulusBackend.Software);

    public void Apply(StimulusStep step)
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

            // Nothing on this machine can be disconnected, so settling it means shutting whatever
            // it has open. On a machine that starts shut this does nothing at all, which is the
            // honest outcome rather than a reason to leave the step unsupported.
            case SettleStep:
                machine.Settle();
                return;

            //  The only button this machine has. A collect on an empty machine prints nothing,
            //  which is what a real one does too, so it is not an error - the assertion waiting
            //  for a ticket is what reports it, and reports it as the machine having no money on
            //  it rather than as a button that does not exist.
            case PressButtonStep press when press.Button is "collect":
                machine.Cashout(walletDirectory);
                return;

            // Refused by every other backend, and honoured here only because this machine is one
            // this build made. Values already counted stay in the unit they were counted in.
            case SetDenominationStep set:
                if (machine.SetAccountingDenomination(set.Code) is { } refusal)
                    throw new StimulusUnavailableException(refusal);

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
        (step, options, declared, channels) =>
        {
            if (step.Transport != ScriptTransport.Loopback)
                return DefaultEnvironment.Create(step, options, declared, channels);

            var bus = new LoopbackBus();
            FakeEgmOptions settings = (machineOptions ?? new FakeEgmOptions()) with
            {
                Address = step.Address,
                Channel = step.Channel,
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
