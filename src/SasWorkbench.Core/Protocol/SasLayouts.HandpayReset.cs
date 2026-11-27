namespace SasWorkbench.Core.Protocol;

public static partial class SasLayouts
{
    /// <summary>The jackpot hand pay reset method and the three sound switches.</summary>
    private static SasMessageLayout[] HandpayResetAndSound() =>
    [
        new()
        {
            Command = 0xA8,
            Name = "enable_jackpot_handpay_reset_method",
            Description = "Says how the jackpot hand pay now pending may be cleared: by an attendant, or onto the credit meter.",
            Request =
            [
                Byte("reset_method", SasUnit.Code, "00 a standard hand pay, 01 a reset to the credit meter."),
            ],
            Response =
            [
                Byte("ack_code", SasUnit.Code,
                    "00 the method is enabled, 01 the machine cannot reset this hand pay that way, 02 there is no hand pay."),
            ],
            DerivedFrom = "lpA8_EnableJackpotHandpayResetMethod",
        },
        SoundSwitch(0x03, "sound_off", "Turns every sound the machine makes off."),
        SoundSwitch(0x04, "sound_on", "Turns the machine's sounds back on."),
        SoundSwitch(0x05, "game_sounds_off", "Turns off the sounds the game itself plays, such as reels spinning."),
    ];

    /// <summary>A sound switch, acknowledged and handed to the game (lpXX_EnableDisable).</summary>
    private static SasMessageLayout SoundSwitch(byte command, string name, string description) => new()
    {
        Command = command,
        Name = name,
        Description = description,
        Shape = SasResponseShape.Acknowledgement,
        DerivedFrom = "lpXX_EnableDisable",
    };
}
