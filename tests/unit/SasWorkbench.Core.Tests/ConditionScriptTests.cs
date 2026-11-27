using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// The script half of group 18 (D62): a poll with a broken check sequence, the machine's own conditions, period
/// meters, the credit-meter key, restricted tickets, the memory devices declared and who offers each.
/// </summary>
public sealed class ConditionScriptTests
{
    private static ScriptTest Test(string body) => ScriptParser.Parse($"test \"t\" {{\n{body}\n}}").Tests[0];

    private static IReadOnlyList<ScriptStep> Steps(string body) => Test(body).Steps;

    [Fact]
    public void A_send_ending_with_bad_crc_keeps_its_poll_and_data()
    {
        SendBadCrcStep step = Assert.IsType<SendBadCrcStep>(Steps("host send lp 21 data 1234 with bad crc")[0]);

        Assert.Equal(0x21, step.Poll.Command);
        Assert.Equal(new byte[] { 0x12, 0x34 }, step.Poll.Data);
        Assert.EndsWith("with bad crc", step.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_conditions_parse_to_their_steps_and_capabilities()
    {
        IReadOnlyList<ScriptStep> steps = Steps(
            "egm tilt on\negm out of service off\negm battery low\negm period meters clear\negm handpay reset credits\negm set restricted tickets on");

        MachineConditionStep tilt = Assert.IsType<MachineConditionStep>(steps[0]);
        Assert.Equal((MachineCondition.GeneralTilt, true), (tilt.Condition, tilt.On));
        Assert.Equal(MachineConditionCapabilities.Tilt, tilt.RequiredCapability);

        MachineConditionStep service = Assert.IsType<MachineConditionStep>(steps[1]);
        Assert.Equal((MachineCondition.OutOfService, false), (service.Condition, service.On));
        Assert.Equal(MachineConditionCapabilities.OutOfService, service.RequiredCapability);

        Assert.Equal(MachineConditionCapabilities.Battery, Assert.IsType<MachineConditionStep>(steps[2]).RequiredCapability);
        Assert.Equal(MachineConditionCapabilities.PeriodMeterClear, Assert.IsType<ClearPeriodMetersStep>(steps[3]).RequiredCapability);
        Assert.Equal(MachineConditionCapabilities.CreditReset, Assert.IsType<HandpayCreditResetStep>(steps[4]).RequiredCapability);
        Assert.True(Assert.IsType<SetRestrictedTicketsStep>(steps[5]).On);
    }

    [Fact]
    public void Memory_found_invalid_at_start_up_needs_its_own_capability()
    {
        MemoryFaultStep step = Assert.IsType<MemoryFaultStep>(Steps("egm memory fault at-start-up")[0]);

        Assert.Equal(MemoryFaultKind.AtStartUp, step.Kind);
        Assert.Equal("machine.memory_fault.at_start_up", step.RequiredCapability);
    }

    [Fact]
    public void The_declarations_claim_their_capabilities_and_require_only_period_meters_and_the_key()
    {
        MachineDevicesDeclaration devices = Test("machine memory faults on\nmachine period meters on\nmachine credit key on").Devices!;

        Assert.True(devices.MemoryFaults && devices.PeriodMeters && devices.CreditKey);
        Assert.Equal(
            [MachineConditionCapabilities.PeriodMeters, MachineConditionCapabilities.CreditKey],
            devices.AsRequirements().Select(r => r.Key));

        var claims = devices.Claims().Select(c => c.Key).ToList();
        Assert.Contains("machine.memory_fault.eeprom", claims);
        Assert.Contains(MachineConditionCapabilities.PeriodMeterClear, claims);
        Assert.Contains(MachineConditionCapabilities.CreditReset, claims);
    }

    [Theory]
    [InlineData("host send lp 1F with bad crc")]
    [InlineData("host send lp 21 data 1234 to 02 with bad crc")]
    [InlineData("egm tilt")]
    [InlineData("egm out of service")]
    [InlineData("egm battery flat")]
    [InlineData("egm period meters")]
    [InlineData("egm set restricted tickets maybe")]
    [InlineData("machine memory faults on\nmachine memory faults on")]
    public void A_malformed_line_is_refused(string body) =>
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse($"test \"t\" {{\n{body}\n}}"));

    [Fact]
    public void The_software_machine_offers_the_platform_s_conditions_and_the_manual_backend_every_one()
    {
        IReadOnlyDictionary<string, string?> software = CapabilityProbe.ForStimulus(StimulusBackend.Software);
        IReadOnlyDictionary<string, string?> manual = CapabilityProbe.ForStimulus(StimulusBackend.Manual);

        foreach (string key in new[]
                 {
                     MachineConditionCapabilities.GeneralTilt, MachineConditionCapabilities.OutOfService,
                     MachineConditionCapabilities.Battery, MachineConditionCapabilities.RestrictedTickets,
                     "machine.memory_fault.at_start_up",
                 })
        {
            Assert.True(software.ContainsKey(key), key);
            Assert.True(manual.ContainsKey(key), key);
        }

        Assert.False(software.ContainsKey(MachineConditionCapabilities.PeriodMeterClear));
        Assert.False(software.ContainsKey(MachineConditionCapabilities.CreditReset));
        Assert.Equal("64", software[MachineConditionCapabilities.ExceptionQueueDepth]);
        Assert.True(CapabilityProbe.Baseline().Has(SendBadCrcStep.Capability));
    }
}
