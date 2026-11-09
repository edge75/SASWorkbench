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

    /// <summary>
    /// The destructive and attendant actions the GSA scripts ask for are offered to a person, and
    /// the prompt says what to do in words a person at the cabinet can follow.
    /// </summary>
    [Theory]
    [InlineData("stimulus.door.main")]
    [InlineData("stimulus.button.service")]
    [InlineData("stimulus.handpay")]
    [InlineData("stimulus.handpay.jackpot")]
    [InlineData("stimulus.handpay.reset")]
    [InlineData("machine.power_cycle")]
    [InlineData("machine.memory_fault")]
    public void APersonIsOfferedTheActionsTheGsaScriptsNeed(string capability)
    {
        var backend = new ManualStimulusBackend((_, _) => PromptAnswer.Done, TimeSpan.FromSeconds(1));

        Assert.Equal("yes", backend.Capabilities[capability]);
    }

    [Fact]
    public void APowerCycleAndAMemoryFaultAreAskedForInPlainWords()
    {
        var asked = new List<string>();
        var backend = new ManualStimulusBackend(
            (request, _) =>
            {
                asked.Add(request);
                return PromptAnswer.Done;
            },
            TimeSpan.FromSeconds(1));

        backend.Apply(new PowerCycleStep(SourceLocation.None));
        backend.Apply(new MemoryFaultStep(SourceLocation.None, MemoryFaultKind.CmosRecovered));

        Assert.Contains("cut the machine's power", asked[0], StringComparison.Ordinal);
        Assert.Contains("CMOS RAM error the meters survive", asked[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// A memory fault prompt says how to cause the fault and when to answer no. On the board a
    /// person met "an EEPROM data error" with no idea what to do; most of these faults cannot
    /// happen on a QSP machine at all, and the prompt has to say so.
    /// </summary>
    [Theory]
    [InlineData(MemoryFaultKind.CmosRecovered, "never raises $31")]
    [InlineData(MemoryFaultKind.CmosLost, "loses the meters")]
    [InlineData(MemoryFaultKind.CmosBadDevice, "never raises $33")]
    [InlineData(MemoryFaultKind.EepromData, "never raises $34")]
    [InlineData(MemoryFaultKind.EepromBadDevice, "never raises $35")]
    [InlineData(MemoryFaultKind.EpromChanged, "different version of the program")]
    [InlineData(MemoryFaultKind.EpromBadChecksum, "never raises $37")]
    [InlineData(MemoryFaultKind.PartitionedEpromChanged, "never raises $38")]
    [InlineData(MemoryFaultKind.PartitionedEpromBadChecksum, "never raises $39")]
    [InlineData(MemoryFaultKind.Clear, "NVRAM reset in the audit menu")]
    public void AMemoryFaultPromptSaysHowAndWhenToAnswerNo(MemoryFaultKind kind, string advice)
    {
        string? asked = null;
        var backend = new ManualStimulusBackend(
            (request, _) =>
            {
                asked = request;
                return PromptAnswer.Done;
            },
            TimeSpan.FromSeconds(1));

        backend.Apply(new MemoryFaultStep(SourceLocation.None, kind));

        Assert.NotNull(asked);
        Assert.Contains("How:", asked, StringComparison.Ordinal);
        Assert.Contains(advice, asked, StringComparison.Ordinal);
    }

    /// <summary>A step with nothing more to say keeps its one-line prompt.</summary>
    [Fact]
    public void AStepWithoutGuidanceIsAskedOnOneLine()
    {
        string? asked = null;
        var backend = new ManualStimulusBackend(
            (request, _) =>
            {
                asked = request;
                return PromptAnswer.Done;
            },
            TimeSpan.FromSeconds(1));

        backend.Apply(Step);

        Assert.DoesNotContain(Environment.NewLine, asked!, StringComparison.Ordinal);
        Assert.DoesNotContain("How:", asked!, StringComparison.Ordinal);
    }

    /// <summary>Real-time event reporting stays an opt-in by name, never a claim of the manual backend.</summary>
    [Fact]
    public void RealTimeEventsAreNotClaimedByAPerson()
    {
        var backend = new ManualStimulusBackend((_, _) => PromptAnswer.Done, TimeSpan.FromSeconds(1));

        Assert.False(backend.Capabilities.ContainsKey("stimulus.real_time_events"));
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
