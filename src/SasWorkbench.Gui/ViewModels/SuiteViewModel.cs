using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Gui.Controls;

namespace SasWorkbench.Gui.ViewModels;

/// <summary>What a node in the suite tree is.</summary>
public enum SuiteNodeKind
{
    /// <summary>A directory: a GSA section, or a feature under regression.</summary>
    Folder,

    /// <summary>One script file.</summary>
    Document,

    /// <summary>One test inside a script.</summary>
    Test,
}

/// <summary>
/// The suite: what tests exist, what each of them came to, and what that verdict is worth.
/// </summary>
/// <remarks>
/// The tree is built by parsing, not by running, so the corpus is visible before anything is
/// connected — a test that has never run reads as unknown rather than as absent.
/// </remarks>
public sealed partial class SuiteViewModel : ObservableObject
{
    public SuiteViewModel()
    {
        Nodes = [];
    }

    /// <summary>The tree, one root per directory under the path.</summary>
    public ObservableCollection<SuiteNode> Nodes { get; }

    /// <summary>The test currently being looked at.</summary>
    [ObservableProperty]
    public partial SuiteNode? Selected { get; set; }

    /// <summary>Every step of the selected test, including the claims that held.</summary>
    public ObservableCollection<StepLine> Steps { get; } = [];

    /// <summary>The frames the selected test captured, so a failure can be read back.</summary>
    public ObservableCollection<FrameLine> Frames { get; } = [];

    /// <summary>How much the selected test's verdict is worth.</summary>
    [ObservableProperty]
    public partial string? Provenance { get; set; }

    /// <summary>Whether the selected test ran on a link that has been measured.</summary>
    [ObservableProperty]
    public partial bool IsProven { get; set; } = true;

    /// <summary>Whether anything is selected at all.</summary>
    public bool HasSelection => Selected is not null;

    /// <summary>
    /// The directory the tree was discovered from, so a location can be shown relative to it.
    /// An absolute path in a step row is noise: what a reader needs is which script and which line.
    /// </summary>
    private string _root = string.Empty;

    /// <summary>Replaces the tree from what is on disk, without running anything.</summary>
    public void Discover(string path)
    {
        Nodes.Clear();
        Selected = null;
        _root = Directory.Exists(path)
            ? Path.GetFullPath(path)
            : Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;

        IReadOnlyList<ScriptDocument> documents = ScriptLoader.Load(path);
        foreach (IGrouping<string, ScriptDocument> folder in documents
                     .GroupBy(d => Folder(d, path))
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var children = new ObservableCollection<SuiteNode>();
            foreach (ScriptDocument document in folder.OrderBy(d => d.Path, StringComparer.OrdinalIgnoreCase))
            {
                var tests = new ObservableCollection<SuiteNode>(
                    document.Tests.Select(t => new SuiteNode(SuiteNodeKind.Test, t.Name)
                    {
                        Tags = t.Tags,
                        Requires = [.. t.Requirements.Select(r => r.Key)],
                        Where = Shorten(t.Location.ToString()),
                    }));

                children.Add(
                    new SuiteNode(SuiteNodeKind.Document, Path.GetFileName(document.Path))
                    {
                        Children = tests,
                    });
            }

            Nodes.Add(new SuiteNode(SuiteNodeKind.Folder, folder.Key) { Children = children });
        }
    }

    /// <summary>Folds a run's results into the tree, matching tests by name.</summary>
    public void Apply(RunReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        Dictionary<string, TestOutcome> byName = report.Tests
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        foreach (SuiteNode test in Nodes.SelectMany(Descend).Where(n => n.Kind == SuiteNodeKind.Test))
            test.Outcome = byName.GetValueOrDefault(test.Name);

        foreach (SuiteNode folder in Nodes.SelectMany(Descend)
                     .Where(n => n.Kind != SuiteNodeKind.Test))
        {
            folder.Rollup();
        }

        if (Selected is not null)
            Inspect(Selected);
    }

    partial void OnSelectedChanged(SuiteNode? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        Inspect(value);
    }

    private void Inspect(SuiteNode? node)
    {
        Steps.Clear();
        Frames.Clear();
        Provenance = null;
        IsProven = true;

        if (node?.Outcome is not { } outcome)
            return;

        // Every step, including the ones that held: a view that shows only failures cannot tell a
        // claim that passed from a claim that was never made.
        foreach (StepResult step in outcome.Steps)
        {
            Steps.Add(new StepLine(
                Chip(step.Verdict),
                step.Description,
                Shorten(step.Location.ToString()),
                step.Reason ?? (step.Expected is null
                    ? string.Empty
                    : $"expected {step.Expected}, got {step.Actual}")));
        }

        foreach (WireRecord frame in outcome.Wire)
        {
            Frames.Add(new FrameLine(
                $"{frame.Elapsed.TotalMilliseconds,9:F0}",
                frame.Direction == WireDirection.Transmit ? "TX" : "RX",
                frame.ToHex(),
                frame.Note ?? string.Empty));
        }

        if (outcome.Provenance is { } how)
        {
            Provenance = how.ToString();
            IsProven = how.Fidelity == TransportFidelity.Proven;
        }
    }

    private string Shorten(string location) =>
        _root.Length > 0 && location.StartsWith(_root, StringComparison.OrdinalIgnoreCase)
            ? location[_root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : location;

    private static IEnumerable<SuiteNode> Descend(SuiteNode node) =>
        new[] { node }.Concat(node.Children.SelectMany(Descend));

    private static string Folder(ScriptDocument document, string root)
    {
        string? directory = Path.GetDirectoryName(document.Path);
        if (string.IsNullOrEmpty(directory))
            return ".";

        string relative = Path.GetFullPath(root) is var full && directory.StartsWith(full, StringComparison.OrdinalIgnoreCase)
            ? directory[full.Length..].Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : directory;

        return relative.Length == 0 ? Path.GetFileName(directory) : relative;
    }

    internal static ChipStatus Chip(Verdict verdict) => verdict switch
    {
        Verdict.Pass => ChipStatus.Pass,
        Verdict.Fail => ChipStatus.Fail,
        Verdict.Skip => ChipStatus.Skip,
        _ => ChipStatus.Error,
    };
}

/// <summary>One node of the suite tree.</summary>
public sealed partial class SuiteNode(SuiteNodeKind kind, string name) : ObservableObject
{
    public SuiteNodeKind Kind { get; } = kind;

    public string Name { get; } = name;

    public IReadOnlyList<SuiteNode> Children { get; init; } = [];

    /// <summary>The test's labels, carried from the script.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>What the test needs from the environment before it can run at all.</summary>
    public IReadOnlyList<string> Requires { get; init; } = [];

    /// <summary>The line the test is written on.</summary>
    public string Where { get; init; } = string.Empty;

    /// <summary>What this test came to, once it has run.</summary>
    [ObservableProperty]
    public partial TestOutcome? Outcome { get; set; }

    /// <summary>A test that has not run is unknown, which is not the same as absent.</summary>
    public ChipStatus Status => Outcome is { } outcome
        ? SuiteViewModel.Chip(outcome.Verdict)
        : ChipStatus.Unknown;

    /// <summary>The chip's word.</summary>
    public string StatusLabel => Outcome is null ? "NOT RUN" : Status.ToString().ToUpperInvariant();

    /// <summary>What a screen reader is told about this node's state.</summary>
    public string StatusReason => Outcome?.SkipReason
                                  ?? Outcome?.Steps.FirstOrDefault(s => s.Verdict == Verdict.Fail)?.Reason
                                  ?? (Outcome is null ? "this test has not been run" : Name);

    /// <summary>The tags and requirements, in one line.</summary>
    public string Labels => string.Join("  ",
        Tags.Select(t => $"#{t}").Concat(Requires.Select(r => $"needs {r}")));

    /// <summary>Whether there is anything in <see cref="Labels"/> to draw.</summary>
    public bool HasLabels => Labels.Length > 0;

    partial void OnOutcomeChanged(TestOutcome? value)
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusReason));
    }

    /// <summary>
    /// Gives a folder the worst verdict beneath it, so a red test cannot hide inside a green tree.
    /// </summary>
    public void Rollup()
    {
        TestOutcome[] outcomes = [.. Leaves().Select(n => n.Outcome).OfType<TestOutcome>()];
        if (outcomes.Length == 0)
        {
            Outcome = null;
            return;
        }

        Verdict worst = outcomes.Select(o => o.Verdict).Aggregate(Verdict.Pass, Worse);
        Outcome = new TestOutcome
        {
            Name = Name,
            Location = SourceLocation.None,
            Verdict = worst,
            Duration = TimeSpan.FromTicks(outcomes.Sum(o => o.Duration.Ticks)),
            SkipReason = worst == Verdict.Skip
                ? outcomes.FirstOrDefault(o => o.Verdict == Verdict.Skip)?.SkipReason
                : null,
        };
    }

    private IEnumerable<SuiteNode> Leaves() => Kind == SuiteNodeKind.Test
        ? [this]
        : Children.SelectMany(c => c.Leaves());

    private static Verdict Worse(Verdict a, Verdict b) => (a, b) switch
    {
        _ when a == Verdict.Error || b == Verdict.Error => Verdict.Error,
        _ when a == Verdict.Fail || b == Verdict.Fail => Verdict.Fail,
        _ when a == Verdict.Skip || b == Verdict.Skip => Verdict.Skip,
        _ => Verdict.Pass,
    };
}

/// <summary>One captured frame, shown beside the test that produced it.</summary>
public sealed record FrameLine(string At, string Direction, string Hex, string Note);
