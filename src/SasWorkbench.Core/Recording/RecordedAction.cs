using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Recording;

/// <summary>
/// One thing a person did to the machine, and what became of it.
/// </summary>
/// <remarks>
/// <para>
/// The recorder keeps what the person <em>did</em>, not what the wire showed, because the two are
/// not the same and only the first is reproducible. A click on a door is an intent; the exceptions
/// that follow are the machine's answer to it, and a script that replayed the answer rather than
/// the intent would assert its way to a pass without ever opening a door.
/// </para>
/// <para>
/// An action the environment would not carry out is kept with the reason rather than dropped or
/// emitted anyway. Emitting it would put a step in the script that does something the recorded
/// session never did; dropping it silently would leave the assertions after it in the script with
/// nothing to be about.
/// </para>
/// </remarks>
/// <param name="At">When, on the session clock.</param>
/// <param name="Channel">Which host did it.</param>
/// <param name="What">One line a person reads.</param>
/// <param name="Step">The step it becomes, or null where it becomes none.</param>
/// <param name="Refused">
/// Why the environment would not do it, when it would not. An action carrying one is never
/// emitted as a step.
/// </param>
public sealed record RecordedAction(
    TimeSpan At,
    SasChannel Channel,
    string What,
    ScriptStep? Step = null,
    string? Refused = null)
{
    /// <summary>
    /// True for the steps that only say what was already true when recording began, such as the
    /// link.
    /// </summary>
    /// <remarks>
    /// Kept apart from what the person did so that "nothing was done while that was recording" can
    /// still be said. A recording holding only its own preamble is an empty one, and calling it a
    /// script would hand somebody a file that connects to a machine and stops.
    /// </remarks>
    public bool Preamble { get; init; }

    /// <summary>True when this action happened and can be written down as a step.</summary>
    public bool Reproducible => Refused is null && Step is not null;

    public override string ToString() => Refused is null
        ? $"[{At.TotalMilliseconds,9:F0}ms] ch{(int)Channel} {What}"
        : $"[{At.TotalMilliseconds,9:F0}ms] ch{(int)Channel} {What} — refused: {Refused}";
}

/// <summary>Which of the things a recording cannot capture this is.</summary>
public enum CaveatKind
{
    /// <summary>
    /// A stimulus nobody wired: the person asked for it and the environment did nothing.
    /// </summary>
    UnwiredStimulus,

    /// <summary>
    /// A value the machine chose rather than the person: a meter, a clock, a ticket's number.
    /// </summary>
    MachineChosenValue,

    /// <summary>A wait whose length was a person's hesitation and not the protocol's.</summary>
    HumanHesitation,

    /// <summary>
    /// A step whose place in a transaction is a guess, because nothing in the bytes tells one
    /// transaction of that kind from another.
    /// </summary>
    AmbiguousEpisode,

    /// <summary>Something the window can do that this build cannot write down as a step.</summary>
    NotExpressible,
}

/// <summary>
/// Something the recording could not capture, said plainly rather than emitted as a step that
/// would do something else.
/// </summary>
/// <param name="Kind">Which of the five it is.</param>
/// <param name="What">What was done, or what value it was about.</param>
/// <param name="Why">Why it cannot be written down, in a sentence.</param>
public sealed record RecordingCaveat(CaveatKind Kind, string What, string Why)
{
    /// <summary>The one-line description each kind carries in a report and in a script header.</summary>
    public static string Describe(CaveatKind kind) => kind switch
    {
        CaveatKind.UnwiredStimulus =>
            "a stimulus nobody wired: the person asked for it and nothing happened, so a step for "
            + "it would do something the recorded session did not",
        CaveatKind.MachineChosenValue =>
            "a value the machine chose rather than the person: asserting the number it happened "
            + "to hold would pass on this machine and fail on the next for no good reason",
        CaveatKind.HumanHesitation =>
            "a wait whose length was a person's hesitation: the protocol never asked for it, so "
            + "it is not written down as one",
        CaveatKind.AmbiguousEpisode =>
            "a step whose place in a transaction is a guess, because nothing in the bytes tells "
            + "one transaction of that kind from another",
        CaveatKind.NotExpressible =>
            "something the window can do that the script language has no step for",
        _ => kind.ToString(),
    };

    public override string ToString() => $"{What} — {Why}";
}
