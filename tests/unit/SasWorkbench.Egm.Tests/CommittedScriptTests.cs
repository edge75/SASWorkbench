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

        //  A wallet of its own, so a suite run here never writes into the one a real machine
        //  and the peripheral simulator share, and two runs cannot see each other's tickets.
        string wallet = Path.Combine(Path.GetTempPath(), "saswb-suite-" + Guid.NewGuid().ToString("N"));

        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(2),
            QuietGap = TimeSpan.FromMilliseconds(15),
            WalletDirectory = wallet,
        });

        RunReport report;
        try
        {
            report = runner.RunAll(documents, TestContext.Current.CancellationToken);
        }
        finally
        {
            if (Directory.Exists(wallet))
                Directory.Delete(wallet, recursive: true);
        }

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
    /// <remarks>
    /// The passing count is asserted as a floor rather than left to a summary line. A suite where
    /// everything skipped would satisfy every other assertion here and would look, from the
    /// outside, exactly like one that measures something.
    /// </remarks>
    [Fact]
    public void Every_validation_case_either_runs_or_says_what_it_needs()
    {
        IReadOnlyList<ScriptDocument> documents = ScriptLoader.Load(Suite("validation"));
        Assert.NotEmpty(documents);

        //  A wallet of its own, so a case that cashes out never writes into the one a real machine
        //  and the peripheral simulator share.
        string wallet = Path.Combine(Path.GetTempPath(), "saswb-validation-" + Guid.NewGuid().ToString("N"));

        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(2),
            QuietGap = TimeSpan.FromMilliseconds(15),
            WalletDirectory = wallet,
        });

        RunReport report;
        try
        {
            report = runner.RunAll(documents, TestContext.Current.CancellationToken);
        }
        finally
        {
            if (Directory.Exists(wallet))
                Directory.Delete(wallet, recursive: true);
        }

        Assert.Equal(0, report.Errored);

        //  As in the conformance tree: a case labelled a finding documents a machine library that
        //  does not do what the document asks, so it must still fail - and every other case must not.
        //  The GSA chapters brought the first such cases into this tree (6.6.1 to 6.6.4, 23.2).
        Assert.All(
            report.Tests.Where(t => t.Tags.Contains("finding")),
            test => Assert.True(test.Verdict is Verdict.Fail or Verdict.Skip,
                $"{test.Name} is labelled a finding but {test.Verdict}: take the label off"));
        Assert.True(
            report.Tests.Where(t => !t.Tags.Contains("finding")).All(t => t.Verdict != Verdict.Fail),
            Explain(report));
        Assert.All(report.Tests, test => Assert.True(
            test.Verdict != Verdict.Skip || !string.IsNullOrWhiteSpace(test.SkipReason),
            $"{test.Name} was skipped without saying why"));

        Assert.True(report.Passed >= 6,
            $"only {report.Passed} of {report.Tests.Count} validation cases ran; "
            + "the accounting denomination a case declares is what unblocks them");
    }

    /// <summary>
    /// A case that declares a unit the machine is not in force at is skipped naming what the
    /// machine is actually in, and is never failed - the machine is fine, the environment is not
    /// the one the case was written for. This is D19 applied to a unit rather than a capability.
    /// </summary>
    [Fact]
    public void A_case_declaring_the_wrong_unit_skips_naming_what_the_machine_is_in()
    {
        ScriptDocument document = ScriptParser.Parse("""
            test "a case written for ten centavos" {
              machine accounting denomination 10c
              host connect loopback address 01
              host poll every 200ms
              host expect link up within 5s
              host send lp 1F
            }
            """);

        //  A machine in force at five cents that ignores what the case declared, which is how
        //  every machine this build did not make behaves: the declaration configures nothing and
        //  is read back off the wire.
        EnvironmentFactory unconfigurable = (step, options, _, channels) =>
            SoftwareMachineEnvironment.For(
                new FakeEgmOptions { AccountingDenomination = 0x02 })(step, options, null);

        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = unconfigurable,
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(2),
            QuietGap = TimeSpan.FromMilliseconds(15),
        });

        RunReport report = runner.RunAll([document], TestContext.Current.CancellationToken);
        TestOutcome outcome = Assert.Single(report.Tests);

        Assert.Equal(Verdict.Skip, outcome.Verdict);
        Assert.Contains("5c", outcome.SkipReason);
        Assert.Contains("10c", outcome.SkipReason);
    }

    /// <summary>
    /// The declaration builds the software machine rather than merely checking it, which is what
    /// it has to do to be worth anything before the link opens.
    /// </summary>
    [Fact]
    public void A_declared_unit_is_the_one_the_machine_reports_and_counts_in()
    {
        ScriptDocument document = ScriptParser.Parse("""
            test "ten centavos" {
              machine accounting denomination 10c
              host connect loopback address 01
              host poll every 200ms
              host expect link up within 5s
              egm bill insert 1000
              host send lp 1F
              host expect field denomination of lp 1F == 3
              host send lp 6F data 0400000C00
              host expect field meters[0].value == 100
            }
            """);

        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(2),
            QuietGap = TimeSpan.FromMilliseconds(15),
        });

        RunReport report = runner.RunAll([document], TestContext.Current.CancellationToken);
        Assert.True(report.Verdict == Verdict.Pass, Explain(report));
    }

    /// <summary>The setter changes what the machine reports and what it counts in, from then on.</summary>
    [Fact]
    public void The_setter_changes_the_unit_for_what_is_counted_after_it()
    {
        ScriptDocument document = ScriptParser.Parse("""
            test "one cent, then ten centavos" {
              machine accounting denomination 1c
              host connect loopback address 01
              host poll every 200ms
              host expect link up within 5s

              host send lp 1F
              host expect field denomination of lp 1F == 1

              egm set accounting denomination 10c
              host send lp 1F
              host expect field denomination of lp 1F == 3

              egm bill insert 1000
              host send lp 6F data 0400000C00
              host expect field meters[0].value == 100
            }
            """);

        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(2),
            QuietGap = TimeSpan.FromMilliseconds(15),
        });

        RunReport report = runner.RunAll([document], TestContext.Current.CancellationToken);
        Assert.True(report.Verdict == Verdict.Pass, Explain(report));
    }

    /// <summary>
    /// The honesty check for the conformance tree. A case there either runs against the machine
    /// this suite can assemble, or says what it needs and skips; it never fails, because a case
    /// the environment cannot host is not evidence about the machine. The passing count is
    /// asserted too: a tree where everything skips proves nothing, and would look identical to
    /// one where the runner had quietly stopped executing anything.
    /// </summary>
    [Fact]
    public void Every_conformance_case_either_runs_or_says_what_it_needs()
    {
        IReadOnlyList<ScriptDocument> documents = ScriptLoader.Load(Suite("conformance"));

        string wallet = Path.Combine(Path.GetTempPath(), "saswb-conformance-" + Guid.NewGuid().ToString("N"));

        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(2),
            QuietGap = TimeSpan.FromMilliseconds(15),
            WalletDirectory = wallet,
        });

        RunReport report;
        try
        {
            report = runner.RunAll(documents, TestContext.Current.CancellationToken);
        }
        finally
        {
            if (Directory.Exists(wallet))
                Directory.Delete(wallet, recursive: true);
        }

        Assert.NotEmpty(report.Tests);
        Assert.Equal(0, report.Errored);
        Assert.True(report.Passed > 0, "no conformance case ran: " + Explain(report));

        //  A case labelled a finding is expected to fail: what it is about is a machine that does
        //  not do what a conformance document asks. Every other failure is a real one. Asserting
        //  that the labelled ones still fail is what stops the label outliving the finding — the
        //  day the machine library changes, this goes red and somebody comes and takes it off.
        foreach (TestOutcome test in report.Tests.Where(t => t.Tags.Contains("finding")))
        {
            Assert.Equal(Verdict.Fail, test.Verdict);
        }

        Assert.All(
            report.Tests.Where(t => !t.Tags.Contains("finding")),
            test => Assert.NotEqual(Verdict.Fail, test.Verdict));

        Assert.All(report.Tests, test => Assert.True(
            test.Verdict != Verdict.Skip || !string.IsNullOrWhiteSpace(test.SkipReason),
            $"{test.Name} was skipped without saying why"));
    }
}
