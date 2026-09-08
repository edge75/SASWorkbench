using SasWorkbench.Core.Overlays;
using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Cli;

/// <summary>
/// Compares this build's catalogue against two descriptions of SAS written by other people.
/// </summary>
/// <remarks>
/// <para>
/// The comparison is the deliverable. Neither overlay is merged into anything this product ships:
/// their files are read from wherever the operator's copies are, the disagreements are printed,
/// and nothing is written. That is decision D4 applied to a diff — what those files contain
/// decides what gets modelled next, and every word shipped here is our own.
/// </para>
/// <para>
/// What the diff produces is questions, not answers. A command one list has and another does not,
/// a reply two of them give different lengths for, a construct one model can state and another
/// cannot: each is something to settle against the gaming machine library or against bytes.
/// </para>
/// </remarks>
internal static class OverlayDiffCommand
{
    public static int Execute(CommandLine line)
    {
        string? legacyPath = line.Value("legacy");
        string? toolkitPath = line.Value("toolkit");
        bool showAgreements = line.Flag("all");

        if (legacyPath is null && toolkitPath is null)
        {
            Console.Error.WriteLine(
                "error: which overlays? Expected at least one of --legacy <the legacy tester's "
                + "configuration file> and --toolkit <the conformance toolkit's directory>.");
            return 2;
        }

        ProtocolOverlay? legacy = null;
        if (legacyPath is not null)
        {
            if (!File.Exists(legacyPath))
            {
                Console.Error.WriteLine($"error: there is no file at {legacyPath}");
                return 2;
            }

            legacy = LegacyIniOverlayReader.Read(legacyPath);
        }

        ProtocolOverlay? toolkit = null;
        if (toolkitPath is not null)
        {
            if (!Directory.Exists(toolkitPath))
            {
                Console.Error.WriteLine($"error: there is no directory at {toolkitPath}");
                return 2;
            }

            if (!ToolkitTemplateOverlayReader.Holds(toolkitPath))
            {
                Console.Error.WriteLine(
                    $"error: {toolkitPath} does not hold the four template files this reads. "
                    + "Expected PollTemplates.xml, ResponseTemplates.xml, EventTemplates.xml and "
                    + "SASTemplates.dtd beside each other.");
                return 2;
            }

            toolkit = ToolkitTemplateOverlayReader.Read(toolkitPath);
        }

        ProtocolOverlay catalogue = CatalogueOverlay.Build();
        OverlayDiffReport report = OverlayDiff.Compare(catalogue, legacy, toolkit);

        Sources(report);
        Commands(report, showAgreements);
        Exceptions(report, showAgreements);
        Models(report);
        Worth(report);

        // Nothing was tested, and a diff that found disagreements is doing its job rather than
        // failing. Three is the code a run that asserted nothing already uses.
        return 3;
    }

    private static void Sources(OverlayDiffReport report)
    {
        Rule("WHAT WAS READ");

        foreach (ProtocolOverlay overlay in report.Sources)
        {
            Console.WriteLine($"  {Name(overlay.Source)}");
            Console.WriteLine($"    {overlay.Description}");
            foreach (string file in overlay.Files)
                Console.WriteLine($"    {file}");
            Console.WriteLine($"    {overlay.Commands.Count} long poll(s), "
                              + $"{overlay.Exceptions.Count} exception(s) named"
                              + (overlay.PlaceholderExceptions.Count > 0
                                  ? $", {overlay.PlaceholderExceptions.Count} exception code(s) "
                                    + "listed with no meaning"
                                  : string.Empty));

            // Two counts about the source on its own, printed because each is a property of that
            // description rather than a disagreement with another one, and both are what a reader
            // would otherwise have to work out from the code-by-code rows.
            OverlayCommand[] askable = overlay.Commands.Values
                .Where(c => c.Request.Count > 0 || c.RequestLength is not null).ToArray();

            int noReply = askable.Count(c => !c.DescribesReply && c.AcknowledgementOnly is not true);
            int unaskable = overlay.Commands.Values.Count(c => c.DescribesReply
                                                               && c.Request.Count == 0
                                                               && c.RequestLength is null);

            if (noReply > 0)
            {
                Console.WriteLine($"      {noReply} of them it can send and cannot read an answer "
                                  + "to, having no description of the reply");
            }

            if (unaskable > 0)
            {
                Console.WriteLine($"      {unaskable} of them it can read an answer to and cannot "
                                  + "send, having no entry in any command list");
            }

            if (overlay.SentAnotherWay.Count > 0)
            {
                Console.WriteLine($"      {overlay.SentAnotherWay.Count} command(s) it can build "
                                  + "from a stored structure instead of from a command list");
            }

            // Named, never dropped. A reader that skipped what it did not understand would turn a
            // hole in the reading into a disagreement about the protocol.
            if (overlay.Unreadable.Count > 0)
            {
                Console.WriteLine($"    {overlay.Unreadable.Count} thing(s) in those files could "
                                  + "not be read, and are not counted above:");
                foreach (string problem in overlay.Unreadable.Take(12))
                    Console.WriteLine($"      {problem}");
                if (overlay.Unreadable.Count > 12)
                    Console.WriteLine($"      and {overlay.Unreadable.Count - 12} more");
            }

            if (overlay.NotCarried.Count > 0)
            {
                Console.WriteLine("    read and deliberately not compared:");
                foreach (string what in overlay.NotCarried)
                    Console.WriteLine($"      {Wrap(what, 6)}");
            }

            Console.WriteLine();
        }
    }

    private static void Commands(OverlayDiffReport report, bool showAgreements)
    {
        Rule("LONG POLLS, CODE BY CODE");

        Console.WriteLine("  A row is  $code  here/legacy/toolkit  layout  — a dot means the source");
        Console.WriteLine("  does not have the code at all. Silence is not agreement: a length only");
        Console.WriteLine("  one source fixes is reported as fixed by one source.");
        Console.WriteLine();

        foreach (CommandComparison command in report.Commands)
        {
            Disagreement[] shown = Shown(command.Disagreements, showAgreements);
            if (shown.Length == 0)
                continue;

            Console.WriteLine($"  ${command.Code:X2}  {Mark(command.InCatalogue)}"
                              + $"{Mark(command.InLegacy)}{Mark(command.InToolkit)}  "
                              + (command.HasLayout ? "decoded" : "no layout"));

            if (command.Here is not null)
                Console.WriteLine($"        here     {command.Here}");
            if (command.Legacy is not null)
                Console.WriteLine($"        legacy   {command.Legacy}");
            if (command.Toolkit is not null)
                Console.WriteLine($"        toolkit  {command.Toolkit}");

            foreach (Disagreement disagreement in shown)
                Console.WriteLine($"        {Kind(disagreement.Kind),-14} {Wrap(disagreement.Detail, 23)}");

            Console.WriteLine();
        }

        Summary(report.Commands.Count,
            report.Commands.Count(c => c.Agrees),
            report.Commands.SelectMany(c => c.Disagreements).ToList(),
            "long poll");
    }

    private static void Exceptions(OverlayDiffReport report, bool showAgreements)
    {
        Rule("EXCEPTIONS, CODE BY CODE");

        Console.WriteLine("  The middle mark is  ?  where the legacy tester lists the code and says");
        Console.WriteLine("  it has no meaning for it, which is neither naming it nor omitting it.");
        Console.WriteLine("  The conformance toolkit describes only the events that carry data of");
        Console.WriteLine("  their own, so its silence is not a claim that a code does not exist.");
        Console.WriteLine();

        foreach (ExceptionComparison exception in report.Exceptions)
        {
            Disagreement[] shown = Shown(exception.Disagreements, showAgreements);
            if (shown.Length == 0)
                continue;

            Console.WriteLine($"  ${exception.Code:X2}  {Mark(exception.InCatalogue)}"
                              + $"{Mark(exception.InLegacy)}{Mark(exception.InToolkit)}");

            if (exception.Here is not null)
                Console.WriteLine($"        here     {exception.Here}");
            if (exception.Legacy is not null)
                Console.WriteLine($"        legacy   {exception.Legacy}");
            if (exception.Toolkit is not null)
                Console.WriteLine($"        toolkit  {exception.Toolkit}");

            foreach (Disagreement disagreement in shown)
                Console.WriteLine($"        {Kind(disagreement.Kind),-14} {Wrap(disagreement.Detail, 23)}");

            Console.WriteLine();
        }

        Summary(report.Exceptions.Count,
            report.Exceptions.Count(e => e.Agrees),
            report.Exceptions.SelectMany(e => e.Disagreements).ToList(),
            "exception");
    }

    /// <summary>
    /// Which disagreements to print. A wording difference is a real difference and is counted in
    /// the summary either way, but printing all of them by default buries the rest: two thirds of
    /// them are this build's own names running their words together, which is an artefact of the
    /// generator rather than anything about the protocol.
    /// </summary>
    private static Disagreement[] Shown(IReadOnlyList<Disagreement> found, bool all) =>
        all ? [.. found] : [.. found.Where(d => d.Kind != DisagreementKind.Wording)];

    private static void Summary(int total, int agreeing, List<Disagreement> found, string what)
    {
        Console.WriteLine($"  {total} {what} code(s) across the sources; {agreeing} with nothing "
                          + $"to report, {total - agreeing} with something.");

        foreach (DisagreementKind kind in Enum.GetValues<DisagreementKind>())
        {
            int count = found.Count(d => d.Kind == kind);
            if (count > 0)
                Console.WriteLine($"    {Kind(kind),-14} {count}"
                                  + (kind == DisagreementKind.Wording ? "   (shown with --all)" : string.Empty));
        }

        Console.WriteLine();
    }

    private static void Models(OverlayDiffReport report)
    {
        Rule("WHAT EACH MODEL CAN SAY");

        Console.WriteLine("  Read out of each model itself rather than out of the data, because a");
        Console.WriteLine("  construct nobody happened to use is still one the model has.");
        Console.WriteLine();
        Console.WriteLine($"  {"here",-6}{"legacy",-8}{"toolkit",-9}");

        foreach (ModelTrait trait in report.Traits)
        {
            Console.WriteLine($"  {Yes(trait.InCatalogue),-6}{Yes(trait.InLegacy),-8}"
                              + $"{Yes(trait.InToolkit),-9}{trait.Trait}");
            Console.WriteLine($"        {Wrap(trait.Evidence, 8)}");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// What the diff is worth, counted rather than claimed: how many commands it puts on the
    /// backlog that no traffic here has ever carried, and which of those a layout can be
    /// established for at all.
    /// </summary>
    private static void Worth(OverlayDiffReport report)
    {
        Rule("WHAT THE DIFF IS WORTH");

        CommandComparison[] undecoded = report.Commands
            .Where(c => c.InCatalogue == OverlayPresence.Named && !c.HasLayout)
            .ToArray();

        CommandComparison[] described = undecoded
            .Where(c => c.InLegacy == OverlayPresence.Named || c.InToolkit == OverlayPresence.Named)
            .ToArray();

        CommandComparison[] replyDescribed = described
            .Where(c => c.LegacyDescribesReply || c.ToolkitDescribesReply)
            .ToArray();

        CommandComparison[] establishable = described.Where(c => c.Handler is not null).ToArray();
        CommandComparison[] notEstablishable = described.Where(c => c.Handler is null).ToArray();

        CommandComparison[] elsewhereOnly = report.Commands
            .Where(c => c.InCatalogue == OverlayPresence.Absent)
            .ToArray();

        CommandComparison[] noHandler = report.Commands
            .Where(c => c.InCatalogue == OverlayPresence.Named && c.Handler is null)
            .ToArray();

        Console.WriteLine($"  {SasLayouts.All.Count} of {SasCatalogue.LongPolls.Count} replies in "
                          + "this build's catalogue decode field by field, so "
                          + $"{undecoded.Length} do not.");
        Console.WriteLine();

        Console.WriteLine($"  {described.Length} of those {undecoded.Length} are described by at "
                          + "least one of the two overlays. That is what the");
        Console.WriteLine("  diff puts on the backlog, produced from two descriptions of the "
                          + "protocol rather than");
        Console.WriteLine("  from traffic. Run 'sasctl explain' over a capture to see which of "
                          + "these, if any, a");
        Console.WriteLine("  recording carries.");
        Console.WriteLine($"    {Codes(described)}");
        Console.WriteLine();

        int bothName = described.Count(c => c.Legacy is not null && c.Toolkit is not null);
        Console.WriteLine($"    of those, {described.Count(c => c.Legacy is not null)} are named by "
                          + $"the legacy tester and {described.Count(c => c.Toolkit is not null)} by "
                          + $"the conformance toolkit; {bothName} by both.");
        Console.WriteLine();

        Console.WriteLine($"  {replyDescribed.Length} of them have a reply described, field by "
                          + "field, by at least one overlay. Those are the");
        Console.WriteLine("  ones where a second opinion exists to check a layout against once it "
                          + "is written:");
        Console.WriteLine($"    {Codes(replyDescribed)}");
        Console.WriteLine();

        Console.WriteLine($"  {establishable.Length} of those can have a layout read out of the "
                          + "gaming machine library's own handler,");
        Console.WriteLine("  which is where every layout here has come from:");
        Console.WriteLine($"    {Codes(establishable)}");
        Console.WriteLine();

        if (notEstablishable.Length > 0)
        {
            Console.WriteLine($"  {notEstablishable.Length} cannot: this build's catalogue has the "
                              + "code because the library's enumeration does, but");
            Console.WriteLine("  the library's dispatcher does not send it anywhere, so there is no "
                              + "handler to read a");
            Console.WriteLine("  layout out of. A description in somebody else's display model is "
                              + "not a layout.");
            Console.WriteLine($"    {Codes(notEstablishable)}");
            Console.WriteLine();
        }

        if (elsewhereOnly.Length > 0)
        {
            Console.WriteLine($"  {elsewhereOnly.Length} command(s) appear in an overlay and in no "
                              + "part of this build. The library's");
            Console.WriteLine("  dispatcher does not list them, so there is no handler to read a "
                              + "layout out of, and a");
            Console.WriteLine("  description in somebody else's display model is not one. Each is a "
                              + "finding to record");
            Console.WriteLine("  honestly rather than a layout to write:");
            Console.WriteLine($"    {Codes(elsewhereOnly)}");
            Console.WriteLine();

            CommandComparison[] replyOnly = elsewhereOnly
                .Where(c => c.Toolkit is null
                            && c.Disagreements.Any(d => d.Detail.Contains(
                                "can read this reply", StringComparison.Ordinal)))
                .ToArray();

            if (replyOnly.Length > 0)
            {
                Console.WriteLine($"  {replyOnly.Length} of those the legacy tester holds only as a "
                                  + "reply it can read and cannot");
                Console.WriteLine("  ask for. Nothing here and nothing in the toolkit has the code "
                                  + "at all:");
                Console.WriteLine($"    {Codes(replyOnly)}");
                Console.WriteLine();
            }
        }

        if (noHandler.Length > 0)
        {
            Console.WriteLine($"  {noHandler.Length} command(s) this build names have no handler in "
                              + "the library's dispatcher at all,");
            Console.WriteLine("  whether an overlay describes them or not:");
            Console.WriteLine($"    {Codes(noHandler)}");
            Console.WriteLine();
        }

        ExceptionComparison[] withData = report.Exceptions
            .Where(e => e.InCatalogue == OverlayPresence.Named
                        && e.Disagreements.Any(d => d.Kind == DisagreementKind.Expressiveness))
            .ToArray();

        if (withData.Length > 0)
        {
            Console.WriteLine($"  A separate backlog: {withData.Length} exception(s) this build "
                              + "names carry data of their own, and");
            Console.WriteLine("  the conformance toolkit describes what follows the code. This build "
                              + "decodes none of");
            Console.WriteLine("  it, which is the real-time event format left undecoded so far:");
            Console.WriteLine($"    {string.Join(' ', withData.Select(e => $"${e.Code:X2}"))}");
            Console.WriteLine();
        }

        Console.WriteLine("  Nothing above is written anywhere. The overlays are read where they");
        Console.WriteLine("  sit, compared, and dropped; no name, description or field of theirs");
        Console.WriteLine("  reaches the catalogue, a layout, or the record a run writes.");
        Console.WriteLine();
    }

    private static string Codes(IEnumerable<CommandComparison> commands)
    {
        string[] codes = commands.Select(c => $"${c.Code:X2}").ToArray();
        return codes.Length == 0 ? "(none)" : Wrap(string.Join(' ', codes), 4);
    }

    private static string Name(OverlaySource source) => source switch
    {
        OverlaySource.Catalogue => "this build",
        OverlaySource.LegacyTester => "the legacy tester",
        _ => "the conformance toolkit",
    };

    private static string Kind(DisagreementKind kind) => kind switch
    {
        DisagreementKind.Presence => "presence",
        DisagreementKind.Name => "name",
        DisagreementKind.Wording => "wording",
        DisagreementKind.RequestLength => "host length",
        DisagreementKind.ReplyLength => "reply length",
        DisagreementKind.ReplyShape => "reply shape",
        DisagreementKind.CheckSequence => "check seq",
        _ => "expressiveness",
    };

    private static char Mark(OverlayPresence presence) => presence switch
    {
        OverlayPresence.Named => 'X',
        OverlayPresence.Placeholder => '?',
        _ => '.',
    };

    private static string Yes(bool value) => value ? "yes" : "no";

    private static void Rule(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 78));
        Console.WriteLine(title);
        Console.WriteLine(new string('=', 78));
        Console.WriteLine();
    }

    /// <summary>Folds a long line so a terminal shows all of it rather than the first screenful.</summary>
    private static string Wrap(string text, int indent)
    {
        const int width = 78;
        var lines = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (string word in text.Split(' '))
        {
            if (current.Length > 0 && indent + current.Length + 1 + word.Length > width)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
                current.Append(' ');

            current.Append(word);
        }

        if (current.Length > 0)
            lines.Add(current.ToString());

        return string.Join(Environment.NewLine + new string(' ', indent), lines);
    }
}
