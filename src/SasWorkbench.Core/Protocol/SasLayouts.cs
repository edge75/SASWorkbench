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


    /// <summary>
    /// A single four-byte meter answered through the library's shared meter handler, offered as a
    /// field rather than as a meter a script may name.
    /// </summary>
    /// <remarks>
    /// The same reasoning the dedicated bill-count commands already carry, and it applies to every
    /// command answering through <c>lpXX_SendMeter</c>: that handler divides the value by the
    /// accounting denomination under two of the machine's meter modes and the bulk commands do
    /// not, so two commands reporting the same meter do not always report the same number. A test
    /// asserts on the field of whichever reply it asked for; what it cannot do is ask for a meter
    /// by name and be given a different number than it used to get.
    /// </remarks>
    private static SasMessageLayout SharedMeterLayout(
        byte command, string name, string field, SasUnit unit, string description, string handler)
        => new()
        {
            Command = command,
            Name = name,
            Description = description,
            Response = [Bcd(field, 4, unit, description)],
            DerivedFrom = handler,
            ConfirmedByCapture = true,
        };

    /// <summary>A reply that echoes the game number it was asked for and then reports meters.</summary>
    private static SasMessageLayout GameMeterLayout(
        byte command, string name, string description, string handler, params SasField[] meters)
        => new()
        {
            Command = command,
            Name = name,
            Description = description,
            Request = [GameNumber()],
            Response = [GameNumber(), .. meters],
            DerivedFrom = handler,
            ConfirmedByCapture = true,
        };

    /// <summary>Which game, as two binary coded decimal digits. Zero means the machine itself.</summary>
    private static SasField GameNumber() =>
        Bcd("game_number", 2, SasUnit.Index, "Which game; zero means the machine as a whole.");

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

            // ---------------------------------------------------------------------------------
            // Established from the library's own handlers and checked against a catalogue sweep
            // of a real gaming machine, which `sasctl sweep` runs. Every reply below was produced
            // by asking that machine, so the bytes are ours rather than somebody else's
            // recording, and the sweep can be run again to check them.
            // ---------------------------------------------------------------------------------

            // The rest of the dedicated bill counts. The previous wave left $31 to $34 undecoded
            // on purpose, because no capture here carried one; the sweep carries all seventeen.
            BillCountLayout(0x31, "bills_1_meter", "bills_1", 1, "lp31_Send1BillsInMeter"),
            BillCountLayout(0x32, "bills_2_meter", "bills_2", 2, "lp32_Send2BillsInMeter"),
            BillCountLayout(0x33, "bills_5_meter", "bills_5", 5,
                "lp33_Send5BillsInMeter"),
            BillCountLayout(0x34, "bills_10_meter", "bills_10", 10, "lp34_Send10BillsInMeter"),
            BillCountLayout(0x38, "bills_500_meter", "bills_500", 500, "lp38_Send500BillsInMeter"),
            BillCountLayout(0x39, "bills_1000_meter", "bills_1000", 1000,
                "lp39_Send1000BillsInMeter"),
            BillCountLayout(0x3A, "bills_200_meter", "bills_200", 200, "lp3A_Send200BillsInMeter"),
            BillCountLayout(0x3B, "bills_25_meter", "bills_25", 25, "lp3B_Send25BillsInMeter"),
            BillCountLayout(0x3C, "bills_2000_meter", "bills_2000", 2000,
                "lp3C_Send2000BillsInMeter"),
            BillCountLayout(0x3E, "bills_2500_meter", "bills_2500", 2500,
                "lp3E_Send2500BillsInMeter"),
            BillCountLayout(0x3F, "bills_5000_meter", "bills_5000", 5000,
                "lp3F_Send5000BillsInMeter"),
            BillCountLayout(0x40, "bills_10000_meter", "bills_10000", 10000,
                "lp40_Send10KBillsInMeter"),
            BillCountLayout(0x41, "bills_20000_meter", "bills_20000", 20000,
                "lp41_Send20KBillsInMeter"),
            BillCountLayout(0x42, "bills_25000_meter", "bills_25000", 25000,
                "lp42_Send25KBillsInMeter"),
            BillCountLayout(0x43, "bills_50000_meter", "bills_50000", 50000,
                "lp43_Send50KBillsInMeter"),
            BillCountLayout(0x44, "bills_100000_meter", "bills_100000", 100000,
                "lp44_Send100KBillsInMeter"),
            BillCountLayout(0x45, "bills_250_meter", "bills_250", 250, "lp45_Send250BillsInMeter"),

            // The other single meters that answer through the same shared handler. $47 reports
            // the very meter $2A does - TotalCreditsFromCoinAcceptor - so on this library the
            // external coin acceptor and the coin acceptor can never disagree. Recorded because a
            // test written against one of them is really written against both.
            SharedMeterLayout(0x2A, "true_coin_in", "true_coin_in", SasUnit.Credits,
                "Credits taken by the coin acceptor.", "lp2A_SendTrueCoinIn"),
            SharedMeterLayout(0x2B, "true_coin_out", "true_coin_out", SasUnit.Credits,
                "Credits paid out of the hopper.", "lp2B_SendTrueCoinOut"),
            SharedMeterLayout(0x46, "bills_accepted_credits", "bills_accepted_credits",
                SasUnit.Credits, "Credit value of every note the machine has taken.",
                "lp46_SendCreditAmountOfAllBillsAccepted"),
            SharedMeterLayout(0x47, "external_coin_acceptor_credits",
                "external_coin_acceptor_credits", SasUnit.Credits,
                "Credits taken by the external coin acceptor.",
                "lp47_SendCoinAmountAcceptedFromExternalCoinAcceptor"),
            SharedMeterLayout(0x49, "bills_in_stacker", "bills_in_stacker", SasUnit.Count,
                "How many notes are in the stacker now.",
                "lp49_SendNumberOfBillsCurrentlyInTheStacker"),
            SharedMeterLayout(0x4A, "stacker_credits", "stacker_credits", SasUnit.Credits,
                "Credit value of the notes in the stacker now.",
                "lp4A_SendTotalCreditAmountOfAllBillsCurrentlyInTheStacker"),

            // Two two-byte counts rather than the usual four-byte meter, which is the whole
            // reason this command exists separately from the ones either side of it.
            new()
            {
                Command = 0x18,
                Name = "games_since_power_up",
                Description = "Games played since the machine last powered up and since its door "
                              + "last closed.",
                Response =
                [
                    Bcd("games_since_power_up", 2, SasUnit.Count,
                        "Games played since the last power up."),
                    Bcd("games_since_door_closure", 2, SasUnit.Count,
                        "Games played since the slot door last closed."),
                ],
                DerivedFrom = "lp18_SendGamesPlayedSinceLastPowerUpAndSlotDoorClosure",
                ConfirmedByCapture = true,
            },
            new()
            {
                Command = 0x19,
                Name = "meters_five",
                Description = "Five accounting meters in one reply.",
                Response =
                [
                    Bcd("total_coin_in", 4, SasUnit.Credits, "Credits wagered."),
                    Bcd("total_coin_out", 4, SasUnit.Credits, "Credits paid out."),
                    Bcd("total_drop", 4, SasUnit.Credits, "Credits dropped into the machine."),
                    Bcd("total_jackpot", 4, SasUnit.Credits, "Credits paid as jackpots."),
                    Bcd("games_played", 4, SasUnit.Count, "Games played."),
                ],
                DerivedFrom = "lp19_SendMeters",
                ConfirmedByCapture = true,
            },
            new()
            {
                Command = 0x1C,
                Name = "meters_eight",
                Description = "Eight accounting meters in one reply.",
                Response =
                [
                    Bcd("total_coin_in", 4, SasUnit.Credits, "Credits wagered."),
                    Bcd("total_coin_out", 4, SasUnit.Credits, "Credits paid out."),
                    Bcd("total_drop", 4, SasUnit.Credits,
                        "Credits dropped into the machine."),
                    Bcd("total_jackpot", 4, SasUnit.Credits, "Credits paid as jackpots."),
                    Bcd("games_played", 4, SasUnit.Count, "Games played."),
                    Bcd("games_won", 4, SasUnit.Count, "Games won."),
                    Bcd("slot_door_opened", 4, SasUnit.Count, "Times the slot door was opened."),
                    Bcd("power_reset", 4, SasUnit.Count, "Times mains power was reset."),
                ],
                DerivedFrom = "lp1C_SendMeters",
                ConfirmedByCapture = true,
            },
            GameMeterLayout(0x2D, "handpay_cancelled_credits",
                "Credits cancelled by a handpay, for one game.",
                "lp2D_SendTotalHandpayCancelledCredits",
                Bcd("handpay_cancelled_credits", 4, SasUnit.Credits,
                    "Credits cancelled by a handpay.")),
            GameMeterLayout(0x52, "game_n_meters", "Four accounting meters for one game.",
                "lp52_SendGameNMeters",
                Bcd("total_coin_in", 4, SasUnit.Credits, "Credits wagered on this game."),
                Bcd("total_coin_out", 4, SasUnit.Credits, "Credits paid out by this game."),
                Bcd("total_jackpot", 4, SasUnit.Credits, "Credits paid as jackpots by this game."),
                Bcd("games_played", 4, SasUnit.Count, "Times this game was played.")),
            GameMeterLayout(0x83, "cumulative_progressive_wins",
                "Progressive wins for one game, deductible and non-deductible added together.",
                "lp83_SendCumulativeProgressiveWins",
                Bcd("cumulative_progressive_win", 4, SasUnit.Credits,
                    "Progressive amounts this game has won, added together.")),
            GameMeterLayout(0x9A, "legacy_bonus_meters",
                "The three legacy bonus meters for one game.", "lp9A_SendLegacyBonusMeters",
                Bcd("deductible", 4, SasUnit.Credits, "Deductible legacy bonus paid."),
                Bcd("non_deductible", 4, SasUnit.Credits, "Non-deductible legacy bonus paid."),
                Bcd("wager_match", 4, SasUnit.Credits, "Wager match legacy bonus paid.")),
            new()
            {
                Command = 0x48,
                Name = "last_accepted_bill",
                Description = "What the last note the machine took was, and how many of that "
                              + "denomination it has taken.",
                Response =
                [
                    Bcd("country_code", 1, SasUnit.Code, "Which currency the note was."),
                    Bcd("denomination_code", 1, SasUnit.Code, "Which denomination the note was."),
                    Bcd("count", 4, SasUnit.Count,
                        "How many notes of that denomination have been taken."),
                ],
                DerivedFrom = "lp48_SendLastAcceptedBillInformation",
                ConfirmedByCapture = true,
            },
            new()
            {
                Command = 0x50,
                Name = "validation_meters",
                Description = "How many tickets of one validation type the machine has issued or "
                              + "taken, and their total value.",
                Request =
                [
                    Byte("validation_type", SasUnit.Code, "Which kind of validation to report."),
                ],
                Response =
                [
                    Byte("validation_type", SasUnit.Code,
                        "The kind of validation being reported."),
                    Bcd("quantity", 4, SasUnit.Count, "How many of them."),
                    Bcd("amount", 5, SasUnit.Cents, "Their total value."),
                ],
                DerivedFrom = "lp50_SendValidationMeters",
                ConfirmedByCapture = true,
            },
            new()
            {
                Command = 0x53,
                Name = "game_n_configuration",
                Description = "How one game is configured: its identifiers, its denomination, its "
                              + "maximum bet, its paytable and its payback percentage.",
                Request = [GameNumber()],
                Response =
                [
                    GameNumber(),
                    Ascii("game_id", 2, "Two characters naming the maker."),
                    Ascii("additional_id", 3, "Three more characters the maker chooses."),
                    Byte("denomination", SasUnit.Code, "Accounting denomination code."),
                    Byte("max_bet", SasUnit.Credits, "Largest bet this game takes, in credits."),
                    Byte("progressive_group", SasUnit.Index,
                        "Which progressive group this game belongs to."),

                    // Little-endian, as the identification reply's game options are. Read the
                    // other way round it decodes into a plausible wrong answer.
                    LittleEndianNumber("game_options", 2, "Game option bits."),
                    Ascii("paytable_id", 6, "Six characters naming the paytable."),
                    Ascii("base_percentage", 4,
                        "Base payback percentage as four digits, so 9210 is 92.10 per cent."),
                ],
                DerivedFrom = "lp53_SendGameNConfiguration",
                ConfirmedByCapture = true,
            },
            new()
            {
                Command = 0x55,
                Name = "selected_game_number",
                Description = "Which game the player currently has selected.",
                Response =
                [
                    Bcd("selected_game", 2, SasUnit.Index, "The game currently selected."),
                ],
                DerivedFrom = "lp55_SendSelectedGameNumber",
                ConfirmedByCapture = true,
            },
            new()
            {
                Command = 0x57,
                Name = "pending_cashout_information",
                Description = "What the machine is waiting to pay out, and how.",
                Response =
                [
                    Byte("cashout_type", SasUnit.Code,
                        "How the machine intends to pay: a ticket, a handpay, or neither."),
                    Bcd("amount", 5, SasUnit.Cents, "What it is waiting to pay."),
                ],
                DerivedFrom = "lp57_SendPendingCashoutInformation",
                ConfirmedByCapture = true,
            },
            new()
            {
                Command = 0x84,
                Name = "progressive_win_amount",
                Description = "The progressive amount the machine most recently won.",
                Response =
                [
                    Byte("progressive_group", SasUnit.Index, "Which progressive group."),
                    Byte("level", SasUnit.Index, "Which level was hit, or zero for none."),
                    Bcd("amount", 5, SasUnit.Cents, "What that level paid."),
                ],
                DerivedFrom = "lp84_SendProgressiveWinAmount",
                ConfirmedByCapture = true,
            },
            new()
            {
                Command = 0x85,
                Name = "sas_progressive_win_amount",
                Description = "The progressive amount the machine most recently won on a level a "
                              + "host had set.",
                Response =
                [
                    Byte("progressive_group", SasUnit.Index, "Which progressive group."),
                    Byte("level", SasUnit.Index, "Which level was hit, or zero for none."),
                    Bcd("amount", 5, SasUnit.Cents, "What that level paid."),
                ],
                DerivedFrom = "lp85_SendSasProgressiveWinAmount",
                ConfirmedByCapture = true,
            },
            new()
            {
                Command = 0x8E,
                Name = "card_information",
                Description = "The hand a card game is showing.",
                Response =
                [
                    Byte("hand_type", SasUnit.Code, "Which stage of the hand this is."),
                    Byte("card_1", SasUnit.Code, "First card."),
                    Byte("card_2", SasUnit.Code, "Second card."),
                    Byte("card_3", SasUnit.Code, "Third card."),
                    Byte("card_4", SasUnit.Code, "Fourth card."),
                    Byte("card_5", SasUnit.Code, "Fifth card."),
                ],
                DerivedFrom = "lp8E_SendCardInformation",
                ConfirmedByCapture = true,
            },

            // Nine reel positions, not five. The overlay diff found the two other descriptions of
            // SAS disagreeing about this reply's length - seven bytes against eleven - and the
            // library's handler settled it at eleven. This is the same answer off a real machine.
            new()
            {
                Command = 0x8F,
                Name = "physical_reel_stop_information",
                Description = "Where each of the machine's reels stopped.",
                Response =
                [
                    Byte("reel_1", SasUnit.Index, "Where reel one stopped."),
                    Byte("reel_2", SasUnit.Index, "Where reel two stopped."),
                    Byte("reel_3", SasUnit.Index, "Where reel three stopped."),
                    Byte("reel_4", SasUnit.Index, "Where reel four stopped."),
                    Byte("reel_5", SasUnit.Index, "Where reel five stopped."),
                    Byte("reel_6", SasUnit.Index, "Where reel six stopped."),
                    Byte("reel_7", SasUnit.Index, "Where reel seven stopped."),
                    Byte("reel_8", SasUnit.Index, "Where reel eight stopped."),
                    Byte("reel_9", SasUnit.Index, "Where reel nine stopped."),
                ],
                DerivedFrom = "lp8F_SendPhysicalReelStopInformation",
                ConfirmedByCapture = true,
            },
            new()
            {
                Command = 0x90,
                Name = "legacy_bonus_win_amount",
                Description = "The legacy bonus the machine most recently paid.",
                Response =
                [
                    Byte("multiplier", SasUnit.None, "What the win was multiplied by."),
                    Bcd("multiplied_win", 4, SasUnit.Credits, "The multiplied win."),
                    Byte("tax_status", SasUnit.Code, "How the bonus is taxed."),
                    Bcd("bonus", 4, SasUnit.Credits, "The bonus itself."),
                ],
                DerivedFrom = "lp90_SendLegacyBonusWinAmount",
                ConfirmedByCapture = true,
            },
            new()
            {
                Command = 0xB3,
                Name = "token_denomination",
                Description = "What the machine's token is worth.",
                Response =
                [
                    Byte("token_denomination", SasUnit.Code, "Denomination code of the token."),
                ],
                DerivedFrom = "lpB3_SendTokenDenomination",
                ConfirmedByCapture = true,
            },

            // The length-prefixed replies. Each states its own length, and in three of them that
            // is the only thing that says where the message ends.
            new()
            {
                Command = 0x4F,
                Name = "current_hopper_status",
                Description = "What the hopper is doing, how full it is, and how much is in it.",
                Response =
                [
                    Byte("length", SasUnit.Count,
                        "Bytes following, not counting the check sequence."),
                    Byte("status", SasUnit.Code, "What the hopper is doing."),
                    Byte("percent_full", SasUnit.None,
                        "How full the hopper is, or $FF when it cannot tell."),

                    // The level is in the reply only when the hopper can measure it. A machine
                    // that cannot answers five bytes rather than nine, and its length byte says so.
                    Bcd("hopper_level", 4, SasUnit.Count, "How many coins are in the hopper.") with
                    {
                        PresentWhen = new SasCondition(
                            "percent_full", SasComparison.NotEqual, 0xFF),
                    },
                ],
                DerivedFrom = "lp4F_SendCurrentHopperStatus",
                ConfirmedByCapture = true,
            },
            new()
            {
                Command = 0x56,
                Name = "enabled_game_numbers",
                Description = "Which of the machine's games are enabled.",
                Response =
                [
                    Byte("length", SasUnit.Count,
                        "Bytes following, not counting the check sequence."),
                    Byte("count", SasUnit.Count, "How many games are enabled."),
                    new()
                    {
                        Name = "games",
                        Length = 0,
                        Repeat = SasRepeat.CountedBy("count"),
                        Description = "One entry per enabled game.",
                        Fields = [Bcd("game_number", 2, SasUnit.Index, "An enabled game.")],
                    },
                ],
                DerivedFrom = "lp56_SendEnabledGameNumbers",
                ConfirmedByCapture = true,
            },
            new()
            {
                Command = 0x87,
                Name = "multiple_sas_progressive_win_amount",
                Description = "Every progressive level the machine most recently won on.",
                Response =
                [
                    Byte("length", SasUnit.Count,
                        "Bytes following, not counting the check sequence."),
                    Byte("progressive_group", SasUnit.Index, "Which progressive group."),
                    Byte("count", SasUnit.Count, "How many levels follow."),
                    new()
                    {
                        Name = "levels",
                        Length = 0,
                        Repeat = SasRepeat.CountedBy("count"),
                        Description = "One entry per level won.",
                        Fields =
                        [
                            Byte("level", SasUnit.Index, "Which level."),
                            Bcd("amount", 5, SasUnit.Cents, "What it paid."),
                        ],
                    },
                ],
                DerivedFrom = "lp87_SendMultipleSasProgressiveWinAmount",
                ConfirmedByCapture = true,
            },
            new()
            {
                Command = 0xB2,
                Name = "enabled_player_denominations",
                Description = "Which denominations a player may choose.",
                Response =
                [
                    Byte("length", SasUnit.Count,
                        "Bytes following, not counting the check sequence."),
                    Byte("count", SasUnit.Count,
                        "How many denominations are enabled."),
                    new()
                    {
                        Name = "denominations",
                        Length = 0,
                        Repeat = SasRepeat.CountedBy("count"),
                        Description = "One code per enabled denomination.",
                        Fields =
                        [
                            Byte("denomination", SasUnit.Code,
                                "An enabled denomination."),
                        ],
                    },
                ],
                DerivedFrom = "lpB2_SendEnabledPlayerDenominations",
                ConfirmedByCapture = true,
            },

            // The coin-in meter here is as wide as the machine says it is, which is the same
            // general shape long poll $6F needed: a field whose length an earlier field states.
            new()
            {
                Command = 0xB4,
                Name = "wager_category_information",
                Description = "What one wager category pays back, and how much has been wagered "
                              + "in it.",
                Request =
                [
                    GameNumber(),
                    new()
                    {
                        Name = "wager_category",
                        Length = 2,
                        Encoding = SasFieldEncoding.Binary,
                        ByteOrder = SasByteOrder.BigEndian,
                        Unit = SasUnit.Code,
                        Description = "Which wager category; zero means the game as a whole.",
                    },
                ],
                Response =
                [
                    Byte("length", SasUnit.Count,
                        "Bytes following, not counting the check sequence."),
                    GameNumber(),
                    new()
                    {
                        Name = "wager_category",
                        Length = 2,
                        Encoding = SasFieldEncoding.Binary,
                        ByteOrder = SasByteOrder.BigEndian,
                        Unit = SasUnit.Code,
                        Description = "The wager category being reported.",
                    },
                    Ascii("payback_percentage", 4,
                        "Payback percentage as four digits, so 9210 is 92.10 per cent."),
                    Byte("meter_size", SasUnit.Count,
                        "How many bytes the coin-in meter that follows occupies."),
                    new()
                    {
                        Name = "coin_in",
                        Length = 0,
                        LengthFromField = "meter_size",
                        Encoding = SasFieldEncoding.Bcd,
                        ByteOrder = SasByteOrder.BigEndian,
                        Unit = SasUnit.Credits,
                        Description = "Credits wagered in this category, as wide as the size byte "
                                      + "says.",
                    },
                ],
                DerivedFrom = "lpB4_SendWagerCategoryInformation",
                ConfirmedByCapture = true,
            },

            // Two length-prefixed strings inside one reply, which is why the fields after them
            // cannot be at fixed offsets. The machine that answered this one is called
            // "Multigame Pack" and its paytable "PT_001". The library can also append a list of
            // player denominations here, which its own comment calls an off-by-default extension
            // for legacy hosts and a deviation from the specification's table; a machine with it
            // turned on produces bytes this layout reports as an unmodelled trailer rather than
            // failing to fit.
            new()
            {
                Command = 0xB5,
                Name = "extended_game_n_information",
                Description = "One game's name, paytable, maximum bet and progressive levels.",
                Request = [GameNumber()],
                Response =
                [
                    Byte("length", SasUnit.Count,
                        "Bytes following, not counting the check sequence."),
                    GameNumber(),
                    Bcd("max_bet", 2, SasUnit.Credits, "Largest bet this game takes."),
                    Byte("progressive_group", SasUnit.Index, "Which progressive group."),

                    // Copied straight out of a machine word rather than reversed, which the
                    // handler's own comment says in as many words.
                    LittleEndianNumber("progressive_levels", 4,
                        "One bit per progressive level this game uses."),
                    Byte("name_length", SasUnit.Count, "How many characters the game name is."),
                    new()
                    {
                        Name = "game_name",
                        Length = 0,
                        LengthFromField = "name_length",
                        Encoding = SasFieldEncoding.Ascii,
                        ByteOrder = SasByteOrder.NotApplicable,
                        Description = "What the game is called.",
                    },
                    Byte("paytable_length", SasUnit.Count,
                        "How many characters the paytable name is."),
                    new()
                    {
                        Name = "paytable_name",
                        Length = 0,
                        LengthFromField = "paytable_length",
                        Encoding = SasFieldEncoding.Ascii,
                        ByteOrder = SasByteOrder.NotApplicable,
                        Description = "What the paytable is called.",
                    },
                    Bcd("wager_categories", 2, SasUnit.Count,
                        "How many wager categories this game has."),
                ],
                DerivedFrom = "lpB5_SendExtendedGameNInformation",
                ConfirmedByCapture = true,
            },
        ];

        return layouts.ToDictionary(l => l.Command).ToFrozenDictionary();
    }
}
