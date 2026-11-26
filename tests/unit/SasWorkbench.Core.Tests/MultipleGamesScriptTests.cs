using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// The multiple games group's script and decoder half: the games declaration, the selection and operator steps, who
/// offers them, and the preamble reply read under its base command's own field names.
/// </summary>
public sealed class MultipleGamesScriptTests
{
    [Fact]
    public void The_declaration_names_each_game_s_denomination_and_becomes_requirements_the_machine_answers()
    {
        ScriptTest test = ScriptParser.Parse("machine games 1c 2c 5c 10c").Tests[0];

        Assert.Equal([1, 2, 5, 10], test.Games!.DenominationsInCents);
        Assert.Equal(
            [CapabilityProbe.MultipleGamesCapability, CapabilityProbe.MultipleDenominationsCapability],
            test.Games.AsRequirements().Select(requirement => requirement.Key));
        Assert.True(CapabilityProbe.IsAskedOfTheMachine(CapabilityProbe.MultipleGamesCapability));
        Assert.True(CapabilityProbe.IsAskedOfTheMachine(CapabilityProbe.MultipleDenominationsCapability));
    }

    [Fact]
    public void Games_all_at_one_denomination_require_several_games_but_not_several_denominations()
    {
        ScriptTest test = ScriptParser.Parse("machine games 5c 5c").Tests[0];

        Assert.Equal([CapabilityProbe.MultipleGamesCapability], test.Games!.AsRequirements().Select(requirement => requirement.Key));
    }

    [Theory]
    [InlineData("machine games")]
    [InlineData("machine games 7c")]
    [InlineData("machine games five")]
    [InlineData("machine games 1c\nmachine games 5c")]
    [InlineData("egm select game")]
    [InlineData("egm select reel 1")]
    [InlineData("egm select denomination 5")]
    [InlineData("egm set game 0 off")]
    [InlineData("egm set game 2 maybe")]
    public void A_malformed_declaration_or_step_is_refused(string script) =>
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse(script));

    [Fact]
    public void The_selection_and_operator_steps_are_stimuli_both_backends_offer()
    {
        IReadOnlyList<ScriptStep> steps = ScriptParser.Parse(
            "egm select game 0\negm select denomination 5c\negm set game 2 off").Tests[0].Steps;

        Assert.Equal(0, Assert.IsType<SelectGameStep>(steps[0]).Game);
        Assert.Equal(5, Assert.IsType<SelectDenominationStep>(steps[1]).Cents);
        Assert.False(Assert.IsType<SetGameEnabledStep>(steps[2]).Enabled);

        foreach (StimulusStep step in steps.Cast<StimulusStep>())
        {
            Assert.Equal("yes", CapabilityProbe.ForStimulus(StimulusBackend.Software)[step.RequiredCapability]);
            Assert.Equal("yes", CapabilityProbe.ForStimulus(StimulusBackend.Manual)[step.RequiredCapability]);
        }
    }

    [Fact]
    public void A_bet_button_and_a_machine_waiting_for_the_player_are_offered_by_a_person_and_otherwise_only_when_declared()
    {
        IReadOnlyDictionary<string, string?> software = CapabilityProbe.ForStimulus(StimulusBackend.Software);
        IReadOnlyDictionary<string, string?> manual = CapabilityProbe.ForStimulus(StimulusBackend.Manual);

        Assert.False(software.ContainsKey("stimulus.bet_button"));
        Assert.False(software.ContainsKey("machine.player_input_state"));
        Assert.Equal("yes", manual["stimulus.bet_button"]);
        Assert.Equal("yes", manual["machine.player_input_state"]);
    }

    [Fact]
    public void A_field_of_the_base_reply_may_be_named_after_a_preamble_and_a_field_of_neither_may_not()
    {
        ScriptParser.Parse("host send lp B0 data 020156\nhost expect field count == 1\nhost expect field base_length == 3");

        Assert.Throws<ScriptParseException>(() =>
            ScriptParser.Parse("host send lp B0 data 020156\nhost expect field meters[0].value == 1"));
    }

    [Fact]
    public void A_preamble_reply_carries_the_base_reply_under_its_own_names()
    {
        // Address, $B0, length, one cent, $56, then $56's own reply: length 3, one game, game 0002.
        SasDecoded decoded = Decode([0x01, 0xB0, 0x06, 0x01, 0x56, 0x03, 0x01, 0x00, 0x02]);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(6, decoded.Number("length"));
        Assert.Equal(1, decoded.Number("denomination"));
        Assert.Equal(0x56, decoded.Number("base_command"));
        Assert.Equal(3, decoded.Number("base_length"));
        Assert.Equal(2, decoded.Number("games[0].game_number"));
    }

    [Fact]
    public void A_refused_preamble_carries_its_reason()
    {
        SasDecoded decoded = Decode([0x01, 0xB0, 0x03, 0x01, 0x00, 0x03]);

        Assert.Equal(0, decoded.Number("base_command"));
        Assert.Equal(3, decoded.Number("error_code"));
        Assert.Null(decoded["base_reply"]);
    }

    private static SasDecoded Decode(byte[] message)
    {
        ushort crc = SasCrc.Compute(message);
        byte[] frame = [.. message, SasCrc.LowByte(crc), SasCrc.HighByte(crc)];
        return SasMessageDecoder.DecodeResponse(SasLayouts.For(0xB0)!, frame);
    }
}
