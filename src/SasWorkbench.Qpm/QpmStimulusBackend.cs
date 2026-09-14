using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Tickets;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Qpm;

/// <summary>
/// Drives the gaming machine's peripherals, unattended, through the shared region.
/// </summary>
/// <remarks>
/// What this backend offers is what the machine says it has. Doors and buttons come from the
/// machine's own configuration, so a machine with no note acceptor configured offers no note
/// stimulus and the tests that need one are skipped by name. Nothing is offered on the strength
/// of the peripheral simulator's labels, which describe a different machine.
/// </remarks>
public sealed class QpmStimulusBackend(QpmMachine machine, string? acceptorDirectory = null)
    : IStimulusBackend, IDisposable
{
    private readonly TicketAcceptor _acceptor =
        new(acceptorDirectory ?? TicketAcceptor.DefaultDirectory);

    public StimulusBackend Kind => StimulusBackend.Qpm;

    /// <summary>The machine being driven, for a caller that wants its lamps or its profile.</summary>
    public QpmMachine Machine { get; } = machine;

    public IReadOnlyDictionary<string, string?> Capabilities => Describe();

    //  The named machine is ignored: the peripheral simulator drives the one cabinet it is
    //  attached to, and a host bound to it polls one address, so every address a case can name
    //  here is that machine's own.
    public void Apply(StimulusStep step, SasAddress? named = null)
    {
        ArgumentNullException.ThrowIfNull(step);

        try
        {
            Carry(step);
        }
        catch (QpmUnavailableException unavailable)
        {
            throw new StimulusUnavailableException(unavailable.Reason);
        }
    }

    public void Dispose() => Machine.Dispose();

    private void Carry(StimulusStep step)
    {
        switch (step)
        {
            case SettleStep:
                Machine.Settle();
                return;

            case InsertBillStep bill:
                Machine.InsertNote(bill.Cents);
                return;

            // A ticket does not go through the shared region: it has a barcode and an amount, and
            // the region carries eight note-value bits and nothing else. It is dropped into the
            // acceptor directory instead, which is the same directory a person uses when they load
            // a ticket by hand in the peripheral simulator - one path, so what is demonstrated and
            // what is tested cannot come apart.
            case InsertTicketStep insert:
                try
                {
                    _acceptor.Offer(insert.Ticket);
                }
                catch (Exception problem) when (problem is IOException
                                                    or UnauthorizedAccessException)
                {
                    throw new StimulusUnavailableException(
                        $"the ticket could not be put into {_acceptor.Directory}: {problem.Message}");
                }

                return;

            case DoorStep door:
                if (!Machine.Profile.TryFindDoor(door.Door, out EgmDoor found))
                    throw new StimulusUnavailableException(WhyNoDoor(door.Door));

                Machine.SetDoor(found, door.Open);
                return;

            case PressButtonStep press:
                if (!Machine.Profile.TryFindButton(press.Button, out EgmButton button))
                    throw new StimulusUnavailableException(WhyNoButton(press.Button));

                Machine.PressButton(button);
                return;

            // Playing a game through the button deck would need the machine to have credit, a bet
            // selected and no condition inhibiting play, and its outcome is decided by a
            // free-running generator, so a step asking for two games and a win of fifty cannot be
            // honoured by pressing anything. The host's own auto-rebet long poll is the lever that
            // makes a machine play by itself.
            case PlayGamesStep play:
                throw new StimulusUnavailableException(
                    $"'{play.Describe()}' cannot be driven through the peripherals: the machine "
                    + "decides its own outcomes, so a bet and a win cannot be asked for from "
                    + "outside");

            default:
                throw new StimulusUnavailableException(
                    $"the peripheral driver cannot do '{step.Describe()}' "
                    + $"(needs {step.RequiredCapability})");
        }
    }

    private string WhyNoDoor(string name)
    {
        if (Machine.Profile.UnavailableBecause is { } reason)
            return $"which door '{name}' is cannot be known: {reason}";

        return $"the machine declares no door called '{name}'. It has: "
            + string.Join(", ", Machine.Profile.DoorNames);
    }

    private string WhyNoButton(string name)
    {
        if (Machine.Profile.UnavailableBecause is { } reason)
            return $"which button '{name}' is cannot be known: {reason}";

        return $"the machine declares no button called '{name}'. It has: "
            + string.Join(", ", Machine.Profile.ButtonNames);
    }

    private Dictionary<string, string?> Describe()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["stimulus"] = "qpm",
            ["stimulus.settle"] = "yes",

            // The note acceptor's mask is part of the shared region itself, so it exists whenever
            // the region does. Whether the machine will do anything with a note is a question
            // about its configuration, and it answers that on the wire.
            ["stimulus.bill"] = "yes",
        };

        // Only when there is somewhere to put one. Whether the machine will do anything with a
        // ticket is its own configuration to answer, on the wire.
        foreach ((string key, string? value) in CapabilityProbe.ForAcceptor(_acceptor.Directory))
            values[key] = value;

        // Named one by one as well, so a test that needs a twenty can require exactly that rather
        // than requiring notes in general and failing on a machine that reads a different set.
        foreach (int cents in QpmLayout.NoteValues)
            values[$"stimulus.bill.{cents}"] = "yes";

        foreach (string door in Machine.Profile.DoorNames)
            values[$"stimulus.door.{door}"] = "yes";

        foreach (string button in Machine.Profile.ButtonNames)
            values[$"stimulus.button.{button}"] = "yes";

        return values;
    }
}
