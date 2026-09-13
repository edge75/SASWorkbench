using System.Text;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Core.Reporting;

/// <summary>Writes the run as Markdown, for pasting into a ticket or a pull request.</summary>
public sealed class MarkdownReportWriter : IReportWriter
{
    public ReportFormat Format => ReportFormat.Markdown;

    public string Extension => ".md";

    public string Render(RunReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# SAS conformance run");
        builder.AppendLine();
        builder.AppendLine($"**{report.Verdict.ToString().ToUpperInvariant()}** — {report.Summary()}");
        builder.AppendLine();
        builder.AppendLine($"Started {report.StartedAt:u}.");
        builder.AppendLine();

        // Stated before the results, not after them: a reader needs to know what the run was
        // carried over before they read a column of green.
        if (report.Provenance.Count > 0)
        {
            builder.AppendLine("## What this run was carried over");
            builder.AppendLine();
            builder.AppendLine("| Link | Weight | Stimulus | Channels | Caveat |");
            builder.AppendLine("|---|---|---|---|---|");
            foreach (RunProvenance provenance in report.Provenance)
            {
                string channels = provenance.Channels is null
                    ? "—"
                    : $"{provenance.Channels} "
                      + $"({(provenance.ChannelsRead ? "read" : "declared")})";

                string caveat = provenance.ChannelRefusal is null
                    ? provenance.Caveat ?? "—"
                    : $"arrangement refused: {provenance.ChannelRefusal}"
                      + (provenance.Caveat is null ? string.Empty : $" {provenance.Caveat}");

                builder.AppendLine(
                    $"| {Escape(provenance.Transport)} "
                    + $"| {provenance.Fidelity.ToString().ToLowerInvariant()} "
                    + $"| {provenance.Stimulus.ToString().ToLowerInvariant()} "
                    + $"| {Escape(channels)} "
                    + $"| {Escape(caveat)} |");
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Tests");
        builder.AppendLine();
        builder.AppendLine("| | Test | Asserts | Time |");
        builder.AppendLine("|---|---|---|---|");
        foreach (TestOutcome test in report.Tests)
        {
            builder.AppendLine(
                $"| {Mark(test.Verdict)} | {Escape(test.Name)} | {test.Assertions} "
                + $"| {test.Duration.TotalSeconds:F1}s |");
        }

        builder.AppendLine();

        TestOutcome[] notPassed = report.Tests.Where(t => t.Verdict != Verdict.Pass).ToArray();
        if (notPassed.Length > 0)
        {
            builder.AppendLine("## What did not pass");
            builder.AppendLine();
            foreach (TestOutcome test in notPassed)
            {
                builder.AppendLine($"### {Mark(test.Verdict)} {Escape(test.Name)}");
                builder.AppendLine();
                builder.AppendLine($"`{test.Location}`");
                builder.AppendLine();

                if (test.SkipReason is not null)
                {
                    builder.AppendLine(Escape(test.SkipReason));
                    builder.AppendLine();
                }

                foreach (StepResult step in test.Steps.Where(s => s.Verdict != Verdict.Pass))
                {
                    builder.AppendLine($"- `{step.Location}` {Escape(step.Description)}");
                    if (step.Expected is not null)
                        builder.AppendLine($"  - expected: {Escape(step.Expected)}");
                    if (step.Actual is not null)
                        builder.AppendLine($"  - actual: {Escape(step.Actual)}");
                    if (step.Reason is not null)
                        builder.AppendLine($"  - {Escape(step.Reason)}");
                }

                builder.AppendLine();
            }
        }

        if (report.Capabilities.Count > 0)
        {
            builder.AppendLine("## What this environment provides");
            builder.AppendLine();
            builder.AppendLine(string.Join(", ", report.Capabilities.Select(c => $"`{c}`")));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string Mark(Verdict verdict) => verdict switch
    {
        Verdict.Pass => "PASS",
        Verdict.Fail => "FAIL",
        Verdict.Skip => "SKIP",
        _ => "ERROR",
    };

    private static string Escape(string text) => text.Replace("|", "\\|");
}
