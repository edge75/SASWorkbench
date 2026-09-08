using SasWorkbench.Core.Protocol;

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
                foreach (SasMeterInfo info in SasCatalogue.Meters.Values.OrderBy(i => i.Code))
                {
                    string readable = MeterIndex.All.Any(m => m.Name == SasNames.Underscore(info.Symbol))
                        ? "readable"
                        : "-";
                    Console.WriteLine($"  ${info.Code:X4}  {readable,-9}  {info.Symbol,-46}  {info.Description}");
                }

                return 0;

            case "layouts":
                Layouts();
                return 0;

            case "diff":
                return OverlayDiffCommand.Execute(line);

            case "readable-meters":
                foreach (MeterSource source in MeterIndex.All.OrderBy(m => m.Name))
                    Console.WriteLine($"  {source.Name,-30}  lp ${source.Command:X2}  {source.Unit}");
                return 0;

            default:
                Console.Error.WriteLine(
                    $"error: '{what}' is not something to list. Expected long-polls, exceptions, "
                    + "meters, layouts, readable-meters or diff.");
                return 2;
        }
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
        Console.WriteLine($"  meters       {MeterIndex.All.Count} readable by name");
        Console.WriteLine();
        Console.WriteLine("A command in the catalogue with no layout is reported as undecoded, so a");
        Console.WriteLine("test that needs it is skipped with that reason rather than guessed at.");
        Console.WriteLine();
        Console.WriteLine("Try 'sasctl catalogue layouts' for the replies, field by field.");
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
