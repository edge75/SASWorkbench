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
            provenance = report.Provenance.Select(Project),
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
                startedAt = test.StartedAt,
                durationMs = test.Duration.TotalMilliseconds,
                assertions = test.Assertions,
                skipReason = test.SkipReason,
                tags = test.Tags,

                // What the case said it needed, and what it was carried over. Both were already
                // known when the run happened; recording them is what lets anything rendered from
                // this file afterwards say what the case was for without reading the script again.
                requires = test.Requirements.Select(r => new
                {
                    file = r.Location.File,
                    line = r.Location.Line,
                    key = r.Key,
                    value = r.ExpectedValue,
                }),
                assumes = test.Machine is { } declared
                    ? new
                    {
                        file = declared.Location.File,
                        line = declared.Location.Line,
                        accountingDenominationCents = declared.Cents,
                        accountingDenominationCode = declared.Code,
                        playerDenominationsInCents = declared.PlayerDenominationsInCents,
                    }
                    : null,
                provenance = test.Provenance is { } how ? Project(how) : null,
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

                    // Which machine, not only which host. A general poll's answer is a bare
                    // exception byte with no address in it, so across a round this is the only
                    // thing that says who spoke. Absent means not known, never machine one.
                    address = record.Address?.Value,
                    atMs = record.Elapsed.TotalMilliseconds,
                    symbols = record.ToHex(),
                    note = record.Note,
                });
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// One projection of provenance, used at both the run level and the test level.
    /// </summary>
    /// <remarks>
    /// Shared rather than written twice so the two cannot drift: a field added to one and not the
    /// other would make a record whose answer to "which host owned this poll" depended on which
    /// line of it you read.
    /// </remarks>
    private static object Project(RunProvenance p) => new
    {
        transport = p.Transport,
        fidelity = p.Fidelity.ToString(),
        stimulus = p.Stimulus.ToString(),
        caveat = p.Caveat,
        wakeupBitCaptured = p.WakeupBitCaptured,

        // The arrangement, which is what makes "which host should have sent this poll" and "which
        // host owns this exception" answerable from the file rather than only from the terminal.
        channels = p.Channels,
        channelsRead = p.ChannelsRead,
        channelRefusal = p.ChannelRefusal,
    };

    private static void Write(StringBuilder builder, object record) =>
        builder.Append(JsonSerializer.Serialize(record, Options)).Append('\n');
}
