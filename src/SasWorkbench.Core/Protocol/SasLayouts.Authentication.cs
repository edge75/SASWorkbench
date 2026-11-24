namespace SasWorkbench.Core.Protocol;

public static partial class SasLayouts
{
    // Reading or authenticating a component carries an addressing mode and an index or a name; the
    // two interrogations carry the action alone.
    private static SasCondition AddressesComponent => new("length", SasComparison.GreaterThan, 1);

    // Only a reply about a component, or about the list, carries more than the check sequence and the status.
    private static SasCondition DescribesComponent => new("length", SasComparison.GreaterThan, 3);

    /// <summary>Long poll $6E, which reads the component list and starts or reads a component authentication.</summary>
    /// <remarks>
    /// Read from <c>lp6E_SendAuthenticationInfo</c>: the list check sequence and the status lead every reply, a
    /// component's name, size and methods follow wherever the library found one, and the method and result only
    /// while an authentication is running or has finished. The tail is read by what is left of the reply rather
    /// than by the status, so a byte the machine leaves out reads as absent, not as the next field.
    /// </remarks>
    private static SasMessageLayout SendAuthenticationInfo() => new()
    {
        Command = 0x6E,
        Name = "send_authentication_info",
        Description = "Reads the authentication component list, one component, or starts or reads an authentication.",
        Request =
        [
            Byte("length", SasUnit.Count, "Bytes following, not counting the check sequence."),
            Byte("action", SasUnit.Code,
                "$00 the list, $01 one component's status, $02 authenticate one, $03 how the last one went."),
            Byte("addressing_mode", SasUnit.Code, "$00 by index, $01 by name.")
                with { PresentWhen = AddressesComponent },
            Byte("index_or_name_length", SasUnit.Count, "How long the index or the name is.")
                with { PresentWhen = AddressesComponent },
            new()
            {
                Name = "index_or_name",
                Length = 0,
                LengthFromField = "index_or_name_length",
                Encoding = SasFieldEncoding.Raw,
                Description = "The component's index as one byte, or its name.",
                PresentWhen = AddressesComponent,
            },
            LittleEndianNumber("method", 4, "The authentication method, one bit.")
                with { PresentWhen = SasCondition.Remaining(SasComparison.AtLeast, 4) },
            Byte("seed_length", SasUnit.Count, "How long the seed is.")
                with { PresentWhen = SasCondition.Remaining(SasComparison.GreaterThan, 0) },
            RawOfLength("seed", "seed_length", "What the method starts from."),
            Byte("offset_length", SasUnit.Count, "How long the offset is.")
                with { PresentWhen = SasCondition.Remaining(SasComparison.GreaterThan, 0) },
            RawOfLength("offset", "offset_length", "Where in the component the method starts, least significant byte first."),
        ],
        Response =
        [
            Byte("length", SasUnit.Count, "Bytes following, not counting the check sequence."),
            LittleEndianNumber("component_list_crc", 2, "The check sequence over every component name, in list order."),
            Byte("status", SasUnit.Code,
                "$00 idle, $01 the list, $40 running, $41 done, $80 to $82 refused, $C0 to $C5 failed, $FF nothing to report."),
            Byte("name_length", SasUnit.Count, "How long the component's name, or the list's, is.")
                with { PresentWhen = DescribesComponent },
            TextOfLength("name", "name_length", "The component's name, or the list's.")
                with { PresentWhen = DescribesComponent },
            Byte("size_length", SasUnit.Count, "How many bytes the size takes.")
                with { PresentWhen = DescribesComponent },
            new()
            {
                Name = "size",
                Length = 0,
                LengthFromField = "size_length",
                Encoding = SasFieldEncoding.Binary,
                ByteOrder = SasByteOrder.LittleEndian,
                Unit = SasUnit.Count,
                Description = "The component's size in bytes, or how many components the list holds.",
                PresentWhen = DescribesComponent,
            },
            LittleEndianNumber("available_methods", 4, "The methods the component offers, one bit each.")
                with { PresentWhen = DescribesComponent },
            LittleEndianNumber("method", 4, "The method of the authentication running or finished.")
                with { PresentWhen = SasCondition.Remaining(SasComparison.AtLeast, 4) },
            Byte("authentication_length", SasUnit.Count, "How long the result is; zero while running.")
                with { PresentWhen = SasCondition.Remaining(SasComparison.GreaterThan, 0) },
            RawOfLength("authentication_data", "authentication_length", "The result, as the method computes it."),
        ],
        DerivedFrom = "lp6E_SendAuthenticationInfo",
    };

    private static SasField RawOfLength(string name, string lengthField, string description) => new()
    {
        Name = name,
        Length = 0,
        LengthFromField = lengthField,
        Encoding = SasFieldEncoding.Raw,
        Description = description,
        PresentWhen = SasCondition.Remaining(SasComparison.GreaterThan, 0),
    };
}
