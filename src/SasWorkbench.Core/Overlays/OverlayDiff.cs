namespace SasWorkbench.Core.Overlays;

/// <summary>Whether a source names a code, lists it without a meaning, or does not have it.</summary>
public enum OverlayPresence
{
    /// <summary>The source does not have the code at all.</summary>
    Absent,

    /// <summary>The source names it.</summary>
    Named,

    /// <summary>The source lists the code and says it has no meaning for it.</summary>
    Placeholder,
}

/// <summary>What kind of disagreement two sources have about one code.</summary>
public enum DisagreementKind
{
    /// <summary>One source has the code and another does not.</summary>
    Presence,

    /// <summary>Both have it and call it different things.</summary>
    Name,

    /// <summary>Both fix a length for what the host sends, and the two are not the same.</summary>
    RequestLength,

    /// <summary>Both fix a length for the reply, and the two are not the same.</summary>
    ReplyLength,

    /// <summary>One says the command is answered with data and another says it is acknowledged.</summary>
    ReplyShape,

    /// <summary>One says a message carries a check sequence and another says it does not.</summary>
    CheckSequence,

    /// <summary>One source describes the message with a construct another model cannot express.</summary>
    Expressiveness,

    /// <summary>Both name the same thing and word it differently.</summary>
    Wording,
}

/// <summary>One disagreement, with enough detail to act on it.</summary>
/// <param name="Kind">What sort of disagreement.</param>
/// <param name="Detail">What the sources say, in our words plus their figures.</param>
public sealed record Disagreement(DisagreementKind Kind, string Detail);

/// <summary>How the three sources compare on one long poll.</summary>
public sealed record CommandComparison
{
    public required byte Code { get; init; }

    /// <summary>What this build calls it, or null when this build does not have it.</summary>
    public string? Here { get; init; }

    public string? Legacy { get; init; }

    public string? Toolkit { get; init; }

    /// <summary>True when this build can decode the reply field by field.</summary>
    public bool HasLayout { get; init; }

    /// <summary>True when the legacy tester describes what comes back, not only the command.</summary>
    public bool LegacyDescribesReply { get; init; }

    /// <summary>True when the conformance toolkit describes what comes back.</summary>
    public bool ToolkitDescribesReply { get; init; }

    /// <summary>
    /// The library handler this command is dispatched to, or null when the dispatcher does not
    /// list it. Null is the difference between a layout that has not been written and one that
    /// cannot be established from anything here.
    /// </summary>
    public string? Handler { get; init; }

    public OverlayPresence InCatalogue { get; init; }

    public OverlayPresence InLegacy { get; init; }

    public OverlayPresence InToolkit { get; init; }

    public IReadOnlyList<Disagreement> Disagreements { get; init; } = [];

    /// <summary>True when all three sources have the code and nothing about it disagrees.</summary>
    public bool Agrees => Disagreements.Count == 0;

    /// <summary>How many sources have the code at all.</summary>
    public int Sources =>
        (InCatalogue == OverlayPresence.Named ? 1 : 0)
        + (InLegacy == OverlayPresence.Named ? 1 : 0)
        + (InToolkit == OverlayPresence.Named ? 1 : 0);
}

/// <summary>How the three sources compare on one exception.</summary>
public sealed record ExceptionComparison
{
    public required byte Code { get; init; }

    public string? Here { get; init; }

    public string? Legacy { get; init; }

    public string? Toolkit { get; init; }

    public OverlayPresence InCatalogue { get; init; }

    public OverlayPresence InLegacy { get; init; }

    public OverlayPresence InToolkit { get; init; }

    public IReadOnlyList<Disagreement> Disagreements { get; init; } = [];

    public bool Agrees => Disagreements.Count == 0;
}

/// <summary>One thing a model can or cannot say, and the evidence for the claim.</summary>
/// <param name="Trait">What is being asked of each model, in our words.</param>
/// <param name="InCatalogue">Whether this build's model can express it.</param>
/// <param name="InLegacy">Whether the legacy tester's can.</param>
/// <param name="InToolkit">Whether the conformance toolkit's can.</param>
/// <param name="Evidence">Where each answer was checked, so the row can be re-checked in one step.</param>
public sealed record ModelTrait(
    string Trait, bool InCatalogue, bool InLegacy, bool InToolkit, string Evidence);

/// <summary>The whole three-way comparison.</summary>
public sealed record OverlayDiffReport
{
    public required IReadOnlyList<CommandComparison> Commands { get; init; }

    public required IReadOnlyList<ExceptionComparison> Exceptions { get; init; }

    public required IReadOnlyList<ModelTrait> Traits { get; init; }

    public required IReadOnlyList<ProtocolOverlay> Sources { get; init; }
}

/// <summary>
/// Compares this build's catalogue against two descriptions of SAS written by other people.
/// </summary>
/// <remarks>
/// <para>
/// The comparison is the deliverable, not the import. Neither overlay is merged into anything:
/// no name, no description and no field of theirs reaches the catalogue, a layout or the record a
/// run writes. What they are for is disagreement — a command one list has and another does not, a
/// reply two of them give different lengths for, a construct one model can state and another
/// cannot. Each of those is a question to answer against the library or against bytes, and the
/// diff's job is to produce the questions rather than to answer them.
/// </para>
/// <para>
/// Where a source says nothing, the comparison says nothing. Silence is not agreement, and a
/// length only one source fixes is reported as fixed by one source rather than as a match.
/// </para>
/// </remarks>
public static class OverlayDiff
{
    /// <summary>Compares the catalogue against however many overlays are supplied.</summary>
    public static OverlayDiffReport Compare(
        ProtocolOverlay catalogue, ProtocolOverlay? legacy, ProtocolOverlay? toolkit)
    {
        var commands = new List<CommandComparison>();
        var exceptions = new List<ExceptionComparison>();

        IEnumerable<byte> commandCodes = catalogue.Commands.Keys
            .Concat(legacy?.Commands.Keys ?? [])
            .Concat(toolkit?.Commands.Keys ?? [])
            .Distinct()
            .Order();

        foreach (byte code in commandCodes)
            commands.Add(CompareCommand(code, catalogue, legacy, toolkit));

        IEnumerable<byte> exceptionCodes = catalogue.Exceptions.Keys
            .Concat(legacy?.Exceptions.Keys ?? [])
            .Concat(legacy?.PlaceholderExceptions ?? [])
            .Concat(toolkit?.Exceptions.Keys ?? [])
            .Distinct()
            .Order();

        foreach (byte code in exceptionCodes)
            exceptions.Add(CompareException(code, catalogue, legacy, toolkit));

        var sources = new List<ProtocolOverlay> { catalogue };
        if (legacy is not null) sources.Add(legacy);
        if (toolkit is not null) sources.Add(toolkit);

        return new OverlayDiffReport
        {
            Commands = commands,
            Exceptions = exceptions,
            Traits = Traits,
            Sources = sources,
        };
    }

    private static CommandComparison CompareCommand(
        byte code, ProtocolOverlay catalogue, ProtocolOverlay? legacy, ProtocolOverlay? toolkit)
    {
        catalogue.Commands.TryGetValue(code, out OverlayCommand? here);
        OverlayCommand? theirs = Get(legacy, code);
        OverlayCommand? tool = Get(toolkit, code);

        var found = new List<Disagreement>();

        Presence(found, "this build", here is not null, "the legacy tester", theirs is not null,
            legacy is not null);
        Presence(found, "this build", here is not null, "the conformance toolkit", tool is not null,
            toolkit is not null);
        if (legacy is not null && toolkit is not null)
            Presence(found, "the legacy tester", theirs is not null, "the conformance toolkit",
                tool is not null, true);

        if (legacy is not null && theirs?.Request.Count is null or 0
            && legacy.SentAnotherWay.TryGetValue(code, out int structures))
        {
            found.Add(new Disagreement(DisagreementKind.Presence,
                "neither of the legacy tester's command lists has it, and " + structures
                + " of its stored message structures build it, so that tool can send it without "
                + "listing it"));
        }

        Name(found, "this build", here?.Name, "the legacy tester", theirs?.Name);
        Name(found, "this build", here?.Name, "the conformance toolkit", tool?.Name);
        Name(found, "the legacy tester", theirs?.Name, "the conformance toolkit", tool?.Name);

        Length(found, DisagreementKind.RequestLength, "what the host sends",
            here?.RequestLength, theirs?.RequestLength, tool?.RequestLength);
        Length(found, DisagreementKind.ReplyLength, "the reply",
            here?.ResponseLength, theirs?.ResponseLength, tool?.ResponseLength);

        CheckSequence(found, here, theirs, tool);
        Shape(found, here, theirs, tool);
        Expressiveness(found, here, theirs, tool);

        return new CommandComparison
        {
            Code = code,
            Here = here?.Name,
            Legacy = theirs?.Name,
            Toolkit = tool?.Name,
            HasLayout = Protocol.SasLayouts.All.ContainsKey(code),
            LegacyDescribesReply = theirs?.Response.Count > 0,
            ToolkitDescribesReply = tool?.Response.Count > 0,
            Handler = Protocol.SasCatalogue.LongPolls.GetValueOrDefault(code)?.Handler,
            InCatalogue = here is null ? OverlayPresence.Absent : OverlayPresence.Named,
            InLegacy = legacy is null ? OverlayPresence.Absent
                : theirs is null ? OverlayPresence.Absent : OverlayPresence.Named,
            InToolkit = toolkit is null ? OverlayPresence.Absent
                : tool is null ? OverlayPresence.Absent : OverlayPresence.Named,
            Disagreements = found,
        };
    }

    private static ExceptionComparison CompareException(
        byte code, ProtocolOverlay catalogue, ProtocolOverlay? legacy, ProtocolOverlay? toolkit)
    {
        catalogue.Exceptions.TryGetValue(code, out OverlayException? here);

        OverlayException? theirs = null;
        OverlayPresence inLegacy = OverlayPresence.Absent;
        if (legacy is not null)
        {
            if (legacy.Exceptions.TryGetValue(code, out theirs))
                inLegacy = OverlayPresence.Named;
            else if (legacy.PlaceholderExceptions.Contains(code))
                inLegacy = OverlayPresence.Placeholder;
        }

        OverlayException? tool = null;
        toolkit?.Exceptions.TryGetValue(code, out tool);

        var found = new List<Disagreement>();

        // A placeholder is neither named nor absent, and it is the more interesting of the two:
        // the file has a slot for the code and says it has no meaning for it.
        if (legacy is not null && here is not null && inLegacy == OverlayPresence.Placeholder)
        {
            found.Add(new Disagreement(DisagreementKind.Presence,
                "this build names it; the legacy tester lists the code and says it has no meaning "
                + "for it"));
        }

        Presence(found, "this build", here is not null, "the legacy tester",
            inLegacy == OverlayPresence.Named, legacy is not null && inLegacy != OverlayPresence.Placeholder);

        // The toolkit describes only the events that carry data of their own, so its silence about
        // a code is not a claim that the code does not exist and is not counted as a disagreement.
        if (toolkit is not null && here is null && tool is not null)
        {
            found.Add(new Disagreement(DisagreementKind.Presence,
                "the conformance toolkit describes data for it; this build does not have the code"));
        }

        Name(found, "this build", here?.Name, "the legacy tester", theirs?.Name);
        Name(found, "this build", here?.Name, "the conformance toolkit", tool?.Name);

        if (tool is { Fields.Count: > 0 } && here is not null)
        {
            found.Add(new Disagreement(DisagreementKind.Expressiveness,
                "the conformance toolkit gives it " + tool.Fields.Count
                + " field(s) of accompanying data; this build carries the code and no layout for "
                + "what follows it"));
        }

        return new ExceptionComparison
        {
            Code = code,
            Here = here?.Name,
            Legacy = theirs?.Name,
            Toolkit = tool?.Name,
            InCatalogue = here is null ? OverlayPresence.Absent : OverlayPresence.Named,
            InLegacy = inLegacy,
            InToolkit = tool is null ? OverlayPresence.Absent : OverlayPresence.Named,
            Disagreements = found,
        };
    }

    private static OverlayCommand? Get(ProtocolOverlay? overlay, byte code) =>
        overlay is not null && overlay.Commands.TryGetValue(code, out OverlayCommand? command)
            ? command
            : null;

    private static void Presence(
        List<Disagreement> found, string first, bool inFirst, string second, bool inSecond,
        bool comparable)
    {
        if (!comparable || inFirst == inSecond)
            return;

        found.Add(new Disagreement(DisagreementKind.Presence,
            (inFirst ? first : second) + " has it; " + (inFirst ? second : first) + " does not"));
    }

    private static void Name(
        List<Disagreement> found, string first, string? inFirst, string second, string? inSecond)
    {
        if (inFirst is null || inSecond is null)
            return;

        NameAgreement agreement = Compare(inFirst, inSecond);
        if (agreement == NameAgreement.Same)
            return;

        found.Add(new Disagreement(
            agreement == NameAgreement.Close ? DisagreementKind.Wording : DisagreementKind.Name,
            first + ": '" + inFirst + "'; " + second + ": '" + inSecond + "'"));
    }

    private static void Length(
        List<Disagreement> found, DisagreementKind kind, string what,
        int? here, int? legacy, int? toolkit)
    {
        var stated = new List<(string Source, int Length)>();
        if (here is { } a) stated.Add(("this build", a));
        if (legacy is { } b) stated.Add(("the legacy tester", b));
        if (toolkit is { } c) stated.Add(("the conformance toolkit", c));

        if (stated.Count < 2 || stated.Select(s => s.Length).Distinct().Count() == 1)
            return;

        found.Add(new Disagreement(kind,
            what + " is " + string.Join("; ",
                stated.Select(s => s.Length + " bytes to " + s.Source))
            + " — counted from the address to the last byte before the check sequence"));
    }

    /// <summary>
    /// Whether the host's own message ends in a check sequence, which all three sources state and
    /// which decides whether a machine will accept the frame at all.
    /// </summary>
    private static void CheckSequence(
        List<Disagreement> found, OverlayCommand? here, OverlayCommand? legacy, OverlayCommand? toolkit)
    {
        var stated = new List<(string Source, bool Carries)>();
        if (here?.HasCheckSequence is { } a) stated.Add(("this build", a));
        if (legacy?.HasCheckSequence is { } b) stated.Add(("the legacy tester", b));
        if (toolkit?.HasCheckSequence is { } c) stated.Add(("the conformance toolkit", c));

        if (stated.Count < 2 || stated.Select(s => s.Carries).Distinct().Count() == 1)
            return;

        found.Add(new Disagreement(DisagreementKind.CheckSequence,
            "the host's own message ends in a check sequence to "
            + string.Join(" and ", stated.Where(s => s.Carries).Select(s => s.Source))
            + ", and does not to "
            + string.Join(" and ", stated.Where(s => !s.Carries).Select(s => s.Source))));
    }

    private static void Shape(
        List<Disagreement> found, OverlayCommand? here, OverlayCommand? legacy, OverlayCommand? toolkit)
    {
        var describes = new List<string>();
        var acknowledges = new List<string>();

        Note("this build", here);
        Note("the legacy tester", legacy);
        Note("the conformance toolkit", toolkit);

        if (describes.Count > 0 && acknowledges.Count > 0)
        {
            found.Add(new Disagreement(DisagreementKind.ReplyShape,
                string.Join(" and ", describes) + " describe(s) a reply carrying data; "
                + string.Join(" and ", acknowledges) + " has it answered with an acknowledgement"));
        }

        // The legacy tester can decode a reply it has no way to ask for. That is a property of
        // that tool rather than of the protocol, and it is worth saying out loud.
        if (legacy is { Response.Count: > 0, Request.Count: 0, RequestLength: null })
        {
            found.Add(new Disagreement(DisagreementKind.Presence,
                "the legacy tester can read this reply and has no entry in either of its command "
                + "lists for asking the question"));
        }

        void Note(string name, OverlayCommand? command)
        {
            if (command is null)
                return;

            if (command.AcknowledgementOnly is true)
                acknowledges.Add(name);
            else if (command.DescribesReply)
                describes.Add(name);
        }
    }

    private static void Expressiveness(
        List<Disagreement> found, OverlayCommand? here, OverlayCommand? legacy, OverlayCommand? toolkit)
    {
        // Only the toolkit has element types invented for one message, so this asks whether a
        // command needed one and, if so, whether this build has a general construct for it.
        OverlayField[] bespoke = (toolkit?.Request ?? [])
            .Concat(toolkit?.Response ?? [])
            .Where(f => f.Native is "variable_meter_parm" or "variable_meter_response"
                or "aft_receipt_parm" or "poll_2F_meter_parm")
            .ToArray();

        if (bespoke.Length > 0)
        {
            found.Add(new Disagreement(DisagreementKind.Expressiveness,
                "the conformance toolkit describes this with " + bespoke.Length
                + " element type(s) that exist only for it — "
                + string.Join(", ", bespoke.Select(f => f.Native).Distinct())
                + (here is not null && Protocol.SasLayouts.All.ContainsKey(here.Code)
                    ? "; this build carries it with the general constructs it already has"
                    : "; this build has no layout for it yet")));
        }

        if (toolkit is not null
            && toolkit.Request.Concat(toolkit.Response).Any(f => f.Native is "optional"))
        {
            found.Add(new Disagreement(DisagreementKind.Expressiveness,
                "the conformance toolkit marks part of this message optional and its own definition "
                + "carries no condition saying when it is there"));
        }

        if (legacy is not null && legacy.Response.Any(f => f.Kind == OverlayFieldKind.Variable))
        {
            found.Add(new Disagreement(DisagreementKind.Expressiveness,
                "the legacy tester places part of this reply by a length byte rather than at a "
                + "fixed offset, so it fixes no length for the whole"));
        }

        // The counterpart in the other direction: where this build says a field is there only
        // sometimes and another source describes a single fixed form, the two do not disagree
        // about a length — one of them cannot say "sometimes" at all.
        OverlayField[] conditional = (here?.Request ?? [])
            .Concat(here?.Response ?? [])
            .Where(f => f.Native?.StartsWith("present only when", StringComparison.Ordinal) is true)
            .ToArray();

        if (conditional.Length > 0
            && (legacy?.RequestLength is not null || legacy?.ResponseLength is not null
                || toolkit?.RequestLength is not null || toolkit?.ResponseLength is not null))
        {
            found.Add(new Disagreement(DisagreementKind.Expressiveness,
                "this build makes " + conditional.Length + " field(s) of this message present only "
                + "when an earlier field says so, and at least one other source describes a single "
                + "fixed form of it"));
        }
    }

    private enum NameAgreement
    {
        Same,
        Close,
        Different,
    }

    private static readonly string[] Ignored =
    [
        "the", "a", "an", "of", "for", "and", "to", "in", "on", "is", "amp", "sas", "gaming",
        "send", "set", "receive", "request", "current", "total",
    ];

    /// <summary>
    /// Compares two names for the same code, allowing for spelling and abbreviation.
    /// </summary>
    /// <remarks>
    /// Deliberately coarse in one direction. Words that carry no distinguishing weight in this
    /// vocabulary — "send", "total", "the" — are dropped before comparing, because two sources
    /// saying "send total coin in meter" and "total coin in" plainly mean the same command, and
    /// reporting that as a disagreement would bury the ones that matter. Anything short of a full
    /// match is still reported; the classification only decides how loudly.
    /// </remarks>
    private static NameAgreement Compare(string first, string second)
    {
        HashSet<string> left = Words(first);
        HashSet<string> right = Words(second);

        if (left.Count == 0 || right.Count == 0)
            return string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase)
                ? NameAgreement.Same
                : NameAgreement.Different;

        if (left.SetEquals(right))
            return NameAgreement.Same;

        // This build's own names come from the library's identifiers, which run their words
        // together — "Enablebillacceptor". Comparing the letters alone as well keeps a spacing
        // artefact of the generator from being reported as a disagreement about the protocol.
        string leftLetters = Letters(first);
        string rightLetters = Letters(second);

        if (leftLetters == rightLetters)
            return NameAgreement.Same;

        if (leftLetters.Length > 0 && rightLetters.Length > 0
            && (leftLetters.Contains(rightLetters, StringComparison.Ordinal)
                || rightLetters.Contains(leftLetters, StringComparison.Ordinal)))
            return NameAgreement.Close;

        int shared = left.Intersect(right).Count();
        int union = left.Union(right).Count();

        return shared * 2 >= union ? NameAgreement.Close : NameAgreement.Different;
    }

    private static string Letters(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static HashSet<string> Words(string name)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);

        foreach (string raw in new string(name.Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
                     .Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string word = raw.ToLowerInvariant();
            word = word switch
            {
                "id" or "ident" => "identification",
                "info" => "information",
                "num" or "no" => "number",
                "amt" => "amount",
                "egm" or "gm" => "machine",
                "prog" => "progressive",
                "promo" => "promotional",
                "meters" => "meter",
                "bills" => "bill",
                "credits" => "credit",
                "games" => "game",
                "cancelled" => "canceled",
                _ => word,
            };

            if (!Ignored.Contains(word))
                words.Add(word);
        }

        return words;
    }

    /// <summary>
    /// What each model can say, with the place each answer was checked.
    /// </summary>
    /// <remarks>
    /// Every row was read out of the source it describes: this build's from the field record, the
    /// toolkit's from its own document type definition, the legacy tester's from the grammar
    /// documented in the comment blocks of the file itself. None of it is inferred from what the
    /// data happens to contain, because a construct nobody used is still one the model has.
    /// </remarks>
    public static IReadOnlyList<ModelTrait> Traits { get; } =
    [
        new("which end of a multi-byte number comes first", true, false, false,
            "SasField.ByteOrder; neither other grammar has an attribute for it — the toolkit's "
            + "binary parameter has a display radix instead, and the legacy tester has a display "
            + "format letter"),
        new("what a number counts — credits, cents, whole currency units", true, false, false,
            "SasField.Unit; neither other model has anywhere to put it, so a credit meter and a "
            + "cent meter compare equal in both"),
        new("what a value is allowed to be", true, true, true,
            "SasField.Constraint; the legacy tester lists permitted values under a command; the "
            + "toolkit's choice entries do the same"),
        new("named values of an enumeration", true, true, true,
            "SasField.Constraint.OneOf; the legacy tester's value lines; the toolkit's choice_entry"),
        new("named bits inside a byte", true, false, true,
            "SasField.BitsOfField with a bit range; the toolkit's bitmap_field carries an offset "
            + "and a size; the legacy tester can only name whole bytes"),
        new("a field whose length an earlier field states", true, true, true,
            "SasField.LengthFromField; the legacy tester's length-byte marker; the toolkit's "
            + "length attribute"),
        new("a field whose width comes from a table keyed on an earlier value", true, false, false,
            "SasField.MeterWidthFromField, which long poll $2F needs; the toolkit added an element "
            + "type for that one command instead"),
        new("a group that repeats", true, false, false,
            "SasField.Repeat, general over any group; the toolkit has four element types that each "
            + "repeat one particular thing; the legacy tester enumerates the repetitions by hand"),
        new("a field present only when an earlier one says so", true, false, false,
            "SasField.PresentWhen; the toolkit's optional element takes a name and nothing else, so "
            + "it can say a group is optional and not when it is there"),
        new("which fields are meters a test may read by name", true, false, false,
            "SasField.IsMeter; both other models describe a display, which has no such notion"),
        new("whether the host's own message ends in a check sequence", true, true, true,
            "SasCommandFraming reads the rule out of the machine library's own handler, command "
            + "by command, and HostMessage.WriteLongPoll honours it; the legacy tester has a "
            + "check-sequence flag per command; the toolkit has a crc attribute per template"),
        new("a description of the message in prose", false, true, true,
            "deliberately not carried: what the other two hold is their words, and decision D4 "
            + "keeps them out of anything this product ships"),
    ];
}
