using SasWorkbench.Core.Reporting;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;
using SasWorkbench.Egm;
using SasWorkbench.Qpm;

namespace SasWorkbench.Cli;

/// <summary>Runs scripts and writes the reports.</summary>
internal static class RunCommand
{
    public static int Execute(CommandLine line)
    {
        // Every option is read and checked before anything can stop early, so a stop never
        // reports one as unknown for having been skipped.
        if (!TryReadConnection(line, out ConnectionOverride connection, out string? connectionError))
        {
            Console.Error.WriteLine($"error: {connectionError}");
            return 2;
        }

        if (!TryBuildOptions(line, out ScriptRunnerOptions options, out string? optionError))
        {
            Console.Error.WriteLine($"error: {optionError}");
            return 2;
        }

        if (!Reports.TryParse(line.Value("report"), out ReportFormat[] formats, out string? unknown))
        {
            Console.Error.WriteLine($"error: '{unknown}' is not a report format. Expected {Reports.Names}.");
            return 2;
        }

        string? outDirectory = line.Value("out");
        if (outDirectory is not null && formats.Length == 0)
        {
            Console.Error.WriteLine("error: --out says where reports go, but no --report says which to write.");
            return 2;
        }

        string? only = line.Value("tag");
        string? without = line.Value("skip-tag");
        bool quiet = line.Flag("quiet");
        bool list = line.Flag("list");

        if (line.Arguments.Count == 0)
        {
            Console.Error.WriteLine("error: 'run' needs a script or a directory of scripts.");
            return 2;
        }

        IReadOnlyList<ScriptDocument> documents = line.Arguments
            .SelectMany(ScriptLoader.Load)
            .ToList();

        documents = Filter(only, without, connection.ApplyTo(documents));

        int tests = documents.Sum(d => d.Tests.Count);
        if (tests == 0)
        {
            Console.Error.WriteLine("error: no tests matched. Nothing was run, which is not a pass.");
            return 3;
        }

        if (list)
        {
            foreach (ScriptDocument document in documents)
            {
                Console.WriteLine(document.Path);
                foreach (ScriptTest test in document.Tests)
                {
                    string tags = test.Tags.Count == 0
                        ? string.Empty
                        : "  " + string.Join(' ', test.Tags.Select(t => "@" + t));
                    Console.WriteLine($"  {test.Name}{tags}");
                    foreach (Requirement requirement in test.Requirements)
                        Console.WriteLine($"      requires {requirement}");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"{tests} test(s) in {documents.Count} file(s)");
            return 0;
        }

        var runner = new ScriptRunner(options with
        {
            OnTestFinished = quiet ? null : Print,
        });

        RunReport report = runner.RunAll(documents);

        Console.WriteLine();
        foreach (RunProvenance provenance in report.Provenance)
            Console.WriteLine($"  over {provenance}");

        Console.WriteLine($"  {report.Verdict.ToString().ToUpperInvariant()}  {report.Summary()}");

        if (formats.Length > 0)
        {
            string directory = outDirectory
                               ?? Path.Combine("runs", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            IReadOnlyList<string> written = Reports.Write(report, directory, formats);

            Console.WriteLine();
            foreach (string path in written)
                Console.WriteLine($"  wrote {path}");
        }

        return report.ExitCode;
    }

    private static void Print(TestOutcome test)
    {
        Console.WriteLine(test.ToString());

        foreach (StepResult step in test.Steps.Where(s => s.Verdict is Verdict.Fail or Verdict.Error))
        {
            Console.WriteLine($"        {step.Location}  {step.Description}");
            if (step.Expected is not null)
                Console.WriteLine($"          expected  {step.Expected}");
            if (step.Actual is not null)
                Console.WriteLine($"          actual    {step.Actual}");
            if (step.Reason is not null)
                Console.WriteLine($"          {step.Reason}");
        }

        foreach (StepResult step in test.Steps.Where(s => s.Verdict == Verdict.Skip))
        {
            if (step.Reason is not null && test.SkipReason != step.Reason)
                Console.WriteLine($"        {step.Location}  skipped: {step.Reason}");
        }
    }

    private static bool TryBuildOptions(
        CommandLine line, out ScriptRunnerOptions options, out string? error)
    {
        error = null;
        options = new ScriptRunnerOptions();

        string backendName = line.Value("stimulus") ?? "none";
        if (!Enum.TryParse(backendName, ignoreCase: true, out StimulusBackend backend))
        {
            error = $"'{backendName}' is not a stimulus backend. Expected none, manual, software "
                    + "or qpm.";
            return false;
        }

        TimeSpan interval = TimeSpan.FromMilliseconds(200);
        string? poll = line.Value("poll");
        if (poll is not null)
        {
            try
            {
                interval = ScriptParser.ParseDuration(poll, SourceLocation.None);
            }
            catch (ScriptParseException parse)
            {
                error = parse.Reason;
                return false;
            }
        }

        TimeSpan askFor = ManualStimulusBackend.DefaultDeadline;
        string? deadline = line.Value("ask-for");
        if (deadline is not null)
        {
            try
            {
                askFor = ScriptParser.ParseDuration(deadline, SourceLocation.None);
            }
            catch (ScriptParseException parse)
            {
                error = parse.Reason;
                return false;
            }

            if (askFor <= TimeSpan.Zero)
            {
                error = "--ask-for needs a duration above zero: a manual step with no time to "
                        + "answer can only ever be skipped.";
                return false;
            }
        }

        //  How long to wait for the machine to dial in. Worth naming on the command line
        //  because a machine that takes half a minute to come up is a fact about the machine and
        //  not about the case, and a case skipped for it reads as a case that could not be run.
        TimeSpan peer = TimeSpan.FromSeconds(10);
        string? waitFor = line.Value("wait-for-machine");
        if (waitFor is not null)
        {
            try
            {
                peer = ScriptParser.ParseDuration(waitFor, SourceLocation.None);
            }
            catch (ScriptParseException parse)
            {
                error = parse.Reason;
                return false;
            }
        }

        // Read whether or not the peripheral driver was asked for, so that naming one of these
        // with the wrong backend is reported as the mistake it is rather than silently ignored.
        var peripherals = new QpmOptions(
            line.Value("qpm-shm"), line.Value("egm-resources"));

        if (backend != StimulusBackend.Qpm
            && (peripherals.RegionPath is not null || peripherals.ResourcesDirectory is not null))
        {
            error = "--qpm-shm and --egm-resources only mean something with --stimulus qpm.";
            return false;
        }

        options = new ScriptRunnerOptions
        {
            Stimulus = backend,
            PollInterval = interval,
            AskPerson = backend == StimulusBackend.Manual ? AskAtTheConsole : null,
            PromptDeadline = askFor,
            PeerTimeout = peer,
            RealTimeEventsAllowed = line.Flag("real-time-events"),
            StimulusOffer = backend == StimulusBackend.Qpm ? QpmEnvironment.Offer(peripherals) : null,

            // The software machine is only wired in when it was asked for. A run that quietly
            // substituted a fixture for the machine under test would be worthless.
            Environment = backend switch
            {
                StimulusBackend.Software => SoftwareMachineEnvironment.For(),
                StimulusBackend.Qpm => QpmEnvironment.For(peripherals),
                _ => DefaultEnvironment.Create,
            },
        };

        return true;
    }

    // One reader for the whole run, so a prompt that timed out cannot swallow the next answer.
    private static readonly LinePrompt ConsolePrompt = new(Console.In, Console.Out);

    /// <summary>Asks at the console, and gives up when nobody is there.</summary>
    private static PromptAnswer AskAtTheConsole(string prompt, TimeSpan deadline) =>
        ConsolePrompt.Ask(prompt, deadline);

    /// <summary>
    /// Reads <c>--tcp</c>, <c>--serial</c>, <c>--loopback</c>, <c>--address</c> and
    /// <c>--channel</c>, which are applied to every connect step so one suite can be pointed at a
    /// different machine without editing it.
    /// </summary>
    private static bool TryReadConnection(
        CommandLine line, out ConnectionOverride change, out string? error)
    {
        error = null;
        change = ConnectionOverride.None;

        string? tcp = line.Value("tcp");
        string? serial = line.Value("serial");
        bool loopback = line.Flag("loopback");
        string? address = line.Value("address");
        string? channel = line.Value("channel");

        int chosen = (tcp is null ? 0 : 1) + (serial is null ? 0 : 1) + (loopback ? 1 : 0);
        if (chosen > 1)
        {
            error = "choose one of --tcp, --serial or --loopback, not several.";
            return false;
        }

        if (chosen == 0 && address is null && channel is null)
            return true;

        (ScriptTransport? transport, string? target) = true switch
        {
            _ when tcp is not null => ((ScriptTransport?)ScriptTransport.Tcp, tcp),
            _ when serial is not null => (ScriptTransport.Serial, serial),
            _ when loopback => (ScriptTransport.Loopback, null),
            _ => (null, null),
        };

        SasAddress? overrideAddress = null;
        if (address is not null)
        {
            if (!SasAddress.TryParse(address, out SasAddress parsed))
            {
                error = $"'{address}' is not a poll address. Expected 00 to 7F.";
                return false;
            }

            overrideAddress = parsed;
        }

        SasChannel? overrideChannel = null;
        if (channel is not null)
        {
            if (!SasChannels.TryParse(channel, out SasChannel parsed))
            {
                error = $"'{channel}' is not a channel. Expected 1 to {SasChannels.Count}.";
                return false;
            }

            overrideChannel = parsed;
        }

        change = new ConnectionOverride(transport, target, overrideAddress, overrideChannel);
        return change.TryValidate(out error);
    }

    private static IReadOnlyList<ScriptDocument> Filter(
        string? only, string? without, IReadOnlyList<ScriptDocument> documents)
    {
        if (only is null && without is null)
            return documents;

        string? wanted = only?.TrimStart('@');
        string? unwanted = without?.TrimStart('@');

        return documents
            .Select(document => document with
            {
                Tests = document.Tests.Where(test =>
                    (wanted is null || test.Tags.Contains(wanted, StringComparer.OrdinalIgnoreCase))
                    && (unwanted is null
                        || !test.Tags.Contains(unwanted, StringComparer.OrdinalIgnoreCase)))
                    .ToList(),
            })
            .Where(document => document.Tests.Count > 0)
            .ToList();
    }
}
