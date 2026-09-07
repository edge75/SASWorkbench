using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Core.Reporting;

/// <summary>Which report to write.</summary>
public enum ReportFormat
{
    /// <summary>One self-contained page, with the event log embedded inside it.</summary>
    Html,

    /// <summary>The XML shape build servers already understand.</summary>
    JUnit,

    /// <summary>Markdown, for pasting into a ticket or a pull request.</summary>
    Markdown,

    /// <summary>One JSON object per line, for anything that wants to read the run.</summary>
    JsonLines,

    /// <summary>
    /// A log that explains itself: what each case is before it runs, and what every packet means.
    /// </summary>
    /// <remarks>
    /// A rendering of the record rather than a second capture, so the same log can be produced
    /// afterwards from a run written with <see cref="JsonLines"/> and will read the same.
    /// </remarks>
    Extended,
}

/// <summary>Writes a run report in one format.</summary>
public interface IReportWriter
{
    /// <summary>Which format this writes.</summary>
    ReportFormat Format { get; }

    /// <summary>The extension the file should have.</summary>
    string Extension { get; }

    /// <summary>Renders the report.</summary>
    string Render(RunReport report);
}

/// <summary>Finds a writer for a format, and parses the list a command line gives.</summary>
public static class Reports
{
    /// <summary>Every writer, by format.</summary>
    public static IReadOnlyDictionary<ReportFormat, IReportWriter> Writers { get; } =
        new Dictionary<ReportFormat, IReportWriter>
        {
            [ReportFormat.Html] = new HtmlReportWriter(),
            [ReportFormat.JUnit] = new JUnitReportWriter(),
            [ReportFormat.Markdown] = new MarkdownReportWriter(),
            [ReportFormat.JsonLines] = new JsonLinesReportWriter(),
            [ReportFormat.Extended] = new ExtendedLogWriter(),
        };

    /// <summary>
    /// Reads a comma-separated list of format names, as a command line gives them.
    /// </summary>
    public static bool TryParse(string? text, out ReportFormat[] formats, out string? unknown)
    {
        unknown = null;
        var parsed = new List<ReportFormat>();

        foreach (string name in (text ?? string.Empty)
                 .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            ReportFormat? format = name.ToLowerInvariant() switch
            {
                "html" => ReportFormat.Html,
                "junit" or "xml" => ReportFormat.JUnit,
                "md" or "markdown" => ReportFormat.Markdown,
                "jsonl" or "json" => ReportFormat.JsonLines,
                "extended" or "explain" => ReportFormat.Extended,
                _ => null,
            };

            if (format is null)
            {
                unknown = name;
                formats = [];
                return false;
            }

            if (!parsed.Contains(format.Value))
                parsed.Add(format.Value);
        }

        formats = parsed.ToArray();
        return true;
    }

    /// <summary>Every format name a command line accepts, for a help line.</summary>
    public static string Names => "html, junit, md, jsonl, extended";

    /// <summary>
    /// Writes each requested report into <paramref name="directory"/>, named after the run.
    /// </summary>
    /// <returns>The files that were written.</returns>
    public static IReadOnlyList<string> Write(
        RunReport report,
        string directory,
        IEnumerable<ReportFormat> formats,
        string baseName = "run")
    {
        Directory.CreateDirectory(directory);
        var written = new List<string>();

        foreach (ReportFormat format in formats)
        {
            IReportWriter writer = Writers[format];
            string path = Path.Combine(directory, baseName + writer.Extension);
            File.WriteAllText(path, writer.Render(report));
            written.Add(path);
        }

        return written;
    }
}
