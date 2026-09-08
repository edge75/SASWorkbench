using System.Net;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Reporting;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Sweep;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Cli;

/// <summary>
/// Asks a machine every command the catalogue holds that can be asked, and writes down what came
/// back.
/// </summary>
/// <remarks>
/// It asserts nothing, so like an import it comes back as a run in which nothing was tested. What
/// it produces is bytes from a real machine for commands no traffic here has ever carried, which
/// is the only thing that turns a handler reading into an established layout.
/// </remarks>
internal static class SweepCommand
{
    public static int Execute(CommandLine line)
    {
        if (line.Flag("list"))
        {
            PrintPlan();
            return 0;
        }

        if (!TryTransport(line, out ISasTransport? transport, out SasAddress address))
            return 2;

        var options = new PollEngineOptions
        {
            Address = address,
            PollInterval = TimeSpan.FromMilliseconds(200),
            WireLogCapacity = 65536,
        };

        using var engine = new PollEngine(transport!, options);
        Console.WriteLine($"Sweeping the catalogue against the machine at {address}.");
        Console.WriteLine("Nothing here asserts anything: it asks, and records what came back.");
        Console.WriteLine();

        engine.Start();
        if (!WaitForLink(engine))
        {
            Console.Error.WriteLine(
                "error: the machine never answered a general poll, so nothing was swept. "
                + "Nothing is reported as silent, because a link that was never up says nothing "
                + "about any command.");
            return 2;
        }

        int done = 0;
        int total = SweepPlan.All.Count;
        var sweep = new CatalogueSweep(engine, finding =>
        {
            done++;
            if (finding.Outcome != SweepOutcome.Withheld)
                Console.WriteLine($"  [{done,3}/{total}] {Row(finding)}");
        });

        if (line.Flag("framing"))
            return Framing(engine, line);

        IReadOnlyList<SweepFinding> findings = sweep.Run();
        engine.Stop();

        Console.WriteLine();
        PrintFindings(findings);

        string directory = line.Value("out") ?? Path.Combine("runs", Stamp() + "-sweep");
        if (!Reports.TryParse(line.Value("report") ?? "jsonl,extended",
                out ReportFormat[] formats, out string? unknown))
        {
            Console.Error.WriteLine(
                $"error: '{unknown}' is not a report this build writes. Available: {Reports.Names}");
            return 2;
        }

        RunReport report = ToReport(findings, engine, transport!);
        Console.WriteLine();
        foreach (string written in Reports.Write(report, directory, formats))
            Console.WriteLine($"wrote {written}");

        // A sweep proves nothing about this build, so it must not be able to look like a pass.
        return 3;
    }

    /// <summary>
    /// Measures the check-sequence rule on the wire instead of reading it out of a handler.
    /// </summary>
    /// <remarks>
    /// Deliberately sends two commands the ordinary sweep withholds, because they are the two the
    /// rule was found on. It sends shutdown both ways and startup both ways, and it sends startup
    /// last so the machine is left playing whatever the run found.
    /// </remarks>
    private static int Framing(PollEngine engine, CommandLine line)
    {
        Console.WriteLine("Measuring the check-sequence rule on the wire.");
        Console.WriteLine("This sends $01 (stop play) and $02 (start play), which the ordinary");
        Console.WriteLine("sweep withholds. $02 goes last, so the machine is left playing.");
        Console.WriteLine();

        (byte Command, bool CheckSequence)[] probes =
        [
            (0x01, false),
            (0x01, true),
            (0x02, false),
            (0x02, true),
        ];

        Console.WriteLine("  cmd  framed as        outcome        reply");
        foreach ((byte command, bool checkSequence) in probes)
        {
            LongPollResult result = engine.Execute(
                command, default, null,
                $"check-sequence probe ${command:X2} {(checkSequence ? "framed" : "bare")}",
                checkSequence);

            string frame = result.Frame.Length == 0 ? "-" : result.ToHex();
            Console.WriteLine($"  ${command:X2}  {(checkSequence ? "with a sequence" : "two bytes only "),-15}  "
                              + $"{result.Outcome,-13}  {frame}");
        }

        Console.WriteLine();
        Console.WriteLine("  What the library says it should be:");
        foreach (byte command in new byte[] { 0x01, 0x02 })
            Console.WriteLine("    " + SasCommandFraming.Explain(command));

        engine.Stop();
        return 3;
    }

    private static void PrintPlan()
    {
        List<SweepEntry> asked = SweepPlan.Asked.ToList();
        List<SweepEntry> withheld = SweepPlan.Withheld.ToList();

        Console.WriteLine($"The sweep asks {asked.Count} of the catalogue's "
                          + $"{SasCatalogue.LongPolls.Count} commands and withholds {withheld.Count}.");
        Console.WriteLine();
        Console.WriteLine("WITHHELD — not sent, because sending it changes the machine");
        foreach (SweepEntry entry in withheld)
            Console.WriteLine($"  ${entry.Command:X2}  {entry.Name,-52}  {entry.Reason}");

        Console.WriteLine();
        Console.WriteLine("ASKED");
        foreach (SweepEntry entry in asked)
        {
            string layout = SasLayouts.For(entry.Command) is null ? "no layout" : "decoded  ";
            Console.WriteLine($"  ${entry.Command:X2}  {layout}  {entry.Name}");
        }
    }

    private static void PrintFindings(IReadOnlyList<SweepFinding> findings)
    {
        Console.WriteLine("WHAT THE MACHINE ANSWERED");
        foreach (SweepFinding finding in findings.Where(f => f.Outcome != SweepOutcome.Withheld))
            Console.WriteLine("  " + Row(finding));

        Console.WriteLine();
        Console.WriteLine("WITHHELD, AND WHY");
        foreach (SweepFinding finding in findings.Where(f => f.Outcome == SweepOutcome.Withheld))
            Console.WriteLine($"  ${finding.Entry.Command:X2}  {finding.Entry.Name,-52}  {finding.Entry.Reason}");

        Console.WriteLine();
        Console.WriteLine("COUNTED");
        foreach (SweepOutcome outcome in Enum.GetValues<SweepOutcome>())
        {
            int count = findings.Count(f => f.Outcome == outcome);
            Console.WriteLine($"  {outcome,-14} {count,4}");
        }

        int newBytes = findings.Count(f => f.Outcome == SweepOutcome.Answered && !f.HadLayout);
        Console.WriteLine();
        Console.WriteLine($"  {newBytes} command(s) answered with data and have no layout here yet. "
                          + "Those are the ones");
        Console.WriteLine("  a layout can now be established for, from the library's handler and "
                          + "these bytes together.");
    }

    private static string Row(SweepFinding finding)
    {
        string mark = finding.HadLayout ? "decoded" : "-      ";
        return $"${finding.Entry.Command:X2}  {mark}  {finding.Entry.Name,-52}  "
               + $"{finding.Outcome,-13}  {finding.Detail}";
    }

    private static RunReport ToReport(
        IReadOnlyList<SweepFinding> findings, PollEngine engine, ISasTransport transport)
    {
        IReadOnlyList<WireRecord> wire = engine.WireLog.Snapshot();
        var provenance = new RunProvenance(
            transport.Description, transport.Capabilities.Fidelity, StimulusBackend.None,
            "A sweep asserts nothing. It asks a machine every command it may ask and records the "
            + "answers, so a layout can be checked against bytes rather than only against source.");

        var tests = new List<TestOutcome>(findings.Count);
        var started = DateTimeOffset.UtcNow;
        foreach (SweepFinding finding in findings)
        {
            var steps = new List<StepResult>();
            foreach (SweepAttempt attempt in finding.Attempts)
            {
                steps.Add(new StepResult(
                    Origin,
                    $"ask ${finding.Entry.Command:X2} with {attempt.Request.Length} request byte(s)",
                    Verdict.Skip,
                    Reason: attempt.Result.ToString()));
            }

            tests.Add(new TestOutcome
            {
                Name = $"${finding.Entry.Command:X2} {finding.Entry.Name}",
                Location = Origin,
                Verdict = Verdict.Skip,
                SkipReason = $"{finding.Outcome}: {finding.Detail}",
                Steps = steps,
                StartedAt = started,
                Provenance = provenance,
                Wire = WireFor(wire, finding),
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
    /// The wire records that belong to one command's turn. Attributed by the note the engine wrote
    /// when it sent the poll, and the reply that followed it, rather than by position — the same
    /// rule the poll engine itself now uses.
    /// </summary>
    private static IReadOnlyList<WireRecord> WireFor(
        IReadOnlyList<WireRecord> wire, SweepFinding finding)
    {
        var kept = new List<WireRecord>();
        bool taking = false;
        string marker = $"sweep ${finding.Entry.Command:X2} ";

        foreach (WireRecord record in wire)
        {
            if (record.Direction == WireDirection.Transmit)
                taking = record.Note is not null && record.Note.Contains(marker, StringComparison.Ordinal);

            if (taking)
                kept.Add(record);
        }

        return kept;
    }

    private static bool WaitForLink(PollEngine engine)
    {
        for (int i = 0; i < 100 && engine.State != LinkState.Up; i++)
            Thread.Sleep(100);

        return engine.State == LinkState.Up;
    }

    private static bool TryTransport(
        CommandLine line, out ISasTransport? transport, out SasAddress address)
    {
        transport = null;
        address = SasAddress.Of(1);

        string addressText = line.Value("address") ?? "01";
        if (!SasAddress.TryParse(addressText, out address) || address.IsSync)
        {
            Console.Error.WriteLine(
                $"error: '{addressText}' is not a machine address. Expected 01 to 7F.");
            return false;
        }

        string? serial = line.Value("serial");
        if (serial is not null)
        {
            transport = new SerialTransport(new SerialTransportOptions(serial));
            return true;
        }

        string endpointText = line.Value("tcp") ?? $"127.0.0.1:{TcpTransportOptions.DefaultPort}";
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

    /// <summary>
    /// A sweep has no script, so every row points at the plan that decided what to send rather
    /// than at a line nobody wrote.
    /// </summary>
    private static SourceLocation Origin { get; } = new("sweep plan", 0);

    private static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss");
}
