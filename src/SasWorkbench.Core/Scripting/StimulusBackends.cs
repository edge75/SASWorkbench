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

    /// <summary>Carries out one step.</summary>
    /// <exception cref="StimulusUnavailableException">This backend cannot do it.</exception>
    void Apply(StimulusStep step);
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

    public void Apply(StimulusStep step) => throw new StimulusUnavailableException(
        $"nothing is driving the gaming machine, so '{step.Describe()}' cannot happen "
        + $"(needs {step.RequiredCapability})");
}

/// <summary>A backend that asks a person to do it.</summary>
/// <remarks>
/// Useful when the hardware exists but nothing can drive it yet. The prompt is a delegate rather
/// than a console write so that the same backend works from a graphical interface, and so a test
/// of the runner can answer it without a person.
/// </remarks>
public sealed class ManualStimulusBackend(Func<string, bool> ask) : IStimulusBackend
{
    public StimulusBackend Kind => StimulusBackend.Manual;

    public IReadOnlyDictionary<string, string?> Capabilities =>
        CapabilityProbe.ForStimulus(StimulusBackend.Manual);

    public void Apply(StimulusStep step)
    {
        if (!ask($"Please do this on the machine, then confirm: {step.Describe()}"))
            throw new StimulusUnavailableException(
                $"a person declined or could not carry out '{step.Describe()}'");
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

    public void Apply(StimulusStep step) => throw new StimulusUnavailableException(
        $"'{step.Describe()}' cannot happen: {reason}");
}
