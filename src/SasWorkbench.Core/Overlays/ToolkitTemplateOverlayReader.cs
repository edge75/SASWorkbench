using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SasWorkbench.Core.Overlays;

/// <summary>
/// Reads the conformance toolkit's message templates as one description of the protocol.
/// </summary>
/// <remarks>
/// <para>
/// The toolkit describes several hundred messages with no per-message code, which makes it the
/// broadest second opinion available here. What it is not is an assertion model: it exists to
/// render a message on a screen, so it has no byte order, no units, no constraints and no
/// predicates, and where a message needed one of those its authors added an element type for that
/// one message instead. Both halves of that show up in the diff, and both are worth knowing before
/// anything is copied from it.
/// </para>
/// <para>
/// The vocabulary is taken from the toolkit's own document type definition rather than from the
/// element names that happen to occur, so an element the definition declares and this reader does
/// not map is reported as carried coarsely, and an element that occurs but is not declared is
/// reported as unreadable. Neither is dropped.
/// </para>
/// </remarks>
public static class ToolkitTemplateOverlayReader
{
    private const string PollFile = "PollTemplates.xml";
    private const string ResponseFile = "ResponseTemplates.xml";
    private const string EventFile = "EventTemplates.xml";
    private const string DefinitionFile = "SASTemplates.dtd";

    /// <summary>True when the directory holds the four files this reader needs.</summary>
    public static bool Holds(string directory) =>
        File.Exists(Path.Combine(directory, PollFile))
        && File.Exists(Path.Combine(directory, ResponseFile))
        && File.Exists(Path.Combine(directory, EventFile))
        && File.Exists(Path.Combine(directory, DefinitionFile));

    /// <summary>Reads one toolkit installation.</summary>
    public static ProtocolOverlay Read(string directory)
    {
        var unreadable = new List<string>();

        string definitionPath = Path.Combine(directory, DefinitionFile);
        if (!File.Exists(definitionPath))
            throw new FileNotFoundException("the toolkit's template definition is not at " + definitionPath);

        IReadOnlySet<string> declared = ReadDeclaredElements(definitionPath);

        Dictionary<byte, (string Name, List<OverlayField> Fields, bool Crc, bool Length)> polls =
            ReadTemplates(Path.Combine(directory, PollFile), declared, unreadable);
        Dictionary<byte, (string Name, List<OverlayField> Fields, bool Crc, bool Length)> replies =
            ReadTemplates(Path.Combine(directory, ResponseFile), declared, unreadable);
        Dictionary<byte, (string Name, List<OverlayField> Fields, bool Crc, bool Length)> events =
            ReadTemplates(Path.Combine(directory, EventFile), declared, unreadable);

        var commands = new Dictionary<byte, OverlayCommand>();

        foreach (byte code in polls.Keys.Concat(replies.Keys).Distinct().Order())
        {
            polls.TryGetValue(code, out var poll);
            replies.TryGetValue(code, out var reply);

            List<OverlayField> request = poll.Fields ?? [];
            List<OverlayField> response = reply.Fields ?? [];

            commands[code] = new OverlayCommand
            {
                Code = code,
                Name = poll.Name ?? reply.Name!,
                Request = request,
                Response = response,
                RequestLength = poll.Name is null ? null : Extent(request, poll.Length),
                ResponseLength = reply.Name is null ? null : Extent(response, reply.Length),

                // Deliberately never claimed. A poll template with no reply template says the
                // toolkit does not describe the answer; whether that answer is an acknowledgement
                // is a reading of ours, and putting it here would turn our inference into that
                // source's statement and then compare the two.
                AcknowledgementOnly = null,
                HasCheckSequence = poll.Name is null ? null : poll.Crc,
                Origin = poll.Name is not null ? PollFile : ResponseFile,
            };
        }

        var exceptions = new Dictionary<byte, OverlayException>();
        foreach ((byte code, var template) in events)
        {
            exceptions[code] = new OverlayException
            {
                Code = code,
                Name = template.Name,
                Fields = template.Fields,
                Origin = EventFile,
            };
        }

        return new ProtocolOverlay
        {
            Source = OverlaySource.ConformanceToolkit,
            Description = "the conformance toolkit's message templates",
            Files = new[] { PollFile, ResponseFile, EventFile, DefinitionFile }
                .Select(f => Path.Combine(directory, f)).ToList(),
            Commands = commands,
            Exceptions = exceptions,
            Unreadable = unreadable,
            NotCarried = NotCarried(declared),
        };
    }

    /// <summary>
    /// Bytes from the address through the last byte before the check sequence, or null when the
    /// template does not fix one.
    /// </summary>
    private static int? Extent(List<OverlayField> fields, bool lengthByte)
    {
        if (fields.Any(f => f.Length is null))
            return null;

        return 2 + (lengthByte ? 1 : 0) + fields.Sum(f => f.Length!.Value);
    }

    private static Dictionary<byte, (string Name, List<OverlayField> Fields, bool Crc, bool Length)>
        ReadTemplates(string path, IReadOnlySet<string> declared, List<string> unreadable)
    {
        var templates = new Dictionary<byte, (string, List<OverlayField>, bool, bool)>();

        if (!File.Exists(path))
        {
            unreadable.Add(path + ": the file is not there");
            return templates;
        }

        // The files name their own definition in a document type declaration. It is not fetched:
        // the vocabulary is read from that file directly, above, so that an element outside it is
        // a finding rather than a parse failure.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore };
        using XmlReader reader = XmlReader.Create(path, settings);
        XDocument document = XDocument.Load(reader, LoadOptions.SetLineInfo);

        foreach (XElement template in document.Descendants("SAS_template"))
        {
            string file = Path.GetFileName(path);
            int line = ((IXmlLineInfo)template).LineNumber;

            string? code = (string?)template.Attribute("cmd");
            string? name = (string?)template.Attribute("name");

            if (code is null || name is null)
            {
                unreadable.Add(file + ":" + line + ": a template with no code or no name");
                continue;
            }

            if (!byte.TryParse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte number))
            {
                unreadable.Add(file + ":" + line + ": '" + code + "' is not a one-byte code");
                continue;
            }

            if (templates.ContainsKey(number))
            {
                unreadable.Add(file + ":" + line + ": a second template for $" + number.ToString("X2")
                               + "; the first is carried and this one is not");
                continue;
            }

            var fields = new List<OverlayField>();
            foreach (XElement element in template.Elements())
                Collect(element, fields, file, declared, unreadable);

            templates[number] = (
                name,
                fields,
                string.Equals((string?)template.Attribute("crc"), "yes", StringComparison.Ordinal),
                string.Equals((string?)template.Attribute("length"), "yes", StringComparison.Ordinal));
        }

        return templates;
    }

    private static void Collect(
        XElement element, List<OverlayField> fields, string file,
        IReadOnlySet<string> declared, List<string> unreadable)
    {
        string kind = element.Name.LocalName;
        int line = ((IXmlLineInfo)element).LineNumber;

        if (!declared.Contains(kind))
        {
            unreadable.Add(file + ":" + line + ": '" + kind
                           + "' is not an element the toolkit's own definition declares");
            return;
        }

        string name = (string?)element.Attribute("name") ?? kind;
        int? size = int.TryParse((string?)element.Attribute("size"), CultureInfo.InvariantCulture,
            out int stated)
            ? stated
            : null;

        // A parameter that states its own length carries a length byte in front of it, and its
        // size attribute is then the largest it may be rather than the size it is.
        bool selfLength = string.Equals((string?)element.Attribute("length"), "yes", StringComparison.Ordinal);

        switch (kind)
        {
            case "bcd_parm":
                fields.Add(new OverlayField(name, OverlayFieldKind.Bcd, size, Native: kind));
                return;

            case "binary_parm":
                fields.Add(new OverlayField(name, OverlayFieldKind.Binary, size, Native: kind));
                return;

            case "hex_parm":
                fields.Add(new OverlayField(name, OverlayFieldKind.Raw, size, Native: kind));
                return;

            case "ascii_parm":
                fields.Add(new OverlayField(name, OverlayFieldKind.Ascii, size, Native: kind));
                return;

            case "bool_parm":
                fields.Add(new OverlayField(name, OverlayFieldKind.Flag, 1, Native: kind));
                return;

            case "padding":
                fields.Add(new OverlayField(name, OverlayFieldKind.Raw, size, Native: kind));
                return;

            case "choice_parm":
                fields.Add(new OverlayField(name, OverlayFieldKind.Choice, size, Native: kind));
                return;

            case "bitmap_parm":
                fields.Add(new OverlayField(name, OverlayFieldKind.Bitmap, size, Native: kind));
                return;

            case "variable_ascii_parm" or "variable_bcd_parm" or "variable_hex_parm":
                fields.Add(new OverlayField(name, OverlayFieldKind.Variable, null,
                    Native: selfLength ? kind + " with its own length byte" : kind));
                return;

            case "variable_meter_parm" or "variable_meter_response" or "aft_receipt_parm"
                or "poll_2F_meter_parm":
                fields.Add(new OverlayField(name, OverlayFieldKind.Repeating, null, Native: kind));
                return;

            case "optional" or "option_choice_parm":
                // The whole group is present or absent, and the definition carries no predicate
                // saying when. Its length is therefore not fixed even when every member's is.
                fields.Add(new OverlayField(name, OverlayFieldKind.Variable, null, Native: kind));
                return;

            case "choice_entry" or "option_choice_entry" or "bitmap_field":
                // Members of the element above them, already counted in that element's size.
                return;

            default:
                fields.Add(new OverlayField(name, OverlayFieldKind.Unclassified, size, Native: kind));
                return;
        }
    }

    /// <summary>The element names the toolkit's own definition declares.</summary>
    private static IReadOnlySet<string> ReadDeclaredElements(string path)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(File.ReadAllText(path), @"<!ELEMENT\s+([A-Za-z0-9_]+)"))
            declared.Add(match.Groups[1].Value);

        return declared;
    }

    private static List<string> NotCarried(IReadOnlySet<string> declared)
    {
        var notCarried = new List<string>
        {
            "the named values of a choice and the named bits of a bitmap, which are counted as "
            + "expressiveness rather than carried as fields of their own",
            "the radix a binary parameter is displayed in, which is a rendering instruction rather "
            + "than a statement about the bytes",
            "the multi-game flag of a template, which says that the poll takes a game number and "
            + "duplicates the parameter that already carries it",
        };

        // Named rather than assumed: if the toolkit's vocabulary grows, the diff says so instead
        // of quietly reducing a new element type to nothing.
        string[] mapped =
        [
            "SAS_templates", "SAS_template", "bcd_parm", "hex_parm", "binary_parm", "bool_parm",
            "ascii_parm", "variable_ascii_parm", "variable_bcd_parm", "variable_hex_parm",
            "variable_meter_parm", "variable_meter_response", "choice_parm", "choice_entry",
            "bitmap_parm", "bitmap_field", "padding", "optional", "aft_receipt_parm",
            "option_choice_parm", "option_choice_entry", "poll_2F_meter_parm",
        ];

        string[] unmapped = declared.Except(mapped, StringComparer.Ordinal).Order().ToArray();
        if (unmapped.Length > 0)
        {
            notCarried.Add("element type(s) the definition declares and this reader has no reading "
                           + "for: " + string.Join(", ", unmapped));
        }

        return notCarried;
    }
}
