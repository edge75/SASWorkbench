using System.Text;
using System.Text.Json;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Core.Reporting;

/// <summary>
/// Writes the run as one JSON object per line: a header, then a record for every test, step and
/// message.
/// </summary>
/// <remarks>
/// The format anything else reads the run through. Line-delimited rather than one document, so a
/// long run can be streamed and a truncated file is still readable up to the point it stops.
/// </remarks>
public sealed class JsonLinesReportWriter : IReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
    };

    public ReportFormat Format => ReportFormat.JsonLines;

    public string Extension => ".jsonl";

    public string Render(RunReport report)
    {
        var builder = new StringBuilder();

        Write(builder, new
        {
            kind = "run",
            startedAt = report.StartedAt,
            durationMs = report.Duration.TotalMilliseconds,
            verdict = report.Verdict.ToString(),
            exitCode = report.ExitCode,
            passed = report.Passed,
            failed = report.Failed,
            skipped = report.Skipped,
            errored = report.Errored,
            provenance = report.Provenance.Select(p => new
            {
                transport = p.Transport,
                fidelity = p.Fidelity.ToString(),
                stimulus = p.Stimulus.ToString(),
                caveat = p.Caveat,
            }),
            capabilities = report.Capabilities,
        });

        foreach (TestOutcome test in report.Tests)
        {
            Write(builder, new
            {
                kind = "test",
                name = test.Name,
                file = test.Location.File,
                line = test.Location.Line,
                verdict = test.Verdict.ToString(),
                durationMs = test.Duration.TotalMilliseconds,
                assertions = test.Assertions,
                skipReason = test.SkipReason,
                tags = test.Tags,
            });

            foreach (StepResult step in test.Steps)
            {
                Write(builder, new
                {
                    kind = "step",
                    test = test.Name,
                    file = step.Location.File,
                    line = step.Location.Line,
                    description = step.Description,
                    verdict = step.Verdict.ToString(),
                    isAssertion = step.IsAssertion,
                    expected = step.Expected,
                    actual = step.Actual,
                    reason = step.Reason,
                    atMs = step.At.TotalMilliseconds,
                });
            }

            foreach (WireRecord record in test.Wire)
            {
                Write(builder, new
                {
                    kind = "wire",
                    test = test.Name,
                    sequence = record.Sequence,
                    direction = record.Direction.ToString(),
                    channel = (int)record.Channel,
                    atMs = record.Elapsed.TotalMilliseconds,
                    symbols = record.ToHex(),
                    note = record.Note,
                });
            }
        }

        return builder.ToString();
    }

    private static void Write(StringBuilder builder, object record) =>
        builder.Append(JsonSerializer.Serialize(record, Options)).Append('\n');
}
