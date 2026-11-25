using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// The host timing and line group's script half: the chirp, the poll without its wakeup bit, the
/// machine's poll address and the host following it, the repeat of an exception in time, and who
/// offers each.
/// </summary>
public sealed class LineScriptTests
{
    private static IReadOnlyList<ScriptStep> Steps(string script) => ScriptParser.Parse(script).Tests[0].Steps;

    [Fact]
    public void The_chirp_is_expected_within_a_time_or_its_absence_for_one()
    {
        IReadOnlyList<ScriptStep> steps = Steps("host expect chirp within 3s\nhost expect no chirp for 4s");

        ExpectChirpStep heard = Assert.IsType<ExpectChirpStep>(steps[0]);
        Assert.False(heard.Negated);
        Assert.Equal(TimeSpan.FromSeconds(3), heard.Within);

        ExpectChirpStep quiet = Assert.IsType<ExpectChirpStep>(steps[1]);
        Assert.True(quiet.Negated);
        Assert.Equal(TimeSpan.FromSeconds(4), quiet.Within);
    }

    [Fact]
    public void A_send_ending_without_wakeup_keeps_its_poll_and_data()
    {
        SendWithoutWakeupStep step = Assert.IsType<SendWithoutWakeupStep>(
            Steps("host send lp 7F data 06152031103000 without wakeup")[0]);

        Assert.Equal(0x7F, step.Poll.Command);
        Assert.Equal(Convert.FromHexString("06152031103000"), step.Poll.Data);
        Assert.EndsWith("without wakeup", step.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_operator_sets_any_address_and_the_host_follows_to_any_but_00()
    {
        IReadOnlyList<ScriptStep> steps = Steps("egm set address 00\negm set address 7F\nhost poll address 26");

        Assert.Equal(SasAddress.Sync, Assert.IsType<SetPollAddressStep>(steps[0]).Address);
        Assert.Equal(SasAddress.Of(0x7F), Assert.IsType<SetPollAddressStep>(steps[1]).Address);
        Assert.Equal(SasAddress.Of(0x26), Assert.IsType<FollowAddressStep>(steps[2]).Address);
    }

    [Fact]
    public void An_exception_repeat_names_its_period()
    {
        ExpectExceptionRepeatStep step = Assert.IsType<ExpectExceptionRepeatStep>(
            Steps("host expect exception aft_transfer_complete every 15s")[0]);

        Assert.Equal(0x69, step.Code);
        Assert.Equal(TimeSpan.FromSeconds(15), step.Period);
    }

    [Theory]
    [InlineData("host expect chirp")]
    [InlineData("host expect chirp for 3s")]
    [InlineData("host expect no chirp within 3s")]
    [InlineData("host send lp 1F to 7F without wakeup")]
    [InlineData("host poll address 00")]
    [InlineData("host poll address 80")]
    [InlineData("egm set address 80")]
    [InlineData("host expect exception aft_transfer_complete every")]
    [InlineData("host expect exception nothing_at_all every 1s")]
    public void A_malformed_line_step_is_refused(string script) =>
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse(script));

    [Fact]
    public void The_host_capabilities_are_the_build_s_and_the_address_is_a_stimulus_both_backends_offer()
    {
        CapabilitySet baseline = CapabilityProbe.Baseline();
        Assert.True(baseline.Has("host.chirp"));
        Assert.True(baseline.Has("host.poll_without_wakeup"));
        Assert.True(baseline.Has("host.timing"));
        Assert.True(baseline.Has("decoder.lp7F"));
        Assert.False(baseline.Has("host.collision"));
        Assert.False(baseline.Has("host.byte_timing"));

        var step = new SetPollAddressStep(new SourceLocation("t.sas", 1), SasAddress.Of(2));
        Assert.Equal("yes", CapabilityProbe.ForStimulus(StimulusBackend.Software)[step.RequiredCapability]);
        Assert.Equal("yes", CapabilityProbe.ForStimulus(StimulusBackend.Manual)[step.RequiredCapability]);
    }
}
