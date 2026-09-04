using System.Text.Json;
using System.Xml.Linq;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Reporting;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

public class ReportingTests
{
    private static RunReport Sample() => new()
    {
        StartedAt = new DateTimeOffset(2026, 9, 4, 8, 0, 0, TimeSpan.Zero),
        Duration = TimeSpan.FromSeconds(3.5),
        Capabilities = ["decoder.lp1F", "stimulus.bill", "transport.tcp"],
        Provenance =
        [
            new RunProvenance("serial COM12 @ 19200", TransportFidelity.Unverified,
                StimulusBackend.Manual, "Serial timing has never been measured against an adapter."),
        ],
        Tests =
        [
            new TestOutcome
            {
                Name = "the link comes up",
                Location = new SourceLocation("tests/regression/wire/link.sas", 3),
                Verdict = Verdict.Pass,
                Duration = TimeSpan.FromSeconds(1.2),
                Tags = ["wire-only"],
                Steps =
                [
                    new StepResult(new SourceLocation("tests/regression/wire/link.sas", 5),
                        "expect link up within 5000ms", Verdict.Pass, "Up", "Up") { IsAssertion = true },
                ],
                Wire =
                [
                    new WireRecord(0, WireDirection.Transmit, SasChannel.One,
                        TimeSpan.FromMilliseconds(12), [SasSymbol.Address(0x80)], "sync poll"),
                    new WireRecord(1, WireDirection.Receive, SasChannel.One,
                        TimeSpan.FromMilliseconds(30), [SasSymbol.Data8(0x00)], null),
                ],
            },
            new TestOutcome
            {
                Name = "a meter that did not move & <the wrong> value",
                Location = new SourceLocation("tests/regression/meters/note.sas", 4),
                Verdict = Verdict.Fail,
                Duration = TimeSpan.FromSeconds(2.3),
                Steps =
                [
                    new StepResult(new SourceLocation("tests/regression/meters/note.sas", 9),
                        "expect meter total_bill_in += 2000", Verdict.Fail,
                        "0 + 2000 = 2000", "500 (read from lp $20)") { IsAssertion = true },
                ],
            },
            new TestOutcome
            {
                Name = "10.1.3 cashout ticket 1",
                Location = new SourceLocation("tests/conformance/10.1/10.1.3.sas", 6),
                Verdict = Verdict.Skip,
                SkipReason = "printer: this environment provides no 'printer'",
                Duration = TimeSpan.Zero,
            },
        ],
    };

    [Fact]
    public void The_run_verdict_is_the_worst_of_its_tests()
    {
        Assert.Equal(Verdict.Fail, Sample().Verdict);
        Assert.Equal(1, Sample().ExitCode);
    }

    [Fact]
    public void Every_format_has_a_writer_and_an_extension()
    {
        foreach (ReportFormat format in Enum.GetValues<ReportFormat>())
        {
            IReportWriter writer = Reports.Writers[format];
            Assert.Equal(format, writer.Format);
            Assert.StartsWith(".", writer.Extension);
        }
    }

    [Theory]
    [InlineData("html", ReportFormat.Html)]
    [InlineData("junit", ReportFormat.JUnit)]
    [InlineData("xml", ReportFormat.JUnit)]
    [InlineData("md", ReportFormat.Markdown)]
    [InlineData("markdown", ReportFormat.Markdown)]
    [InlineData("jsonl", ReportFormat.JsonLines)]
    public void Format_names_a_command_line_offers_are_read(string text, ReportFormat expected)
    {
        Assert.True(Reports.TryParse(text, out ReportFormat[] formats, out _));
        Assert.Equal(expected, Assert.Single(formats));
    }

    [Fact]
    public void A_format_that_does_not_exist_is_named_rather_than_ignored()
    {
        Assert.False(Reports.TryParse("html,pdf", out _, out string? unknown));
        Assert.Equal("pdf", unknown);
    }

    [Fact]
    public void Several_formats_can_be_asked_for_at_once_without_repeats()
    {
        Assert.True(Reports.TryParse("html, junit, html", out ReportFormat[] formats, out _));
        Assert.Equal(2, formats.Length);
    }

    [Fact]
    public void The_junit_report_is_well_formed_xml()
    {
        string xml = new JUnitReportWriter().Render(Sample());

        XDocument document = XDocument.Parse(xml);
        Assert.Equal("testsuites", document.Root!.Name.LocalName);
        Assert.Equal("3", document.Root.Attribute("tests")!.Value);
        Assert.Equal("1", document.Root.Attribute("failures")!.Value);
        Assert.Equal("1", document.Root.Attribute("skipped")!.Value);
    }

    /// <summary>
    /// A skip has to be written as a skip. A build server shows skipped and passed differently,
    /// and reporting one as the other is how a suite that tested nothing comes to look healthy.
    /// </summary>
    [Fact]
    public void A_skipped_test_is_written_as_skipped_with_its_reason()
    {
        XDocument document = XDocument.Parse(new JUnitReportWriter().Render(Sample()));

        XElement skipped = document.Descendants("skipped").Single();
        Assert.Contains("printer", skipped.Attribute("message")!.Value);
    }

    [Fact]
    public void A_failure_carries_both_the_expected_and_the_actual_value()
    {
        XDocument document = XDocument.Parse(new JUnitReportWriter().Render(Sample()));

        XElement failure = document.Descendants("failure").Single();
        Assert.Contains("2000", failure.Value);
        Assert.Contains("500", failure.Value);
        Assert.Contains("note.sas:9", failure.Attribute("message")!.Value);
    }

    /// <summary>
    /// Honest labelling, in the artefact a build server keeps. A green suite over a link nobody
    /// has measured must carry that fact with it.
    /// </summary>
    [Fact]
    public void The_junit_report_records_what_the_run_was_carried_over()
    {
        XDocument document = XDocument.Parse(new JUnitReportWriter().Render(Sample()));

        string[] properties = document.Descendants("property")
            .Select(p => $"{p.Attribute("name")!.Value}={p.Attribute("value")!.Value}")
            .ToArray();

        Assert.Contains("fidelity=Unverified", properties);
        Assert.Contains(properties, p => p.StartsWith("caveat=", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_line_of_the_json_report_is_a_json_object()
    {
        string jsonl = new JsonLinesReportWriter().Render(Sample());

        string[] lines = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 7, $"only {lines.Length} line(s)");

        foreach (string line in lines)
        {
            using JsonDocument document = JsonDocument.Parse(line);
            Assert.True(document.RootElement.TryGetProperty("kind", out _));
        }
    }

    [Fact]
    public void The_json_report_carries_the_wire_log()
    {
        string jsonl = new JsonLinesReportWriter().Render(Sample());

        string[] wire = jsonl.Split('\n')
            .Where(l => l.Contains("\"kind\":\"wire\"", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, wire.Length);
        Assert.Contains("1.80", wire[0]);
    }

    [Fact]
    public void The_markdown_report_escapes_a_value_that_would_break_a_table()
    {
        string markdown = new MarkdownReportWriter().Render(Sample() with
        {
            Tests =
            [
                new TestOutcome
                {
                    Name = "a name with | a pipe in it",
                    Location = SourceLocation.None,
                    Verdict = Verdict.Pass,
                },
            ],
        });

        Assert.Contains(@"a name with \| a pipe in it", markdown);
    }

    [Fact]
    public void The_markdown_report_leads_with_the_verdict_and_what_it_was_carried_over()
    {
        string markdown = new MarkdownReportWriter().Render(Sample());

        int verdict = markdown.IndexOf("**FAIL**", StringComparison.Ordinal);
        int provenance = markdown.IndexOf("carried over", StringComparison.Ordinal);
        int tests = markdown.IndexOf("## Tests", StringComparison.Ordinal);

        Assert.True(verdict >= 0 && provenance > verdict && tests > provenance,
            "the verdict and the provenance must both come before the results");
    }

    /// <summary>
    /// A report that needs a stylesheet from somewhere stops working the moment it is emailed.
    /// </summary>
    [Fact]
    public void The_html_report_pulls_nothing_in_from_outside()
    {
        string html = new HtmlReportWriter().Render(Sample());

        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("<link", html);
        Assert.DoesNotContain("src=", html);
    }

    [Fact]
    public void The_html_report_escapes_text_that_would_otherwise_be_markup()
    {
        string html = new HtmlReportWriter().Render(Sample());

        Assert.Contains("&lt;the wrong&gt;", html);
        Assert.Contains("&amp;", html);
        Assert.DoesNotContain("<the wrong>", html);
    }

    /// <summary>
    /// The page a person reads and the data a tool reads are the same file, so the two cannot
    /// disagree about what happened.
    /// </summary>
    [Fact]
    public void The_html_report_carries_the_machine_readable_run_inside_it()
    {
        string html = new HtmlReportWriter().Render(Sample());

        int start = html.IndexOf("id=\"saswb-run\">", StringComparison.Ordinal);
        Assert.True(start > 0, "the embedded log is missing");

        start = html.IndexOf('\n', start) + 1;
        int end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        string embedded = html[start..end].Replace("<\\/", "</");

        string[] lines = embedded.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (string liner in lines)
        {
            using JsonDocument document = JsonDocument.Parse(liner);
            Assert.True(document.RootElement.TryGetProperty("kind", out _));
        }

        Assert.Contains(lines, l => l.Contains("\"kind\":\"wire\"", StringComparison.Ordinal));
    }

    [Fact]
    public void The_html_report_says_what_the_run_was_carried_over_before_the_results()
    {
        string html = new HtmlReportWriter().Render(Sample());

        int provenance = html.IndexOf("carried over", StringComparison.Ordinal);
        int tests = html.IndexOf("class=\"tests\"", StringComparison.Ordinal);

        Assert.True(provenance > 0 && tests > provenance);
        Assert.Contains("unverified", html);
    }

    [Fact]
    public void Writing_reports_produces_one_file_per_format()
    {
        string directory = Path.Combine(Path.GetTempPath(), "saswb-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            IReadOnlyList<string> written = Reports.Write(
                Sample(), directory, [ReportFormat.Html, ReportFormat.JUnit, ReportFormat.JsonLines]);

            Assert.Equal(3, written.Count);
            Assert.All(written, path => Assert.True(File.Exists(path), path));
            Assert.Contains(written, p => p.EndsWith("run.html", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
