using System.Globalization;

namespace SasWorkbench.Core.Overlays;

/// <summary>
/// Reads the legacy tester's configuration file as one description of the protocol.
/// </summary>
/// <remarks>
/// <para>
/// The file is not a settings file: four of its sections are a message database, and the tool
/// that reads it has no per-command code at all. That makes it a description of SAS written by
/// somebody else, which is what a diff needs — a second opinion produced without sight of ours.
/// </para>
/// <para>
/// Its grammar is documented inside the file itself, in the comment blocks above the command and
/// response sections, and this reader follows those comments rather than inferring a grammar from
/// the data. Where a line does not fit, it is named in <see cref="ProtocolOverlay.Unreadable"/>
/// and no fact is invented for it.
/// </para>
/// </remarks>
public static class LegacyIniOverlayReader
{
    private const string CommandSection = "Long Poll Commands";
    private const string FundsSection = "Electronic Funds Transfer";
    private const string ExceptionSection = "General Poll";
    private const string ResponseSection = "Messages";
    private const string StructureSection = "Data Structures";

    /// <summary>The placeholder the file uses for an exception code it has no meaning for.</summary>
    private const string Placeholder = "Unknown exception!";

    /// <summary>Reads one configuration file.</summary>
    public static ProtocolOverlay Read(string path)
    {
        string[] lines = File.ReadAllLines(path);

        var commands = new Dictionary<byte, OverlayCommand>();
        var alternates = new Dictionary<byte, int>();
        var requests = new Dictionary<byte, List<OverlayField>>();
        var responses = new Dictionary<byte, List<OverlayField>>();
        var exceptions = new Dictionary<byte, OverlayException>();
        var placeholders = new List<byte>();
        var structures = new Dictionary<byte, int>();
        var unreadable = new List<string>();
        bool startOfStructure = false;

        string section = string.Empty;
        byte lastCommand = 0;
        bool haveLastCommand = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            int number = i + 1;

            if (line.Length == 0 || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                haveLastCommand = false;
                continue;
            }

            switch (section)
            {
                case CommandSection or FundsSection:
                    ReadCommandLine(line, number, path, section, commands, alternates, requests,
                        ref lastCommand, ref haveLastCommand, unreadable);
                    break;

                case ExceptionSection:
                    ReadExceptionLine(line, number, path, exceptions, placeholders, unreadable);
                    break;

                case ResponseSection:
                    ReadResponseLine(line, number, path, responses, unreadable);
                    break;

                case StructureSection:
                    ReadStructureLine(line, number, path, structures, ref startOfStructure, unreadable);
                    break;
            }
        }

        foreach ((byte code, List<OverlayField> fields) in requests)
        {
            if (commands.TryGetValue(code, out OverlayCommand? command))
            {
                var all = new List<OverlayField>(command.Request);
                all.AddRange(fields);
                commands[code] = command with { Request = all, RequestLength = Extent(all) };
            }
        }

        foreach ((byte code, List<OverlayField> fields) in responses)
        {
            // A reply decoder for a command the file cannot send is a finding, not an error: the
            // tool can read an answer it has no way to ask for. It is carried as a command with a
            // response and no request so that the diff can say exactly that.
            OverlayCommand command = commands.TryGetValue(code, out OverlayCommand? known)
                ? known
                : new OverlayCommand
                {
                    Code = code,
                    Name = fields[0].Name,
                    Origin = Path.GetFileName(path) + " [" + ResponseSection + "]",
                };

            commands[code] = command with { Response = fields, ResponseLength = ResponseExtent(fields) };
        }

        var notCarried = new List<string>
        {
            "the display format of a response field — decimal, ASCII or hexadecimal — which is a "
            + "rendering instruction rather than a statement about the bytes",
            "the extended funds-transfer type and encryption flags, which nothing in the file sets "
            + "and whose width on the wire this reader does not know",
            "the prompt text of an input box, which is that tool's user interface",
            "the permitted values listed under a command, which are counted as expressiveness "
            + "rather than carried as fields",
        };

        if (alternates.Count > 0)
        {
            notCarried.Add(alternates.Count + " command(s) are listed more than once, as alternative "
                           + "forms of one code; the first is carried and the rest are counted");
        }

        return new ProtocolOverlay
        {
            Source = OverlaySource.LegacyTester,
            Description = "the legacy tester's configuration file",
            Files = [path],
            Commands = commands,
            Exceptions = exceptions,
            PlaceholderExceptions = placeholders,
            SentAnotherWay = structures,
            Unreadable = unreadable,
            NotCarried = notCarried,
        };
    }

    /// <summary>
    /// The host message's own bytes, address through the last byte before the check sequence.
    /// Measured this way so that all three sources are measured the same way.
    /// </summary>
    private static int? Extent(List<OverlayField> fields)
    {
        if (fields.Any(f => f.Length is null or 0))
            return null;

        return 2 + fields.Sum(f => f.Length!.Value);
    }

    private static int? ResponseExtent(List<OverlayField> fields)
    {
        int end = 0;
        foreach (OverlayField field in fields)
        {
            if (field.Offset is not { } offset || field.Length is not { } length || length == 0)
                return null;

            end = Math.Max(end, offset + length - 1);
        }

        return end == 0 ? null : end;
    }

    private static void ReadCommandLine(
        string line, int number, string path, string section,
        Dictionary<byte, OverlayCommand> commands, Dictionary<byte, int> alternates,
        Dictionary<byte, List<OverlayField>> requests,
        ref byte lastCommand, ref bool haveLastCommand, List<string> unreadable)
    {
        char marker = line[0];
        if (marker is not ('$' or '?' or '*'))
        {
            unreadable.Add(Where(path, number) + "a line in [" + section
                           + "] that is neither a command, an input box nor a permitted value");
            return;
        }

        if (!TryCode(line, out byte code, out string rest))
        {
            unreadable.Add(Where(path, number) + Quote(line) + " does not begin with a two-digit code");
            return;
        }

        switch (marker)
        {
            case '$':
                lastCommand = code;
                haveLastCommand = true;

                if (commands.ContainsKey(code))
                {
                    alternates[code] = alternates.GetValueOrDefault(code) + 1;
                    return;
                }

                // TryCommand names its own reason, of which there are two, so nothing is added
                // here: a second message would say the wrong thing about one of them.
                if (!TryCommand(code, rest, section, path, unreadable, out OverlayCommand? command))
                    return;

                commands[code] = command!;
                return;

            case '?':
                if (!haveLastCommand || code != lastCommand)
                {
                    unreadable.Add(Where(path, number) + "an input box for $" + code.ToString("X2")
                                   + " that follows no definition of it");
                    return;
                }

                // An alternative form's boxes would be appended to the first form's and make one
                // longer command out of two. They are counted with the alternate instead.
                if (alternates.ContainsKey(code))
                    return;

                if (!TryInputBox(rest, out OverlayField? field))
                {
                    unreadable.Add(Where(path, number) + "an input box for $" + code.ToString("X2")
                                   + " with no byte count");
                    return;
                }

                requests.TryAdd(code, []);
                requests[code].Add(field!);
                return;

            default:
                // A permitted value of the box above it. The diff compares shapes and lengths, so
                // these are reported as expressiveness rather than as fields of their own.
                return;
        }
    }

    private static bool TryCommand(
        byte code, string rest, string section, string path, List<string> unreadable,
        out OverlayCommand? command)
    {
        command = null;

        int equals = rest.IndexOf('=');
        string[] parts = equals < 0 ? [] : rest[(equals + 1)..].Split(',');

        if (parts.Length < 8)
        {
            unreadable.Add("$" + code.ToString("X2") + " in [" + section + "] does not carry the "
                           + "seven flags its own grammar requires");
            return false;
        }

        // The seven flags are documented in the file's own comment block, in this order: length,
        // transaction number, acknowledgement, type, encryption, check sequence, input-box count.
        // The first three are bytes on the wire, not settings — a funds-transfer command carries a
        // transaction number and an acknowledgement flag ahead of its data — so leaving them out
        // would make every such command read two bytes shorter than the other sources have it.
        var fields = new List<OverlayField>();
        if (Yes(parts[1]))
            fields.Add(new OverlayField("length", OverlayFieldKind.Binary, 1, 3, "length flag"));
        if (Yes(parts[2]))
            fields.Add(new OverlayField("transaction number", OverlayFieldKind.Binary, 1,
                Native: "transaction flag"));
        if (Yes(parts[3]))
            fields.Add(new OverlayField("acknowledgement", OverlayFieldKind.Flag, 1,
                Native: "acknowledgement flag"));

        // The last two are documented as extended funds transfer only, and nothing in the file
        // sets either. Guessing a width for a flag no example uses would be inventing a length.
        if (Yes(parts[4]) || Yes(parts[5]))
        {
            unreadable.Add("$" + code.ToString("X2") + " sets the type or encryption flag, whose "
                           + "width on the wire this reader does not know; its length is not counted");
            return false;
        }

        command = new OverlayCommand
        {
            Code = code,
            Name = parts[0].Trim(),
            Request = fields,

            // Extent, not a special case for the empty list: a command whose flags add bytes and
            // whose definition has no input box after it still has a length this file fixes, and
            // leaving it null would report "not stated" about something the file states.
            RequestLength = Extent(fields),
            HasCheckSequence = Yes(parts[6]),
            Origin = Path.GetFileName(path) + " [" + section + "]",
        };

        return true;
    }

    private static bool TryInputBox(string rest, out OverlayField? field)
    {
        field = null;

        int at = rest.LastIndexOf('@');
        if (at < 0)
            return false;

        if (!int.TryParse(rest[(at + 1)..].Trim(), CultureInfo.InvariantCulture, out int bytes))
            return false;

        int equals = rest.IndexOf('=');
        string name = equals >= 0 ? rest[(equals + 1)..at].Trim() : rest[..at].Trim();

        // Zero means the tool asks for something it cannot size in advance — a data structure the
        // operator picks, or free text. That is a variable field, not an empty one.
        field = new OverlayField(
            name,
            bytes == 0 ? OverlayFieldKind.Variable : OverlayFieldKind.Binary,
            bytes == 0 ? null : bytes,
            Native: "input box");

        return true;
    }

    private static void ReadExceptionLine(
        string line, int number, string path,
        Dictionary<byte, OverlayException> exceptions, List<byte> placeholders, List<string> unreadable)
    {
        if (line[0] != '$' || !TryCode(line, out byte code, out string rest))
        {
            unreadable.Add(Where(path, number) + Quote(line) + " is not an exception definition");
            return;
        }

        int equals = rest.IndexOf('=');
        if (equals < 0)
        {
            unreadable.Add(Where(path, number) + "the entry for $" + code.ToString("X2")
                           + " has no meaning after it");
            return;
        }

        string name = rest[(equals + 1)..].Trim();

        if (string.Equals(name, Placeholder, StringComparison.Ordinal))
        {
            // Listed, and listed as not known. Neither named nor absent, and folding it into
            // either would misstate what that file says by dozens of codes.
            placeholders.Add(code);
            return;
        }

        exceptions[code] = new OverlayException
        {
            Code = code,
            Name = name.TrimEnd('.'),
            Origin = Path.GetFileName(path) + " [" + ExceptionSection + "]",
        };
    }

    private static void ReadResponseLine(
        string line, int number, string path,
        Dictionary<byte, List<OverlayField>> responses, List<string> unreadable)
    {
        char marker = line[0];
        if (marker is not ('$' or '&' or '*'))
        {
            unreadable.Add(Where(path, number) + "a line in [" + ResponseSection + "] with no marker");
            return;
        }

        if (!TryCode(line, out byte code, out string rest))
        {
            unreadable.Add(Where(path, number) + Quote(line) + " does not begin with a two-digit code");
            return;
        }

        // The grammar is  name = ,offset,%format+size  — the offset counts the address as byte one.
        int comma = rest.IndexOf(',');
        if (comma < 0)
        {
            unreadable.Add(Where(path, number) + "the response entry for $" + code.ToString("X2")
                           + " carries no offset");
            return;
        }

        string[] parts = rest[(comma + 1)..].Split(',');
        if (parts.Length < 2 || parts[1].Trim().Length < 3 || parts[1].Trim()[0] != '%')
        {
            unreadable.Add(Where(path, number) + "the response entry for $" + code.ToString("X2")
                           + " carries no display format and byte count");
            return;
        }

        if (!int.TryParse(parts[0].Trim(), CultureInfo.InvariantCulture, out int offset))
        {
            unreadable.Add(Where(path, number) + Quote(parts[0].Trim()) + " is not an offset");
            return;
        }

        string format = parts[1].Trim();
        if (!int.TryParse(format[2..].Trim(), CultureInfo.InvariantCulture, out int size))
        {
            unreadable.Add(Where(path, number) + Quote(format) + " is not a display format and byte count");
            return;
        }

        // The grammar puts the name between two equals signs, and the second is optional:
        //   $XX = name = ,offset,%format+size    and    &XX = name,offset,%format+size
        string name = rest[..comma].Trim().TrimStart('=').Trim().TrimEnd('=').Trim();

        OverlayFieldKind kind = char.ToLowerInvariant(format[1]) switch
        {
            'a' => OverlayFieldKind.Ascii,
            'x' => OverlayFieldKind.Raw,
            _ => OverlayFieldKind.Binary,
        };

        // Offset zero with size zero is how the file writes a field whose place depends on a
        // length byte earlier in the reply. That is variable, not absent.
        if (offset == 0 && size == 0)
            kind = OverlayFieldKind.Variable;

        responses.TryAdd(code, []);
        responses[code].Add(new OverlayField(
            name,
            kind,
            size == 0 ? null : size,
            offset == 0 ? null : offset,
            marker switch
            {
                '&' => "carries an overall length byte",
                '*' => "a length byte for the data that follows",
                _ => "fixed offset",
            }));
    }

    /// <summary>
    /// Reads the stored message structures, which are the tool's other way of sending a command.
    /// </summary>
    /// <remarks>
    /// Each block opens with BEGIN, names itself on the next line with the command it builds, and
    /// closes with END. A command reachable only this way is absent from both command lists and is
    /// still one the tool can send, so counting it as absent would overstate the disagreement.
    /// </remarks>
    private static void ReadStructureLine(
        string line, int number, string path,
        Dictionary<byte, int> structures, ref bool startOfStructure, List<string> unreadable)
    {
        if (string.Equals(line, "BEGIN", StringComparison.Ordinal))
        {
            startOfStructure = true;
            return;
        }

        if (string.Equals(line, "END", StringComparison.Ordinal))
        {
            startOfStructure = false;
            return;
        }

        if (!startOfStructure)
            return;

        startOfStructure = false;

        int equals = line.LastIndexOf('=');
        if (equals < 0
            || !byte.TryParse(line[(equals + 1)..].Trim(), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out byte code))
        {
            unreadable.Add(Where(path, number) + "a stored structure whose first line names no command");
            return;
        }

        structures[code] = structures.GetValueOrDefault(code) + 1;
    }

    private static bool TryCode(string line, out byte code, out string rest)
    {
        code = 0;
        rest = string.Empty;

        if (line.Length < 3)
            return false;

        if (!byte.TryParse(line.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
            return false;

        rest = line[3..];
        return true;
    }

    private static bool Yes(string flag)
    {
        string trimmed = flag.Trim();
        return trimmed.Length > 0 && char.ToLowerInvariant(trimmed[0]) == 'y';
    }

    private static string Where(string path, int number) => path + ":" + number + ": ";

    private static string Quote(string text) =>
        "'" + (text.Length <= 24 ? text : text[..24] + "...") + "'";
}
