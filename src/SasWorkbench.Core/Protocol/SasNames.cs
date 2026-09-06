using System.Collections.Frozen;
using System.Text;

namespace SasWorkbench.Core.Protocol;

/// <summary>
/// Turns the names people write in scripts into the codes on the wire.
/// </summary>
/// <remarks>
/// Every name here is derived from the catalogue, which is itself generated, so the vocabulary a
/// script may use grows with the library rather than with a list somebody maintains. Both the
/// library's own spelling and an underscored form are accepted, because
/// <c>USD20BillAccepted</c> and <c>usd_20_bill_accepted</c> are the same thing and a person will
/// write whichever they read last. A handful of aliases exist on top for the phrasings that read
/// naturally in a test.
/// </remarks>
public static class SasNames
{
    /// <summary>
    /// Extra spellings for names that read awkwardly in a script. Kept deliberately short: an
    /// alias that hides which meter is really being read makes a failure harder to explain.
    /// </summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bill_accepted_1"] = "USD1BillAccepted",
        ["bill_accepted_2"] = "USD2BillAccepted",
        ["bill_accepted_5"] = "USD5BillAccepted",
        ["bill_accepted_10"] = "USD10BillAccepted",
        ["bill_accepted_20"] = "USD20BillAccepted",
        ["bill_accepted_50"] = "USD50BillAccepted",
        ["bill_accepted_100"] = "USD100BillAccepted",
        ["slot_door_open"] = "SlotDoorOpened",
        ["slot_door_closed"] = "SlotDoorClosed",
    };

    private static readonly FrozenDictionary<string, byte> ExceptionsByName = BuildExceptionNames();

    private static readonly FrozenDictionary<string, ushort> MetersByName = BuildMeterNames();

    private static readonly FrozenDictionary<string, byte> TicketCodesByName = BuildTicketCodeNames();

    /// <summary>Turns an identifier into the underscored form a script writer would type.</summary>
    public static string Underscore(string symbol)
    {
        var builder = new StringBuilder(symbol.Length + 8);
        for (int i = 0; i < symbol.Length; i++)
        {
            char c = symbol[i];
            if (c == '_')
            {
                builder.Append('_');
                continue;
            }

            bool boundary = i > 0
                && (char.IsUpper(c) && !char.IsUpper(symbol[i - 1])
                    || char.IsDigit(c) && !char.IsDigit(symbol[i - 1])
                    || char.IsUpper(c) && char.IsUpper(symbol[i - 1])
                       && i + 1 < symbol.Length && char.IsLower(symbol[i + 1]));

            if (boundary && builder.Length > 0 && builder[^1] != '_')
                builder.Append('_');

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Resolves an exception written as a name, an underscored name, an alias, or a hex code.
    /// </summary>
    public static bool TryResolveException(string? text, out byte code)
    {
        code = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string name = text.Trim();
        if (Aliases.TryGetValue(name, out string? aliased))
            name = aliased;

        if (ExceptionsByName.TryGetValue(name, out code))
            return true;

        return TryParseCode(name, out code) && SasCatalogue.Exceptions.ContainsKey(code);
    }

    /// <summary>Resolves a meter written as a name, an underscored name, or a hex code.</summary>
    public static bool TryResolveMeter(string? text, out ushort meter)
    {
        meter = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string name = text.Trim();
        if (MetersByName.TryGetValue(name, out meter))
            return true;

        if (!TryParseCode(name, out byte code))
            return false;

        meter = code;
        return SasCatalogue.Meters.ContainsKey(meter);
    }

    /// <summary>The catalogue's own description of an exception, or a plain hex label.</summary>
    public static string DescribeException(byte code) =>
        SasCatalogue.Exceptions.TryGetValue(code, out SasExceptionInfo? info)
            ? $"${code:X2} {info.Description}"
            : $"${code:X2} (not in the catalogue)";

    /// <summary>The underscored name of an exception, for a report.</summary>
    public static string ExceptionName(byte code) =>
        SasCatalogue.Exceptions.TryGetValue(code, out SasExceptionInfo? info)
            ? Underscore(info.Symbol)
            : $"${code:X2}";

    /// <summary>
    /// Resolves a value written into a command field: a number, a hex code, or the library's own
    /// name for a ticket transfer code or a gaming machine status.
    /// </summary>
    /// <remarks>
    /// The names are the library's, shouted underscores and all, rather than friendlier ones
    /// invented here. A script that says <c>ttc_valid_cashable_ticket</c> can be checked against
    /// the header in one step; a script that says <c>accept</c> cannot, and two people would not
    /// choose the same word for it.
    /// </remarks>
    public static bool TryResolveFieldValue(string? text, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string name = text.Trim();
        if (TicketCodesByName.TryGetValue(name, out byte code))
        {
            value = code;
            return true;
        }

        if (name.StartsWith('$') || name.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseCode(name, out byte parsed))
                return false;

            value = parsed;
            return true;
        }

        // Plain decimal, and deliberately not the bare-two-hex-digits rule TryParseCode applies:
        // an amount of 20 cents written next to a code of 20 must not become thirty-two.
        return long.TryParse(name, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Every name a command field may be given a value by, sorted, for a message.</summary>
    public static IReadOnlyList<string> FieldValueNames =>
        TicketCodesByName.Keys.Where(k => k.Contains('_')).Order(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Parses a validation style written the way a script or a report spells it.
    /// </summary>
    /// <remarks>
    /// The reserved style is deliberately not parseable. It is a value a misconfigured machine can
    /// report, so it has to be readable off the wire, but it is not one anybody may ask for.
    /// </remarks>
    public static bool TryParseValidation(string text, out SasCashoutValidation validation)
    {
        validation = default;
        string bare = text.Trim().Replace("_", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse(bare, ignoreCase: true, out validation)
               && validation != SasCashoutValidation.Reserved
               && Enum.IsDefined(validation);
    }

    /// <summary>Every validation style a script may ask for, sorted, for a message.</summary>
    public static IReadOnlyList<string> ValidationNames =>
        Enum.GetValues<SasCashoutValidation>()
            .Where(v => v != SasCashoutValidation.Reserved)
            .Select(v => Underscore(v.ToString()))
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>Parses <c>$4A</c>, <c>0x4A</c>, <c>4A</c> or a plain decimal number.</summary>
    public static bool TryParseCode(string text, out byte code)
    {
        code = 0;
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
            return false;

        bool hex = false;
        if (trimmed[0] == '$')
        {
            trimmed = trimmed[1..];
            hex = true;
        }
        else if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
            hex = true;
        }
        else if (trimmed.Length == 2 && trimmed.Any(char.IsAsciiHexDigitUpper))
        {
            // A bare two-character code is written in hex everywhere in this protocol.
            hex = true;
        }

        return byte.TryParse(
            trimmed,
            hex ? System.Globalization.NumberStyles.HexNumber
                : System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out code);
    }

    private static FrozenDictionary<string, byte> BuildExceptionNames()
    {
        var names = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (SasExceptionInfo info in SasCatalogue.Exceptions.Values)
        {
            names.TryAdd(info.Symbol, info.Code);
            names.TryAdd(Underscore(info.Symbol), info.Code);
        }

        return names.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The ticket transfer codes and machine statuses, by the library's name for each.
    /// </summary>
    /// <remarks>
    /// One table over both enumerations. Their names cannot collide - every transfer code begins
    /// <c>TTC_</c> and every machine status <c>GMSC_</c> - so a caller does not have to say which
    /// of the two it meant, and a field that takes one of them will not silently accept the other
    /// by number alone.
    /// </remarks>
    private static FrozenDictionary<string, byte> BuildTicketCodeNames()
    {
        var names = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        foreach (SasTicketTransferCode code in Enum.GetValues<SasTicketTransferCode>())
        {
            names.TryAdd(code.ToString(), (byte)code);
            names.TryAdd(Underscore(code.ToString()), (byte)code);
        }

        foreach (SasTicketMachineStatus status in Enum.GetValues<SasTicketMachineStatus>())
        {
            names.TryAdd(status.ToString(), (byte)status);
            names.TryAdd(Underscore(status.ToString()), (byte)status);
        }

        foreach (SasTicketStatus status in Enum.GetValues<SasTicketStatus>())
        {
            names.TryAdd(status.ToString(), (byte)status);
            names.TryAdd(Underscore(status.ToString()), (byte)status);
        }

        return names.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenDictionary<string, ushort> BuildMeterNames()
    {
        var names = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        foreach (SasMeterInfo info in SasCatalogue.Meters.Values)
        {
            names.TryAdd(info.Symbol, info.Code);
            names.TryAdd(Underscore(info.Symbol), info.Code);
        }

        return names.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
