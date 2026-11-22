using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// The setter steps and capabilities of the chapter 16 group: turning transfers on and off and setting the
/// credit limit at the machine's configuration, and the transfer limit read off the machine.
/// </summary>
public sealed class MachineLimitStepTests
{
    [Theory]
    [InlineData("on", true)]
    [InlineData("off", false)]
    public void Transfers_are_turned_on_and_off_at_the_machine(string word, bool on)
    {
        ScriptTest test = ScriptParser.Parse($"egm set aft {word}").Tests[0];

        var step = Assert.IsType<SetAftStep>(Assert.Single(test.Steps));
        Assert.Equal(on, step.On);
        Assert.Equal("stimulus.aft_configuration", step.RequiredCapability);
    }

    [Fact]
    public void The_credit_limit_is_set_in_cents()
    {
        ScriptTest test = ScriptParser.Parse("egm set credit limit 100000").Tests[0];

        var step = Assert.IsType<SetCreditLimitStep>(Assert.Single(test.Steps));
        Assert.Equal(100_000, step.Cents);
        Assert.Equal("stimulus.credit_limit", step.RequiredCapability);
    }

    [Theory]
    [InlineData("egm set credit limit")]
    [InlineData("egm set credit limit -5")]
    [InlineData("egm set credit limit lots")]
    [InlineData("egm set aft maybe")]
    public void A_malformed_setter_is_refused(string line)
    {
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse(line));
    }

    [Fact]
    public void The_transfer_limit_is_asked_of_the_machine()
    {
        Assert.True(CapabilityProbe.IsAskedOfTheMachine("machine.transfer_limit"));
    }

    [Fact]
    public void The_software_machine_has_a_credit_limit_restricted_credit_and_a_configuration_a_case_can_work()
    {
        IReadOnlyDictionary<string, string?> software = CapabilityProbe.ForStimulus(StimulusBackend.Software);

        foreach (string key in new[]
                 {
                     "machine.credit_limit", "machine.restricted_credits", "machine.configuration_options",
                     "stimulus.credit_limit", "stimulus.aft_configuration",
                 })
        {
            Assert.True(software.ContainsKey(key), key);
        }

        IReadOnlyDictionary<string, string?> manual = CapabilityProbe.ForStimulus(StimulusBackend.Manual);
        Assert.True(manual.ContainsKey("stimulus.credit_limit"));
        Assert.True(manual.ContainsKey("stimulus.aft_configuration"));
        Assert.False(manual.ContainsKey("machine.restricted_credits"));
    }
}
