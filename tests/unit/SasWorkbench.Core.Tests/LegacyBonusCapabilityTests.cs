using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// What a legacy bonusing case needs to learn before it runs: whether the machine takes legacy
/// bonuses and multiplied jackpots, both asked of the machine off its enabled-features reply, and
/// the operator's switch a script can work where the backend has a hand on it.
/// </summary>
public class LegacyBonusCapabilityTests
{
    [Fact]
    public void Legacy_bonus_and_multiplied_jackpots_are_asked_of_the_machine()
    {
        Assert.True(CapabilityProbe.IsAskedOfTheMachine("machine.legacy_bonus"));
        Assert.True(CapabilityProbe.IsAskedOfTheMachine("machine.multiplied_jackpot"));
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("off", false)]
    public void The_legacy_bonusing_switch_is_a_stimulus_step(string word, bool on)
    {
        ScriptTest test = ScriptParser.Parse($"egm set legacy bonus {word}").Tests[0];

        var step = Assert.IsType<SetLegacyBonusStep>(Assert.Single(test.Steps));
        Assert.Equal(on, step.On);
        Assert.Equal("stimulus.legacy_bonus", step.RequiredCapability);
    }

    [Fact]
    public void A_legacy_bonusing_switch_with_no_position_is_refused()
    {
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse("egm set legacy bonus maybe"));
    }

    [Fact]
    public void The_software_machine_and_a_person_can_work_the_switch_and_nothing_else_can()
    {
        Assert.True(CapabilityProbe.ForStimulus(StimulusBackend.Software).ContainsKey("stimulus.legacy_bonus"));
        Assert.True(CapabilityProbe.ForStimulus(StimulusBackend.Manual).ContainsKey("stimulus.legacy_bonus"));
        Assert.False(CapabilityProbe.ForStimulus(StimulusBackend.Qpm).ContainsKey("stimulus.legacy_bonus"));
        Assert.False(CapabilityProbe.ForStimulus(StimulusBackend.None).ContainsKey("stimulus.legacy_bonus"));
    }
}
