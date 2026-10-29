using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Events;

/// <summary>How a measurement here stands to produce one event.</summary>
public enum EventReach
{
    /// <summary>Something this tool drives makes the machine report it.</summary>
    Driven,

    /// <summary>The machine can report it, and nothing here can bring it about unattended.</summary>
    NotDrivenHere,

    /// <summary>Nothing in the machine or the platform above it ever raises it.</summary>
    NeverRaised,
}

/// <summary>One payload-carrying exception and what it would take to see one.</summary>
/// <param name="Code">The exception code.</param>
/// <param name="Reach">Whether a measurement here can bring it about.</param>
/// <param name="How">What raises it, in one sentence, in terms of the machine.</param>
/// <param name="RaisedBy">The platform or library function that raises it, so the claim is checkable.</param>
public sealed record EventProbeEntry(byte Code, EventReach Reach, string How, string RaisedBy)
{
    /// <summary>The payload this build has established for it, when it has one.</summary>
    public SasEventLayout? Layout => SasEventLayouts.For(Code);

    public string Name => Layout?.Name ?? SasNames.ExceptionName(Code);
}

/// <summary>
/// Which exception payloads a deliberate measurement here stands to produce, and which it does not.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="Sweep.SweepPlan"/>, and it exists for the opposite reason. A long
/// poll can be asked for, so the question there was which ones are safe to ask. An event can only
/// be waited for, so the question here is what makes one happen — and for several of them the
/// honest answer is that nothing here does. Those are recorded as unproduced rather than guessed
/// at, which is the whole point of writing the plan down before running anything.
/// </para>
/// <para>
/// Every reach is read off the platform's own call sites rather than reasoned about. Where the
/// platform never calls the library's builder, no machine running it can raise that event and no
/// stimulus would change that.
/// </para>
/// </remarks>
public static class RealTimeEventPlan
{
    /// <summary>Every payload-carrying exception, with what it would take to see one.</summary>
    public static IReadOnlyList<EventProbeEntry> All { get; } =
    [
        new(0x4F, EventReach.Driven,
            "the machine takes a note, which the peripheral driver can offer it",
            "SAS_BillAccepted, from the platform's own note handler"),
        new(0x7E, EventReach.Driven,
            "a game begins, which the host's auto-rebet command makes happen once the machine "
            + "has credit",
            "SAS_RTE_GameStart, from the platform's game-started condition"),
        new(0x7F, EventReach.Driven,
            "a game finishes, so the same auto-rebet run produces one for every start",
            "SAS_RTE_GameEnd, from the platform's game-ended condition"),
        new(0x88, EventReach.Driven,
            "a reel comes to rest, and the demo game reports its stop positions at the end of "
            + "every spin",
            "SAS_RTE_ReelStopped, from the platform's reel-stop reporting"),
        new(0x8C, EventReach.NotDrivenHere,
            "the player chooses a different game, and nothing here can work the machine's game "
            + "menu",
            "SAS_RTE_GameSelected, from the platform's selected-game report"),
        new(0x8A, EventReach.NotDrivenHere,
            "somebody puts a past game back on the screen, which is an audit-replay screen "
            + "nothing here drives",
            "SAS_RTE_GameRecallEntry, from the platform's replay start"),
        new(0x8B, EventReach.NotDrivenHere,
            "a card game reports what the player kept, and the game this machine runs has reels "
            + "rather than cards",
            "SAS_RTE_CardHeld, from the platform's card-held report"),
        new(0x7C, EventReach.NotDrivenHere,
            "the host pays a legacy bonus into the machine, which is money and is not sent "
            + "unattended",
            "SAS_LegacyBonusAward, from the machine library's own bonus handler"),
        new(0x9E, EventReach.NeverRaised,
            "a play session begins, and nothing in the platform above the machine library ever "
            + "reports one",
            "SAS_RTE_SessionStart, which the platform never calls"),
        new(0x9F, EventReach.NeverRaised,
            "a play session ends, and nothing in the platform above the machine library ever "
            + "reports one",
            "SAS_RTE_SessionEnd, which the platform never calls"),
    ];

    /// <summary>What this plan says about one exception, or null when it carries no payload.</summary>
    public static EventProbeEntry? For(byte code) => All.FirstOrDefault(e => e.Code == code);

    /// <summary>The commands a measurement of events sends that an ordinary sweep withholds.</summary>
    /// <remarks>
    /// Named here rather than left implicit, because all three are commands the sweep refuses on
    /// purpose and a person running this is entitled to know that before the link opens. Long poll
    /// <c>$0E</c> changes how the machine answers every general poll after it, <c>$AA</c> makes
    /// the machine play rounds by itself, and <c>$06</c> enables the note acceptor. The first two
    /// are put back at the end of the run, which says whether that succeeded; the acceptor is left on.
    /// </remarks>
    public static IReadOnlyList<(byte Command, string What)> DeliberatelySent { get; } =
    [
        (0x0E, "turns real-time event reporting on, which is the whole measurement"),
        (0xAA, "makes the machine play rounds by itself, which is the only lever here that "
               + "produces a game"),
        (0x06, "tells the machine to take notes, because a note it will not take reports nothing "
               + "and a host is the only thing that can say so"),
    ];
}
