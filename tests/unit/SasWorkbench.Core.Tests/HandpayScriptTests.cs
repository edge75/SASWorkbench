using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Core.Tests;

/// <summary>The hand pay group's script half: the declarations, and the requirements only a machine built that way meets.</summary>
public sealed class HandpayScriptTests
{
    [Fact]
    public void Legacy_reporting_and_validation_become_two_requirements()
    {
        ScriptTest test = ScriptParser.Parse("machine handpay legacy\nmachine handpay validation on").Tests[0];

        Assert.NotNull(test.Handpay);
        Assert.True(test.Handpay!.Legacy);
        Assert.True(test.Handpay.Validation);
        Assert.Equal(
            ["machine.legacy_handpay", "machine.handpay_validation"],
            test.Handpay.AsRequirements().Select(requirement => requirement.Key));
    }

    [Fact]
    public void Validation_alone_leaves_the_queue_in_place()
    {
        ScriptTest test = ScriptParser.Parse("machine handpay validation on").Tests[0];

        Assert.False(test.Handpay!.Legacy);
        Assert.Equal(["machine.handpay_validation"], test.Handpay.AsRequirements().Select(requirement => requirement.Key));
    }

    [Theory]
    [InlineData("machine handpay queue")]
    [InlineData("machine handpay legacy on")]
    [InlineData("machine handpay validation off")]
    [InlineData("machine handpay")]
    [InlineData("machine handpay legacy\nmachine handpay legacy")]
    public void Anything_but_the_two_declarations_once_each_is_refused(string script) =>
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse(script));
}
