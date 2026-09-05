namespace SasWorkbench.Qpm.Tests;

/// <summary>
/// The driver's job is to be honest about whether a stimulus actually happened. Writing to the
/// shared region always succeeds — the bytes land whether or not anybody is reading them — so
/// every test here is about the difference between "written" and "seen".
/// </summary>
public class QpmMachineTests
{
    private static readonly EgmDoor Main = new("main", 2);
    private static readonly EgmButton Collect = new("Collect", "collect", 0);

    [Fact]
    public void AMissingRegionSaysWhereItLookedAndWhyItIsMissing()
    {
        using var directory = new TemporaryDirectory();
        string absent = Path.Combine(directory.Path, "not-there.shm");

        QpmUnavailableException refused =
            Assert.Throws<QpmUnavailableException>(() => QpmSharedRegion.Open(absent));

        Assert.Contains(absent, refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileTooSmallToBeTheRegionIsRefusedRatherThanReadPastItsEnd()
    {
        using var directory = new TemporaryDirectory();
        string truncated = FakeGamingMachine.StartNothing(directory.Path, bytes: 64);

        QpmUnavailableException refused =
            Assert.Throws<QpmUnavailableException>(() => QpmSharedRegion.Open(truncated));

        Assert.Contains("64 bytes", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AMachineThatIsNotRunningIsRefusedRatherThanWrittenTo()
    {
        using var directory = new TemporaryDirectory();
        string idle = FakeGamingMachine.StartNothing(directory.Path);

        QpmUnavailableException refused = Assert.Throws<QpmUnavailableException>(
            () => QpmMachine.Attach(idle, null, FakeGamingMachine.Timings));

        Assert.Contains("heartbeat", refused.Reason, StringComparison.Ordinal);
        Assert.Contains("silently lost", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunningMachineIsAccepted()
    {
        using var directory = new TemporaryDirectory();
        using var egm = FakeGamingMachine.Start(directory.Path);

        using QpmMachine machine =
            QpmMachine.Attach(egm.Path, null, FakeGamingMachine.Timings);

        Assert.Equal(egm.Path, machine.RegionPath);
    }

    [Fact]
    public void SomethingElseRewritingTheMasksIsRefusedRatherThanRacedWith()
    {
        using var directory = new TemporaryDirectory();
        using var egm = FakeGamingMachine.Start(directory.Path, clobbersTheMasks: true);

        QpmUnavailableException refused = Assert.Throws<QpmUnavailableException>(
            () => QpmMachine.Attach(egm.Path, null, FakeGamingMachine.Timings));

        Assert.Contains("something else is writing", refused.Reason, StringComparison.Ordinal);
        Assert.Contains("simulator", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ADoorSetsItsOwnBitAndLeavesTheOthersAlone()
    {
        using var directory = new TemporaryDirectory();
        using var egm = FakeGamingMachine.Start(directory.Path);
        using QpmMachine machine = QpmMachine.Attach(egm.Path, null, FakeGamingMachine.Timings);

        machine.SetDoor(Main, open: true);
        Assert.Equal(1u << 2, egm.Mask(QpmLayout.Intrusion));

        machine.SetDoor(new EgmDoor("stacker", 5), open: true);
        Assert.Equal((1u << 2) | (1u << 5), egm.Mask(QpmLayout.Intrusion));

        machine.SetDoor(Main, open: false);
        Assert.Equal(1u << 5, egm.Mask(QpmLayout.Intrusion));
    }

    [Fact]
    public void AButtonIsPressedAndReleased()
    {
        using var directory = new TemporaryDirectory();
        using var egm = FakeGamingMachine.Start(directory.Path);
        using QpmMachine machine = QpmMachine.Attach(egm.Path, null, FakeGamingMachine.Timings);

        machine.PressButton(Collect);

        Assert.Equal(0u, egm.Mask(QpmLayout.Dio));
    }

    [Fact]
    public void AButtonIsHeldForAtLeastTwoHeartbeatsSoTheMachineCanSampleIt()
    {
        using var directory = new TemporaryDirectory();
        using var egm = FakeGamingMachine.Start(directory.Path);
        using QpmMachine machine = QpmMachine.Attach(egm.Path, null, FakeGamingMachine.Timings);

        uint before = egm.Mask(QpmLayout.Heartbeat);
        machine.SetButton(Collect, pressed: true);
        uint after = egm.Mask(QpmLayout.Heartbeat);

        Assert.True(after - before >= 2,
            $"the button was held for {after - before} heartbeat(s), and a machine sampling once "
            + "per heartbeat can miss anything shorter than two");
    }

    [Fact]
    public void ANoteTheMachineCannotReadIsRefusedAndTheOnesItCanAreNamed()
    {
        using var directory = new TemporaryDirectory();
        using var egm = FakeGamingMachine.Start(directory.Path);
        using QpmMachine machine = QpmMachine.Attach(egm.Path, null, FakeGamingMachine.Timings);

        QpmUnavailableException refused =
            Assert.Throws<QpmUnavailableException>(() => machine.InsertNote(2500));

        Assert.Contains("2500", refused.Reason, StringComparison.Ordinal);
        Assert.Contains("2000", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ANoteRisesAndFallsOnItsOwnBit()
    {
        using var directory = new TemporaryDirectory();
        using var egm = FakeGamingMachine.Start(directory.Path);
        using QpmMachine machine = QpmMachine.Attach(egm.Path, null, FakeGamingMachine.Timings);

        machine.InsertNote(2000);

        // Bit four is the twenty-unit note in the machine's own table.
        Assert.Equal(4, QpmLayout.NoteBit(2000));
        Assert.Equal(0u, egm.Mask(QpmLayout.Bna));
    }

    [Fact]
    public void SettlingCyclesEveryDoorTheMachineDeclaresAndSaysSo()
    {
        using var directory = new TemporaryDirectory();
        using var egm = FakeGamingMachine.Start(directory.Path);
        WriteConfiguration(directory.Path);

        using QpmMachine machine =
            QpmMachine.Attach(egm.Path, directory.Path, FakeGamingMachine.Timings);

        IReadOnlyList<string> done = machine.Settle();

        Assert.Contains(done, line => line.Contains("main", StringComparison.Ordinal));
        Assert.Contains(done, line => line.Contains("note acceptor", StringComparison.Ordinal));
        Assert.Equal(0u, egm.Mask(QpmLayout.Intrusion));
        Assert.Equal(0u, egm.Mask(QpmLayout.Bna));
    }

    [Fact]
    public void SettlingAMachineWhoseDoorsAreUnknownSaysThatRatherThanClaimingItSettledThem()
    {
        using var directory = new TemporaryDirectory();
        using var egm = FakeGamingMachine.Start(directory.Path);
        using QpmMachine machine = QpmMachine.Attach(egm.Path, null, FakeGamingMachine.Timings);

        IReadOnlyList<string> done = machine.Settle();

        Assert.Contains(done, line => line.Contains("no doors were touched", StringComparison.Ordinal));
    }

    internal static void WriteConfiguration(string directory)
    {
        File.WriteAllText(Path.Combine(directory, "configuration.json"), """
            {
              "Hardware": {
                "Doors": [
                  { "type": "tamper#1", "intrusion": 0, "contacts": "NO" },
                  { "type": "main",     "intrusion": 2, "contacts": "NO" },
                  { "type": "topbox",   "intrusion": 3, "contacts": "NO" },
                  { "type": "logic",    "intrusion": 4, "contacts": "NO" },
                  { "type": "stacker",  "intrusion": 5, "contacts": "NO" }
                ]
              }
            }
            """);

        File.WriteAllText(Path.Combine(directory, "game_configuration.json"), """
            {
              "buttons": {
                "digital_inputs": [
                  { "dio_line": 0, "button_type": "collect",   "data": 0, "label": "Collect" },
                  { "dio_line": 1, "button_type": "none",      "data": 0, "label": null },
                  { "dio_line": 4, "button_type": "play",      "data": 0, "label": "Play" },
                  { "dio_line": 5, "button_type": "keyswitch", "data": 0, "label": "Audit" },
                  { "dio_line": 7, "button_type": "bet",       "data": 1, "label": "Bet x1" },
                  { "dio_line": 8, "button_type": "bet",       "data": 2, "label": "Bet x2" }
                ]
              }
            }
            """);
    }
}
