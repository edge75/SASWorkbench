using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SasWorkbench.Core.Tickets;

/// <summary>
/// A ticket as a JSON object.
/// </summary>
/// <remarks>
/// <para>
/// The property names are the same names the text encoding uses, and they come from the same
/// table, so the correspondence between the two files is literal rather than remembered. Numbers
/// and booleans are written natively; dates and times are written as strings, and as null when
/// they are not set, because an empty string would claim a date was written when none was.
/// </para>
/// <para>
/// The validation number is written as a string. It is an identifier nobody does arithmetic on,
/// and a secure enhanced one is sixteen digits — past what a JSON reader backed by a double can
/// hold exactly, which would silently hand back a different ticket.
/// </para>
/// </remarks>
public static class TicketJsonCodec
{
    /// <summary>Renders a ticket as JSON.</summary>
    public static string Write(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();

            foreach (TicketField field in TicketFields.All)
            {
                string value = field.Format(ticket);
                switch (field.Kind)
                {
                    case TicketFieldKind.Number:
                        writer.WriteNumber(field.Name, long.Parse(value, CultureInfo.InvariantCulture));
                        break;

                    case TicketFieldKind.Boolean:
                        writer.WriteBoolean(field.Name, value == "true");
                        break;

                    case TicketFieldKind.Date:
                    case TicketFieldKind.Timestamp:
                        if (value.Length == 0)
                            writer.WriteNull(field.Name);
                        else
                            writer.WriteString(field.Name, value);

                        break;

                    default:
                        writer.WriteString(field.Name, value);
                        break;
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    /// <summary>Reads a ticket from JSON, or explains why the text is not one.</summary>
    /// <exception cref="TicketFormatException">The text is not JSON, or names something unknown.</exception>
    public static Ticket Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            throw new TicketFormatException($"this is not JSON: {ex.Message}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new TicketFormatException(
                    $"a ticket is a JSON object, and this is {document.RootElement.ValueKind.ToString().ToLowerInvariant()}.");

            var ticket = new Ticket();
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                TicketField? field = TicketFields.Find(property.Name);
                if (field is null)
                    throw new TicketFormatException($"a ticket has no field called '{property.Name}'.");

                string value = Lexical(field, property.Value);
                try
                {
                    ticket = field.Apply(ticket, value);
                }
                catch (TicketFormatException ex)
                {
                    throw new TicketFormatException($"'{field.Name}': {ex.Reason}");
                }
            }

            return ticket;
        }
    }

    // Every field is understood through the one lexical form the table defines, so JSON and the
    // text encoding cannot come to disagree about what a value means.
    private static string Lexical(TicketField field, JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => throw new TicketFormatException(
                $"'{field.Name}' is {element.ValueKind.ToString().ToLowerInvariant()}, which is not a value a ticket field can hold."),
        };
    }
}
