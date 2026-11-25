namespace SasWorkbench.Core.Protocol;

public static partial class SasLayouts
{
    /// <summary>The preamble command, whose reply wraps another command's.</summary>
    public const byte PreambleCommand = 0xB0;

    /// <summary>Long poll $09, which enables or disables one game.</summary>
    /// <remarks>
    /// Read from <c>lp09_EnableDisableGameN</c>: two digits of game number and an enable byte, answered with the
    /// address, or refused with it and bit 7 for game zero and a game the machine does not have.
    /// </remarks>
    private static SasMessageLayout EnableDisableGame() => new()
    {
        Command = 0x09,
        Name = "enable_disable_game",
        Description = "Enables or disables one game.",
        Request =
        [
            GameNumber(),
            Byte("enable", SasUnit.Code, "Zero disables the game, anything else enables it."),
        ],
        Shape = SasResponseShape.Acknowledgement,
        DerivedFrom = "lp09_EnableDisableGameN",
    };

    /// <summary>Long poll $B0, which asks another long poll at one player denomination.</summary>
    /// <remarks>
    /// Read from <c>lpB0_MultiDenomPreamble</c>: the reply echoes the denomination and the base command and carries
    /// the base command's own reply from its length byte on, or a base command of zero and a reason. The base
    /// reply is decoded against its own layout as well, so a case reads its fields by their usual names; its
    /// length byte is renamed <c>base_length</c>, as the preamble has a length of its own.
    /// </remarks>
    private static SasMessageLayout MultiDenominationPreamble() => new()
    {
        Command = PreambleCommand,
        Name = "multi_denomination_preamble",
        Description = "Asks another long poll about one player denomination.",
        Request =
        [
            Byte("length", SasUnit.Count, "Bytes following, not counting the check sequence."),
            Byte("denomination", SasUnit.Code, "The player denomination asked about; zero means the one in play."),
            Byte("base_command", SasUnit.Code, "The long poll asked."),
            new()
            {
                Name = "base_data",
                Length = SasField.Remaining,
                Encoding = SasFieldEncoding.Raw,
                Description = "The base long poll's own data, without address, command or check sequence.",
            },
        ],
        Response =
        [
            Byte("length", SasUnit.Count, "Bytes following, not counting the check sequence."),
            Byte("denomination", SasUnit.Code, "The denomination asked about."),
            Byte("base_command", SasUnit.Code, "The long poll answered, or zero when it was not."),
            Byte("error_code", SasUnit.Code,
                "Why not: $01 not supported, $02 badly formed, $03 not per denomination, $04 not in that form, "
                + "$05 not a player denomination.")
                with { PresentWhen = new SasCondition("base_command", SasComparison.Equal, 0) },
            new()
            {
                Name = "base_reply",
                Length = SasField.Remaining,
                Encoding = SasFieldEncoding.Raw,
                Description = "The base long poll's reply from its length byte on.",
                PresentWhen = new SasCondition("base_command", SasComparison.NotEqual, 0),
            },
        ],
        DerivedFrom = "lpB0_MultiDenomPreamble",
    };
}
