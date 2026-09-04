using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;

namespace SasWorkbench.Egm;

/// <summary>
/// Drives the software gaming machine peer directly, in the same process.
/// </summary>
/// <remarks>
/// Only the stimuli the peer actually implements are offered. A button press, for instance, needs
/// the peripheral simulator, so asking for one here produces a skip naming the missing
/// capability rather than a quiet no-op followed by an assertion that passes for the wrong reason.
/// </remarks>
public sealed class SoftwareMachineStimulus(FakeEgm machine) : IStimulusBackend
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

            case DoorStep { Door: "slot" } door:
                if (door.Open)
                    machine.OpenSlotDoor();
                else
                    machine.CloseSlotDoor();
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
        (step, options) =>
        {
            if (step.Transport != ScriptTransport.Loopback)
                return DefaultEnvironment.Create(step, options);

            var bus = new LoopbackBus();
            FakeEgmOptions settings = (machineOptions ?? new FakeEgmOptions()) with
            {
                Address = step.Address,
                Channel = step.Channel,
            };

            var machine = new FakeEgm(bus.Egm, settings);
            machine.Start();

            var stimulus = new SoftwareMachineStimulus(machine);
            return new ScriptEnvironment(
                bus.Host,
                stimulus,
                stimulus.Capabilities,
                new Composite(machine, bus));
        };

    private sealed class Composite(params IDisposable[] parts) : IDisposable
    {
        public void Dispose()
        {
            foreach (IDisposable part in parts)
                part.Dispose();
        }
    }
}
