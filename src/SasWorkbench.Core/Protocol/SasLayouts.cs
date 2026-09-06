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

    /// <summary>Which meter, least significant byte first.</summary>
    /// <remarks>
    /// Binary rather than BCD, and little-endian: meter <c>00A0</c> goes on the wire as
    /// <c>A0 00</c>. The machine copies these two bytes back into its reply untouched, so reading
    /// them the other way round names a different meter and still decodes.
    /// </remarks>
    private static SasField MeterCode() => new()
    {
        Name = "code",
        Length = 2,
        Encoding = SasFieldEncoding.Binary,
        ByteOrder = SasByteOrder.LittleEndian,
        Unit = SasUnit.Code,
        Description = "Which meter, least significant byte first.",
    };

    /// <summary>
    /// The selected-meter reply, whose shape no fixed layout can describe.
    /// </summary>
    /// <remarks>
    /// Two things stop this being a table of offsets. Each meter states its own width, because the
    /// machine widens the field as the value grows rather than letting it roll over; and a meter
    /// the machine does not support is left out of the reply altogether, code included, so the
    /// entry that follows moves up. A reader must therefore walk the reply, and an absent code
    /// means "not supported" rather than "zero". Meter codes C0 and C1 are always absent here by
    /// design and are read with the legacy meters command instead.
    /// </remarks>
    private static SasMessageLayout SelectedMeters(byte command, string name) => new()
    {
        Command = command,
        Name = name,
        Description = "Meters chosen by the host, each answered with its own width.",
        Request =
        [
            Byte("length", SasUnit.Count, "Bytes of game number and meter codes that follow."),
            Bcd("game_number", 2, SasUnit.Index,
                "Which game the meters are for; zero means the machine as a whole."),
            new()
            {
                Name = "requested",
                Length = 0,
                Repeat = SasRepeat.ToEndOfMessage,
                Description = "The meter codes the host is asking for.",
                Fields = [MeterCode()],
            },
        ],
        Response =
        [
            Byte("length", SasUnit.Count, "Bytes following, not counting the check sequence."),
            Bcd("game_number", 2, SasUnit.Index,
                "Which game the meters are for; zero means the machine as a whole."),
            new()
            {
                Name = "meters",
                Length = 0,
                Repeat = SasRepeat.ToEndOfMessage,
                Description = "One entry per meter the machine supports, in the order asked for.",
                Fields =
                [
                    MeterCode(),
                    Byte("size", SasUnit.Count, "How many bytes this meter's value occupies."),
                    new()
                    {
                        Name = "value",
                        Length = 0,
                        LengthFromField = "size",
                        Encoding = SasFieldEncoding.Bcd,
                        ByteOrder = SasByteOrder.BigEndian,
                        Unit = SasUnit.None,
                        Description = "The meter itself, as many bytes wide as the size byte says.",
                        IsMeter = true,
                    },
                ],
            },
        ],
        DerivedFrom = "lp6F_SendExtendedMetersForGameN",
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
            SelectedMeters(0x6F, "selected_meters"),

            // The same reply under a second command. SAS makes two identical consecutive long polls
            // an implied negative acknowledgement, so a host sweeping more meters than one command
            // holds has to alternate between the two - which is the whole reason both codes exist.
            SelectedMeters(0xAF, "selected_meters_alternate"),
            // Whole currency units, not credits: this is the one bill meter the specification
            // fixes in dollars, so it does not scale with the accounting denomination.
            MeterLayout(0x20, "total_bill_value", "total_bill_value", SasUnit.Dollars,
                "Total value of bills accepted, in whole currency units.",
                "lp20_SendTotalDollarValueOfBills"),
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

            // The two halves of a redemption. A ticket goes into the acceptor, the machine holds
            // it and says so; the host asks what is being held with $70 and answers with $71.
            // Neither is a meter read: both move a real transaction along, so a test that sends
            // one has changed the machine whether or not it asserts on the reply.
            new()
            {
                Command = 0x70,
                Name = "ticket_validation_data",
                Description = "What ticket, if any, the machine is holding in its acceptor.",

                // Nothing follows the command byte, and nothing checks one: the machine reads
                // this poll without verifying a check sequence, so the host sends two bytes.
                Response =
                [
                    Byte("length", SasUnit.Count, "Bytes following, not counting the check sequence."),
                    Byte("ticket_status", SasUnit.Code,
                        "Zero while a ticket is held in escrow, $FF when none is.",
                        SasConstraint.AnyOf(0x00, 0xFF)),

                    // The machine answers zero here, every time. It has read a barcode, not a
                    // value: what the ticket is worth is the host's to say in the reply to $71,
                    // so a test reading an amount from this poll is reading a placeholder.
                    Bcd("transfer_amount", 5, SasUnit.Cents,
                        "Always zero: the machine does not know what the ticket is worth.")
                        with { PresentWhen = new SasCondition("ticket_status", SasComparison.Equal, 0x00) },
                    Byte("parsing_code", SasUnit.Code,
                        "How the validation data is to be read; zero is the plain form.")
                        with { PresentWhen = new SasCondition("ticket_status", SasComparison.Equal, 0x00) },
                    Bcd("validation_data", 9, SasUnit.None,
                        "The number printed on the ticket, as eighteen digits.")
                        with { PresentWhen = new SasCondition("ticket_status", SasComparison.Equal, 0x00) },
                ],
                DerivedFrom = "lp70_SendTicketValidationData",
            },
            new()
            {
                Command = 0x71,
                Name = "redeem_ticket",
                Description = "The host's verdict on the ticket being held, and what the machine did with it.",

                // A transfer code of $FF asks for the current status and carries nothing else,
                // which is how a host reads the outcome after the machine has taken the ticket.
                // The validation data is nine bytes here because the parsing code is zero; a
                // non-zero parsing code makes it as long as the length byte allows, and this
                // version does not model that.
                Request =
                [
                    Byte("length", SasUnit.Count, "Bytes following, not counting the check sequence."),
                    Byte("transfer_code", SasUnit.Code,
                        "What the host decided: $00 to $02 accept, $80 upwards refuse, $FF only asks."),
                    Bcd("transfer_amount", 5, SasUnit.Cents, "What the host says the ticket is worth.")
                        with { PresentWhen = new SasCondition("transfer_code", SasComparison.NotEqual, 0xFF) },
                    Byte("parsing_code", SasUnit.Code, "How the validation data is to be read.")
                        with { PresentWhen = new SasCondition("transfer_code", SasComparison.NotEqual, 0xFF) },
                    Bcd("validation_data", 9, SasUnit.None, "The number the host is redeeming.")
                        with { PresentWhen = new SasCondition("transfer_code", SasComparison.NotEqual, 0xFF) },

                    // Only a restricted ticket carries these, and only on a machine that supports
                    // the validation extensions. A restricted redemption that leaves them out is
                    // legal and the machine falls back to its own default, so they are written
                    // only when a caller supplies them.
                    Bcd("restricted_expiration_month", 1, SasUnit.None, "Month the restricted credits expire.")
                        with { PresentWhen = new SasCondition("transfer_code", SasComparison.Equal, 0x01) },
                    Bcd("restricted_expiration_day", 1, SasUnit.None, "Day the restricted credits expire.")
                        with { PresentWhen = new SasCondition("transfer_code", SasComparison.Equal, 0x01) },
                    Bcd("restricted_expiration_year", 2, SasUnit.None, "Year the restricted credits expire.")
                        with { PresentWhen = new SasCondition("transfer_code", SasComparison.Equal, 0x01) },
                    new()
                    {
                        Name = "pool_id",
                        Length = 2,
                        Encoding = SasFieldEncoding.Binary,

                        // Least significant byte first, unlike the rest of this command.
                        ByteOrder = SasByteOrder.LittleEndian,
                        Unit = SasUnit.Index,
                        Description = "Which restricted pool the credits belong to.",
                        PresentWhen = new SasCondition("transfer_code", SasComparison.Equal, 0x01),
                    },
                ],

                // $FF says no redemption has ever happened and $C0 says this command does not
                // belong to the cycle in progress. Both answer with the status alone.
                Response =
                [
                    Byte("length", SasUnit.Count, "Bytes following, not counting the check sequence."),
                    Byte("machine_status", SasUnit.Code, "What the machine did with the ticket."),
                    Bcd("transfer_amount", 5, SasUnit.Cents, "The amount the machine recorded.")
                        with { PresentWhen = new SasCondition("machine_status", SasComparison.LessThan, 0xC0) },
                    Byte("parsing_code", SasUnit.Code, "How the validation data is to be read.")
                        with { PresentWhen = new SasCondition("machine_status", SasComparison.LessThan, 0xC0) },
                    Bcd("validation_data", 9, SasUnit.None, "The number the machine has on the ticket.")
                        with { PresentWhen = new SasCondition("machine_status", SasComparison.LessThan, 0xC0) },
                ],
                DerivedFrom = "lp71_RedeemTicket",
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
