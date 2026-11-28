using System.Diagnostics;
using SasWorkbench.Core.Scripting;
using static SasWorkbench.Qpm.Tests.QpmDemoStimulusTests;

namespace SasWorkbench.Qpm.Tests;

/// <summary>
/// Outcomes given to the real demo through its scripted build (D64). A stand-in for the build takes the queue the way
/// the RNG substitution does and writes its log lines; the tests check what is queued, what is waited for, and that
/// every step refuses - naming why - on a machine that is not a live scripted build.
/// </summary>
public class QpmScriptedOutcomeTests
{
    private const uint PlayLine = 1u << 4;

    [Fact]
    public void APlainBuildOffersTheProgressiveStepButRefusesItNamingTheMissingMarker()
    {
        using var rig = Rig.Start();

        Assert.Equal("yes", rig.Backend.Capabilities["stimulus.progressive"]);
        Assert.False(rig.Backend.Capabilities.ContainsKey(RunProvenance.MachineBuildCapability));
        Assert.False(rig.Backend.Capabilities.ContainsKey(QpmStimulusBackend.ScriptedOutcomesCapability));

        string? refused = rig.Backend.Refusal(Progressive(1));
        Assert.NotNull(refused);
        Assert.Contains("no marker", refused, StringComparison.Ordinal);
        Assert.Contains("SasWorkbenchM0-Scripted", refused, StringComparison.Ordinal);
    }

    [Fact]
    public void ALosingPlayIsRefusedOnAPlainBuildToo()
    {
        using var rig = Rig.Start(betCredits: 50);
        var lose = new PlayGamesStep(SourceLocation.None, 1, 50, 0) { WinStated = true };

        Assert.Contains("scripted-outcomes build", rig.Backend.Refusal(lose), StringComparison.Ordinal);
    }

    [Fact]
    public void AMarkerLeftByAProcessThatHasGoneVouchesForNothing()
    {
        using var directory = new TemporaryDirectory();
        var outcomes = new ScriptedOutcomes(directory.Path);
        File.WriteAllText(Path.Combine(directory.Path, "scripted-outcomes.marker"), "format=1\npid=999999\ncreated=1\nexe=x\n");

        Assert.Contains("is no longer running", outcomes.WhyNotScripted(), StringComparison.Ordinal);
    }

    [Fact]
    public void AMarkerWhoseProcessIdWasReusedVouchesForNothing()
    {
        using var directory = new TemporaryDirectory();
        var outcomes = new ScriptedOutcomes(directory.Path);
        File.WriteAllText(Path.Combine(directory.Path, "scripted-outcomes.marker"),
            $"format=1\npid={Environment.ProcessId}\ncreated=12345\nexe=x\n");

        Assert.Contains("another process", outcomes.WhyNotScripted(), StringComparison.Ordinal);
    }

    [Fact]
    public void ALiveScriptedBuildNamesItselfSoEveryResultIsLabelled()
    {
        using var build = FakeScriptedBuild.Start();
        using var rig = Rig.Start(outcomes: build.Outcomes);

        Assert.Equal(ScriptedOutcomes.Label, rig.Backend.Capabilities[RunProvenance.MachineBuildCapability]);
        Assert.Equal("yes", rig.Backend.Capabilities[QpmStimulusBackend.ScriptedOutcomesCapability]);
        Assert.Null(rig.Backend.Refusal(Progressive(1)));
    }

    [Fact]
    public void LevelOneQueuesFiveJackpotsOnTheSlotsTopRowPressesPlayAndWaitsForTheBuildToDrawThem()
    {
        using var build = FakeScriptedBuild.Start();
        using var rig = Rig.Start(outcomes: build.Outcomes);

        rig.Backend.Apply(Progressive(1));

        Assert.Equal("slot=15:12,16:13,15:13,14:13,13:12", build.Taken.Single().Split(' ', 2)[1]);
        Assert.Contains(PlayLine, rig.Egm.InputsSeen);
    }

    [Fact]
    public void LevelsOneAndTwoAreTheMiddleRowAndLevelTwoAloneMayAlsoBeTheWheel()
    {
        using var build = FakeScriptedBuild.Start();
        using var rig = Rig.Start(outcomes: build.Outcomes);

        rig.Backend.Apply(Progressive(1, 2));
        rig.Backend.Apply(Progressive(2));

        Assert.Equal("slot=15:11,16:12,15:12,14:12,13:11", build.Taken[0].Split(' ', 2)[1]);
        Assert.Equal("slot=15:10,16:11,15:11,14:11,13:10 wheel=19:3", build.Taken[1].Split(' ', 2)[1]);
    }

    [Fact]
    public void AnOutcomeTheBuildRefusesEndsTheCaseWithItsOwnReasonAndIsWithdrawn()
    {
        using var build = FakeScriptedBuild.Start(refuse: true);
        using var rig = Rig.Start(outcomes: build.Outcomes);

        StimulusUnavailableException refused = Assert.Throws<StimulusUnavailableException>(
            () => rig.Backend.Apply(Progressive(1)));

        Assert.True(refused.EndsTheCase);
        Assert.Contains("no alternative starts with a draw of 0..19", refused.Reason, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(build.Outcomes.Directory, "queue.txt")));
    }

    [Fact]
    public void AnOutcomeNoGameDrawsIsTakenBackSoALaterGameCannotDrawIt()
    {
        using var build = FakeScriptedBuild.Start(draws: false);
        using var rig = Rig.Start(outcomes: build.Outcomes);

        StimulusUnavailableException refused = Assert.Throws<StimulusUnavailableException>(
            () => rig.Backend.Apply(Progressive(1)));

        Assert.Contains("no game drew the scripted outcome", refused.Reason, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(build.Outcomes.Directory, "queue.txt")));
    }

    [Theory]
    [InlineData("egm progressive win level=3", "level 2 alone, and nothing else")]
    [InlineData("egm progressive win level=1,2,3", "level 2 alone, and nothing else")]
    [InlineData("egm progressive win level=1 win=10", "paytable part")]
    [InlineData("egm progressive win level=1 win=0", "1000 credits per bet")]
    [InlineData("egm progressive win level=1 bet=1", "--egm-bet-credits")]
    public void WhatNoDrawOfTheDemoCanHonourIsRefusedBeforeTheCaseStarts(string line, string because)
    {
        using var build = FakeScriptedBuild.Start();
        using var rig = Rig.Start(outcomes: build.Outcomes);
        StimulusStep step = (StimulusStep)ScriptParser.Parse(line).Tests[0].Steps[0];

        Assert.Contains(because, rig.Backend.Refusal(step), StringComparison.Ordinal);
    }

    [Fact]
    public void ALosingPlayQueuesALosingDrawForEitherGameEachTimeAndWaitsForTheGameToEnd()
    {
        using var build = FakeScriptedBuild.Start();
        using var rig = Rig.Start(betCredits: 50, outcomes: build.Outcomes);
        var host = new FakeHost { Heard = { 0x7F } };

        rig.Backend.Apply(new PlayGamesStep(SourceLocation.None, 2, 50, 0) { WinStated = true }, null, host);

        Assert.Equal(2, build.Taken.Count);
        Assert.All(build.Taken, line => Assert.EndsWith(" slot=15:0,16:2,15:0,14:0,13:0 wheel=19:0", line, StringComparison.Ordinal));
    }

    [Fact]
    public void AWinningPlayIsStillRefusedBecauseNoExactStopsAreSolvedHere()
    {
        using var build = FakeScriptedBuild.Start();
        using var rig = Rig.Start(betCredits: 50, outcomes: build.Outcomes);

        Assert.Contains("only a losing game", rig.Backend.Refusal(new PlayGamesStep(SourceLocation.None, 1, 50, 100) { WinStated = true }),
            StringComparison.Ordinal);
    }

    private static ProgressiveWinStep Progressive(params int[] levels) => new(SourceLocation.None, levels, 1);

    /// <summary>Stands in for the scripted build: a live marker for this process, and a thread that draws what is queued.</summary>
    private sealed class FakeScriptedBuild : IDisposable
    {
        private readonly TemporaryDirectory _directory = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly Thread _thread;
        private readonly bool _refuse;
        private readonly bool _draws;

        private FakeScriptedBuild(bool refuse, bool draws)
        {
            _refuse = refuse;
            _draws = draws;
            Outcomes = new ScriptedOutcomes(_directory.Path);
            using Process self = Process.GetCurrentProcess();
            File.WriteAllText(Path.Combine(_directory.Path, "scripted-outcomes.marker"),
                $"format=1\npid={Environment.ProcessId}\ncreated={self.StartTime.ToFileTimeUtc()}\nexe={self.MainModule?.FileName}\n");
            _thread = new Thread(Run) { IsBackground = true, Name = "fake scripted build" };
            _thread.Start();
        }

        public ScriptedOutcomes Outcomes { get; }

        /// <summary>Every queue line the build took, in order.</summary>
        public List<string> Taken { get; } = [];

        public static FakeScriptedBuild Start(bool refuse = false, bool draws = true) => new(refuse, draws);

        private void Run()
        {
            string queue = Path.Combine(_directory.Path, "queue.txt");
            string log = Path.Combine(_directory.Path, "draws.log");
            while (!_stopping.IsCancellationRequested)
            {
                Thread.Sleep(5);
                if (!_draws || !File.Exists(queue))
                    continue;

                string line;
                try
                {
                    line = File.ReadAllText(queue).Trim();
                    File.Delete(queue);
                }
                catch (IOException)
                {
                    continue;
                }

                lock (Taken)
                    Taken.Add(line);

                string id = line.Split(' ')[0];
                File.AppendAllText(log, _refuse
                    ? $"1 refused {id}: no alternative starts with a draw of 0..19\n"
                    : $"1 forced {id} slot 1/1 0..15 -> 0\n1 done {id} slot\n");
            }
        }

        public void Dispose()
        {
            _stopping.Cancel();
            _thread.Join();
            _directory.Dispose();
        }
    }
}
