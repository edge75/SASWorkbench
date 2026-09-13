using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Cli;

/// <summary>Lists the protocol this build knows, and — separately — the part it can decode.</summary>
internal static class CatalogueCommand
{
    public static int Execute(CommandLine line)
    {
        string what = line.Arguments.Count > 0 ? line.Arguments[0].ToLowerInvariant() : "summary";

        switch (what)
        {
            case "summary":
                Summary();
                return 0;

            case "long-polls" or "longpolls" or "lp":
                foreach (SasLongPollInfo info in SasCatalogue.LongPolls.Values.OrderBy(i => i.Code))
                {
                    string decodable = SasLayouts.All.ContainsKey(info.Code) ? "decoded" : "-";
                    Console.WriteLine($"  ${info.Code:X2}  {decodable,-8}  {info.Symbol,-32}  {info.Description}");
                }

                return 0;

            case "exceptions":
                foreach (SasExceptionInfo info in SasCatalogue.Exceptions.Values.OrderBy(i => i.Code))
                    Console.WriteLine($"  ${info.Code:X2}  {SasNames.ExceptionName(info.Code),-40}  {info.Description}");
                return 0;

            case "meters":
                Meters();
                return 0;

            case "layouts":
                Layouts();
                return 0;

            case "events":
                Events();
                return 0;

            case "diff":
                return OverlayDiffCommand.Execute(line);

            case "readable-meters":
                ReadableMeters();
                return 0;

            case "channels":
                Channels();
                return 0;

            default:
                Console.Error.WriteLine(
                    $"error: '{what}' is not something to list. Expected long-polls, exceptions, "
                    + "events, meters, layouts, readable-meters, channels or diff.");
                return 2;
        }
    }

    /// <summary>
    /// The seven categories a channel can own, and what each of them decides.
    /// </summary>
    /// <remarks>
    /// Three tables, all read out of the gaming machine library rather than written here: the
    /// categories themselves are its channel-control structure's one-bit fields; a command's
    /// category is the gate at the top of its handler; an exception's is one of five predicates.
    /// A command or an exception in no category is answered, or heard, on every channel — which is
    /// a fact about the library and the reason the last section of this listing exists.
    /// </remarks>
    private static void Channels()
    {
        Console.WriteLine("Channel categories, read from the gaming machine library");
        Console.WriteLine();

        foreach (SasCategory category in SasChannelPlan.Categories)
        {
            byte[] commands =
                [.. SasCatalogue.CommandCategories
                    .Where(pair => pair.Value == category).Select(pair => pair.Key).Order()];
            byte[] exceptions =
                [.. SasCatalogue.ExceptionCategories
                    .Where(pair => pair.Value == category).Select(pair => pair.Key).Order()];

            Console.WriteLine($"  {SasCategories.NameOf(category)}");
            Console.WriteLine($"    long polls   {commands.Length,3}  "
                              + string.Join(' ', commands.Select(c => $"${c:X2}")));
            Console.WriteLine($"    exceptions   {exceptions.Length,3}  "
                              + string.Join(' ', exceptions.Select(c => $"${c:X2}")));

            if (SasCatalogue.ChannelFeatureMasks.TryGetValue(
                    category, out IReadOnlyList<SasFeatureMask>? masks))
            {
                Console.WriteLine("    a channel without it reports "
                                  + string.Join(", ", masks.Select(m => m.ToString())));
            }

            Console.WriteLine();
        }

        int gated = SasCatalogue.CommandCategories.Count;
        int filtered = SasCatalogue.ExceptionCategories.Count;
        Console.WriteLine("COUNTED");
        Console.WriteLine($"  long polls gated on a category      {gated,4} of "
                          + $"{SasCatalogue.LongPolls.Count}");
        Console.WriteLine($"  exceptions filtered by category     {filtered,4} of "
                          + $"{SasCatalogue.Exceptions.Count}");
        Console.WriteLine();
        Console.WriteLine("  Everything else is answered, and heard, on every open channel. That is");
        Console.WriteLine("  not a gap in this table: the cashout ticket information poll is one of");
        Console.WriteLine("  them, so a host on a channel that does not own validation is answered");
        Console.WriteLine("  with the ticket's real validation number and amount.");
        Console.WriteLine();
        Console.WriteLine("  A category pointed at a channel with no poll address reaches no host at");
        Console.WriteLine("  all, and is refused rather than served. The machine's own menu cannot");
        Console.WriteLine("  produce one - it skips a channel with no address while cycling - but a");
        Console.WriteLine("  configuration written any other way can.");
    }

    /// <summary>
    /// Every meter the library keeps, and whether a script can ask for it by name.
    /// </summary>
    /// <remarks>
    /// The readable column is answered by meter code rather than by comparing names, which is what
    /// it used to do. Only five of the sixteen names then offered are spelled the way the library
    /// spells its meter, so eleven readable meters were printed as unreadable.
    /// </remarks>
    private static void Meters()
    {
        var readable = MeterIndex.All
            .Where(m => m.Meter is not null)
            .ToLookup(m => (ushort)m.Meter!.Value);

        foreach (SasMeterInfo info in SasCatalogue.Meters.Values.OrderBy(i => i.Code))
        {
            string mark = readable.Contains(info.Code) ? "readable" : "-";
            Console.WriteLine($"  ${info.Code:X4}  {mark,-9}  {info.Symbol,-46}  {info.Description}");
        }
    }

    /// <summary>
    /// Every name a script may read, the command it is read from, and the commands it is not.
    /// </summary>
    /// <remarks>
    /// The second line of an entry is the part worth printing. Where two commands report one
    /// meter they need not answer the same number, so which one a name is attached to is a
    /// decision, and a decision nobody can see is one nobody can check.
    /// </remarks>
    private static void ReadableMeters()
    {
        foreach (MeterSource source in MeterIndex.All.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            string meter = source.Meter is null
                ? "computed from 24 meters"
                : $"meter ${(ushort)source.Meter.Value:X4}";
            string scaled = source.ScaledByAccountingDenomination
                ? "  scaled by the accounting denomination"
                : string.Empty;
            Console.WriteLine(
                $"  {source.Name,-30}  lp ${source.Command:X2}  {source.Unit,-8}  {meter}{scaled}");

            IReadOnlyList<MeterSource> others = MeterIndex.SourcesFor(source.Name).Skip(1).ToList();
            if (others.Count > 0)
            {
                Console.WriteLine("                                  also reported by "
                                  + string.Join(", ", others.Select(o =>
                                      $"lp ${o.Command:X2}"
                                      + (o.ScaledByAccountingDenomination ? " (scaled)" : string.Empty))));
            }
        }

        int scaledCount = MeterIndex.All.Count(m => m.ScaledByAccountingDenomination);
        Console.WriteLine();
        Console.WriteLine($"  {MeterIndex.All.Count} name(s), {scaledCount} of which are read from a "
                          + "command the library scales.");
        Console.WriteLine("  A scaled reading is divided by the accounting denomination when the");
        Console.WriteLine("  machine is in either of its two default meter modes, so it can differ");
        Console.WriteLine("  from what another command reports for the same meter.");
    }

    private static void Summary()
    {
        (int longPolls, int exceptions, int meters) = SasCatalogue.Counts;

        Console.WriteLine("Catalogue, generated from the gaming machine library's own headers");
        Console.WriteLine($"  long polls   {longPolls}");
        Console.WriteLine($"  exceptions   {exceptions}");
        Console.WriteLine($"  meters       {meters}");
        Console.WriteLine();

        // The gap between the two is the honest part. Knowing a command exists is not the same as
        // knowing what its reply means, and a runner has to be able to tell a test the difference.
        Console.WriteLine("Of those, what this build can decode field by field");
        Console.WriteLine($"  replies      {SasLayouts.All.Count} of {longPolls}");
        Console.WriteLine($"  events       {SasEventLayouts.All.Count} exception payloads, and "
                          + $"{SasEventLayouts.NeverQueued.Count} the library names and never "
                          + "produces");
        Console.WriteLine($"  meters       {MeterIndex.All.Count} readable by name");
        Console.WriteLine();
        Console.WriteLine("A command in the catalogue with no layout is reported as undecoded, so a");
        Console.WriteLine("test that needs it is skipped with that reason rather than guessed at.");
        Console.WriteLine();
        Console.WriteLine("Try 'sasctl catalogue layouts' for the replies, field by field, and");
        Console.WriteLine("'sasctl catalogue events' for what an exception carries after its code.");
    }

    private static void Layouts()
    {
        foreach (SasMessageLayout layout in SasLayouts.All.Values.OrderBy(l => l.Command))
        {
            string confirmed = layout.ConfirmedByCapture ? "  [confirmed against a capture]" : string.Empty;
            Console.WriteLine($"${layout.Command:X2}  {layout.Name}{confirmed}");
            Console.WriteLine($"     {layout.Description}");
            Console.WriteLine($"     read from {layout.DerivedFrom}");

            if (layout.Request.Count > 0)
            {
                Console.WriteLine("     host sends:");
                foreach (SasField field in layout.Request)
                    Console.WriteLine($"       {Describe(field)}");
            }

            switch (layout.Shape)
            {
                case SasResponseShape.Acknowledgement:
                    Console.WriteLine("     machine answers: its address, or the address with bit 7 to refuse");
                    break;

                case SasResponseShape.None:
                    Console.WriteLine("     machine answers: nothing");
                    break;

                default:
                    Console.WriteLine("     machine answers:");
                    foreach (SasField field in layout.Response)
                        Console.WriteLine($"       {Describe(field)}");
                    break;
            }

            Console.WriteLine();
        }
    }

    /// <summary>
    /// What an exception carries after its code, when the machine is reporting in real time.
    /// </summary>
    /// <remarks>
    /// Whether the bytes have been seen on a link is printed beside every payload, because it is
    /// the difference between a layout read out of the library and one a machine has confirmed —
    /// and for events that difference is larger than it is for replies: a reply can be asked for,
    /// an event can only be waited for.
    /// </remarks>
    private static void Events()
    {
        Console.WriteLine(
            $"{SasEventLayouts.All.Count} exceptions have a payload the machine library builds, "
            + "and this build reads all of them.");
        Console.WriteLine(
            "Every other exception is the whole event in three bytes: the address, the marker "
            + "and the code.");
        Console.WriteLine();

        foreach (SasEventLayout layout in SasEventLayouts.All.Values.OrderBy(e => e.Code))
        {
            string confirmed = layout.ConfirmedByCapture
                ? "  [confirmed against a capture]"
                : "  [read from source; no run here has produced one]";
            Console.WriteLine($"${layout.Code:X2}  {layout.Name}{confirmed}");
            Console.WriteLine($"     {layout.Description}");
            Console.WriteLine($"     read from {layout.DerivedFrom}");
            Console.WriteLine($"     machine sends, after the code:");
            foreach (SasField field in layout.Payload)
                Console.WriteLine($"       {Describe(field)}");

            Console.WriteLine();
        }

        Console.WriteLine("Exceptions the library will only send in the event format:");
        Console.WriteLine("  " + string.Join(' ',
            SasEventLayouts.RealTimeOnly.Order().Select(c => $"${c:X2}")));
        Console.WriteLine();
        foreach ((byte code, string why) in SasEventLayouts.NeverQueued.OrderBy(e => e.Key))
            Console.WriteLine($"  ${code:X2}  {why}");
    }

    private static string Describe(SasField field)
    {
        string length = field.Length == SasField.Remaining ? "rest" : field.Length.ToString();
        var parts = new List<string> { field.Encoding.ToString().ToLowerInvariant() };

        if (field.Length > 1 && field.ByteOrder != SasByteOrder.NotApplicable)
            parts.Add(field.ByteOrder == SasByteOrder.LittleEndian ? "low byte first" : "high byte first");
        if (field.Unit != SasUnit.None)
            parts.Add(field.Unit.ToString().ToLowerInvariant());
        if (field.Constraint is { } constraint)
            parts.Add(Constrain(constraint));
        if (field.PresentWhen is { } condition)
            parts.Add($"only when {condition}");

        return $"{field.Name,-28} {length,4}  {string.Join(", ", parts)}";
    }

    private static string Constrain(SasConstraint constraint)
    {
        if (constraint.OneOf is { Count: > 0 } allowed)
            return "one of " + string.Join('/', allowed);
        if (constraint.Minimum is { } min && constraint.Maximum is { } max)
            return $"{min} to {max}";
        if (constraint.Minimum is { } only)
            return $"at least {only}";
        if (constraint.Maximum is { } cap)
            return $"at most {cap}";

        return "constrained";
    }
}
