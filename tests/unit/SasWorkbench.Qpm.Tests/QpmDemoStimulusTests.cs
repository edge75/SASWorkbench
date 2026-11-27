using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Qpm.Tests;

/// <summary>
/// The real demo's own levers (D63): what each one presses, what it waits for on the wire, and - more often - what it
/// refuses and why. Every refusal ends the case, because the steps after it would measure a machine nobody drove.
/// </summary>
public class QpmDemoStimulusTests
{
    private const uint AuditLine = 1u << 5;
    private const uint PlayLine = 1u << 4;
    private const uint BetX2Line = 1u << 8;

    [Fact]
    public void TheAttendantMenuIsTheAuditKeyTurnedAndReturnedWithNoDoorOpened()
    {
        using var rig = Rig.Start();

        rig.Backend.Apply(new MenuStep(SourceLocation.None, MachineMenu.Attendant, Enter: true));
        Assert.Equal(AuditLine, rig.Egm.Mask(QpmLayout.Dio));
        Assert.Equal(0u, rig.Egm.Mask(QpmLayout.Intrusion));

        rig.Backend.Apply(new MenuStep(SourceLocation.None, MachineMenu.Attendant, Enter: false));
        Assert.Equal(0u, rig.Egm.Mask(QpmLayout.Dio));
        Assert.Equal("yes", rig.Backend.Capabilities[MenuStep.Capability]);
    }

    [Fact]
    public void TheOperatorMenuIsRefusedBecauseOnlyTouchReachesItAndTheRefusalEndsTheCase()
    {
        using var rig = Rig.Start();

        StimulusUnavailableException refused = Assert.Throws<StimulusUnavailableException>(
            () => rig.Backend.Apply(new MenuStep(SourceLocation.None, MachineMenu.Operator, Enter: true)));

        Assert.Contains("touch", refused.Reason, StringComparison.Ordinal);
        Assert.True(refused.EndsTheCase);
    }

    [Fact]
    public void ThereIsNoBetOneButtonBecauseTheBetButtonsChooseAMultiplier()
    {
        using var rig = Rig.Start();

        StimulusUnavailableException refused = Assert.Throws<StimulusUnavailableException>(
            () => rig.Backend.Apply(new BetButtonStep(SourceLocation.None)));

        Assert.Contains("multiplier", refused.Reason, StringComparison.Ordinal);
        Assert.False(rig.Backend.Capabilities.ContainsKey(PlayedGameCapabilities.BetButton));
    }

    [Fact]
    public void AJamSetsTheAcceptorsJamBitAndClearingTakesItAway()
    {
        using var rig = Rig.Start();

        rig.Backend.Apply(new PeripheralFaultStep(SourceLocation.None, PeripheralDevice.Note, "jam"));
        Assert.Equal(1u << QpmLayout.BnaJamBit, rig.Egm.Mask(QpmLayout.Bna));

        rig.Backend.Apply(new PeripheralFaultStep(SourceLocation.None, PeripheralDevice.Note, PeripheralFaultStep.Cleared));
        Assert.Equal(0u, rig.Egm.Mask(QpmLayout.Bna));
    }

    [Theory]
    [InlineData(PeripheralDevice.Note, "failure", "only the jam")]
    [InlineData(PeripheralDevice.Note, "cashbox_full", "only the jam")]
    [InlineData(PeripheralDevice.Printer, "paper_out", "printer mask")]
    public void OtherFaultsAreRefusedNamingWhatIsNotEstablished(PeripheralDevice device, string fault, string because)
    {
        using var rig = Rig.Start();

        StimulusUnavailableException refused = Assert.Throws<StimulusUnavailableException>(
            () => rig.Backend.Apply(new PeripheralFaultStep(SourceLocation.None, device, fault)));

        Assert.Contains(because, refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AWinWrittenAsZeroIsStillAnOutcomeTheMachineCannotBeAskedFor()
    {
        using var rig = Rig.Start(betCredits: 50);
        var play = new PlayGamesStep(SourceLocation.None, 1, 50, 0) { WinStated = true };

        StimulusUnavailableException refused = Assert.Throws<StimulusUnavailableException>(
            () => rig.Backend.Apply(play, null, new FakeHost()));

        Assert.Contains("decides its own outcomes", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void APlayWithNoWinPressesTheBetButtonThatWagersItThenPlayAndWaitsForTheGameToEnd()
    {
        using var rig = Rig.Start(betCredits: 50);
        var host = new FakeHost { Heard = { 0x7F } };

        rig.Backend.Apply(new PlayGamesStep(SourceLocation.None, 1, 100, 0), null, host);

        IReadOnlyList<uint> seen = rig.Egm.InputsSeen;
        int bet = IndexOf(seen, BetX2Line);
        int play = IndexOf(seen, PlayLine);
        Assert.True(bet >= 0 && play > bet, $"inputs seen: {string.Join(", ", seen)}");
    }

    [Fact]
    public void APlayWhoseGameNeverEndsIsRefusedRatherThanAssumedDone()
    {
        using var rig = Rig.Start(betCredits: 50);

        StimulusUnavailableException refused = Assert.Throws<StimulusUnavailableException>(
            () => rig.Backend.Apply(new PlayGamesStep(SourceLocation.None, 1, 50, 0), null, new FakeHost()));

        Assert.Contains("no game ended", refused.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(75L)]
    [InlineData(150L)]
    public void ABetNoMultiplierWagersIsRefusedListingTheOnesThatExist(long credits)
    {
        using var rig = Rig.Start(betCredits: 50);

        StimulusUnavailableException refused = Assert.Throws<StimulusUnavailableException>(
            () => rig.Backend.Apply(new PlayGamesStep(SourceLocation.None, 1, credits, 0), null, new FakeHost()));

        Assert.Contains("x1, x2", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutTheBetDeclaredPlayingIsNeitherOfferedNorDone()
    {
        using var rig = Rig.Start();

        Assert.False(rig.Backend.Capabilities.ContainsKey("stimulus.play"));
        StimulusUnavailableException refused = Assert.Throws<StimulusUnavailableException>(
            () => rig.Backend.Apply(new PlayGamesStep(SourceLocation.None, 1, 50, 0), null, new FakeHost()));

        Assert.Contains("--egm-bet-credits", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ASequencedCaseCannotHaveAPlayConfirmedSoItIsRefused()
    {
        using var rig = Rig.Start(betCredits: 50);

        StimulusUnavailableException refused = Assert.Throws<StimulusUnavailableException>(
            () => rig.Backend.Apply(new PlayGamesStep(SourceLocation.None, 1, 50, 0), null,
                new FakeHost { Unavailable = "sequenced" }));

        Assert.Contains("cannot be confirmed: sequenced", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AGameThatEndedBeforeGameEndIsReportedAsTooShortToHoldOpen()
    {
        using var rig = Rig.Start(betCredits: 50);
        var host = new FakeHost { Heard = { 0x7E, 0x7F } };

        rig.Backend.Apply(new StartGameStep(SourceLocation.None, 50), null, host);
        StimulusUnavailableException refused = Assert.Throws<StimulusUnavailableException>(
            () => rig.Backend.Apply(new EndGameStep(SourceLocation.None, 0), null, host));

        Assert.Contains("already ended", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheFirstEnabledGameCanBeChosen()
    {
        using var rig = Rig.Start(betCredits: 50);
        var host = new FakeHost { Replies = { [0x56] = [0x05, 0x02, 0x00, 0x03, 0x00, 0x04] } };

        StimulusUnavailableException refused = Assert.Throws<StimulusUnavailableException>(
            () => rig.Backend.Apply(new SelectGameStep(SourceLocation.None, 1), null, host));

        Assert.Contains("only game 3", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFirstEnabledGameAlreadyShownIsChosenWithoutAPress()
    {
        using var rig = Rig.Start(betCredits: 50);
        var host = new FakeHost
        {
            Replies = { [0x56] = [0x03, 0x01, 0x00, 0x01], [0x55] = [0x00, 0x01] },
        };

        rig.Backend.Apply(new SelectGameStep(SourceLocation.None, 1), null, host);

        Assert.DoesNotContain(rig.Egm.InputsSeen, inputs => inputs != 0);
        Assert.Equal("yes", rig.Backend.Capabilities["stimulus.select_game"]);
    }

    [Fact]
    public void PowerCyclingAndRegisteringNeedTheExecutableNamedAndAreOfferedOnlyThen()
    {
        using var rig = Rig.Start();

        Assert.False(rig.Backend.Capabilities.ContainsKey("machine.power_cycle"));
        Assert.False(rig.Backend.Capabilities.ContainsKey("stimulus.aft_registration"));
        Assert.Contains("--egm-exe", Assert.Throws<StimulusUnavailableException>(
            () => rig.Backend.Apply(new PowerCycleStep(SourceLocation.None))).Reason, StringComparison.Ordinal);
        Assert.Contains("--egm-exe", Assert.Throws<StimulusUnavailableException>(
            () => rig.Backend.Apply(new AftRegisterStep(SourceLocation.None))).Reason, StringComparison.Ordinal);

        IReadOnlyDictionary<string, string?> offer = QpmStimulusBackend.Offer(
            rig.Backend.Machine.Profile, rig.Directory.Path, new DemoProcess(Path.Combine(rig.Directory.Path, "demo.exe")));
        Assert.Equal("yes", offer["machine.power_cycle"]);
        Assert.Equal("yes", offer["stimulus.aft_registration"]);
    }

    [Fact]
    public void ADemoThatIsNotRunningFromTheDeclaredPathIsNeverTouched()
    {
        using var directory = new TemporaryDirectory();
        string missing = Path.Combine(directory.Path, "OpenGL_Game.exe");
        File.WriteAllBytes(missing, []);

        QpmUnavailableException refused = Assert.Throws<QpmUnavailableException>(
            () => new DemoProcess(missing).Restart(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

        Assert.Contains("no gaming machine is running from", refused.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AFT registration: request accepted, status now 0x80", 0x80)]
    [InlineData("AFT registration: request accepted, status now 0x01\r", 0x01)]
    public void TheStatusTheDemoLogsIsRead(string line, int status) =>
        Assert.Equal((byte)status, QpmStimulusBackend.StatusIn(line));

    [Theory]
    [InlineData("egm play games=1 bet=1", "cannot be placed")]
    [InlineData("egm play games=1 bet=50 win=0", "decides its own outcomes")]
    [InlineData("egm game end win=0", "decides its own outcomes")]
    [InlineData("egm menu operator enter", "touch")]
    [InlineData("egm note fault failure", "only the jam")]
    [InlineData("egm printer fault paper_out", "printer mask")]
    [InlineData("egm select game 0", "first enabled game")]
    [InlineData("egm power cycle", "--egm-exe")]
    [InlineData("egm handpay jackpot 150000", "decides its own outcomes")]
    [InlineData("egm bill insert 300", "no 300-cent note")]
    public void WhatCanNeverBeDoneIsRefusedBeforeTheCaseStarts(string line, string because)
    {
        using var rig = Rig.Start(betCredits: 50);
        StimulusStep step = Step(line);

        Assert.Contains(because, rig.Backend.Refusal(step), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("egm play games=2 bet=100")]
    [InlineData("egm game start bet=50")]
    [InlineData("egm game end")]
    [InlineData("egm menu attendant enter")]
    [InlineData("egm note fault jam")]
    [InlineData("egm select game 1")]
    [InlineData("egm door main open")]
    public void WhatMayWorkIsLeftToTheCase(string line)
    {
        using var rig = Rig.Start(betCredits: 50);
        StimulusStep step = Step(line);

        Assert.Null(rig.Backend.Refusal(step));
    }

    [Fact]
    public void TheButtonsCarryWhatTheMachinePassesWithThem()
    {
        using var directory = new TemporaryDirectory();
        Rig.WriteConfiguration(directory.Path);

        EgmHardwareProfile profile = EgmHardwareProfile.ReadFrom(directory.Path);

        Assert.True(profile.TryFindButton("bet_x2", out EgmButton bet));
        Assert.Equal(2, bet.Data);
    }

    private static StimulusStep Step(string line) =>
        (StimulusStep)ScriptParser.Parse("test \"t\" {" + Environment.NewLine + line + Environment.NewLine + "}", "t.sas")
            .Tests[0].Steps[0];

    private static int IndexOf(IReadOnlyList<uint> seen, uint line)
    {
        for (int i = 0; i < seen.Count; i++)
        {
            if ((seen[i] & line) != 0)
                return i;
        }

        return -1;
    }

    /// <summary>A machine, its configuration and a backend, cleaned up together.</summary>
    private sealed class Rig : IDisposable
    {
        private Rig(TemporaryDirectory directory, FakeGamingMachine egm, QpmStimulusBackend backend)
        {
            Directory = directory;
            Egm = egm;
            Backend = backend;
        }

        public TemporaryDirectory Directory { get; }

        public FakeGamingMachine Egm { get; }

        public QpmStimulusBackend Backend { get; }

        public static Rig Start(long? betCredits = null)
        {
            var directory = new TemporaryDirectory();
            var egm = FakeGamingMachine.Start(directory.Path);
            WriteConfiguration(directory.Path);
            var backend = new QpmStimulusBackend(
                QpmMachine.Attach(egm.Path, directory.Path, FakeGamingMachine.Timings), betCredits: betCredits);
            return new Rig(directory, egm, backend);
        }

        public static void WriteConfiguration(string directory)
        {
            QpmMachineTests.WriteConfiguration(directory);
            File.WriteAllText(Path.Combine(directory, "game_configuration.json"), """
                {
                  "buttons": {
                    "digital_inputs": [
                      { "dio_line": 0, "button_type": "collect",   "data": 0, "label": "Collect" },
                      { "dio_line": 4, "button_type": "play",      "data": 0, "label": "Play" },
                      { "dio_line": 5, "button_type": "keyswitch", "data": 0, "label": "Audit" },
                      { "dio_line": 6, "button_type": "game_menu", "data": 0, "label": "Game Menu" },
                      { "dio_line": 7, "button_type": "bet",       "data": 1, "label": "Bet x1" },
                      { "dio_line": 8, "button_type": "bet",       "data": 2, "label": "Bet x2" }
                    ]
                  }
                }
                """);
        }

        public void Dispose()
        {
            Backend.Dispose();
            Egm.Dispose();
            Directory.Dispose();
        }
    }

    /// <summary>A host that hears whichever exceptions it is told to, and answers long polls from a table.</summary>
    private sealed class FakeHost : IHostView
    {
        private long _sequence;

        public string? Unavailable { get; init; }

        /// <summary>Codes heard after every mark, as a machine that does what it was asked would raise them.</summary>
        public HashSet<byte> Heard { get; } = [];

        /// <summary>Payloads by command.</summary>
        public Dictionary<byte, byte[]> Replies { get; } = [];

        public long Mark() => ++_sequence;

        public ExceptionReport? ExceptionAfter(byte code, long mark, TimeSpan within) =>
            Heard.Contains(code) ? new ExceptionReport(code, SasChannel.One, TimeSpan.Zero, mark + 1) : null;

        public LongPollResult Ask(byte command, byte[] data)
        {
            if (!Replies.TryGetValue(command, out byte[]? payload))
                return new LongPollResult(command, SasChannel.One, LongPollOutcome.Silent, [], TimeSpan.Zero);

            byte[] frame = [0x01, command, .. payload, 0x00, 0x00];
            return new LongPollResult(command, SasChannel.One, LongPollOutcome.Answered, frame, TimeSpan.Zero);
        }
    }
}
