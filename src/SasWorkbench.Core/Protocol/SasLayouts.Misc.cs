namespace SasWorkbench.Core.Protocol;

public static partial class SasLayouts
{
    /// <summary>The layouts of the note acceptor switch, the ROM signature, the ticket text polls and the cash-out limit.</summary>
    private static SasMessageLayout[] Miscellaneous() =>
    [
        new()
        {
            Command = 0x06,
            Name = "enable_bill_acceptor",
            Description = "Lets the note acceptor take notes again.",
            Shape = SasResponseShape.Acknowledgement,
            DerivedFrom = "lpXX_EnableDisable",
        },
        new()
        {
            Command = 0x07,
            Name = "disable_bill_acceptor",
            Description = "Stops the note acceptor taking notes.",
            Shape = SasResponseShape.Acknowledgement,
            DerivedFrom = "lpXX_EnableDisable",
        },
        RomSignature(),
        SetCustomTicketData(),
        SetExtendedTicketData(),
        SetTicketData(),
        new()
        {
            Command = 0xA4,
            Name = "cashout_limit",
            Description = "The most the machine pays before it locks up for an attendant, for one game.",
            Request =
            [
                Bcd("game_number", 2, SasUnit.Index, "Which game to ask about; zero means the machine as a whole."),
            ],
            Response =
            [
                Bcd("game_number", 2, SasUnit.Index, "Which game this answers for, echoed from the request."),
                Bcd("cashout_limit", 2, SasUnit.Credits, "The limit, in accounting units."),
            ],
            DerivedFrom = "lpA4_SendCashoutLimit",
        },
    ];

    /// <summary>The two bytes of the seed and of the signature, most significant first (internalReverseCopy in lp21_RomSignature).</summary>
    private static SasField BigEndianWord(string name, string description) => new()
    {
        Name = name,
        Length = 2,
        Encoding = SasFieldEncoding.Binary,
        ByteOrder = SasByteOrder.BigEndian,
        Unit = SasUnit.Code,
        Description = description,
    };

    /// <summary>Long poll $21, acknowledged at once; the signature itself answers a later general poll.</summary>
    private static SasMessageLayout RomSignature() => new()
    {
        Command = 0x21,
        Name = "rom_signature",
        Description = "Asks the machine to compute a signature of its program, seeded by the host.",
        Request = [BigEndianWord("seed", "What the signature calculation starts from.")],
        Shape = SasResponseShape.Acknowledgement,
        DerivedFrom = "lp21_RomSignature",
    };

    /// <summary>The reply a ROM signature arrives in, on a general poll or in place of a long poll's reply.</summary>
    public static SasMessageLayout RomSignatureReply => new()
    {
        Command = 0x21,
        Name = "rom_signature_reply",
        Description = "The signature the machine computed from the host's seed.",
        Response = [BigEndianWord("signature", "The signature, a check sequence of the program.")],
        DerivedFrom = "sasThread, the pending ROM signature branch",
    };

    // One element: a data code, its length and its text.
    private static SasField TicketElements(string description) => new()
    {
        Name = "elements",
        Length = 0,
        Repeat = SasRepeat.ToEndOfMessage,
        Description = description,
        Fields =
        [
            Byte("code", SasUnit.Code, "Which line the text is for."),
            Byte("element_length", SasUnit.Count, "How many characters follow; zero reverts the line."),
            TextOfLength("text", "element_length", "The text itself."),
        ],
    };

    /// <summary>Long poll $76, the custom text and graphics a transfer ticket may carry.</summary>
    private static SasMessageLayout SetCustomTicketData() => new()
    {
        Command = 0x76,
        Name = "set_custom_ticket_data",
        Description = "Loads or clears the custom text a ticket printed from a transfer may carry.",
        Request =
        [
            Byte("length", SasUnit.Count, "Bytes following, not counting the check sequence."),
            Byte("function_code", SasUnit.Code, "$00 sets the elements that follow, $80 clears them all."),
            TicketElements("Each element as a code, a length and its text."),
        ],
        Response =
        [
            Byte("length", SasUnit.Count, "How many element codes follow."),
            RawOfLength("element_codes", "length", "The code of every element the machine holds, in code order."),
        ],
        DerivedFrom = "lp76_SetCustomAftTicketData",
        Broadcast = true,
    };

    /// <summary>Long poll $7C, the location, address lines and titles printed on tickets.</summary>
    private static SasMessageLayout SetExtendedTicketData() => new()
    {
        Command = 0x7C,
        Name = "set_extended_ticket_data",
        Description = "Sets the text printed on tickets, line by line; with no lines it only asks.",
        Request =
        [
            Byte("length", SasUnit.Count, "Bytes following, not counting the check sequence."),
            TicketElements("Each line as a data code, a length and its text."),
        ],
        Response =
        [
            Byte("ticket_data_status", SasUnit.Code, "One when the data was set since the last reply, zero otherwise."),
        ],
        DerivedFrom = "lp7C_SetExtendedTicketData",
        Broadcast = true,
    };

    /// <summary>Long poll $7D, the older form: a host identifier, an expiration and three lines in a fixed order.</summary>
    private static SasMessageLayout SetTicketData() => new()
    {
        Command = 0x7D,
        Name = "set_ticket_data",
        Description = "Sets the host identifier, expiration, location and address lines printed on tickets.",
        Request =
        [
            Byte("length", SasUnit.Count, "Bytes following, not counting the check sequence."),
            LittleEndianNumber("host_id", 2, "The host's identifier.")
                with { PresentWhen = new SasCondition("length", SasComparison.AtLeast, 2) },
            Byte("expiration", SasUnit.Count, "Days a ticket stays redeemable.")
                with { PresentWhen = new SasCondition("length", SasComparison.GreaterThan, 2) },
            new()
            {
                Name = "lines",
                Length = 0,
                Repeat = SasRepeat.ToEndOfMessage,
                Description = "Location, then address line 1, then address line 2, each a length and its text.",
                PresentWhen = new SasCondition("length", SasComparison.GreaterThan, 2),
                Fields =
                [
                    Byte("line_length", SasUnit.Count, "How many characters follow."),
                    TextOfLength("text", "line_length", "The text itself."),
                ],
            },
        ],
        Response =
        [
            Byte("ticket_data_status", SasUnit.Code, "One when the data was set since the last reply, zero otherwise."),
        ],
        DerivedFrom = "lp7D_SetTicketData",
        Broadcast = true,
    };
}
