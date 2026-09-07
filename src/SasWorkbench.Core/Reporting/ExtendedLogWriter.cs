using System.Text;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Reporting;

/// <summary>
/// Writes the run as a log that explains itself: what each case is before it runs, and what every
/// packet means.
/// </summary>
/// <remarks>
/// <para>
/// A rendering of the record and never a second capture. Everything printed here is derived from
/// the run report, which is the same thing <see cref="JsonLinesReader"/> produces from a file — so
/// asking for an extended log before a run and producing one afterwards from a log that already
/// exists go through this class either way, and cannot drift apart. The raw record stays the only
/// artefact that has to be right; this can be regenerated, corrected and re-rendered without
/// re-running anything.
/// </para>
/// <para>
/// Every packet gets a line. Where this build cannot read a reply's bytes, the line says the
/// command is known and not decoded, and names it — never a plausible reading. That makes the log
/// a coverage measurement as well as a log, and the tally at the end is the number breadth work
/// moves.
/// </para>
/// </remarks>
public sealed class ExtendedLogWriter : IReportWriter
{
    private const int Width = 78;

    public ReportFormat Format => ReportFormat.Extended;

    public string Extension => ".log";

    /// <summary>Renders the whole run.</summary>
    public string Render(RunReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        Header(builder, report);

        var everything = new List<PacketExplanation>();
        for (int i = 0; i < report.Tests.Count; i++)
        {
            IReadOnlyList<PacketExplanation> packets = Test(
                builder, report.Tests[i], i + 1, report.Tests.Count);
            everything.AddRange(packets);
        }

        Coverage(builder, "WHAT THE WHOLE RUN EXPLAINED, COUNTED",
            PacketCoverage.Of(everything), report.Tests.Count > 1);
        return builder.ToString();
    }

    private static void Header(StringBuilder builder, RunReport report)
    {
        builder.AppendLine("SAS EXTENDED LOG");
        builder.AppendLine(new string('=', Width));
        builder.AppendLine();
        Wrapped(builder,
            "A rendering of one recorded run. Every line below is derived from the record: "
            + "nothing was captured a second time to produce it, so the same record will render "
            + "the same log again, and a correction to this rendering needs no new run.");
        builder.AppendLine();

        Pair(builder, "started", report.StartedAt == default
            ? "not recorded"
            : $"{report.StartedAt:u}  (local {report.StartedAt.ToLocalTime():HH:mm:ss})");
        Pair(builder, "duration", $"{report.Duration.TotalSeconds:F1}s");
        Pair(builder, "verdict",
            $"{report.Verdict.ToString().ToUpperInvariant()} — {report.Summary()}");

        foreach (RunProvenance provenance in report.Provenance)
            Pair(builder, "carried over", provenance.ToString());

        if (report.Provenance.Count == 0)
            Pair(builder, "carried over", "the record does not say");

        if (report.Capabilities.Count > 0)
        {
            builder.AppendLine("  provided");
            Wrapped(builder, string.Join(", ", report.Capabilities), indent: 4);
        }

        builder.AppendLine();
    }

    private static IReadOnlyList<PacketExplanation> Test(
        StringBuilder builder, TestOutcome test, int index, int total)
    {
        builder.AppendLine(new string('-', Width));
        builder.AppendLine(
            $"TEST {index} of {total}   {test.Verdict.ToString().ToUpperInvariant()}   {test.Name}");
        builder.AppendLine(new string('-', Width));
        builder.AppendLine();

        Description(builder, test);
        Steps(builder, test);

        IReadOnlyList<PacketExplanation> packets = PacketExplainer.ExplainAll(
            test.Wire, address: null, wakeupBitCaptured: test.Provenance?.WakeupBitCaptured ?? true);
        Packets(builder, packets);
        Coverage(builder, "WHAT THIS CASE EXPLAINED, COUNTED", PacketCoverage.Of(packets), false);
        return packets;
    }

    /// <summary>
    /// What the case is, before any of it runs, said only in what the script says about itself.
    /// </summary>
    /// <remarks>
    /// A case that carries no labels and declares nothing it needs produces a thin description,
    /// and the log says so rather than filling the space with prose nobody wrote. The gap is a
    /// prompt to improve the script; inventing a purpose for a case would put words in the
    /// author's mouth and make the log worse than silence.
    /// </remarks>
    private static void Description(StringBuilder builder, TestOutcome test)
    {
        builder.AppendLine("WHAT THIS CASE IS");

        Pair(builder, "written at", test.Location.ToString());

        if (test.StartedAt != default)
        {
            Pair(builder, "began",
                $"{test.StartedAt.ToLocalTime():HH:mm:ss.fff} local  ({test.StartedAt:u})");
        }

        Pair(builder, "labelled", test.Tags.Count == 0
            ? "nothing"
            : string.Join(' ', test.Tags.Select(t => "@" + t)));

        if (test.Requirements.Count == 0)
        {
            Pair(builder, "needs", "nothing it declares");
        }
        else
        {
            bool first = true;
            foreach (Requirement requirement in test.Requirements)
            {
                Pair(builder, first ? "needs" : string.Empty,
                    $"{requirement}   (declared at line {requirement.Location.Line})");
                first = false;
            }
        }

        if (test.Machine is { } declared)
            Pair(builder, "assumes", $"{declared}   (declared at line {declared.Location.Line})");

        Pair(builder, "does", Shape(test));

        if (test.Provenance is { } provenance)
            Pair(builder, "carried over", provenance.ToString());

        if (test.SkipReason is { } reason)
            Pair(builder, test.Verdict == Verdict.Skip ? "not run" : "note", reason);

        if (test.Tags.Count == 0 && test.Requirements.Count == 0)
        {
            builder.AppendLine();
            Wrapped(builder,
                "This case says nothing about itself beyond its name and the line it is written "
                + "on: it carries no labels and declares nothing it needs. That is a description "
                + "of the script rather than of the case, and the way to a fuller one is to say "
                + "in the script what the case assumes.");
        }

        builder.AppendLine();
    }

    /// <summary>What the case does, counted off its own steps rather than described.</summary>
    private static string Shape(TestOutcome test)
    {
        if (test.Steps.Count == 0)
            return "nothing the record holds";

        int claims = test.Steps.Count(s => s.IsAssertion);
        int[] lines = test.Steps
            .Where(s => s.Location.Line > 0)
            .Select(s => s.Location.Line)
            .ToArray();

        string span = lines.Length == 0
            ? string.Empty
            : lines.Min() == lines.Max()
                ? $", on line {lines.Min()}"
                : $", between lines {lines.Min()} and {lines.Max()}";

        return $"{test.Steps.Count} step(s){span}, {claims} of which make a claim about the machine";
    }

    private static void Steps(StringBuilder builder, TestOutcome test)
    {
        if (test.Steps.Count == 0)
            return;

        builder.AppendLine("WHAT THE SCRIPT DID");

        foreach (StepResult step in test.Steps)
        {
            builder.AppendLine(
                $"  {step.Verdict.ToString().ToUpperInvariant(),-5} {Line(step.Location),5}  "
                + step.Description);

            if (step.Expected is not null)
                builder.AppendLine($"        {"expected",-9} {step.Expected}");
            if (step.Actual is not null)
                builder.AppendLine($"        {"actual",-9} {step.Actual}");
            // A reply's own detail is already inside 'actual', and printing it twice reads as two
            // findings where there is one.
            if (step.Reason is not null && step.Actual?.Contains(step.Reason, StringComparison.Ordinal) != true)
                builder.AppendLine($"        {step.Reason}");
        }

        builder.AppendLine();
    }

    private static void Packets(StringBuilder builder, IReadOnlyList<PacketExplanation> packets)
    {
        builder.AppendLine("WHAT CROSSED THE WIRE");

        if (packets.Count == 0)
        {
            Wrapped(builder, "The record holds no packets for this case. A case skipped before "
                             + "its link opened puts nothing on the wire.");
            builder.AppendLine();
            return;
        }

        Wrapped(builder,
            $"{packets.Count} packet(s), timed from the moment the link opened in this case. "
            + Own(packets));
        builder.AppendLine();

        foreach (PacketExplanation packet in packets)
            Packet(builder, packet);

        builder.AppendLine();
    }

    /// <summary>Which packets no step of the script accounts for, and what asked for them.</summary>
    /// <remarks>
    /// The host sends polls of its own — it asks a machine what validation style it performs
    /// before a case that requires one, and it reads a meter to establish what a relative
    /// assertion is measured against. A reader counting the packets against the steps would come
    /// up short and have no way of knowing why, so the record marks them as the host sends them
    /// and this says what the marks were.
    /// </remarks>
    private static string Own(IReadOnlyList<PacketExplanation> packets)
    {
        string[] reasons = packets
            .Where(p => p.Purpose is not null)
            .Select(p => p.Purpose!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (reasons.Length == 0)
            return "Every one was sent by the poll loop or asked for by a step.";

        int count = packets.Count(p => p.Purpose is not null);
        return $"{count} of them the host sent on its own behalf, with no step asking: "
               + $"{string.Join(", ", reasons)}. They are marked in the margin.";
    }

    private static void Packet(StringBuilder builder, PacketExplanation packet)
    {
        string arrow = packet.Direction == WireDirection.Transmit ? "TX" : "RX";
        string channel = packet.Channel == SasChannel.One ? "  " : $"c{(int)packet.Channel}";
        string margin = packet.Purpose is null ? string.Empty : $"   [host's own: {packet.Purpose}]";

        builder.AppendLine(
            $"  #{packet.Sequence,-5} {arrow} {channel} {packet.At.TotalMilliseconds,9:F1}ms  "
            + $"{packet.Hex}{margin}");
        builder.AppendLine($"        {packet.Headline}");

        foreach (PacketField field in packet.Fields)
        {
            builder.AppendLine(
                $"          {field.Path,-28} = {field.Rendered,-22} {field.Hex}");
            if (field.Meaning is not null)
                builder.AppendLine($"            {field.Meaning}");
            if (field.Violation is not null)
                builder.AppendLine($"            ** {field.Violation}");
        }

        if (packet.Limit is not null)
            builder.AppendLine($"        ** {packet.MeaningLabel}: {packet.Limit}");

        foreach (string finding in packet.Findings)
            builder.AppendLine($"        ** {finding}");

        if (packet.DerivedFrom is not null)
            builder.AppendLine($"        layout read from {packet.DerivedFrom}");
    }

    /// <summary>
    /// How much was explained, as a number.
    /// </summary>
    /// <remarks>
    /// Two tallies, and the second is the one that means something. A poll loop is the great
    /// majority of any session's packets and every one of them is explained in full, so a count
    /// over all packets reads as near-total coverage however little of the protocol this build
    /// understands. The count over the distinct long polls a run exchanged does not flatter
    /// anything, and the commands listed under it are the layouts to establish next.
    /// </remarks>
    private static void Coverage(
        StringBuilder builder, string title, PacketCoverage coverage, bool separate)
    {
        if (separate)
        {
            builder.AppendLine(new string('=', Width));
            builder.AppendLine();
        }

        builder.AppendLine(title);
        Pair(builder, "packets", coverage.Packets.ToString());
        Tally(builder, "explained in full", coverage.Explained, coverage.Packets);
        Tally(builder, "known but not decoded", coverage.NamedOnly, coverage.Packets);
        Tally(builder, "layout did not fit", coverage.LayoutDidNotFit, coverage.Packets);
        Tally(builder, "not a message at all", coverage.NotAMessage, coverage.Packets);
        Tally(builder, "not named at all", coverage.Unknown, coverage.Packets);

        if (coverage.HostsOwn > 0)
            Tally(builder, "of which the host's own", coverage.HostsOwn, 0);

        builder.AppendLine();
        Pair(builder, "long polls", $"{coverage.Commands} distinct command(s)");
        Tally(builder, "explained in full", coverage.CommandsExplained.Count, coverage.Commands,
            coverage.CommandsExplained);
        Tally(builder, "known but not decoded", coverage.CommandsNamedOnly.Count,
            coverage.Commands, coverage.CommandsNamedOnly);
        Tally(builder, "not named at all", coverage.CommandsUnknown.Count, coverage.Commands,
            coverage.CommandsUnknown);

        if (coverage.CommandsNamedOnly.Count > 0)
        {
            builder.AppendLine();
            Wrapped(builder,
                "Each command listed as known but not decoded is a layout this build has not "
                + "established. They are what this log leaves unexplained, and therefore where "
                + "breadth work is worth most.");
            foreach (byte command in coverage.CommandsNamedOnly)
            {
                builder.AppendLine($"    ${command:X2}  "
                    + (SasCatalogue.LongPolls.TryGetValue(command, out SasLongPollInfo? info)
                        ? info.Description
                        : "in no long poll list this build holds"));
            }
        }

        builder.AppendLine();
    }

    private static void Tally(
        StringBuilder builder, string label, int count, int total, IReadOnlyList<byte>? codes = null)
    {
        // Integer arithmetic rather than a percentage format, which puts a space before the sign
        // in some cultures and would make one machine's log differ from another's.
        string share = total == 0 ? string.Empty : $"  ({count * 100 / total}%)";
        string which = codes is { Count: > 0 } && codes.Count <= 12
            ? "  " + string.Join(' ', codes.Select(c => $"${c:X2}"))
            : string.Empty;
        builder.AppendLine($"  {"  " + label,-26}  {count,5}{share}{which}");
    }

    private static string Line(SourceLocation location) =>
        location.Line > 0 ? ":" + location.Line : string.Empty;

    private static void Pair(StringBuilder builder, string label, string value) =>
        builder.AppendLine($"  {label,-14} {value}");

    private static void Wrapped(StringBuilder builder, string text, int indent = 2)
    {
        string margin = new(' ', indent);
        var line = new StringBuilder(margin);
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > indent && line.Length + 1 + word.Length > Width)
            {
                builder.AppendLine(line.ToString());
                line.Clear().Append(margin);
            }

            if (line.Length > indent)
                line.Append(' ');
            line.Append(word);
        }

        if (line.Length > indent)
            builder.AppendLine(line.ToString());
    }
}
