using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Qpm.Tests;

/// <summary>
/// What the backend refuses matters more than what it does. Every refusal here has to carry a
/// reason a person can act on, because it becomes the skip line of a report — and a skip that
/// reads "not available" teaches people to stop reading skips.
/// </summary>
public class QpmStimulusBackendTests
{
    [Fact]
    public void ItOffersTheDoorsAndButtonsTheMachineDeclares()
    {
        using var directory = new TemporaryDirectory();
        using var egm = FakeGamingMachine.Start(directory.Path);
        QpmMachineTests.WriteConfiguration(directory.Path);

        using var backend = new QpmStimulusBackend(
            QpmMachine.Attach(egm.Path, directory.Path, FakeGamingMachine.Timings));

        Assert.Equal("qpm", backend.Capabilities["stimulus"]);
        Assert.True(backend.Capabilities.ContainsKey("stimulus.door.slot"));
        Assert.True(backend.Capabilities.ContainsKey("stimulus.button.collect"));
        Assert.True(backend.Capabilities.ContainsKey("stimulus.bill"));
    }

    [Fact]
    public void ItOffersNoDoorAtAllWhenTheMachineNeverSaidWhatItsDoorsAre()
    {
        using var directory = new TemporaryDirectory();
        using var egm = FakeGamingMachine.Start(directory.Path);

        using var backend = new QpmStimulusBackend(
            QpmMachine.Attach(egm.Path, null, FakeGamingMachine.Timings));

        Assert.DoesNotContain(backend.Capabilities.Keys,
            key => key.StartsWith("stimulus.door.", StringComparison.Ordinal));
    }

    [Fact]
    public void ADoorTheMachineDoesNotHaveIsSkippedWithTheListOfOnesItDoes()
    {
        using var directory = new TemporaryDirectory();
        using var egm = FakeGamingMachine.Start(directory.Path);
        QpmMachineTests.WriteConfiguration(directory.Path);

        using var backend = new QpmStimulusBackend(
            QpmMachine.Attach(egm.Path, directory.Path, FakeGamingMachine.Timings));

        StimulusUnavailableException refused = Assert.Throws<StimulusUnavailableException>(
            () => backend.Apply(new DoorStep(SourceLocation.None, "moon_roof", Open: true)));

        Assert.Contains("moon_roof", refused.Reason, StringComparison.Ordinal);
        Assert.Contains("slot", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void PlayingGamesIsSkippedBecauseTheMachineDecidesItsOwnOutcomes()
    {
        using var directory = new TemporaryDirectory();
        using var egm = FakeGamingMachine.Start(directory.Path);
        QpmMachineTests.WriteConfiguration(directory.Path);

        using var backend = new QpmStimulusBackend(
            QpmMachine.Attach(egm.Path, directory.Path, FakeGamingMachine.Timings));

        StimulusUnavailableException refused = Assert.Throws<StimulusUnavailableException>(
            () => backend.Apply(new PlayGamesStep(SourceLocation.None, 2, 100, 50)));

        Assert.Contains("decides its own outcomes", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ANoteGoesInThroughTheAcceptorMask()
    {
        using var directory = new TemporaryDirectory();
        using var egm = FakeGamingMachine.Start(directory.Path);
        QpmMachineTests.WriteConfiguration(directory.Path);

        using var backend = new QpmStimulusBackend(
            QpmMachine.Attach(egm.Path, directory.Path, FakeGamingMachine.Timings));

        backend.Apply(new InsertBillStep(SourceLocation.None, 2000));

        Assert.Equal(0u, egm.Mask(QpmLayout.Bna));
    }

    [Fact]
    public void AMachineThatIsNotRunningBecomesASkipRatherThanAnErrorThatStopsTheSuite()
    {
        using var directory = new TemporaryDirectory();
        string idle = FakeGamingMachine.StartNothing(directory.Path);

        IStimulusBackend backend = QpmEnvironment.Attach(
            new QpmOptions(idle, directory.Path, FakeGamingMachine.Timings));

        StimulusUnavailableException refused = Assert.Throws<StimulusUnavailableException>(
            () => backend.Apply(new InsertBillStep(SourceLocation.None, 2000)));

        Assert.Contains("heartbeat", refused.Reason, StringComparison.Ordinal);
    }
}
