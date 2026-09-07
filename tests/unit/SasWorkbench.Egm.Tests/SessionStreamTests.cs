using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Session;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The stream, end to end. A run puts the wire and the claims made about it into one ordered list
/// on one clock — which is the thing that makes "the meter moved within two seconds of the note"
/// checkable by reading the log rather than by trusting two stopwatches.
/// </summary>
public class SessionStreamTests
{
    private static (RunReport Report, SessionLog Log) Run(string script)
    {
        var log = new SessionLog();
        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(2),
            QuietGap = TimeSpan.FromMilliseconds(15),
            Log = log,
        });

        RunReport report = runner.RunAll([ScriptParser.Parse(script, "stream.sas")]);
        return (report, log);
    }

    private const string Script = """
        host connect loopback address 01
        host poll every 20ms
        host expect link up within 2s
        host send lp 1F
        host expect field game_id == QX
        """;

    /// <summary>
    /// The wire is recorded on the poll thread and a step on the runner thread, so what arrives
    /// second may have happened first. The stream a reader gets is ordered by the clock regardless.
    /// </summary>
    [Fact]
    public void ARunProducesOneStreamOrderedByTheClock()
    {
        (RunReport report, SessionLog log) = Run(Script);
        IReadOnlyList<SessionEvent> events = log.Snapshot();

        Assert.Equal(Verdict.Pass, report.Verdict);
        Assert.NotEmpty(events);
        for (int i = 1; i < events.Count; i++)
            Assert.True(events[i].At >= events[i - 1].At, $"event {i} went backwards");
    }

    [Fact]
    public void TheStreamHoldsTheWireAndTheClaimsMadeAboutIt()
    {
        (_, SessionLog log) = Run(Script);
        SessionLane[] lanes = log.Snapshot().Select(e => e.Lane).Distinct().ToArray();

        Assert.Contains(SessionLane.Transmit, lanes);
        Assert.Contains(SessionLane.Receive, lanes);
        Assert.Contains(SessionLane.Assertion, lanes);
        Assert.Contains(SessionLane.TestStarted, lanes);
        Assert.Contains(SessionLane.Verdict, lanes);
    }

    /// <summary>
    /// The reconciliation, observed rather than asserted in the abstract: the engine starts after
    /// the test does, so its first frame must fall after the test-start marker on the session clock
    /// even though its own clock reads close to zero.
    /// </summary>
    [Fact]
    public void EveryLaneIsOnTheSameClock()
    {
        (_, SessionLog log) = Run(Script);
        IReadOnlyList<SessionEvent> events = log.Snapshot();

        SessionEvent started = events.First(e => e.Lane == SessionLane.TestStarted);
        SessionEvent firstFrame = events.First(e => e.Lane == SessionLane.Transmit);
        SessionEvent verdict = events.Last(e => e.Lane == SessionLane.Verdict);

        Assert.True(firstFrame.At >= started.At);
        Assert.True(verdict.At >= firstFrame.At);
    }

    [Fact]
    public void EveryEventOfATestCarriesTheTestName()
    {
        (_, SessionLog log) = Run(Script);

        Assert.All(
            log.Snapshot().Where(e => e.Lane != SessionLane.Verdict),
            e => Assert.Equal("stream", e.Test));
    }

    /// <summary>An assertion that held is in the log, because a view must be able to show it.</summary>
    [Fact]
    public void AssertionsThatPassedAreInTheStream()
    {
        (_, SessionLog log) = Run(Script);

        Assert.Contains(
            log.Snapshot(),
            e => e.Lane == SessionLane.Assertion && e.Verdict == Verdict.Pass);
    }

    /// <summary>
    /// The subject is the verdict reaching the stream, not the capability: any requirement
    /// nothing offers would do. This one is a button no backend has, chosen because a button is
    /// a property of a machine's own wiring and so is never claimed on this peer's behalf.
    /// </summary>
    [Fact]
    public void ATestThatCannotRunStillReachesAVerdictInTheStream()
    {
        (RunReport report, SessionLog log) = Run("""
            requires stimulus.button.service
            host connect loopback address 01
            host expect link up within 2s
            """);

        Assert.Equal(Verdict.Skip, report.Verdict);
        SessionEvent verdict = Assert.Single(
            log.Snapshot(), e => e.Lane == SessionLane.Verdict);
        Assert.Equal(Verdict.Skip, verdict.Verdict);
        Assert.Contains("stimulus.button.service", verdict.Detail);
    }
}
