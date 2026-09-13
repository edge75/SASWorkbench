using CommunityToolkit.Mvvm.ComponentModel;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Tickets;

namespace SasWorkbench.Gui.ViewModels;

/// <summary>
/// One thing a person can do to the machine from the window, and what it becomes in a script.
/// </summary>
/// <remarks>
/// Each of these carries the very step a script would carry, rather than a label the recorder
/// would have to translate. That is what makes a recorded script and a hand-written one the same
/// thing: the window cannot express a stimulus the language cannot, and cannot record one it did
/// not actually apply.
/// </remarks>
/// <param name="Label">What the button says.</param>
/// <param name="Step">The step it applies, and the step a recording writes down.</param>
public sealed record StimulusAction(string Label, StimulusStep Step)
{
    /// <summary>What a screen reader is told, including what the machine must provide.</summary>
    public string Automation => $"{Label} — needs {Step.RequiredCapability}";

    public override string ToString() => Label;
}

/// <summary>
/// The stimulus panel: what a person can do to the machine by hand.
/// </summary>
/// <remarks>
/// <para>
/// It offers only what the script language has a step for, and the whole list is offered whatever
/// the backend can do. That is deliberate: a button hidden because the backend cannot honour it
/// leaves a person wondering whether the machine has no door or the build has no button, whereas a
/// button that refuses says which. The refusal is the backend's own words.
/// </para>
/// <para>
/// A refused action is still recorded — as an action that did not happen, with the reason — and
/// the emitted script carries no step for it. Emitting one would open a door the session never
/// opened, and every assertion after it would be about a machine that had not had one opened.
/// </para>
/// </remarks>
public sealed partial class StimulusViewModel : ObservableObject
{
    /// <summary>The ticket a hand-driven redemption offers, which a person may change.</summary>
    public const long DefaultTicketCents = 500;

    /// <summary>Everything the window can do to a machine, in the order the panel lists them.</summary>
    public IReadOnlyList<StimulusAction> Actions { get; } =
    [
        new("Settle: shut every door", new SettleStep(SourceLocation.None)),
        new("Open the slot door", new DoorStep(SourceLocation.None, "slot", Open: true)),
        new("Shut the slot door", new DoorStep(SourceLocation.None, "slot", Open: false)),
        new("Open the drop door", new DoorStep(SourceLocation.None, "drop", Open: true)),
        new("Shut the drop door", new DoorStep(SourceLocation.None, "drop", Open: false)),
        new("Feed a $20 note", new InsertBillStep(SourceLocation.None, 2000)),
        new("Feed a $10 note", new InsertBillStep(SourceLocation.None, 1000)),
        new("Press collect: print a ticket",
            new PressButtonStep(SourceLocation.None, "collect")),
        new("Feed a $5 ticket", new InsertTicketStep(
            SourceLocation.None,
            new Ticket
            {
                AmountCents = DefaultTicketCents,
                ValidationNumber = 1234567890123456,
                ValidationType = TicketValidationType.CashableFromCashout,
            },
            File: null)),
        new("Play one game at one credit",
            new PlayGamesStep(SourceLocation.None, Games: 1, BetCredits: 1, WinCredits: 0)),
    ];

    [ObservableProperty]
    public partial StimulusAction? Selected { get; set; }

    /// <summary>What the last thing a person asked for came to.</summary>
    [ObservableProperty]
    public partial string? Outcome { get; set; }
}
