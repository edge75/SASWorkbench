using System.Net;
using System.Text;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Core.Reporting;

/// <summary>
/// Writes the run as one self-contained page, with the machine-readable log embedded inside it.
/// </summary>
/// <remarks>
/// <para>
/// One file, no external anything: a report that needs a stylesheet from somewhere is a report
/// that stops working the moment it is emailed. And the same run in machine-readable form is
/// embedded in the page, so the file that a person reads and the file that a tool reads are the
/// same file and cannot disagree about what happened.
/// </para>
/// <para>
/// The provenance block is at the top rather than in a footnote. A page full of green that was
/// produced over a link nobody has measured says something much weaker than the same page
/// produced against hardware, and a reader must not have to hunt for which one they are holding.
/// </para>
/// </remarks>
public sealed class HtmlReportWriter : IReportWriter
{
    public ReportFormat Format => ReportFormat.Html;

    public string Extension => ".html";

    public string Render(RunReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine($"<title>SAS run — {Text(report.Verdict.ToString())}</title>");
        builder.AppendLine(Style);
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");

        WriteHeader(builder, report);
        WriteProvenance(builder, report);
        WriteTests(builder, report);
        WriteCapabilities(builder, report);
        WriteEmbeddedLog(builder, report);

        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static void WriteHeader(StringBuilder builder, RunReport report)
    {
        string verdict = report.Verdict.ToString().ToLowerInvariant();
        builder.AppendLine("<header>");
        builder.AppendLine($"<p class=\"verdict {verdict}\">{Text(report.Verdict.ToString().ToUpperInvariant())}</p>");
        builder.AppendLine("<h1>SAS conformance run</h1>");
        builder.AppendLine($"<p class=\"summary\">{Text(report.Summary())}</p>");
        builder.AppendLine($"<p class=\"when\">Started {Text(report.StartedAt.ToString("u"))}</p>");
        builder.AppendLine("</header>");
    }

    private static void WriteProvenance(StringBuilder builder, RunReport report)
    {
        if (report.Provenance.Count == 0)
            return;

        builder.AppendLine("<section class=\"provenance\">");
        builder.AppendLine("<h2>What this run was carried over</h2>");
        foreach (RunProvenance provenance in report.Provenance)
        {
            string weight = provenance.Fidelity.ToString().ToLowerInvariant();
            builder.AppendLine($"<div class=\"link {weight}\">");
            builder.AppendLine($"<p class=\"transport\">{Text(provenance.Transport)}</p>");
            builder.AppendLine(
                $"<p class=\"meta\">weight <strong>{Text(weight)}</strong> · stimulus "
                + $"<strong>{Text(provenance.Stimulus.ToString().ToLowerInvariant())}</strong></p>");
            if (provenance.Caveat is not null)
                builder.AppendLine($"<p class=\"caveat\">{Text(provenance.Caveat)}</p>");
            builder.AppendLine("</div>");
        }

        builder.AppendLine("</section>");
    }

    private static void WriteTests(StringBuilder builder, RunReport report)
    {
        builder.AppendLine("<section class=\"tests\">");
        builder.AppendLine("<h2>Tests</h2>");

        foreach (TestOutcome test in report.Tests)
        {
            string verdict = test.Verdict.ToString().ToLowerInvariant();
            bool interesting = test.Verdict != Verdict.Pass;

            builder.AppendLine($"<details class=\"test {verdict}\"{(interesting ? " open" : string.Empty)}>");
            builder.AppendLine("<summary>");
            builder.AppendLine($"<span class=\"tag {verdict}\">{Text(test.Verdict.ToString().ToUpperInvariant())}</span>");
            builder.AppendLine($"<span class=\"name\">{Text(test.Name)}</span>");
            builder.AppendLine(
                $"<span class=\"stats\">{test.Assertions} assert(s) · "
                + $"{test.Duration.TotalSeconds:F1}s</span>");
            builder.AppendLine("</summary>");

            builder.AppendLine($"<p class=\"where\">{Text(test.Location.ToString())}</p>");

            if (test.SkipReason is not null)
                builder.AppendLine($"<p class=\"reason\">{Text(test.SkipReason)}</p>");

            if (test.Tags.Count > 0)
            {
                builder.AppendLine("<p class=\"tags\">"
                                   + string.Join(' ', test.Tags.Select(t => $"<code>@{Text(t)}</code>"))
                                   + "</p>");
            }

            WriteSteps(builder, test);
            WriteWire(builder, test);
            builder.AppendLine("</details>");
        }

        builder.AppendLine("</section>");
    }

    private static void WriteSteps(StringBuilder builder, TestOutcome test)
    {
        if (test.Steps.Count == 0)
            return;

        builder.AppendLine("<table class=\"steps\">");
        builder.AppendLine("<thead><tr><th></th><th>Line</th><th>Step</th><th>Expected</th>"
                           + "<th>Actual</th></tr></thead>");
        builder.AppendLine("<tbody>");

        foreach (StepResult step in test.Steps)
        {
            string verdict = step.Verdict.ToString().ToLowerInvariant();
            builder.AppendLine($"<tr class=\"{verdict}\">");
            builder.AppendLine($"<td class=\"tag {verdict}\">{Text(Mark(step.Verdict))}</td>");
            builder.AppendLine($"<td class=\"line\">{step.Location.Line}</td>");
            builder.AppendLine($"<td class=\"what\"><code>{Text(step.Description)}</code>"
                               + (step.Reason is null
                                   ? string.Empty
                                   : $"<span class=\"reason\">{Text(step.Reason)}</span>")
                               + "</td>");
            builder.AppendLine($"<td>{Text(step.Expected ?? string.Empty)}</td>");
            builder.AppendLine($"<td>{Text(step.Actual ?? string.Empty)}</td>");
            builder.AppendLine("</tr>");
        }

        builder.AppendLine("</tbody></table>");
    }

    private static void WriteWire(StringBuilder builder, TestOutcome test)
    {
        if (test.Wire.Count == 0)
            return;

        builder.AppendLine("<details class=\"wire\">");
        builder.AppendLine($"<summary>Wire log — {test.Wire.Count} message(s)</summary>");
        builder.AppendLine("<pre>");
        foreach (WireRecord record in test.Wire)
            builder.AppendLine(Text(record.ToString()));
        builder.AppendLine("</pre>");
        builder.AppendLine("</details>");
    }

    private static void WriteCapabilities(StringBuilder builder, RunReport report)
    {
        if (report.Capabilities.Count == 0)
            return;

        builder.AppendLine("<section class=\"capabilities\">");
        builder.AppendLine("<h2>What this environment provides</h2>");
        builder.AppendLine("<p>"
                           + string.Join(' ', report.Capabilities.Select(c => $"<code>{Text(c)}</code>"))
                           + "</p>");
        builder.AppendLine("</section>");
    }

    private static void WriteEmbeddedLog(StringBuilder builder, RunReport report)
    {
        // The same run in machine-readable form, inside the same file. A script tag of a type the
        // browser does not execute, so this is data the page carries rather than code it runs.
        // Appended verbatim rather than line by line, so the bytes a tool reads back out are
        // exactly the bytes the standalone log file holds.
        string log = new JsonLinesReportWriter().Render(report);
        builder.Append("<script type=\"application/x-ndjson\" id=\"saswb-run\">\n");
        builder.Append(log.Replace("</", "<\\/"));
        builder.Append("</script>\n");
    }

    private static string Mark(Verdict verdict) => verdict switch
    {
        Verdict.Pass => "PASS",
        Verdict.Fail => "FAIL",
        Verdict.Skip => "SKIP",
        _ => "ERR",
    };

    private static string Text(string value) => WebUtility.HtmlEncode(value);

    private const string Style = """
        <style>
          :root {
            color-scheme: light dark;
            --ink: #14171a;
            --paper: #fbfbfa;
            --panel: #ffffff;
            --line: #e2e2df;
            --muted: #62666b;
            --pass: #1d7a4d;
            --fail: #b3261e;
            --skip: #8a6100;
            --error: #6b21a8;
          }

          @media (prefers-color-scheme: dark) {
            :root {
              --ink: #e9eaec;
              --paper: #16181b;
              --panel: #1e2125;
              --line: #32363c;
              --muted: #9aa0a6;
              --pass: #63c68e;
              --fail: #f2867d;
              --skip: #e0b45c;
              --error: #c99bf5;
            }
          }

          * { box-sizing: border-box; }

          body {
            margin: 0 auto;
            padding: 2rem 1.25rem 4rem;
            max-width: 60rem;
            background: var(--paper);
            color: var(--ink);
            font: 15px/1.55 ui-sans-serif, system-ui, -apple-system, "Segoe UI", sans-serif;
          }

          h1 { font-size: 1.5rem; margin: .25rem 0 .5rem; }
          h2 { font-size: .8rem; text-transform: uppercase; letter-spacing: .08em;
               color: var(--muted); margin: 2rem 0 .75rem; }

          code, pre { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; }

          .verdict { display: inline-block; margin: 0; padding: .2rem .55rem; border-radius: .3rem;
                     font-size: .75rem; font-weight: 700; letter-spacing: .06em; color: #fff; }
          .verdict.pass { background: var(--pass); }
          .verdict.fail { background: var(--fail); }
          .verdict.skip { background: var(--skip); }
          .verdict.error { background: var(--error); }

          .summary { font-size: 1.05rem; margin: .25rem 0; }
          .when { color: var(--muted); font-size: .85rem; margin: 0; }

          .provenance .link { border: 1px solid var(--line); border-left-width: 4px;
                              border-radius: .4rem; padding: .7rem .9rem; margin-bottom: .6rem;
                              background: var(--panel); }
          .provenance .link.proven { border-left-color: var(--pass); }
          .provenance .link.unverified { border-left-color: var(--skip); }
          .provenance .transport { margin: 0; font-weight: 600; font-family: ui-monospace, monospace; }
          .provenance .meta { margin: .2rem 0 0; color: var(--muted); font-size: .85rem; }
          .provenance .caveat { margin: .4rem 0 0; font-size: .88rem; }

          details.test { border: 1px solid var(--line); border-radius: .4rem; margin-bottom: .5rem;
                         background: var(--panel); overflow: hidden; }
          details.test > summary { cursor: pointer; padding: .6rem .8rem; display: flex;
                                   gap: .6rem; align-items: center; }
          details.test > summary::-webkit-details-marker { display: none; }
          details.test .name { font-weight: 600; flex: 1 1 auto; }
          details.test .stats { color: var(--muted); font-size: .8rem; white-space: nowrap; }
          details.test > * { margin-left: .8rem; margin-right: .8rem; }
          details.test > summary { margin: 0; }

          .tag { font-size: .68rem; font-weight: 700; letter-spacing: .05em; padding: .12rem .4rem;
                 border-radius: .25rem; color: #fff; white-space: nowrap; }
          .tag.pass { background: var(--pass); }
          .tag.fail { background: var(--fail); }
          .tag.skip { background: var(--skip); }
          .tag.error { background: var(--error); }

          .where { color: var(--muted); font-size: .8rem; font-family: ui-monospace, monospace; }
          .reason { color: var(--muted); font-size: .85rem; display: block; }
          .tags code { font-size: .75rem; }

          table.steps { width: calc(100% - 1.6rem); border-collapse: collapse; font-size: .85rem;
                        margin-bottom: .8rem; display: block; overflow-x: auto; }
          table.steps th { text-align: left; font-size: .7rem; text-transform: uppercase;
                           letter-spacing: .06em; color: var(--muted); padding: .3rem .4rem;
                           border-bottom: 1px solid var(--line); }
          table.steps td { padding: .3rem .4rem; border-bottom: 1px solid var(--line);
                           vertical-align: top; }
          table.steps td.line { color: var(--muted); text-align: right; width: 3rem; }
          table.steps tr.pass td { opacity: .72; }
          table.steps tr.fail td { background: color-mix(in srgb, var(--fail) 8%, transparent); }

          details.wire { margin-bottom: .8rem; }
          details.wire > summary { cursor: pointer; font-size: .8rem; color: var(--muted); }
          details.wire pre { overflow-x: auto; font-size: .78rem; padding: .6rem;
                             border: 1px solid var(--line); border-radius: .3rem;
                             background: var(--paper); }

          .capabilities code { font-size: .75rem; color: var(--muted); }

          @media print {
            details { open: true; }
            body { max-width: none; }
          }
        </style>
        """;
}
