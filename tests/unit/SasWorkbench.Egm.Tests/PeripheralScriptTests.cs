using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The service button and the peripheral faults end to end through the script runner, and the
/// mechanisms the platform does not have skipping rather than failing.
/// </summary>
public class PeripheralScriptTests
{
    private static TestOutcome Run(string script)
    {
        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(5),
            QuietGap = TimeSpan.FromMilliseconds(15),
        });
        return runner.Run(ScriptParser.Parse(script, "peripherals.sas").Tests[0]);
    }

    private static string Explain(TestOutcome result) =>
        string.Join('\n', result.Steps.Select(s => s.ToString()));

    [Fact]
    public void The_service_button_and_a_printer_fault_reach_the_host_as_the_platform_reports_them()
    {
        TestOutcome result = Run("""
            requires stimulus.button.service, stimulus.printer_fault, stimulus.settle
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            egm press service
            host expect exception change_lamp_on within 5s
            egm printer fault disconnected
            host expect exception printer_power_off within 5s
            egm settle
            host expect exception printer_power_on within 5s
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }

    [Theory]
    [InlineData("stimulus.coin")]
    [InlineData("stimulus.hopper")]
    [InlineData("stimulus.reel")]
    [InlineData("stimulus.note_fault.reversed")]
    public void A_mechanism_the_platform_lacks_skips_the_case(string capability)
    {
        TestOutcome result = Run($"""
            requires {capability}
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            """);

        Assert.Equal(Verdict.Skip, result.Verdict);
        Assert.StartsWith(capability + ":", result.SkipReason!, StringComparison.Ordinal);
    }
}
