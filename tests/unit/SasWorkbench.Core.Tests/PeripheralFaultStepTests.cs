using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// The note acceptor and printer fault steps: how a script writes them, the capability each needs,
/// and which backends offer them.
/// </summary>
public class PeripheralFaultStepTests
{
    private static ScriptStep Only(string line) => ScriptParser.Parse(line).Tests[0].Steps[0];

    private static string TestOf(string line) => $$"""
        test "t" {
          {{line}}
        }
        """;

    [Theory]
    [InlineData("egm printer fault paper_out", PeripheralDevice.Printer, "paper_out", "stimulus.printer_fault")]
    [InlineData("egm printer fault cleared", PeripheralDevice.Printer, "cleared", "stimulus.printer_fault")]
    [InlineData("egm note fault counterfeit", PeripheralDevice.Note, "counterfeit", "stimulus.note_fault")]
    [InlineData("egm NOTE fault Rejected", PeripheralDevice.Note, "rejected", "stimulus.note_fault")]
    public void A_fault_is_read_with_its_device_and_capability(
        string line, PeripheralDevice device, string fault, string capability)
    {
        PeripheralFaultStep step = Assert.IsType<PeripheralFaultStep>(Only(TestOf(line)));

        Assert.Equal((device, fault, capability), (step.Device, step.Fault, step.RequiredCapability));
    }

    // A fault some machines cannot have at all needs its own capability, so a case that names it
    // skips up front on such a machine instead of failing the assertion after a skipped step.
    [Theory]
    [InlineData("egm note fault reversed", "stimulus.note_fault.reversed")]
    [InlineData("egm printer fault ribbon", "stimulus.printer_fault.ribbon")]
    public void A_fault_only_some_machines_can_have_needs_a_capability_of_its_own(string line, string capability)
    {
        Assert.Equal(capability, Assert.IsType<PeripheralFaultStep>(Only(TestOf(line))).RequiredCapability);
    }

    [Theory]
    [InlineData("egm printer fault")]
    [InlineData("egm printer jam")]
    [InlineData("egm printer fault smoke")]
    [InlineData("egm note fault paper_out")]
    [InlineData("egm note fault jam now")]
    public void A_fault_the_device_cannot_have_is_refused_when_the_script_is_read(string line)
    {
        ScriptParseException refused = Assert.Throws<ScriptParseException>(() => ScriptParser.Parse(TestOf(line)));

        Assert.Contains("fault", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_software_machine_offers_the_platform_faults_and_none_of_the_mechanisms_it_lacks()
    {
        IReadOnlyDictionary<string, string?> software = CapabilityProbe.ForStimulus(StimulusBackend.Software);

        foreach (string offered in new[] { "stimulus.button.service", "stimulus.note_fault", "stimulus.printer_fault" })
            Assert.Equal("yes", software[offered]);

        foreach (string absent in new[]
                 {
                     "stimulus.coin", "stimulus.hopper", "stimulus.reel", "machine.partial_pay",
                     "machine.external_coin_acceptor", "stimulus.note_fault.reversed", "stimulus.printer_fault.ribbon",
                 })
        {
            Assert.False(software.ContainsKey(absent), absent);
        }
    }

    [Fact]
    public void A_person_is_offered_every_fault()
    {
        IReadOnlyDictionary<string, string?> manual = CapabilityProbe.ForStimulus(StimulusBackend.Manual);

        foreach (string offered in new[]
                 {
                     "stimulus.note_fault", "stimulus.note_fault.reversed",
                     "stimulus.printer_fault", "stimulus.printer_fault.ribbon",
                 })
        {
            Assert.Equal("yes", manual[offered]);
        }
    }

    [Fact]
    public void A_person_is_told_how_to_bring_each_fault_about()
    {
        foreach ((PeripheralDevice device, IReadOnlyList<string> faults) in PeripheralFaultStep.Faults)
        {
            foreach (string fault in faults)
                Assert.False(string.IsNullOrWhiteSpace(new PeripheralFaultStep(default!, device, fault).Guidance), fault);
        }
    }
}
