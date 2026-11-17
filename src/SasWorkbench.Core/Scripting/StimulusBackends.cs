using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Scripting;

/// <summary>Something that can act on the gaming machine.</summary>
/// <remarks>
/// A backend that cannot carry out a step must say so by throwing
/// <see cref="StimulusUnavailableException"/>. It must never return quietly having done nothing:
/// the assertion that follows would then be testing the machine's previous state and would pass
/// for the wrong reason.
/// </remarks>
public interface IStimulusBackend
{
    /// <summary>Which backend this is, for a report.</summary>
    StimulusBackend Kind { get; }

    /// <summary>What this backend can do, added to the environment's capabilities.</summary>
    IReadOnlyDictionary<string, string?> Capabilities { get; }

    /// <summary>Carries out one step, on the machine the case has moved to.</summary>
    /// <param name="step">What to do.</param>
    /// <param name="machine">
    /// Which machine to do it to, or null where the case has not said. Null keeps the meaning it
    /// has on a wire record: <em>not known</em>, never machine one. On a host bound to one machine
    /// there is nothing to choose and a backend ignores it; on a floor it is the difference
    /// between driving the machine the case named and driving whichever one happened to be first.
    /// </param>
    /// <exception cref="StimulusUnavailableException">This backend cannot do it.</exception>
    void Apply(StimulusStep step, SasAddress? machine = null);
}

/// <summary>Thrown when a stimulus step cannot be carried out, with the reason for the skip.</summary>
public sealed class StimulusUnavailableException(string reason) : Exception(reason)
{
    /// <summary>Why it could not be done, for the skip line in the report.</summary>
    public string Reason => Message;
}

/// <summary>A backend that does nothing and admits it.</summary>
public sealed class NoStimulusBackend : IStimulusBackend
{
    public StimulusBackend Kind => StimulusBackend.None;

    public IReadOnlyDictionary<string, string?> Capabilities =>
        CapabilityProbe.ForStimulus(StimulusBackend.None);

    public void Apply(StimulusStep step, SasAddress? machine = null) =>
        throw new StimulusUnavailableException(
            $"nothing is driving the gaming machine, so '{step.Describe()}' cannot happen "
            + $"(needs {step.RequiredCapability})");
}

/// <summary>What came back when a person was asked to do something.</summary>
public enum PromptAnswer
{
    /// <summary>They did it.</summary>
    Done,

    /// <summary>They said they could not, or would not.</summary>
    Declined,

    /// <summary>Nobody answered before the deadline.</summary>
    NoAnswer,
}

/// <summary>
/// Asks a person to do something and waits no longer than <paramref name="deadline"/>.
/// </summary>
/// <remarks>
/// The deadline is not optional. A prompt without one hangs an unattended run for ever and
/// produces no verdict at all, which is the silent failure this project exists to refuse: better
/// a skip that says nobody answered than a suite that never finishes.
/// </remarks>
public delegate PromptAnswer StimulusPrompt(string request, TimeSpan deadline);

/// <summary>A backend that asks a person to do it.</summary>
/// <remarks>
/// Useful when the hardware exists but nothing can drive it yet. The prompt is a delegate rather
/// than a console write so that the same backend works from a graphical interface, and so a test
/// of the runner can answer it without a person.
/// </remarks>
public sealed class ManualStimulusBackend(StimulusPrompt ask, TimeSpan deadline)
    : IStimulusBackend
{
    /// <summary>How long to wait for a person when a caller has no preference.</summary>
    public static TimeSpan DefaultDeadline { get; } = TimeSpan.FromMinutes(2);

    public StimulusBackend Kind => StimulusBackend.Manual;

    public IReadOnlyDictionary<string, string?> Capabilities =>
        CapabilityProbe.ForStimulus(StimulusBackend.Manual);

    /// <summary>How long a person is given to answer.</summary>
    public TimeSpan Deadline { get; } = deadline > TimeSpan.Zero
        ? deadline
        : throw new ArgumentOutOfRangeException(nameof(deadline), deadline,
            "A manual prompt needs a deadline: without one an unattended run never finishes.");

    public void Apply(StimulusStep step, SasAddress? machine = null)
    {
        ArgumentNullException.ThrowIfNull(step);

        //  The machine is named in the prompt when the case named one, because on a floor the
        //  person has to walk to the right cabinet and "do this on the machine" does not say which.
        string where = machine is { } address ? $"on machine ${address}" : "on the machine";

        string request = $"Please do this {where}, then confirm: {step.Describe()}";
        if (step.Guidance is { } guidance)
            request += Environment.NewLine + "  " + guidance;

        Settle(ask(request, Deadline), Deadline, $"'{step.Describe()}'");
    }

    /// <summary>Extra time a person is given for each action of a group, on top of the deadline.</summary>
    public static TimeSpan PerGroupedAction { get; } = TimeSpan.FromSeconds(15);

    /// <summary>True for an action a person can do several times in a row from one prompt.</summary>
    public static bool CanGroup(StimulusStep step) => step is DoorStep or PressButtonStep;

    /// <summary>Asks a person for several door and button actions at once, answered with one confirmation.</summary>
    public void ApplyGroup(IReadOnlyList<StimulusStep> steps, SasAddress? machine = null)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0 || steps.Any(s => !CanGroup(s)))
            throw new ArgumentException("only door and button actions are asked for together", nameof(steps));

        string where = machine is { } address ? $"on machine ${address}" : "on the machine";
        string request = $"Please do these {steps.Count} actions {where}, in this order, then confirm once:"
            + Environment.NewLine + GroupedActions(steps);

        TimeSpan deadline = Deadline + PerGroupedAction * steps.Count;
        Settle(ask(request, deadline), deadline, $"the {steps.Count} actions asked together");
    }

    /// <summary>The actions in words, numbered one per line, never folded (R39: a folded tail was done out of order).</summary>
    public static string GroupedActions(IReadOnlyList<StimulusStep> steps) =>
        string.Join(Environment.NewLine, steps.Select((step, i) => $"  {i + 1}. {InWords(step)}"));

    private static string InWords(StimulusStep step) => step switch
    {
        DoorStep door => $"{(door.Open ? "open" : "close")} the {door.Door} door",
        PressButtonStep press => $"press {press.Button} once",
        _ => step.Describe(),
    };

    private static void Settle(PromptAnswer answer, TimeSpan deadline, string what)
    {
        switch (answer)
        {
            case PromptAnswer.Done:
                return;

            case PromptAnswer.NoAnswer:
                throw new StimulusUnavailableException(
                    $"nobody answered within {deadline.TotalSeconds:F0}s, so {what} did not happen");

            default:
                throw new StimulusUnavailableException(
                    $"a person declined or could not carry out {what}");
        }
    }
}

/// <summary>A backend that refuses everything, for one stated reason.</summary>
/// <remarks>
/// Used when a stimulus backend was asked for but cannot apply here. The reason travels with the
/// refusal so the skip line explains itself instead of reading as "nothing was connected".
/// </remarks>
public sealed class RefusingStimulusBackend(string reason) : IStimulusBackend
{
    public StimulusBackend Kind => StimulusBackend.None;

    public IReadOnlyDictionary<string, string?> Capabilities =>
        new Dictionary<string, string?> { ["stimulus"] = "refused" };

    public void Apply(StimulusStep step, SasAddress? machine = null) =>
        throw new StimulusUnavailableException($"'{step.Describe()}' cannot happen: {reason}");
}
