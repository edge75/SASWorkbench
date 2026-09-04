using System.Text;
using System.Xml;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Core.Reporting;

/// <summary>Writes the run in the XML shape build servers already understand.</summary>
/// <remarks>
/// A skip is written as <c>skipped</c> with its reason, never as a pass. Build servers show the
/// two differently, which is the whole point: a suite that could not run half its tests must not
/// look like one that ran them all.
/// </remarks>
public sealed class JUnitReportWriter : IReportWriter
{
    public ReportFormat Format => ReportFormat.JUnit;

    public string Extension => ".junit.xml";

    public string Render(RunReport report)
    {
        var builder = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false,
        };

        using var writer = XmlWriter.Create(builder, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("testsuites");
        writer.WriteAttributeString("name", "SasWorkbench");
        writer.WriteAttributeString("tests", report.Tests.Count.ToString());
        writer.WriteAttributeString("failures", report.Failed.ToString());
        writer.WriteAttributeString("errors", report.Errored.ToString());
        writer.WriteAttributeString("skipped", report.Skipped.ToString());
        writer.WriteAttributeString("time", Seconds(report.Duration));
        writer.WriteAttributeString("timestamp", report.StartedAt.ToString("o"));

        foreach (IGrouping<string, TestOutcome> suite in report.Tests.GroupBy(t => t.Location.File))
        {
            writer.WriteStartElement("testsuite");
            writer.WriteAttributeString("name", suite.Key);
            writer.WriteAttributeString("tests", suite.Count().ToString());
            writer.WriteAttributeString("failures",
                suite.Count(t => t.Verdict == Verdict.Fail).ToString());
            writer.WriteAttributeString("errors",
                suite.Count(t => t.Verdict == Verdict.Error).ToString());
            writer.WriteAttributeString("skipped",
                suite.Count(t => t.Verdict == Verdict.Skip).ToString());

            // What the run was carried over belongs in the report, so a green suite on an
            // unmeasured link cannot be mistaken for a green suite on a measured one.
            if (report.Provenance.Count > 0)
            {
                writer.WriteStartElement("properties");
                foreach (RunProvenance provenance in report.Provenance)
                {
                    WriteProperty(writer, "transport", provenance.Transport);
                    WriteProperty(writer, "fidelity", provenance.Fidelity.ToString());
                    WriteProperty(writer, "stimulus", provenance.Stimulus.ToString());
                    if (provenance.Caveat is not null)
                        WriteProperty(writer, "caveat", provenance.Caveat);
                }

                writer.WriteEndElement();
            }

            foreach (TestOutcome test in suite)
                WriteTest(writer, test);

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
        writer.Flush();

        return builder.ToString();
    }

    private static void WriteTest(XmlWriter writer, TestOutcome test)
    {
        writer.WriteStartElement("testcase");
        writer.WriteAttributeString("name", test.Name);
        writer.WriteAttributeString("classname", test.Location.File);
        writer.WriteAttributeString("time", Seconds(test.Duration));

        switch (test.Verdict)
        {
            case Verdict.Fail:
                foreach (StepResult step in test.Steps.Where(s => s.Verdict == Verdict.Fail))
                {
                    writer.WriteStartElement("failure");
                    writer.WriteAttributeString("message",
                        $"{step.Location}: {step.Description}");
                    writer.WriteString(
                        $"expected {step.Expected}\nactual   {step.Actual}"
                        + (step.Reason is null ? string.Empty : $"\n{step.Reason}"));
                    writer.WriteEndElement();
                }

                break;

            case Verdict.Error:
                writer.WriteStartElement("error");
                writer.WriteAttributeString("message", test.SkipReason ?? "the test could not run");
                writer.WriteString(Detail(test));
                writer.WriteEndElement();
                break;

            case Verdict.Skip:
                writer.WriteStartElement("skipped");
                writer.WriteAttributeString("message",
                    test.SkipReason ?? "the environment could not run this test");
                writer.WriteEndElement();
                break;
        }

        string detail = Detail(test);
        if (detail.Length > 0)
        {
            writer.WriteStartElement("system-out");
            writer.WriteCData(detail);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteProperty(XmlWriter writer, string name, string value)
    {
        writer.WriteStartElement("property");
        writer.WriteAttributeString("name", name);
        writer.WriteAttributeString("value", value);
        writer.WriteEndElement();
    }

    private static string Detail(TestOutcome test) =>
        string.Join('\n', test.Steps.Select(s => $"{s.Location} {s}"));

    private static string Seconds(TimeSpan duration) =>
        duration.TotalSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
}
