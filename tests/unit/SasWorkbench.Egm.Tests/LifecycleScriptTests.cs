using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// A power cycle, a memory fault and a memory clear end to end through the script runner, and the
/// memory faults the platform never reports skipping rather than failing.
/// </summary>
public class LifecycleScriptTests
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
        return runner.Run(ScriptParser.Parse(script, "lifecycle.sas").Tests[0]);
    }

    private static string Explain(TestOutcome result) =>
        string.Join('\n', result.Steps.Select(s => s.ToString()));

    [Fact]
    public void A_power_cycle_reaches_the_host_as_power_lost_then_power_applied()
    {
        TestOutcome result = Run("""
            requires machine.power_cycle
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            egm power cycle
            host expect link up within 5s
            host expect exception ac_power_lost within 5s
            host expect exception ac_power_applied within 5s
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }

    [Fact]
    public void A_memory_error_and_the_memory_clear_reach_the_host()
    {
        TestOutcome result = Run("""
            requires machine.memory_fault, machine.nvram_clear
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            egm memory fault cmos-lost
            host expect exception cmosram_error_no_data_recovered within 5s
            egm nvram clear
            host expect link up within 5s
            host expect exception memory_error_reset within 5s
            host expect exception gaming_machine_soft_meters_reset_to_zero within 5s
            host expect exception operator_changed_options within 5s
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }

    [Theory]
    [InlineData("machine.memory_fault.cmos_recovered")]
    [InlineData("machine.memory_fault.cmos_bad_device")]
    [InlineData("machine.memory_fault.eeprom")]
    [InlineData("machine.memory_fault.eprom_checksum")]
    [InlineData("machine.memory_fault.partitioned_eprom")]
    public void A_memory_fault_the_platform_never_reports_skips_the_case(string capability)
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
