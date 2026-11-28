using SasWorkbench.Core.Reporting;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// A result from a machine that is not the measured build says so wherever it is read (D64), and a progressive win
/// keeps whether its bet and win were written, so a machine that cannot honour every one can tell.
/// </summary>
public class MachineBuildLabelTests
{
    private static readonly RunProvenance Scripted =
        new("tcp 127.0.0.1:9760", TransportFidelity.Unverified, StimulusBackend.Qpm, null)
        {
            MachineBuild = "scripted outcomes (dev build)",
        };

    [Fact]
    public void TheProvenanceLineNamesTheMachineBuild()
    {
        Assert.Contains("machine: scripted outcomes (dev build)", Scripted.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("machine:", (Scripted with { MachineBuild = null }).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheLabelSurvivesTheRecordAndEveryReport()
    {
        var report = new RunReport
        {
            Provenance = [Scripted],
            Tests = [new TestOutcome { Name = "t", Location = new SourceLocation("case.sas", 1), Verdict = Verdict.Pass }],
        };

        JsonLinesReader.Result read = JsonLinesReader.Read(
            new JsonLinesReportWriter().Render(report).Split('\n', StringSplitOptions.RemoveEmptyEntries));

        Assert.Empty(read.Problems);
        Assert.Equal("scripted outcomes (dev build)", Assert.Single(read.Report.Provenance).MachineBuild);
        Assert.Contains("scripted outcomes (dev build)", new MarkdownReportWriter().Render(report), StringComparison.Ordinal);
        Assert.Contains("scripted outcomes (dev build)", new HtmlReportWriter().Render(report), StringComparison.Ordinal);
        Assert.Contains("scripted outcomes (dev build)", new JUnitReportWriter().Render(report), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("egm progressive win level=1", false, false)]
    [InlineData("egm progressive win level=1 bet=2", true, false)]
    [InlineData("egm progressive win level=1 win=0", false, true)]
    public void AProgressiveWinKeepsWhetherItsBetAndWinWereWritten(string line, bool bet, bool win)
    {
        var step = (ProgressiveWinStep)ScriptParser.Parse(line).Tests[0].Steps[0];

        Assert.Equal(bet, step.BetStated);
        Assert.Equal(win, step.WinStated);
        Assert.Equal(win, step.Describe().Contains("win=0", StringComparison.Ordinal));
    }
}
