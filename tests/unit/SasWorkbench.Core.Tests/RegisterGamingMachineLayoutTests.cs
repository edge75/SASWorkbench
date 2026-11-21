using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// Long poll $73's reply, assembled by hand from lp73_RegisterGamingMachine's own offsets: a status,
/// the asset number, the twenty-byte key and the point of sale, every field binary and least
/// significant byte first. Also the two script steps that work the machine's own transfer menu.
/// </summary>
public class RegisterGamingMachineLayoutTests
{
    private static readonly SasMessageLayout Layout = SasLayouts.For(0x73)!;

    [Fact]
    public void A_registered_reply_reads_field_by_field()
    {
        string key = "3132333435363738393000000000000000000000";
        byte[] frame = [0x01, 0x73, .. Convert.FromHexString("1D" + "01" + "39300000" + key + "45230100"), 0x00, 0x00];

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(Layout, frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0x1D, decoded.Number("length"));
        Assert.Equal(0x01, decoded.Number("registration_status"));
        Assert.Equal(12345, decoded.Number("asset_number"));
        Assert.Equal(Convert.FromHexString(key), decoded["registration_key"]!.Bytes);
        Assert.Equal(0x00012345, decoded.Number("pos_id"));
    }

    [Fact]
    public void A_read_request_carries_the_code_alone()
    {
        SasDecoded decoded = SasMessageDecoder.DecodeRequest(Layout, [0x01, 0x73, 0x01, 0xFF, 0x00, 0x00]);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0xFF, decoded.Number("registration_code"));
        Assert.Null(decoded["asset_number"]);
    }

    [Fact]
    public void Registration_is_decodable_so_a_case_needing_it_runs()
    {
        Assert.True(CapabilityProbe.Baseline().Has("decoder.lp73"));
    }

    [Fact]
    public void The_attendant_register_action_is_a_stimulus_step()
    {
        ScriptTest test = ScriptParser.Parse("egm aft register").Tests[0];

        var step = Assert.IsType<AftRegisterStep>(Assert.Single(test.Steps));
        Assert.Equal("stimulus.aft_registration", step.RequiredCapability);
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("off", false)]
    public void The_bonusing_switch_is_a_stimulus_step(string word, bool on)
    {
        ScriptTest test = ScriptParser.Parse($"egm set aft bonus {word}").Tests[0];

        var step = Assert.IsType<SetAftBonusStep>(Assert.Single(test.Steps));
        Assert.Equal(on, step.On);
        Assert.Equal("stimulus.aft_bonus", step.RequiredCapability);
    }

    [Fact]
    public void A_bonusing_switch_with_no_position_is_refused()
    {
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse("egm set aft bonus maybe"));
    }

    [Fact]
    public void Bonus_and_receipts_are_asked_of_the_machine()
    {
        Assert.True(CapabilityProbe.IsAskedOfTheMachine("machine.aft_bonus"));
        Assert.True(CapabilityProbe.IsAskedOfTheMachine("machine.aft_receipts"));
    }
}
