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
/// <param name="machine">The machine's shared region and profile.</param>
/// <param name="acceptorDirectory">Where tickets are offered, or null for the usual place.</param>
/// <param name="demo">The executable the run declared, which power cycles and window keys need; null when none was.</param>
/// <param name="betCredits">The credits one bet multiplier wagers, which plays need; null when the run did not say.</param>
/// <param name="outcomes">Where a scripted build of the machine takes its outcomes; null when there is none to use.</param>
public sealed partial class QpmStimulusBackend(
    QpmMachine machine,
    string? acceptorDirectory = null,
    DemoProcess? demo = null,
    long? betCredits = null,
    ScriptedOutcomes? outcomes = null)
    : IHostAwareStimulusBackend, IDisposable
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
    public void Apply(StimulusStep step, SasAddress? named = null) => Apply(step, named, host: null);

    //  Every refusal ends the case: the steps after it would assert on a machine nobody drove.
    public void Apply(StimulusStep step, SasAddress? named, IHostView? host)
    {
        ArgumentNullException.ThrowIfNull(step);

        try
        {
            if (Refusal(step) is { } refused)
                throw new StimulusUnavailableException(refused);

            if (!CarryOnTheDemo(step, host))
                Carry(step);
        }
        catch (QpmUnavailableException unavailable)
        {
            throw new StimulusUnavailableException(unavailable.Reason) { EndsTheCase = true };
        }
        catch (StimulusUnavailableException refused) when (!refused.EndsTheCase)
        {
            throw new StimulusUnavailableException(refused.Reason) { EndsTheCase = true };
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

            // The host has already taken the printer out of use as the cash-out device (long poll
            // 7B bit 0), which the runner does for this backend; a note and the collect button then
            // leave the machine owing a cancelled-credit hand pay, as the GSA documents create one.
            case HandpayStep { Kind: HandpayKind.CancelledCredit } handpay:
                if (!Machine.Profile.TryFindButton("collect", out EgmButton collect))
                    throw new StimulusUnavailableException(WhyNoButton("collect"));
                if (handpay.AmountCents > int.MaxValue)
                    throw new StimulusUnavailableException($"no note is worth {handpay.AmountCents} cents");

                Machine.InsertNote((int)handpay.AmountCents);
                Machine.PressButton(collect);
                return;

            case HandpayStep jackpot:
                throw new StimulusUnavailableException(
                    $"'{jackpot.Describe()}' cannot be driven through the peripherals: the machine "
                    + "decides its own outcomes, so a jackpot cannot be asked for from outside");

            case HandpayResetStep:
                if (ResetKey(Machine.Profile) is not { } key)
                    throw new StimulusUnavailableException(
                        "the machine declares no reset keyswitch, so an attendant cannot reset a "
                        + "hand pay from here. It has: " + string.Join(", ", Machine.Profile.ButtonNames));

                Machine.PressButton(key);
                return;

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

    private Dictionary<string, string?> Describe() => Offer(Machine.Profile, _acceptor.Directory, demo, betCredits, outcomes);

    /// <summary>
    /// What this backend offers a machine with this profile, without attaching to it.
    /// </summary>
    /// <remarks>
    /// A case that opens no link is judged against this, so it names what the machine actually
    /// lacks rather than everything a backend nobody has attached cannot vouch for.
    /// </remarks>
    public static Dictionary<string, string?> Offer(
        EgmHardwareProfile profile, string acceptorDirectory, DemoProcess? demo = null, long? betCredits = null,
        ScriptedOutcomes? outcomes = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

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
        foreach ((string key, string? value) in CapabilityProbe.ForAcceptor(acceptorDirectory))
            values[key] = value;

        // Named one by one as well, so a test that needs a twenty can require exactly that rather
        // than requiring notes in general and failing on a machine that reads a different set.
        foreach (int cents in QpmLayout.NoteValues)
            values[$"stimulus.bill.{cents}"] = "yes";

        foreach (string door in profile.DoorNames)
            values[$"stimulus.door.{door}"] = "yes";

        foreach (string button in profile.ButtonNames)
            values[$"stimulus.button.{button}"] = "yes";

        // Named for how it is done, because the host takes part: the runner withdraws the printer
        // with long poll 7B before the note and the collect, and restores it after the reset.
        if (profile.TryFindButton("collect", out _))
            values["stimulus.handpay"] = HandpayByWithdrawnPrinter;

        if (ResetKey(profile) is not null)
            values["stimulus.handpay.reset"] = HandpayByWithdrawnPrinter;

        OfferOnTheDemo(values, profile, demo, betCredits);
        OfferOutcomes(values, profile, demo, outcomes);
        return values;
    }

    /// <summary>The value of the hand pay capabilities when the host must withdraw the printer.</summary>
    public const string HandpayByWithdrawnPrinter = CapabilityProbe.HandpayByWithdrawnPrinter;

    /// <summary>The keyswitch an attendant turns to reset a hand pay, or null when the machine has none.</summary>
    private static EgmButton? ResetKey(EgmHardwareProfile profile) =>
        profile.Buttons.FirstOrDefault(b =>
            b.Kind.Equals("keyswitch", StringComparison.OrdinalIgnoreCase)
            && b.Label.Contains("reset", StringComparison.OrdinalIgnoreCase));
}
