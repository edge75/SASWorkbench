namespace SasWorkbench.Qpm.Tests;

/// <summary>
/// Every index used to poke the machine comes from the machine's own configuration. These tests
/// exist because the peripheral simulator labels the same bits differently, and a driver that
/// trusted the labels would open a door the test did not ask for and then assert on the exception
/// a different door produced — a failure that reads as a protocol defect and is not one.
/// </summary>
public class EgmHardwareProfileTests
{
    [Fact]
    public void DoorsAreFoundByTheMachinesWordForThemAndByTheProtocolsWord()
    {
        using var directory = new TemporaryDirectory();
        QpmMachineTests.WriteConfiguration(directory.Path);

        EgmHardwareProfile profile = EgmHardwareProfile.ReadFrom(directory.Path);

        Assert.True(profile.TryFindDoor("main", out EgmDoor main));
        Assert.Equal(2, main.Intrusion);

        Assert.True(profile.TryFindDoor("slot", out EgmDoor slot));
        Assert.Equal(2, slot.Intrusion);

        Assert.True(profile.TryFindDoor("cashbox", out EgmDoor cashbox));
        Assert.Equal(5, cashbox.Intrusion);

        Assert.True(profile.TryFindDoor("tamper1", out EgmDoor tamper));
        Assert.Equal(0, tamper.Intrusion);
    }

    [Fact]
    public void ButtonsAreFoundByTheirLabelAndByWhatTheyDo()
    {
        using var directory = new TemporaryDirectory();
        QpmMachineTests.WriteConfiguration(directory.Path);

        EgmHardwareProfile profile = EgmHardwareProfile.ReadFrom(directory.Path);

        Assert.True(profile.TryFindButton("Collect", out EgmButton collect));
        Assert.Equal(0, collect.DioLine);

        Assert.True(profile.TryFindButton("play", out EgmButton play));
        Assert.Equal(4, play.DioLine);

        Assert.True(profile.TryFindButton("bet_x1", out EgmButton bet));
        Assert.Equal(7, bet.DioLine);
    }

    [Fact]
    public void AKindTwoButtonsShareIsNotOfferedAsAName()
    {
        using var directory = new TemporaryDirectory();
        QpmMachineTests.WriteConfiguration(directory.Path);

        EgmHardwareProfile profile = EgmHardwareProfile.ReadFrom(directory.Path);

        // Two bet buttons exist, so pressing "bet" would be pressing whichever was read first.
        Assert.False(profile.TryFindButton("bet", out _));
        Assert.True(profile.TryFindButton("bet_x2", out _));
    }

    [Fact]
    public void ALineTheMachineSaysDoesNothingIsNotOfferedAsAButton()
    {
        using var directory = new TemporaryDirectory();
        QpmMachineTests.WriteConfiguration(directory.Path);

        EgmHardwareProfile profile = EgmHardwareProfile.ReadFrom(directory.Path);

        Assert.DoesNotContain(profile.Buttons, b => b.DioLine == 1);
    }

    [Fact]
    public void AMachineWithNoConfigurationOffersNothingAndCarriesTheReason()
    {
        using var directory = new TemporaryDirectory();

        EgmHardwareProfile profile = EgmHardwareProfile.ReadFrom(directory.Path);

        Assert.Empty(profile.Doors);
        Assert.Empty(profile.Buttons);
        Assert.NotNull(profile.UnavailableBecause);
    }

    [Fact]
    public void ADirectoryThatDoesNotExistIsAReasonRatherThanAnException()
    {
        EgmHardwareProfile profile = EgmHardwareProfile.ReadFrom("no/such/place");

        Assert.NotNull(profile.UnavailableBecause);
        Assert.Contains("no/such/place", profile.UnavailableBecause, StringComparison.Ordinal);
    }

    [Fact]
    public void UnreadableConfigurationIsAReasonRatherThanAnException()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "configuration.json"), "{ not json");

        EgmHardwareProfile profile = EgmHardwareProfile.ReadFrom(directory.Path);

        Assert.NotNull(profile.UnavailableBecause);
    }
}
