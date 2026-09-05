using System.Globalization;
using System.Text;

namespace SasWorkbench.Core.Tickets;

/// <summary>How a field is rendered when an encoding has native types to choose from.</summary>
public enum TicketFieldKind
{
    /// <summary>Free text, written as a string in every encoding.</summary>
    Text,

    /// <summary>A whole number, written as a JSON number and as plain digits in text.</summary>
    Number,

    /// <summary>
    /// A whole number that stays text even in JSON, because it is an identifier nobody does
    /// arithmetic on and it can be wider than a JSON reader's double can hold exactly. A secure
    /// enhanced validation number is sixteen digits, which is past 2^53, so writing it as a JSON
    /// number would let a reader round it and hand back a ticket that is not the one written.
    /// </summary>
    Digits,

    /// <summary>True or false, written as a JSON boolean and as true/false in text.</summary>
    Boolean,

    /// <summary>A calendar day as yyyy-MM-dd, or nothing at all when it is not set.</summary>
    Date,

    /// <summary>A moment as yyyy-MM-ddTHH:mm:ss, or nothing at all when it is not set.</summary>
    Timestamp,
}

/// <summary>
/// One field of a ticket, named once and understood by every encoding.
/// </summary>
/// <param name="Name">The key in both the text file and the JSON object.</param>
/// <param name="Kind">What the field is, so an encoding with native types can use them.</param>
/// <param name="Format">The field's lexical value, or the empty string when it is not set.</param>
/// <param name="Apply">A ticket with this one field taken from a lexical value.</param>
public sealed record TicketField(
    string Name,
    TicketFieldKind Kind,
    Func<Ticket, string> Format,
    Func<Ticket, string, Ticket> Apply);

/// <summary>
/// The one table both encodings read.
/// </summary>
/// <remarks>
/// This exists so a ticket cannot come to mean two different things. A field added here appears in
/// the text file and in the JSON at once, and a field added to only one of two separate readers is
/// the failure this table makes impossible. The order is the order a written file carries, chosen
/// so the identity and the money sit at the top where a person editing one looks first.
/// </remarks>
public static class TicketFields
{
    /// <summary>Every field of a ticket, in the order a written file carries them.</summary>
    public static IReadOnlyList<TicketField> All { get; } =
    [
        new("validation_number", TicketFieldKind.Digits,
            t => t.ValidationNumber.ToString(CultureInfo.InvariantCulture),
            (t, v) => t with { ValidationNumber = ParseUnsigned(v, ulong.MaxValue, "validation_number") }),

        new("validation_type", TicketFieldKind.Text,
            t => SnakeCase(t.ValidationType.ToString()),
            (t, v) => t with { ValidationType = ParseValidationType(v) }),

        new("amount_cents", TicketFieldKind.Number,
            t => t.AmountCents.ToString(CultureInfo.InvariantCulture),
            (t, v) => t with { AmountCents = ParseSigned(v, "amount_cents") }),

        new("ticket_number", TicketFieldKind.Number,
            t => t.TicketNumber.ToString(CultureInfo.InvariantCulture),
            (t, v) => t with { TicketNumber = (uint)ParseUnsigned(v, uint.MaxValue, "ticket_number") }),

        new("pool_id", TicketFieldKind.Number,
            t => t.PoolId.ToString(CultureInfo.InvariantCulture),
            (t, v) => t with { PoolId = (ushort)ParseUnsigned(v, ushort.MaxValue, "pool_id") }),

        new("expiration", TicketFieldKind.Date,
            t => t.Expiration?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            (t, v) => t with { Expiration = ParseDate(v) }),

        new("validation_system_id", TicketFieldKind.Number,
            t => t.ValidationSystemId.ToString(CultureInfo.InvariantCulture),
            (t, v) => t with { ValidationSystemId = (byte)ParseUnsigned(v, byte.MaxValue, "validation_system_id") }),

        new("printed_at", TicketFieldKind.Timestamp,
            t => t.PrintedAt?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty,
            (t, v) => t with { PrintedAt = ParseTimestamp(v) }),

        new("asset_number", TicketFieldKind.Number,
            t => t.AssetNumber.ToString(CultureInfo.InvariantCulture),
            (t, v) => t with { AssetNumber = (uint)ParseUnsigned(v, uint.MaxValue, "asset_number") }),

        new("location", TicketFieldKind.Text, t => t.Location, (t, v) => t with { Location = v }),
        new("address1", TicketFieldKind.Text, t => t.Address1, (t, v) => t with { Address1 = v }),
        new("address2", TicketFieldKind.Text, t => t.Address2, (t, v) => t with { Address2 = v }),

        new("redeemed", TicketFieldKind.Boolean,
            t => t.Redeemed ? "true" : "false",
            (t, v) => t with { Redeemed = ParseBoolean(v) }),

        new("note", TicketFieldKind.Text, t => t.Note, (t, v) => t with { Note = v }),
    ];

    /// <summary>The field of that name, or null when a file names something we do not know.</summary>
    public static TicketField? Find(string name)
    {
        foreach (TicketField field in All)
        {
            if (string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase))
                return field;
        }

        return null;
    }

    /// <summary>PascalCase to the snake_case a person reads and writes.</summary>
    internal static string SnakeCase(string name)
    {
        var text = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]))
            {
                if (i > 0)
                    text.Append('_');

                text.Append(char.ToLowerInvariant(name[i]));
            }
            else
            {
                text.Append(name[i]);
            }
        }

        return text.ToString();
    }

    private static TicketValidationType ParseValidationType(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
            return TicketValidationType.CashableFromCashout;

        // A number is accepted because the wire codes are what a specification-shaped case names.
        if (TryParseNumber(trimmed, out ulong code))
        {
            if (code > byte.MaxValue || !Enum.IsDefined((TicketValidationType)(byte)code))
                throw new TicketFormatException($"'{value}' is not a validation type this build knows.");

            return (TicketValidationType)(byte)code;
        }

        string wanted = trimmed.Replace("_", string.Empty);
        foreach (TicketValidationType candidate in Enum.GetValues<TicketValidationType>())
        {
            if (string.Equals(candidate.ToString(), wanted, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        throw new TicketFormatException($"'{value}' is not a validation type this build knows.");
    }

    private static ulong ParseUnsigned(string value, ulong max, string field)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
            return 0;

        if (!TryParseNumber(trimmed, out ulong parsed) || parsed > max)
            throw new TicketFormatException($"'{value}' is not a whole number {field} can hold.");

        return parsed;
    }

    private static long ParseSigned(string value, string field)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
            return 0;

        bool negative = trimmed.StartsWith('-');
        if (negative)
            trimmed = trimmed[1..];

        if (!TryParseNumber(trimmed, out ulong magnitude) || magnitude > long.MaxValue)
            throw new TicketFormatException($"'{value}' is not a whole number {field} can hold.");

        return negative ? -(long)magnitude : (long)magnitude;
    }

    // Hex is accepted because several of these values are quoted in hex by the specification, and a
    // person copying one out should not have to convert it first.
    private static bool TryParseNumber(string text, out ulong value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

        return ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static DateOnly? ParseDate(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
            return null;

        if (!DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateOnly parsed))
            throw new TicketFormatException($"'{value}' is not a date; expiration is written as yyyy-MM-dd.");

        return parsed;
    }

    private static DateTime? ParseTimestamp(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
            return null;

        if (!DateTime.TryParseExact(trimmed, ["yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
            throw new TicketFormatException($"'{value}' is not a time; printed_at is written as yyyy-MM-ddTHH:mm:ss.");

        return parsed;
    }

    private static bool ParseBoolean(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "" or "false" or "no" or "0" => false,
            "true" or "yes" or "1" => true,
            _ => throw new TicketFormatException($"'{value}' is not true or false."),
        };
    }
}
