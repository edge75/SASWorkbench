using System.Text;

namespace SasWorkbench.Core.Tickets;

/// <summary>
/// A ticket as a key/value text file in the shape of a classic .ini.
/// </summary>
/// <remarks>
/// <para>
/// This is the encoding a person hand-authoring a negative case edits — expired, wrong machine,
/// already redeemed — which is the reason it exists at all. It is deliberately forgiving about
/// what it accepts and exact about what it writes: comments may start with # or ;, a [ticket]
/// section header is optional, keys are matched without regard to case, and a missing key simply
/// leaves that field at its default.
/// </para>
/// <para>
/// It is not forgiving about a key it does not recognise, or one given twice. Both are far more
/// likely to be a typo in a hand-written file than an intention, and silently ignoring either
/// would hand back a ticket that is not the one on the page.
/// </para>
/// <para>
/// A value is quoted only when it has to be — leading or trailing spaces, an embedded newline, or
/// a leading quote — so that an ordinary file stays plain to read while a value with awkward
/// whitespace still survives a round trip exactly.
/// </para>
/// </remarks>
public static class TicketKeyValueCodec
{
    /// <summary>Renders a ticket as text.</summary>
    public static string Write(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var text = new StringBuilder();
        text.Append("# A ticket. This file and its JSON form carry the same ticket.\n");
        text.Append("[ticket]\n");

        int width = 0;
        foreach (TicketField field in TicketFields.All)
            width = Math.Max(width, field.Name.Length);

        foreach (TicketField field in TicketFields.All)
        {
            string value = Quote(field.Format(ticket));
            text.Append(field.Name.PadRight(width));
            text.Append(value.Length == 0 ? " =" : " = ");
            text.Append(value);
            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Reads a ticket from text, or explains why the text is not one.</summary>
    /// <exception cref="TicketFormatException">A line, a key or a value is not usable.</exception>
    public static Ticket Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var ticket = new Ticket();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] lines = text.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            int number = i + 1;

            if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                continue;

            if (line[0] == '[')
                continue;

            int split = line.IndexOf('=');
            if (split < 0)
                throw new TicketFormatException(
                    $"line {number}: '{lines[i].Trim()}' is neither a comment nor a 'name = value' pair.");

            string name = line[..split].Trim();
            string value = line[(split + 1)..];

            TicketField? field = TicketFields.Find(name);
            if (field is null)
                throw new TicketFormatException(
                    $"line {number}: a ticket has no field called '{name}'.");

            if (!seen.Add(field.Name))
                throw new TicketFormatException(
                    $"line {number}: '{field.Name}' is given more than once, so which one is meant is unknowable.");

            try
            {
                ticket = field.Apply(ticket, Unquote(value, number));
            }
            catch (TicketFormatException ex)
            {
                throw new TicketFormatException($"line {number}: {ex.Reason}");
            }
        }

        return ticket;
    }

    private static string Quote(string value)
    {
        bool needed = value.Length > 0
                      && (char.IsWhiteSpace(value[0])
                          || char.IsWhiteSpace(value[^1])
                          || value[0] == '"'
                          || value.Contains('\n')
                          || value.Contains('\r'));

        if (!needed)
            return value;

        var text = new StringBuilder(value.Length + 8);
        text.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': text.Append("\\\\"); break;
                case '"': text.Append("\\\""); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                default: text.Append(c); break;
            }
        }

        text.Append('"');
        return text.ToString();
    }

    private static string Unquote(string raw, int line)
    {
        string value = raw.Trim();
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
            return value;

        var text = new StringBuilder(value.Length);
        for (int i = 1; i < value.Length - 1; i++)
        {
            if (value[i] != '\\')
            {
                text.Append(value[i]);
                continue;
            }

            i++;
            if (i >= value.Length - 1)
                throw new TicketFormatException($"line {line}: the value ends in a half-written escape.");

            text.Append(value[i] switch
            {
                '\\' => '\\',
                '"' => '"',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                _ => throw new TicketFormatException(
                    $"line {line}: '\\{value[i]}' is not an escape this build knows."),
            });
        }

        return text.ToString();
    }
}
