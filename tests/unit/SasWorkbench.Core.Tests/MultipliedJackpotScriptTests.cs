using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Core.Tests;

/// <summary>The multiplied jackpot group's script half: the declaration, and that the machine answers it.</summary>
public sealed class MultipliedJackpotScriptTests
{
    [Fact]
    public void The_declaration_becomes_a_requirement_the_machine_answers()
    {
        ScriptTest test = ScriptParser.Parse("machine multiplied jackpot on").Tests[0];

        Assert.NotNull(test.MultipliedJackpot);
        Requirement requirement = test.MultipliedJackpot!.AsRequirement();
        Assert.Equal("machine.multiplied_jackpot", requirement.Key);
        Assert.True(CapabilityProbe.IsAskedOfTheMachine(requirement.Key));
    }

    [Theory]
    [InlineData("machine multiplied jackpot off")]
    [InlineData("machine multiplied jackpots on")]
    [InlineData("machine multiplied jackpot")]
    [InlineData("machine multiplied jackpot on\nmachine multiplied jackpot on")]
    public void Anything_but_one_declaration_turning_it_on_is_refused(string script) =>
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse(script));
}
