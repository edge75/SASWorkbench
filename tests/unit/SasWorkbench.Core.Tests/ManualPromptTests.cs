using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// A manual step asks a person to act. The interesting case is the one where nobody is at the
/// desk: an unattended run must reach a verdict rather than wait for ever, and the verdict must
/// be a skip that says why.
/// </summary>
public class ManualPromptTests
{
    private static readonly StimulusStep Step =
        new InsertBillStep(SourceLocation.None, 2000);

    [Fact]
    public void APersonWhoDoesItLetsTheStepThrough()
    {
        var backend = new ManualStimulusBackend(
            (_, _) => PromptAnswer.Done, TimeSpan.FromSeconds(1));

        backend.Apply(Step);
    }

    [Fact]
    public void APersonWhoDeclinesSkipsWithTheirReason()
    {
        var backend = new ManualStimulusBackend(
            (_, _) => PromptAnswer.Declined, TimeSpan.FromSeconds(1));

        StimulusUnavailableException refused =
            Assert.Throws<StimulusUnavailableException>(() => backend.Apply(Step));
        Assert.Contains("declined", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NobodyAnsweringSkipsAndNamesTheDeadlineRatherThanHanging()
    {
        var backend = new ManualStimulusBackend(
            (_, _) => PromptAnswer.NoAnswer, TimeSpan.FromSeconds(45));

        StimulusUnavailableException refused =
            Assert.Throws<StimulusUnavailableException>(() => backend.Apply(Step));
        Assert.Contains("nobody answered within 45s", refused.Reason, StringComparison.Ordinal);
    }

    /// <summary>The prompt is told how long it has, so it can put a countdown in front of a person.</summary>
    [Fact]
    public void ThePromptIsGivenTheDeadline()
    {
        TimeSpan given = TimeSpan.Zero;
        var backend = new ManualStimulusBackend(
            (_, deadline) =>
            {
                given = deadline;
                return PromptAnswer.Done;
            },
            TimeSpan.FromSeconds(30));

        backend.Apply(Step);

        Assert.Equal(TimeSpan.FromSeconds(30), given);
    }

    /// <summary>
    /// A backend built without a deadline is the failure mode this type exists to prevent, so it
    /// is refused at construction rather than discovered when a run never finishes.
    /// </summary>
    [Fact]
    public void ADeadlineIsNotOptional()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ManualStimulusBackend((_, _) => PromptAnswer.Done, TimeSpan.Zero));
    }

    /// <summary>The deadline a run was configured with is the one the backend actually enforces.</summary>
    [Fact]
    public void TheRunnerHandsItsDeadlineToTheBackend()
    {
        var options = new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Manual,
            AskPerson = (_, _) => PromptAnswer.Done,
            PromptDeadline = TimeSpan.FromSeconds(15),
        };

        using ScriptEnvironment environment = DefaultEnvironment.Create(
            new ConnectStep(SourceLocation.None, ScriptTransport.Tcp, "127.0.0.1:19760",
                SasAddress.Of(0x01), SasChannel.One),
            options);

        var backend = Assert.IsType<ManualStimulusBackend>(environment.Stimulus);
        Assert.Equal(TimeSpan.FromSeconds(15), backend.Deadline);
    }

    [Fact]
    public void TheRequestSaysWhatToDo()
    {
        string asked = string.Empty;
        var backend = new ManualStimulusBackend(
            (request, _) =>
            {
                asked = request;
                return PromptAnswer.Done;
            },
            TimeSpan.FromSeconds(1));

        backend.Apply(Step);

        Assert.Contains("2000", asked, StringComparison.Ordinal);
    }
}
