using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The scripts committed to this repository, run as scripts. Anything that stops one of them
/// loading is a broken suite, and a broken suite is worth catching here rather than the next
/// time somebody runs it.
/// </summary>
public class CommittedScriptTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "tests", "conformance")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root from the test output.");
    }

    /// <summary>Every step that did not pass, so a failure here names the script line.</summary>
    private static string Explain(RunReport report) => string.Join(Environment.NewLine, report.Tests
        .SelectMany(t => t.Steps.Where(s => s.Verdict != Verdict.Pass)
            .Select(s => $"{t.Name}: {s.Location} {s}")));

    private static string Suite(params string[] parts) =>
        Path.Combine([RepoRoot(), "tests", .. parts]);

    [Fact]
    public void Every_committed_script_parses()
    {
        string[] files =
        [
            .. ScriptLoader.Discover(Suite("regression")),
            .. ScriptLoader.Discover(Suite("conformance")),
        ];

        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            ScriptDocument document = ScriptLoader.LoadFile(file);
            Assert.NotEmpty(document.Tests);
            Assert.All(document.Tests, test => Assert.NotEmpty(test.Steps));
        }
    }

    [Fact]
    public void The_regression_suite_passes_against_the_software_machine()
    {
        IReadOnlyList<ScriptDocument> documents = ScriptLoader.Load(Suite("regression"));

        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(),
            PollInterval = TimeSpan.FromMilliseconds(20),
            QuietGap = TimeSpan.FromMilliseconds(15),
        });

        RunReport report = runner.RunAll(documents, TestContext.Current.CancellationToken);

        Assert.True(report.Verdict == Verdict.Pass, Explain(report));
        Assert.Equal(0, report.Failed);
        Assert.Equal(0, report.Skipped);
    }

    /// <summary>
    /// The machine-validation suites are written from a document in full, including the cases this
    /// build cannot yet run, so most of them skip. What must hold is that every one of them skips
    /// for a reason that names what is missing: a case that errored, or that passed without the
    /// machine ever being configured, would both be lies about coverage.
    /// </summary>
    [Fact]
    public void Every_validation_case_either_runs_or_says_what_it_needs()
    {
        IReadOnlyList<ScriptDocument> documents = ScriptLoader.Load(Suite("validation"));
        Assert.NotEmpty(documents);

        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(),
            PollInterval = TimeSpan.FromMilliseconds(20),
            QuietGap = TimeSpan.FromMilliseconds(15),
        });

        RunReport report = runner.RunAll(documents, TestContext.Current.CancellationToken);

        Assert.Equal(0, report.Errored);
        Assert.All(report.Tests, test => Assert.True(
            test.Verdict != Verdict.Skip || !string.IsNullOrWhiteSpace(test.SkipReason),
            $"{test.Name} was skipped without saying why"));
    }

    /// <summary>
    /// This is the honesty check for the milestone. The conformance test asks for the validation
    /// configuration, which nothing here can provide yet, so it must report as skipped with that
    /// reason — not pass, not fail, and not quietly be missing from the suite.
    /// </summary>
    [Fact]
    public void The_conformance_test_reports_as_skipped_and_says_what_is_missing()
    {
        IReadOnlyList<ScriptDocument> documents = ScriptLoader.Load(Suite("conformance"));

        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(),
            PollInterval = TimeSpan.FromMilliseconds(20),
        });

        RunReport report = runner.RunAll(documents, TestContext.Current.CancellationToken);

        Assert.NotEmpty(report.Tests);
        Assert.Equal(0, report.Passed);
        Assert.Equal(0, report.Failed);
        Assert.All(report.Tests, test =>
        {
            Assert.Equal(Verdict.Skip, test.Verdict);
            Assert.False(string.IsNullOrWhiteSpace(test.SkipReason));
        });

        Assert.Equal(Verdict.Skip, report.Verdict);
        Assert.Equal(3, report.ExitCode);
    }
}
