using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// The authentication group's script and decoder half: the declaration, the component change step, who offers
/// it, and the $6E reply read by what is left of it, so a byte a machine leaves out reads as absent.
/// </summary>
public sealed class AuthenticationScriptTests
{
    [Fact]
    public void The_declaration_becomes_a_requirement_the_machine_answers()
    {
        ScriptTest test = ScriptParser.Parse("machine component authentication on").Tests[0];

        Assert.NotNull(test.Authentication);
        Requirement requirement = test.Authentication!.AsRequirement();
        Assert.Equal("machine.component_authentication", requirement.Key);
        Assert.True(CapabilityProbe.IsAskedOfTheMachine(requirement.Key));
        Assert.True(CapabilityProbe.IsAskedOfTheMachine(CapabilityProbe.MultipleComponentsCapability));
    }

    [Theory]
    [InlineData("machine component authentication off")]
    [InlineData("machine component authentication")]
    [InlineData("machine component authentication on\nmachine component authentication on")]
    public void Anything_but_one_declaration_turning_it_on_is_refused(string script) =>
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse(script));

    [Fact]
    public void A_component_change_is_a_stimulus_both_backends_offer()
    {
        var step = Assert.IsType<ComponentChangeStep>(Assert.Single(ScriptParser.Parse("egm component change").Tests[0].Steps));

        Assert.Equal("stimulus.component_change", step.RequiredCapability);
        Assert.Equal("yes", CapabilityProbe.ForStimulus(StimulusBackend.Software)[step.RequiredCapability]);
        Assert.Equal("yes", CapabilityProbe.ForStimulus(StimulusBackend.Manual)[step.RequiredCapability]);
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse("egm component swap"));
    }

    [Fact]
    public void A_running_reply_without_its_length_byte_decodes_with_the_byte_absent()
    {
        // Address, $6E, length, list CRC, $40, name "AB", size 1 byte 10, methods 1, method 1, CRC.
        byte[] frame = Convert.FromHexString("016E10341240024142011001000000010000000000");
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x6E)!, frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0x40, decoded.Number("status"));
        Assert.Equal("AB", decoded.Text("name"));
        Assert.Equal(1, decoded.Number("method"));
        Assert.Null(decoded["authentication_length"]);
    }

    [Fact]
    public void A_complete_reply_carries_its_length_and_result_and_a_refusal_only_its_status()
    {
        byte[] complete = Convert.FromHexString("016E133412410241420110010000000100000002BEEF0000");
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x6E)!, complete);
        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(2, decoded.Number("authentication_length"));
        Assert.Equal([0xBE, 0xEF], decoded["authentication_data"]!.Bytes);

        SasDecoded refused = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x6E)!, Convert.FromHexString("016E033412FF0000"));
        Assert.True(refused.Succeeded, refused.Failure);
        Assert.Equal(0xFF, refused.Number("status"));
        Assert.Null(refused["name_length"]);
    }
}
