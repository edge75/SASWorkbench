using SasWorkbench.Core.Reporting;

namespace SasWorkbench.Cli;

/// <summary>
/// Produces the extended log from a run that has already happened.
/// </summary>
/// <remarks>
/// The half of the extended log that decides its design. Because a log can be explained after the
/// fact, the explanation is a rendering of the record and never a second recording — so the raw
/// record stays the only artefact that has to be right, and an explanation that turns out wrong is
/// corrected and re-rendered rather than re-measured. This command and <c>run --report extended</c>
/// go through the same renderer, so the two cannot drift apart.
/// </remarks>
internal static class ExplainCommand
{
    public static int Execute(CommandLine line)
    {
        if (line.Arguments.Count == 0)
        {
            Console.Error.WriteLine(
                "error: which run? Expected: sasctl explain <report.jsonl> [--out <file>]");
            return 2;
        }

        string path = line.Arguments[0];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"error: there is no file at '{path}'.");
            return 2;
        }

        JsonLinesReader.Result read = JsonLinesReader.ReadFile(path);

        // Said before the log rather than after it: a reader who is about to trust a rendering is
        // entitled to know first what the record could not be read for.
        foreach (string problem in read.Problems)
            Console.Error.WriteLine($"warning: {problem}");

        if (read.IsEmpty)
        {
            Console.Error.WriteLine(
                $"error: {path} describes no tests. A run writes this file with --report jsonl.");
            return 2;
        }

        string log = new ExtendedLogWriter().Render(read.Report);

        if (line.Value("out") is { } output)
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(output, log);
            Console.WriteLine($"wrote {output}");
        }
        else
        {
            Console.Out.Write(log);
        }

        return read.Problems.Count == 0 ? 0 : 2;
    }
}
