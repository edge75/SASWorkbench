using System.Xml.Linq;
using SasWorkbench.Core.Session;

namespace SasWorkbench.Gui.Tests;

/// <summary>
/// The extended log is reachable from the window as well as from the command line, and the four
/// timestamp modes are all offered.
/// </summary>
/// <remarks>
/// Read off the markup rather than by building the window, because constructing the view model
/// starts a dispatcher timer and enumerates serial ports. What matters here is what the markup
/// binds to: a button that produces the log and a place that shows it. Whether the text is right
/// is settled where the renderer is tested, since both surfaces call the same one.
/// </remarks>
public sealed class ExtendedLogSurfaceTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    [Fact]
    public void TheWindowHasAWayToExplainTheLastRun()
    {
        Assert.Contains(
            Bindings("Command"),
            command => command.Contains("ExplainRunCommand", StringComparison.Ordinal));
    }

    [Fact]
    public void TheWindowHasAPlaceToShowTheExplanation()
    {
        Assert.Contains(
            Bindings("Text"),
            text => text.Contains("ExtendedLog", StringComparison.Ordinal));
    }

    /// <summary>
    /// The explanation is about the whole run, so it must not be reachable only by selecting one
    /// case in the tree.
    /// </summary>
    [Fact]
    public void TheExplanationDoesNotDependOnATestBeingSelected()
    {
        XElement tabs = Suite()
            .Descendants(Avalonia + "TabControl")
            .Single(t => t.Descendants(Avalonia + "TabItem")
                .Any(i => (string?)i.Attribute("Header") == "Explained"));

        Assert.Equal("{Binding HasSuiteDetail}", (string?)tabs.Attribute("IsVisible"));
    }

    /// <summary>
    /// D28 names four timestamp modes and the selector offers whatever the enumeration holds, so
    /// the fourth appearing there is the fourth being built.
    /// </summary>
    [Fact]
    public void AllFourTimestampModesAreOffered()
    {
        Assert.Contains(TimestampMode.WallClock, Enum.GetValues<TimestampMode>());
        Assert.Equal(4, Enum.GetValues<TimestampMode>().Length);
    }

    private static IEnumerable<string> Bindings(string attribute) =>
        new DirectoryInfo(Gui())
            .GetFiles("*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.FullName.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => XDocument.Load(f.FullName).Descendants())
            .Select(e => (string?)e.Attribute(attribute))
            .Where(v => v is not null)
            .Select(v => v!);

    private static XElement Suite() =>
        XDocument.Load(Path.Combine(Gui(), "Views", "SuiteInstrument.axaml")).Root!;

    private static string Gui()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "SasWorkbench.Gui");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "could not find src/SasWorkbench.Gui above the test binaries");
    }
}
