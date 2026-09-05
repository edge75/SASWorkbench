using System.Collections.Frozen;

namespace SasWorkbench.Core.Protocol;

/// <summary>
/// The message layouts this version can decode, each read from the gaming machine library's own
/// handler for that command.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue in <see cref="SasCatalogue"/> is generated and covers every command the library
/// knows about. This is the smaller set whose field layout has actually been established, and the
/// two are deliberately different sizes: knowing a command exists is not the same as knowing what
/// its reply means, and a runner must be able to tell a test the difference rather than decode a
/// reply it has only guessed at.
/// </para>
/// <para>
/// Each layout names the handler it came from. One is additionally marked as confirmed by a
/// capture, meaning its bytes have been seen coming off a real link and not merely read out of
/// source.
/// </para>
/// </remarks>
public static class SasLayouts
{
    private static SasField Bcd(string name, int bytes, SasUnit unit, string description) => new()
    {
        Name = name,
        Length = bytes,
        Encoding = SasFieldEncoding.Bcd,
        ByteOrder = SasByteOrder.BigEndian,
        Unit = unit,
        Description = description,
    };

    /// <summary>An accumulating meter a test may read by name.</summary>
    private static SasField Meter(string name, int bytes, SasUnit unit, string description) =>
        Bcd(name, bytes, unit, description) with { IsMeter = true };

    private static SasField Byte(string name, SasUnit unit, string description,
        SasConstraint? constraint = null) => new()
    {
        Name = name,
        Length = 1,
        Encoding = SasFieldEncoding.Binary,
        ByteOrder = SasByteOrder.NotApplicable,
        Unit = unit,
        Description = description,
        Constraint = constraint,
    };

    private static SasField Ascii(string name, int bytes, string description) => new()
    {
        Name = name,
        Length = bytes,
        Encoding = SasFieldEncoding.Ascii,
        ByteOrder = SasByteOrder.NotApplicable,
        Description = description,
    };

    /// <summary>A four-byte accumulating meter, the shape most meter commands answer with.</summary>
    private static SasMessageLayout MeterLayout(
        byte command, string name, string field, SasUnit unit, string description, string handler)
        => new()
        {
            Command = command,
            Name = name,
            Description = description,
            Response = [Meter(field, 4, unit, description)],
            DerivedFrom = handler,
        };

    /// <summary>Every layout, by command byte.</summary>
    public static FrozenDictionary<byte, SasMessageLayout> All { get; } = Build();

    /// <summary>Every layout, by the short name a script uses.</summary>
    public static FrozenDictionary<string, SasMessageLayout> ByName { get; } =
        All.Values.ToDictionary(l => l.Name, StringComparer.OrdinalIgnoreCase).ToFrozenDictionary(
            StringComparer.OrdinalIgnoreCase);

    /// <summary>The layout for a command, or null when its reply has not been established.</summary>
    public static SasMessageLayout? For(byte command) =>
        All.TryGetValue(command, out SasMessageLayout? layout) ? layout : null;

    /// <summary>
    /// True when the command exists in the library but this version cannot decode its reply.
    /// A runner turns that into a skip with a reason, never into a pass.
    /// </summary>
    public static bool IsKnownButUndecoded(byte command) =>
        !All.ContainsKey(command) && SasCatalogue.LongPolls.ContainsKey(command);

    private static FrozenDictionary<byte, SasMessageLayout> Build()
    {
        SasMessageLayout[] layouts =
        [
            new()
            {
                Command = 0x01,
                Name = "shutdown",
                Description = "Disables play on the gaming machine.",
                Shape = SasResponseShape.Acknowledgement,
                DerivedFrom = "lpXX_EnableDisable",
            },
            new()
            {
                Command = 0x02,
                Name = "startup",
                Description = "Re-enables play on the gaming machine.",
                Shape = SasResponseShape.Acknowledgement,
                DerivedFrom = "lpXX_EnableDisable",
            },
            new()
            {
                Command = 0x08,
                Name = "configure_bill_denominations",
                Description = "Which notes the machine may accept, and what to do after each one.",
                Request =
                [
                    new()
                    {
                        Name = "denominations",
                        Length = 4,
                        Encoding = SasFieldEncoding.Bits,

                        // Least significant byte first, unlike almost every other multi-byte
                        // field in this protocol. One bit per denomination in the protocol's
                        // own order: $1, $2, $5, $10, $20, $25, $50, $100, and upwards.
                        ByteOrder = SasByteOrder.LittleEndian,
                        Description = "One bit per note denomination the machine may take.",
                    },
                    Byte("action", SasUnit.Code,
                        "What the machine does after accepting a note."),
                ],
                Shape = SasResponseShape.Acknowledgement,
                DerivedFrom = "lp08_ConfigureBillDenominations",
            },
            new()
            {
                Command = 0x0E,
                Name = "real_time_event_reporting",
                Description = "Switches exception reporting between its plain and timestamped forms.",
                Request =
                [
                    Byte("enabled", SasUnit.Code, "Zero disables reporting, one enables it.",
                        SasConstraint.AnyOf(0, 1)),
                ],
                Shape = SasResponseShape.Acknowledgement,
                DerivedFrom = "lp0E_EnableDisableRealTimeEventReporting",
            },
            new()
            {
                Command = 0x0F,
                Name = "meters_10_15",
                Description = "Six accumulating meters in one reply.",
                Response =
                [
                    Meter("total_cancelled_credits", 4, SasUnit.Credits, "Credits cancelled by a handpay."),
                    Meter("total_coin_in", 4, SasUnit.Credits, "Credits wagered."),
                    Meter("total_coin_out", 4, SasUnit.Credits, "Credits paid out."),
                    Meter("total_drop", 4, SasUnit.Credits, "Credits dropped into the machine."),
                    Meter("total_jackpot", 4, SasUnit.Credits, "Credits paid as jackpots."),
                    Meter("games_played", 4, SasUnit.Count, "Games played."),
                ],
                DerivedFrom = "lp0F_SendMeters",
            },
            MeterLayout(0x10, "total_cancelled_credits", "total_cancelled_credits", SasUnit.Credits,
                "Credits cancelled by a handpay.", "lp10_SendTotalCancelledCredits"),
            MeterLayout(0x11, "total_coin_in", "total_coin_in", SasUnit.Credits,
                "Credits wagered.", "lp11_SendTotalCoinInCredits"),
            MeterLayout(0x12, "total_coin_out", "total_coin_out", SasUnit.Credits,
                "Credits paid out.", "lp12_SendTotalCoinOutCredits"),
            MeterLayout(0x13, "total_drop", "total_drop", SasUnit.Credits,
                "Credits dropped into the machine.", "lp13_SendTotalDropInCredits"),
            MeterLayout(0x14, "total_jackpot", "total_jackpot", SasUnit.Credits,
                "Credits paid as jackpots.", "lp14_SendTotalJackpot"),
            MeterLayout(0x15, "games_played", "games_played", SasUnit.Count,
                "Games played.", "lp15_SendGamesPlayed"),
            MeterLayout(0x16, "games_won", "games_won", SasUnit.Count,
                "Games won.", "lp16_SendGamesWon"),
            MeterLayout(0x17, "games_lost", "games_lost", SasUnit.Count,
                "Games lost.", "lp17_SendGamesLost"),
            MeterLayout(0x1A, "current_credits", "current_credits", SasUnit.Credits,
                "Credits currently on the machine.", "lp1A_SendCurrentCredits"),
            new()
            {
                Command = 0x1B,
                Name = "handpay_information",
                Description = "Details of the handpay the machine is currently holding.",
                Response =
                [
                    Byte("progressive_group", SasUnit.Index, "Progressive group the handpay belongs to."),
                    Byte("level", SasUnit.Code, "Which progressive level was hit, or zero for none."),
                    Bcd("amount", 5, SasUnit.Cents, "Amount owed to the player."),
                    Bcd("partial_pay", 2, SasUnit.Credits, "Credits already paid towards it."),
                    Byte("reset_id", SasUnit.Code, "Who is permitted to reset the handpay."),
                    Bcd("session_game_win", 5, SasUnit.Cents, "Win amount for the session."),
                    Bcd("session_game_pay", 5, SasUnit.Cents, "Amount paid for the session."),
                ],
                DerivedFrom = "lp1B_SendHandpayInformation",
            },
            new()
            {
                Command = 0x1E,
                Name = "bill_meters",
                Description = "How many bills of each of six denominations the machine has taken.",
                Response =
                [
                    Meter("bills_1", 4, SasUnit.Count, "Number of one-unit bills accepted."),
                    Meter("bills_5", 4, SasUnit.Count, "Number of five-unit bills accepted."),
                    Meter("bills_10", 4, SasUnit.Count, "Number of ten-unit bills accepted."),
                    Meter("bills_20", 4, SasUnit.Count, "Number of twenty-unit bills accepted."),
                    Meter("bills_50", 4, SasUnit.Count, "Number of fifty-unit bills accepted."),
                    Meter("bills_100", 4, SasUnit.Count, "Number of hundred-unit bills accepted."),
                ],
                DerivedFrom = "lp1E_SendBillMeters",
            },
            new()
            {
                Command = 0x1F,
                Name = "machine_id",
                Description = "Which machine this is and how it is configured.",
                Response =
                [
                    Ascii("game_id", 2, "Two-character manufacturer identifier."),
                    Ascii("additional_id", 3, "Three-character machine identifier."),
                    Byte("denomination", SasUnit.Code, "Accounting denomination code."),
                    Byte("max_bet", SasUnit.Credits, "Largest bet the machine will accept."),
                    Byte("progressive_group", SasUnit.Index, "Progressive group the machine belongs to."),
                    new()
                    {
                        Name = "game_options",
                        Length = 2,
                        Encoding = SasFieldEncoding.Bits,

                        // Least significant byte first, unlike almost every other multi-byte field
                        // in the protocol. Reading it the other way round produces a wrong answer
                        // that looks reasonable.
                        ByteOrder = SasByteOrder.LittleEndian,
                        Description = "Machine option bits.",
                    },
                    Ascii("paytable_id", 6, "Paytable identifier."),
                    new()
                    {
                        Name = "base_percentage",
                        Length = 4,
                        Encoding = SasFieldEncoding.AsciiNumber,
                        Unit = SasUnit.HundredthsOfPercent,
                        Description = "Theoretical payback percentage, in hundredths of a percent.",

                        // Four decimal digits, so 99.99% is the most the field can express.
                        Constraint = SasConstraint.Between(0, 9999),
                    },
                ],
                DerivedFrom = "lp1F_SendGamingMachineIdAndInformation",
                ConfirmedByCapture = true,
            },
            MeterLayout(0x20, "total_bill_value", "total_bill_value", SasUnit.Credits,
                "Total value of bills accepted.", "lp20_SendTotalDollarValueOfBills"),
            new()
            {
                Command = 0x2C,
                Name = "hopper_level",
                Description = "How many coins are in the hopper right now.",

                // Not marked as a meter, and the reason is not a judgement call: the gaming
                // machine library keeps no meter code for the hopper level. It reads the figure
                // from the device, so it is machine state and a test asserts on it as a field.
                Response =
                [
                    Bcd("hopper_level", 4, SasUnit.Count, "Coins currently in the hopper."),
                ],
                DerivedFrom = "lp2C_SendCurrentHopperLevel",
            },
            new()
            {
                Command = 0x54,
                Name = "version_and_serial",
                Description = "Which revision of the protocol the machine speaks, and its serial number.",
                Response =
                [
                    Byte("length", SasUnit.Count, "How many bytes follow this one."),
                    Ascii("sas_version", 3, "Protocol revision, as three digits."),
                    new()
                    {
                        Name = "serial_number",
                        Length = SasField.Remaining,
                        Encoding = SasFieldEncoding.Ascii,
                        Description = "Machine serial number, as many characters as the length allows.",
                    },
                ],
                DerivedFrom = "lp54_SendSasVersionIdAndGamingMachineSerialNumber",
            },
            new()
            {
                Command = 0x7E,
                Name = "date_and_time",
                Description = "The machine's own clock.",
                Response =
                [
                    Bcd("month", 1, SasUnit.None, "Month, one to twelve.")
                        with { Constraint = SasConstraint.Between(1, 12) },
                    Bcd("day", 1, SasUnit.None, "Day of the month.")
                        with { Constraint = SasConstraint.Between(1, 31) },
                    Bcd("year", 2, SasUnit.None, "Four-digit year."),
                    Bcd("hour", 1, SasUnit.None, "Hour, on a twenty-four hour clock.")
                        with { Constraint = SasConstraint.Between(0, 23) },
                    Bcd("minute", 1, SasUnit.None, "Minute.")
                        with { Constraint = SasConstraint.Between(0, 59) },
                    Bcd("second", 1, SasUnit.None, "Second.")
                        with { Constraint = SasConstraint.Between(0, 59) },
                ],
                DerivedFrom = "lp7E_SendCurrentDateAndTime",
                Broadcast = true,
            },
            new()
            {
                Command = 0xB1,
                Name = "current_player_denomination",
                Description = "The denomination the player is currently playing at.",
                Response =
                [
                    Byte("denomination", SasUnit.Code, "Denomination code currently selected."),
                ],
                DerivedFrom = "lpB1_SendCurrentPlayerDenomination",
            },
        ];

        return layouts.ToDictionary(l => l.Command).ToFrozenDictionary();
    }
}
