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

    /// <summary>A plain binary number written least significant byte first.</summary>
    private static SasField LittleEndianNumber(string name, int bytes, string description) => new()
    {
        Name = name,
        Length = bytes,
        Encoding = SasFieldEncoding.Binary,
        ByteOrder = SasByteOrder.LittleEndian,
        Unit = SasUnit.None,
        Description = description,
    };

    /// <summary>
    /// One of the dedicated bill-count commands, whose field is deliberately not a readable meter.
    /// </summary>
    /// <remarks>
    /// The count is genuinely a meter, and it is not offered under a meter name because long poll
    /// <c>$1E</c> already answers the same six meters and the two commands do not always agree —
    /// the library divides this one by the accounting denomination under two of its meter modes
    /// and does not divide <c>$1E</c>. A test can still assert on the field of this reply; what it
    /// cannot do is ask for the twenty-unit bill meter by name and silently be given a different
    /// number than it used to get.
    /// </remarks>
    private static SasMessageLayout BillCountLayout(
        byte command, string name, string field, int denomination, string handler) => new()
        {
            Command = command,
            Name = name,
            Description = $"How many {denomination}-unit bills the machine has taken.",
            Response =
            [
                new()
                {
                    Name = field,
                    Length = 4,
                    Encoding = SasFieldEncoding.Bcd,
                    ByteOrder = SasByteOrder.BigEndian,
                    Unit = SasUnit.Count,
                    Description = $"Number of {denomination}-unit bills accepted.",
                },
            ],
            DerivedFrom = handler,
            ConfirmedByCapture = true,
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
            // Standard validation's one command, and the only long poll whose answer is a
            // property of the last ticket rather than of the machine's present state. The
            // library reads the validation buffer at its write index, so what comes back is the
            // most recently printed ticket - and there is no length byte, unlike the other
            // ticket commands.
            //
            // A machine performing secure enhanced or system validation does not answer this at
            // all, which is the library's own reading of section 15.5. Silence rather than a
            // refusal: the two say different things, and only one of them is true.
            new()
            {
                Command = 0x3D,
                Name = "cashout_ticket_information",
                Description = "The number and value of the ticket the machine printed most recently.",
                Response =
                [
                    Bcd("validation_number", 4, SasUnit.None,
                        "The number printed on the ticket, as eight digits."),
                    Bcd("ticket_amount", 5, SasUnit.Cents, "What the ticket is worth."),
                ],
                DerivedFrom = "lp3D_SendCashoutTicketInformation",
            },
            // What the machine says it can do, one game at a time, with game zero meaning the
            // machine itself. Three bytes of flags and three the specification has not assigned.
            //
            // The answer is per channel rather than per machine: the library clears the bits of
            // any category this channel has not been given, so a channel that does not carry
            // validation reports the standard style whatever the machine is configured in. That
            // is a true answer about this link and not about the machine, and a run against one
            // channel can only ever learn the first.
            new()
            {
                Command = 0xA0,
                Name = "enabled_features",
                Description = "Which optional features the machine has, for one game.",
                Request =
                [
                    Bcd("game_number", 2, SasUnit.Index,
                        "Which game to ask about; zero means the machine as a whole."),
                ],
                Response =
                [
                    Bcd("game_number", 2, SasUnit.Index,
                        "Which game this answers for, echoed from the request."),
                    new()
                    {
                        Name = "feature_codes_1",
                        Length = 1,
                        Encoding = SasFieldEncoding.Bits,
                        ByteOrder = SasByteOrder.NotApplicable,
                        Description = "Bonusing, tournament and validation flags.",
                    },
                    new()
                    {
                        Name = "validation_style",
                        Length = 0,
                        BitsOfField = "feature_codes_1",
                        Bits = new SasBitRange(6, 5),
                        Unit = SasUnit.Code,
                        Description =
                            "Which validation the machine performs when it cashes out: standard, "
                            + "system or secure enhanced.",

                        // The fourth value is representable and is assigned to nothing, so a
                        // machine reporting it is misconfigured. Reported as a violation rather
                        // than a decode failure: the byte is still worth seeing.
                        Constraint = SasConstraint.Between(0, 2),
                    },
                    new()
                    {
                        Name = "validation_extensions",
                        Length = 0,
                        BitsOfField = "feature_codes_1",
                        Bits = SasBitRange.Bit(4),
                        Description = "Whether the extended validation commands are available.",
                    },
                    new()
                    {
                        Name = "ticket_redemption",
                        Length = 0,
                        BitsOfField = "feature_codes_1",
                        Bits = SasBitRange.Bit(7),
                        Description = "Whether the machine will take a ticket for redemption.",
                    },
                    new()
                    {
                        Name = "feature_codes_2",
                        Length = 1,
                        Encoding = SasFieldEncoding.Bits,
                        ByteOrder = SasByteOrder.NotApplicable,
                        Description = "Metering, authentication and funds transfer flags.",
                    },
                    new()
                    {
                        Name = "meter_model",
                        Length = 0,
                        BitsOfField = "feature_codes_2",
                        Bits = new SasBitRange(1, 0),
                        Unit = SasUnit.Code,
                        Description = "When a win is added to the meters, or zero if unstated.",
                    },
                    new()
                    {
                        Name = "feature_codes_3",
                        Length = 1,
                        Encoding = SasFieldEncoding.Bits,
                        ByteOrder = SasByteOrder.NotApplicable,
                        Description = "Polling rate, progressive and session flags.",
                    },
                    new()
                    {
                        Name = "reserved",
                        Length = 3,
                        Encoding = SasFieldEncoding.Raw,
                        Description = "Three bytes the specification has not assigned.",
                    },
                ],
                DerivedFrom = "lpA0_SendEnabledFeatures",

                // Read from the library's handler and then checked against an exchange captured
                // from a machine that is not ours, which is the strongest evidence a layout here
                // has: twelve bytes back for six sent, three of them the reserved bytes the
                // handler writes as zero.
                ConfirmedByCapture = true,
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
            // The selected meters again, under an older command and in a shape no size byte
            // describes. Two things separate it from $6F: a meter code is one byte rather than
            // two, and a meter's value carries no width of its own - the library writes
            // getMeterMinimumSize() bytes for it, four for most meters and five for the ones it
            // counts in cents. So the only thing that says where one meter ends is which meter it
            // is, which is why this layout needs a width taken from the code.
            //
            // Proved against the legacy tester's own capture, which carries eight of these
            // exchanges: a request of 01 2F 03 00 00 05 and its reply 01 2F 07 00 00 05 00 00 00
            // 26, one meter of four bytes inside a seven-byte payload.
            new()
            {
                Command = 0x2F,
                Name = "meters_for_game",
                Description = "Meters chosen by the host, each as wide as the meter itself is.",
                Request =
                [
                    Byte("length", SasUnit.Count,
                        "Bytes of game number and meter codes that follow."),
                    Bcd("game_number", 2, SasUnit.Index,
                        "Which game the meters are for; zero means the machine as a whole."),
                    new()
                    {
                        Name = "requested",
                        Length = 0,
                        Repeat = SasRepeat.ToEndOfMessage,
                        Description = "The meter codes the host is asking for, one byte each.",
                        Fields =
                        [
                            Byte("code", SasUnit.Code, "Which meter."),
                        ],
                    },
                ],
                Response =
                [
                    Byte("length", SasUnit.Count,
                        "Bytes following, not counting the check sequence."),
                    Bcd("game_number", 2, SasUnit.Index,
                        "Which game the meters are for; zero means the machine as a whole."),
                    new()
                    {
                        Name = "meters",
                        Length = 0,
                        Repeat = SasRepeat.ToEndOfMessage,
                        Description =
                            "One entry per meter the machine answered, in the order asked for.",
                        Fields =
                        [
                            Byte("code", SasUnit.Code, "Which meter."),
                            new()
                            {
                                Name = "value",
                                Length = 0,
                                MeterWidthFromField = "code",
                                Encoding = SasFieldEncoding.Bcd,
                                ByteOrder = SasByteOrder.BigEndian,
                                Unit = SasUnit.None,
                                Description =
                                    "The meter itself, as wide as the library's own table makes "
                                    + "this meter.",
                                IsMeter = true,
                            },
                        ],
                    },
                ],
                DerivedFrom = "lp2F_SendSelectedMetersForGameN",
                ConfirmedByCapture = true,
            },

            // Three of the seven dedicated bill-count commands, and only three, because these are
            // the three the imported corpus carries. $31 to $34 share the same handler and the
            // same six bytes, and are left out rather than written from the shape of their
            // neighbours - no capture here shows one.
            //
            // Their fields are deliberately not offered as readable meters, and that is not a
            // formality. The library answers these through lpXX_SendMeter, which divides the value
            // by the accounting denomination under two of its meter modes, while long poll $1E
            // answers the same six meters undivided. The two commands therefore do not always
            // agree, so a script that has always read bills_20 from $1E keeps reading it from $1E
            // rather than being moved to a command that may answer something else.
            BillCountLayout(0x35, "bills_20_meter", "bills_20", 20, "lp35_Send20BillsInMeter"),
            BillCountLayout(0x36, "bills_50_meter", "bills_50", 50, "lp36_Send50BillsInMeter"),
            BillCountLayout(0x37, "bills_100_meter", "bills_100", 100, "lp37_Send100BillsInMeter"),

            // Both halves of this message are the same six bytes, because the machine answers with
            // what it now holds - which is what the host just sent unless the host sent a zero
            // identifier, the library's way of asking without setting. Both numbers are plain
            // binary least significant byte first, which the handler states in as many words
            // before it assembles them by hand.
            //
            // Proved against the toolkit's 10.2.1 traces: 01 4C 01 00 10 01 00 00 sets identifier
            // $100001 and sequence 1, and 01 4C 01 00 10 99 00 00 comes back carrying sequence
            // $99 after the machine had advanced it.
            new()
            {
                Command = 0x4C,
                Name = "secure_enhanced_validation_id",
                Description =
                    "Sets the identifier and sequence number a machine puts on validated tickets.",
                Request =
                [
                    LittleEndianNumber("validation_id", 3,
                        "Identifier the host is giving this machine; zero asks without setting."),
                    LittleEndianNumber("sequence_number", 3,
                        "Where the machine is to start numbering."),
                ],
                Response =
                [
                    LittleEndianNumber("validation_id", 3,
                        "The identifier the machine now holds."),
                    LittleEndianNumber("sequence_number", 3,
                        "The number the machine will put on its next validated ticket."),
                ],
                DerivedFrom = "lp4C_SetSecureEnhancedValidationId",
                ConfirmedByCapture = true,
            },

            // One entry of the machine's validation buffer. The function code chooses which: zero
            // reads the current one and moves the machine on when the host acknowledges, $FF looks
            // at the current one without moving, and anything else is an index into the buffer.
            //
            // Proved against the toolkit's 10.1.3 step8 trace, where the reply's date and time
            // read 07/20/2026 17:57:38 against a trace whose own header says it started on
            // 07/20/26 - which is what fixes the offsets, since a layout one byte out would put
            // the year where the day is and still decode.
            new()
            {
                Command = 0x4D,
                Name = "enhanced_validation_information",
                Description = "One entry of the machine's validation buffer.",
                Request =
                [
                    Byte("function", SasUnit.Code,
                        "Zero reads the current entry, $FF looks at it without advancing, and any "
                        + "other value is an index into the buffer."),
                ],
                Response =
                [
                    Byte("validation_type", SasUnit.Code, "What kind of ticket or handpay this was."),
                    Byte("index", SasUnit.Index, "Which entry of the buffer answered."),
                    Bcd("month", 1, SasUnit.None, "Month the entry was made.")
                        with { Constraint = SasConstraint.Between(0, 12) },
                    Bcd("day", 1, SasUnit.None, "Day the entry was made.")
                        with { Constraint = SasConstraint.Between(0, 31) },
                    Bcd("year", 2, SasUnit.None, "Four-digit year the entry was made."),
                    Bcd("hour", 1, SasUnit.None, "Hour, on a twenty-four hour clock.")
                        with { Constraint = SasConstraint.Between(0, 23) },
                    Bcd("minute", 1, SasUnit.None, "Minute.")
                        with { Constraint = SasConstraint.Between(0, 59) },
                    Bcd("second", 1, SasUnit.None, "Second.")
                        with { Constraint = SasConstraint.Between(0, 59) },
                    Bcd("validation_number", 8, SasUnit.None,
                        "The number printed on the ticket, as sixteen digits."),
                    Bcd("amount", 5, SasUnit.Cents, "What the ticket or handpay was worth."),
                    LittleEndianNumber("ticket_number", 2,
                        "The machine's own sequence number for this ticket."),
                    Bcd("validation_system_id", 1, SasUnit.Code,
                        "Which validation system the number belongs to."),
                    Bcd("expiration_month", 1, SasUnit.None, "Month the ticket expires.")
                        with { Constraint = SasConstraint.Between(0, 12) },
                    Bcd("expiration_day", 1, SasUnit.None, "Day the ticket expires.")
                        with { Constraint = SasConstraint.Between(0, 31) },
                    Bcd("expiration_year", 2, SasUnit.None, "Four-digit year the ticket expires."),
                    LittleEndianNumber("pool_id", 2, "Which restricted pool the ticket belongs to."),
                ],
                DerivedFrom = "lp4D_SendEnhancedValidationInfo",
                ConfirmedByCapture = true,
            },
            new()
            {
                Command = 0x51,
                Name = "games_implemented",
                Description = "How many games the machine holds.",
                Response =
                [
                    Bcd("games", 2, SasUnit.Count, "Number of games implemented."),
                ],
                DerivedFrom = "lp51_SendNumberOfGamesImplemented",
                ConfirmedByCapture = true,
            },

            // A broadcast, and the only command modelled here that normally goes to address zero,
            // where nothing answers at all. Sent to a machine's own address it is acknowledged.
            // The length byte counts the group byte plus six bytes per level, so a message
            // carrying two levels reads $0D.
            //
            // Proved against the toolkit's session log, where 00 86 0D 01 01 00 00 00 04 14 02 00
            // 00 00 04 14 broadcasts $4.14 to levels one and two of group one.
            new()
            {
                Command = 0x86,
                Name = "multiple_level_progressive",
                Description = "Broadcasts the current amount of several progressive levels at once.",
                Request =
                [
                    Byte("length", SasUnit.Count,
                        "Bytes following, not counting the check sequence."),
                    Byte("progressive_group", SasUnit.Index,
                        "Which progressive group these levels belong to."),
                    new()
                    {
                        Name = "levels",
                        Length = 0,
                        Repeat = SasRepeat.ToEndOfMessage,
                        Description = "One entry per level being broadcast.",
                        Fields =
                        [
                            Byte("level", SasUnit.Index, "Which level, one to thirty-two.",
                                SasConstraint.Between(1, 32)),
                            Bcd("amount", 5, SasUnit.Cents, "What that level currently stands at."),
                        ],
                    },
                ],
                Shape = SasResponseShape.Acknowledgement,
                DerivedFrom = "lp86_MultipleLevelProgressive",
                Broadcast = true,
                ConfirmedByCapture = true,
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
