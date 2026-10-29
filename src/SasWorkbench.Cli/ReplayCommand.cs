using System.Globalization;
using System.Net;
using SasWorkbench.Core.Captures;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Replay;
using SasWorkbench.Core.Reporting;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transactions;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;
using SasWorkbench.Egm;

namespace SasWorkbench.Cli;

/// <summary>
/// Sends a recorded session's host messages to a machine and says what the differences mean.
/// </summary>
/// <remarks>
/// <para>
/// The only expectation in this project that somebody else wrote. Every other case says a machine
/// ought to do something because we decided it ought to; this one says a machine did something,
/// once, and asks whether the machine in front of it does the same.
/// </para>
/// <para>
/// A bare diff of that would be worthless — two machines are never in the same configuration and
/// have never done the same things — so the whole command is about the classification. Only the
/// rows nothing explains make it red.
/// </para>
/// </remarks>
internal static class ReplayCommand
{
    public static int Execute(CommandLine line)
    {
        if (line.Flag("causes"))
        {
            PrintCauses();
            return 0;
        }

        if (line.Arguments.Count == 0)
        {
            Console.Error.WriteLine(
                "error: which recording? Expected: sasctl replay <run.jsonl or capture> "
                + "[--tcp <host:port> | --serial <port> | --loopback] [--address <hh>] "
                + "[--limit <n>] [--out <dir>]");
            return 2;
        }

        // A directory of captures is read as one recording. The replay groups by request
        // anyway, so thirty-two traces of the same machine produce one row per distinct
        // question rather than thirty-two accounts of the same one.
        var paths = new List<string>();
        foreach (string argument in line.Arguments)
        {
            if (File.Exists(argument))
            {
                paths.Add(argument);
                continue;
            }

            IReadOnlyList<string> found = CaptureImporter.Discover(argument);
            if (found.Count == 0)
            {
                Console.Error.WriteLine(
                    $"error: '{argument}' is neither a file nor a directory holding a capture "
                    + "this build reads.");
                return 2;
            }

            paths.AddRange(found);
        }

        string path = paths.Count == 1 ? paths[0] : $"{paths.Count} recording(s) under {line.Arguments[0]}";

        if (!TryReadLimit(line, "limit", out int? limit)
            || !TryReadLimit(line, "packets", out int? packets))
        {
            return 2;
        }

        // Read before the link opens: a mistyped report name should stop the run rather than be
        // discovered after a machine has been polled for two minutes.
        string directory = line.Value("out") ?? Path.Combine("runs", Stamp() + "-replay");
        if (!Reports.TryParse(line.Value("report") ?? "jsonl,extended",
                out ReportFormat[] formats, out string? unknown))
        {
            Console.Error.WriteLine(
                $"error: '{unknown}' is not a report this build writes. Available: {Reports.Names}");
            return 2;
        }

        string? onlyTest = line.Value("test");
        var records = new List<WireRecord>();
        var sources = new List<string>();
        foreach (string file in paths)
        {
            if (!TryRead(file, packets, onlyTest, out IReadOnlyList<WireRecord> read,
                    out string source))
            {
                return 2;
            }

            records.AddRange(read);
            if (!sources.Contains(source))
                sources.Add(source);
        }

        if (!TryTransport(line, out ISasTransport? transport, out SasAddress address,
                out IDisposable? owned))
        {
            return 2;
        }

        TransactionSplit recorded = TransactionSplitter.Split(records);
        int replayable = ReplayRunner.Group(recorded).Count;

        Console.WriteLine($"REPLAYING {path}");
        Console.WriteLine($"  read as      {string.Join(", ", sources)}");
        Console.WriteLine($"  holds        {recorded.Records:N0} packet(s), "
                          + $"{recorded.Exchanges.Count:N0} exchange(s)");
        Console.WriteLine($"  of those     {replayable:N0} distinct request(s) to send");
        Console.WriteLine($"  polled       {string.Join(", ", recorded.Addresses.Select(a => $"${a.Value:X2}"))}"
                          + (recorded.Addresses.Count > 1
                              ? "  - more than one machine, and everything here goes to the one below"
                              : string.Empty));
        Console.WriteLine($"  against      the machine at ${address.Value:X2} over "
                          + $"{transport!.Description}");
        Console.WriteLine();
        Console.WriteLine("  Nothing that changes the machine is sent. Every such command is");
        Console.WriteLine("  withheld by name with what it would have changed, out of the same plan");
        Console.WriteLine("  the catalogue sweep uses. General polls are not replayed either: what");
        Console.WriteLine("  one returns is whichever exception that machine had queued.");
        Console.WriteLine();
        Console.WriteLine("  Every request is sent twice, so a field that moves on its own is");
        Console.WriteLine("  measured rather than assumed.");
        Console.WriteLine();

        var options = new PollEngineOptions
        {
            Address = address,
            PollInterval = TimeSpan.FromMilliseconds(200),
            WireLogCapacity = 262144,
        };

        try
        {
            using var engine = new PollEngine(transport, options);
            engine.Start();
            if (!WaitForLink(engine))
            {
                Console.Error.WriteLine(engine.LinkFault is { } fault
                    ? $"error: {fault} Nothing was replayed."
                    : "error: the machine never answered a general poll, so nothing was replayed. "
                    + "No difference is reported, because a link that was never up says nothing "
                    + "about any command.");
                return 2;
            }

            int done = 0;
            var runner = new ReplayRunner(
                engine,
                new ReplayOptions(AskTwice: !line.Flag("once"), Limit: limit),
                finding =>
                {
                    done++;
                    Console.WriteLine($"  [{done,3}/{replayable}] {Row(finding)}");
                });

            ReplayResult result = runner.Run(recorded);
            engine.Stop();

            Console.WriteLine();
            PrintResult(result);

            RunReport report = ToReport(result, engine, transport, path,
                string.Join(", ", sources));
            Console.WriteLine();
            foreach (string written in Reports.Write(report, directory, formats))
                Console.WriteLine($"wrote {written}");

            return report.ExitCode;
        }
        finally
        {
            owned?.Dispose();
        }
    }

    private static string Row(ReplayFinding finding)
    {
        string name = SasCatalogue.LongPolls.TryGetValue(finding.Command ?? 0, out SasLongPollInfo? info)
            ? info.Description
            : "not in the catalogue";

        return $"${finding.Command ?? 0:X2} {name,-46} x{finding.Occurrences,-6:N0} "
               + $"{finding.Cause,-14} {finding.Explanation}";
    }

    private static void PrintResult(ReplayResult result)
    {
        Console.WriteLine("WHAT THE DIFFERENCES MEAN");
        foreach (ReplayCause cause in Enum.GetValues<ReplayCause>())
        {
            int count = result.Count(cause);
            Console.WriteLine($"  {Label(cause),-42} {count,4}");
        }

        Console.WriteLine();
        Console.WriteLine($"  {result.Polls:N0} general or synchronisation poll(s) in the recording "
                          + "were not replayed.");

        IReadOnlyList<ReplayFinding> withheld = [.. result.Findings.Where(f =>
            f.Cause is ReplayCause.NotReplayed or ReplayCause.NotComparable)];
        if (withheld.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("WHAT PRODUCED NO COMPARISON, AND WHY");
            foreach (ReplayFinding finding in withheld)
                Console.WriteLine($"  ${finding.Command ?? 0:X2}  {finding.Explanation}");
        }

        if (result.Disagreements.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("WHAT THE TWO MACHINES ARE CONFIGURED TO BE, WHERE THEY DIFFER");
            foreach ((string field, (string recorded, string live)) in result.Disagreements
                         .OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {field,-24} recorded {recorded,-16} answered {live}");
            }

            Console.WriteLine("  Measured from the replies themselves. It is what lets a silence on");
            Console.WriteLine("  a command the library gates be read as a configuration rather than");
            Console.WriteLine("  as something nothing explains.");
        }

        var moved = result.Findings
            .SelectMany(f => f.MovedOnTheMachine)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (moved.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("MEASURED TO MOVE ON THE MACHINE, BETWEEN TWO READS");
            Console.WriteLine("  " + string.Join(", ", moved));
            Console.WriteLine("  These are state whatever any table here says about them.");
        }

        var unclassified = result.Findings
            .SelectMany(f => f.Unclassified)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (unclassified.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("FIELDS THIS BUILD CLASSIFIES NEITHER WAY");
            Console.WriteLine("  " + string.Join(", ", unclassified));
            Console.WriteLine("  Each one is a row this build calls unexplained. Some are defects");
            Console.WriteLine("  and some are gaps in the table; nothing here can tell which.");
        }

        if (result.Unexplained.Any())
        {
            Console.WriteLine();
            Console.WriteLine("NOTHING HERE EXPLAINS THESE");
            foreach (ReplayFinding finding in result.Unexplained)
            {
                Console.WriteLine($"  ${finding.Command ?? 0:X2}  {finding.Explanation}");
                Console.WriteLine($"        recorded  {Hex(finding.RecordedReply)}");
                Console.WriteLine($"        answered  {Hex(finding.LiveReply)}");
                foreach (ReplayFieldDiff field in finding.Fields
                             .Where(f => f.Cause.Cause == ReplayCause.Defect))
                {
                    Console.WriteLine($"        {field}");
                }
            }
        }
    }

    private static void PrintCauses()
    {
        Console.WriteLine("WHAT A REPLAY CAN CONCLUDE FROM A DIFFERENCE");
        Console.WriteLine();
        Console.WriteLine("  A recording and a machine almost never agree byte for byte, so a bare");
        Console.WriteLine("  diff is a wall of red with one real finding somewhere in it. Every");
        Console.WriteLine("  difference is therefore classified, and the rules are applied in order");
        Console.WriteLine("  so the strongest evidence wins.");
        Console.WriteLine();
        Console.WriteLine("  AGREES         the same reply, byte for byte after the echoed address.");
        Console.WriteLine("                 The address is excluded because the replay re-addresses");
        Console.WriteLine("                 every message to the machine in front of it.");
        Console.WriteLine();
        Console.WriteLine("  CONFIGURATION  a machine in another configuration. Either one side");
        Console.WriteLine("                 refused the command — which is the machine saying it");
        Console.WriteLine("                 will not do this as it stands — or every field that");
        Console.WriteLine("                 differs is what the machine is and how it was set up.");
        Console.WriteLine();
        Console.WriteLine("  REVISION       a machine of another revision. Either the machine");
        Console.WriteLine("                 library has no handler for the command at all, or the");
        Console.WriteLine("                 reply lengths differ and exactly one of them is the");
        Console.WriteLine("                 length that library's own handler writes.");
        Console.WriteLine();
        Console.WriteLine("  STATE          the same kind of machine, having lived a different life.");
        Console.WriteLine("                 The fields that differ are meters, or the machine's");
        Console.WriteLine("                 current condition, or were measured to move between two");
        Console.WriteLine("                 reads of one machine.");
        Console.WriteLine();
        Console.WriteLine("  DEFECT         nothing here explains the difference: a defect in one of");
        Console.WriteLine("                 the two machines, or a gap in this build's classifying");
        Console.WriteLine("                 of these fields. The only cause that makes a replay red,");
        Console.WriteLine("                 and every one of them needs a person.");
        Console.WriteLine();
        Console.WriteLine("  NOT COMPARABLE the request was sent and the recording holds no answer");
        Console.WriteLine("                 to compare it against. Silence in a file is not the");
        Console.WriteLine("                 same as a machine having answered nothing: the");
        Console.WriteLine("                 toolkit's tracer is filtered and holds answers to");
        Console.WriteLine("                 polls it never wrote down, and its host polls a");
        Console.WriteLine("                 machine at $7F that never answers.");
        Console.WriteLine();
        Console.WriteLine("  NOT REPLAYED   the request was not sent. A command that changes the");
        Console.WriteLine("                 machine, or a recorded message whose own check sequence");
        Console.WriteLine("                 does not check out.");
        Console.WriteLine();
        Console.WriteLine($"  This build classifies {ReplayFields.Names.Count} field names by hand — "
                          + $"{ReplayFields.CountOf(ReplayCause.Configuration)} as configuration and "
                          + $"{ReplayFields.CountOf(ReplayCause.State)} as state.");
        Console.WriteLine("  Meters are not among them: a meter is marked as one in the layout, which");
        Console.WriteLine("  is checked against the library's own handlers by a test.");
    }

    private static string Label(ReplayCause cause) => cause switch
    {
        ReplayCause.Agrees => "agrees",
        ReplayCause.Configuration => "a machine in another configuration",
        ReplayCause.Revision => "a machine of another revision",
        ReplayCause.State => "the same machine, a different life",
        ReplayCause.Defect => "nothing here explains it",
        ReplayCause.NotComparable => "nothing in the recording to compare against",
        _ => "not replayed",
    };

    private static string Hex(byte[] bytes) =>
        bytes.Length == 0 ? "(nothing)" : Convert.ToHexString(bytes);

    /// <summary>
    /// A replay's rows as a run's tests.
    /// </summary>
    /// <remarks>
    /// The verdicts are the point of the whole command. A difference with a legitimate cause is a
    /// pass — a machine that answers differently for a reason the recording gives no grounds to
    /// complain about must not read as red — and only a difference nothing explains fails. A
    /// request that was not sent is a skip with the reason on it, so a replay of a capture that is
    /// mostly setters comes back honestly as a run that mostly tested nothing.
    /// </remarks>
    private static RunReport ToReport(
        ReplayResult result, PollEngine engine, ISasTransport transport,
        string path, string source)
    {
        var provenance = new RunProvenance(
            transport.Description, transport.Capabilities.Fidelity, StimulusBackend.None,
            $"Replay of {source} at {path}. The expectation is another tool's recording of another "
            + "machine, so a difference is classified rather than failed: only a difference "
            + "nothing here explains is a finding.")
        {
            // A single-host command polls one machine on one channel, so the arrangement the
            // record needs is that one: every category reaches the only host there is. Marked as
            // not read, because nothing here asked the machine what its own map is.
            Channels = SasChannelPlan.Only(engine.Channel, engine.Address).Describe(),
        };

        IReadOnlyList<WireRecord> wire = engine.WireLog.Snapshot();
        var started = DateTimeOffset.UtcNow;
        var tests = new List<TestOutcome>(result.Findings.Count);

        foreach (ReplayFinding finding in result.Findings)
        {
            (Verdict verdict, string? skip) = finding.Cause switch
            {
                ReplayCause.NotReplayed or ReplayCause.NotComparable =>
                    (Verdict.Skip, finding.Explanation),
                ReplayCause.Defect => (Verdict.Fail, (string?)null),
                _ => (Verdict.Pass, null),
            };

            var steps = new List<StepResult>
            {
                new(Origin, $"replay ${finding.Command ?? 0:X2}", verdict,
                    Expected: Hex(finding.RecordedReply),
                    Actual: Hex(finding.LiveReply),
                    Reason: $"{finding.Cause}: {finding.Explanation}"),
            };

            steps.AddRange(finding.Fields.Select(field => new StepResult(
                Origin, field.Path,
                field.Cause.Cause == ReplayCause.Defect ? Verdict.Fail : Verdict.Pass,
                Expected: field.Recorded, Actual: field.Live,
                Reason: $"{field.Cause.Cause}: {field.Cause.Why}")));

            tests.Add(new TestOutcome
            {
                Name = $"replay ${finding.Command ?? 0:X2} x{finding.Occurrences}",
                Location = Origin,
                Verdict = verdict,
                SkipReason = skip,
                Steps = steps,
                StartedAt = started,
                Provenance = provenance,
                Wire = WireFor(wire, finding.Command),
            });
        }

        return new RunReport
        {
            Tests = tests,
            StartedAt = started,
            Duration = engine.Elapsed,
            Provenance = [provenance],
            Capabilities = [],
        };
    }

    /// <summary>
    /// The wire records of one command's turn, attributed by the note the engine wrote when it
    /// sent the poll rather than by position — the same rule the poll engine itself uses.
    /// </summary>
    private static IReadOnlyList<WireRecord> WireFor(IReadOnlyList<WireRecord> wire, byte? command)
    {
        if (command is not { } asked)
            return [];

        var kept = new List<WireRecord>();
        bool taking = false;
        string marker = $"replay ${asked:X2} ";

        foreach (WireRecord record in wire)
        {
            if (record.Direction == WireDirection.Transmit)
                taking = record.Note?.Contains(marker, StringComparison.Ordinal) == true;

            if (taking)
                kept.Add(record);
        }

        return kept;
    }

    /// <summary>
    /// Reads a recording, optionally only the cases of it whose name carries a given word.
    /// </summary>
    /// <remarks>
    /// The filter earns its place on a suite. A run of a whole suite is one file holding a machine
    /// that was reconfigured between cases, so its features reply may describe the machine in one
    /// case and not the one that answered in another — and a replay reading it as one recording
    /// then cannot explain a silence it would have explained per case. Replaying one case at a
    /// time is the fix, and it is a flag rather than a rewrite of the classifier because the
    /// limitation is in the recording, not in the reading of it.
    /// </remarks>
    private static bool TryRead(
        string path, int? packets, string? onlyTest,
        out IReadOnlyList<WireRecord> records, out string source)
    {
        records = [];
        source = string.Empty;

        if (CaptureImporter.Detect(path) is { } format)
        {
            Capture capture = CaptureImporter.ReadFile(path, packets);
            source = format switch
            {
                CaptureFormat.GsaTrace => "the conformance toolkit's trace",
                CaptureFormat.GsaHostLog => "the conformance toolkit's session log",
                _ => "the legacy tester's serial log",
            };

            records = Cap(capture.ToWire(), packets);
            return true;
        }

        JsonLinesReader.Result read = JsonLinesReader.ReadFile(path);
        foreach (string problem in read.Problems)
            Console.Error.WriteLine($"warning: {problem}");

        if (read.IsEmpty)
        {
            Console.Error.WriteLine(
                $"error: '{path}' is neither a capture this build reads nor a run written with "
                + "--report jsonl.");
            return false;
        }

        IEnumerable<TestOutcome> tests = read.Report.Tests;
        if (onlyTest is not null)
        {
            tests = tests.Where(test =>
                test.Name.Contains(onlyTest, StringComparison.OrdinalIgnoreCase));
            if (!tests.Any())
            {
                Console.Error.WriteLine(
                    $"error: no case in '{path}' has '{onlyTest}' in its name. Nothing was "
                    + "replayed, which is not a pass.");
                return false;
            }
        }

        source = onlyTest is null
            ? "a run written by this build"
            : $"the cases of a run written by this build whose name carries '{onlyTest}'";
        records = Cap([.. tests.SelectMany(test => test.Wire)], packets);
        return true;
    }

    private static IReadOnlyList<WireRecord> Cap(IReadOnlyList<WireRecord> records, int? limit) =>
        limit is { } count && records.Count > count ? [.. records.Take(count)] : records;

    private static bool TryReadLimit(CommandLine line, string name, out int? limit)
    {
        limit = null;
        if (line.Value(name) is not { } asked)
            return true;

        if (!int.TryParse(asked, CultureInfo.InvariantCulture, out int count) || count <= 0)
        {
            Console.Error.WriteLine($"error: '--{name} {asked}' is not a positive number.");
            return false;
        }

        limit = count;
        return true;
    }

    /// <summary>
    /// Opens the link the replay runs over, including the software machine in this process.
    /// </summary>
    /// <remarks>
    /// The software peer is offered because it is the honest first target: a replay against it says
    /// whether this build's own machine answers a real host's traffic the way a real machine did,
    /// and it costs nothing to run. What it proves is bounded in the same way every loopback run is
    /// — both ends are our own code — and the provenance on the report says so.
    /// </remarks>
    private static bool TryTransport(
        CommandLine line, out ISasTransport? transport, out SasAddress address,
        out IDisposable? owned)
    {
        transport = null;
        owned = null;
        address = SasAddress.Of(1);

        string addressText = line.Value("address") ?? "01";
        if (!SasAddress.TryParse(addressText, out address) || address.IsSync)
        {
            Console.Error.WriteLine(
                $"error: '{addressText}' is not a machine address. Expected 01 to 7F.");
            return false;
        }

        string? serial = line.Value("serial");
        string? tcp = line.Value("tcp");
        bool loopback = line.Flag("loopback");
        if ((serial is null ? 0 : 1) + (tcp is null ? 0 : 1) + (loopback ? 1 : 0) > 1)
        {
            Console.Error.WriteLine("error: choose one of --tcp, --serial or --loopback.");
            return false;
        }

        if (loopback)
        {
            var bus = new LoopbackBus();
            var machine = new FakeEgm(bus.Egm, new FakeEgmOptions { Address = address });
            machine.Start();
            transport = bus.Host;
            owned = new Both(machine, bus);
            return true;
        }

        if (serial is not null)
        {
            transport = new SerialTransport(new SerialTransportOptions(serial));
            return true;
        }

        string endpointText = tcp ?? $"127.0.0.1:{TcpTransportOptions.DefaultPort}";
        if (!TcpTransportOptions.TryParseEndpoint(endpointText, out IPEndPoint endpoint))
        {
            Console.Error.WriteLine(
                $"error: '{endpointText}' is not an endpoint. Expected something like 127.0.0.1:9760.");
            return false;
        }

        TcpRole role = line.Flag("dial") ? TcpRole.Connect : TcpRole.Listen;
        transport = new TcpTransport(new TcpTransportOptions(endpoint, role));
        return true;
    }

    private static bool WaitForLink(PollEngine engine)
    {
        for (int i = 0; i < 100 && engine.State != LinkState.Up && engine.LinkFault is null; i++)
            Thread.Sleep(100);

        return engine.State == LinkState.Up;
    }

    private static SourceLocation Origin { get; } = new("replay", 0);

    private static string Stamp() =>
        DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    private sealed class Both(params IDisposable[] parts) : IDisposable
    {
        public void Dispose()
        {
            foreach (IDisposable part in parts)
                part.Dispose();
        }
    }
}
