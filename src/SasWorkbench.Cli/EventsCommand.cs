using System.Net;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Events;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Reporting;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;
using SasWorkbench.Qpm;

namespace SasWorkbench.Cli;

/// <summary>
/// Turns real-time event reporting on, drives the machine into events, and writes down what the
/// payloads were.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of the catalogue sweep, for the half of the protocol a sweep cannot reach. A
/// long poll can be asked for; an event has to be caused. And the command that turns reporting on
/// is one the sweep withholds on purpose, because it changes how the machine answers every general
/// poll after it — so this is a measurement somebody asks for by name, it says what it is about to
/// send before the link opens, and it puts the machine back at the end.
/// </para>
/// <para>
/// It asserts nothing, so like a sweep it comes back as a run in which nothing was tested.
/// </para>
/// </remarks>
internal static class EventsCommand
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

        int seconds = 45;
        if (line.Value("seconds") is { } given
            && (!int.TryParse(given, out seconds) || seconds is < 1 or > 3600))
        {
            Console.Error.WriteLine($"error: '{given}' is not a number of seconds between 1 and 3600.");
            return 2;
        }

        var peripherals = new QpmOptions(line.Value("qpm-shm"), line.Value("egm-resources"));
        int noteCents = 2000;
        if (line.Value("note") is { } noteText
            && (!int.TryParse(noteText, out noteCents) || noteCents <= 0))
        {
            Console.Error.WriteLine($"error: '{noteText}' is not a note value in cents.");
            return 2;
        }

        // Read before the link opens, so a mistyped report name stops the run rather than being
        // discovered after the machine has been driven into events nobody kept.
        string directory = line.Value("out") ?? Path.Combine("runs", Stamp() + "-events");
        if (!Reports.TryParse(line.Value("report") ?? "jsonl,extended",
                out ReportFormat[] formats, out string? unknown))
        {
            Console.Error.WriteLine(
                $"error: '{unknown}' is not a report this build writes. Available: {Reports.Names}");
            return 2;
        }

        var options = new PollEngineOptions
        {
            Address = address,
            PollInterval = TimeSpan.FromMilliseconds(200),
            WireLogCapacity = 262144,
        };

        using var engine = new PollEngine(transport!, options);
        PrintIntent(address, seconds);

        engine.Start();
        if (!WaitForLink(engine))
        {
            Console.Error.WriteLine(
                "error: the machine never answered a general poll, so nothing was measured. "
                + "No event is reported as unproduced, because a link that was never up says "
                + "nothing about any of them.");
            return 2;
        }

        using var probe = new RealTimeEventProbe(engine);

        LongPollResult on = probe.Enable();
        Console.WriteLine($"  $0E on   {on.Outcome}  {Describe(on)}");
        if (on.Outcome != LongPollOutcome.Acknowledged)
        {
            Console.Error.WriteLine(
                "error: the machine did not acknowledge real-time event reporting, so it is still "
                + "reporting the old way and nothing here would measure a payload. Stopping "
                + "rather than driving it and reading bare codes.");
            engine.Stop();
            return 2;
        }

        Drive(engine, probe, peripherals, noteCents, seconds);

        LongPollResult off = probe.Disable();
        Console.WriteLine($"  $0E off  {off.Outcome}  {Describe(off)}");
        if (off.Outcome != LongPollOutcome.Acknowledged)
        {
            Console.Error.WriteLine(
                "warning: the machine did not acknowledge reporting being turned off, so it may "
                + "still be answering general polls in the event format. Anything run against it "
                + "next would be measuring a machine this run left changed.");
        }

        IReadOnlyList<EventFinding> findings = probe.Findings();
        engine.Stop();

        Console.WriteLine();
        PrintFindings(probe, findings);

        Console.WriteLine();
        foreach (string written in Reports.Write(
                     ToReport(findings, probe, engine, transport!), directory, formats))
        {
            Console.WriteLine($"wrote {written}");
        }

        // A measurement proves nothing about this build, so it must not be able to look like a
        // pass. The same rule the sweep follows.
        return 3;
    }

    /// <summary>
    /// Works the machine so that it has something to report.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ordered by what each step needs of the machine. A door needs nothing, so it goes first and
    /// proves the event format is arriving before anything harder is tried. Then the machine is
    /// left alone until it stops reporting, because a note offered into a burst of door exceptions
    /// is a note the machine refuses while it thinks a door is open — and a refused note looks
    /// exactly like an event this build cannot read.
    /// </para>
    /// <para>
    /// Then the note, which is both an event of its own and the credit a game needs. Then the game
    /// menu, which is how a game gets chosen. Then the machine's own play-by-itself mode, and one
    /// press of the play button to start the first round: that mode arms the <em>next</em> round
    /// when one ends, so it continues a sequence and cannot begin one.
    /// </para>
    /// </remarks>
    private static void Drive(
        PollEngine engine, RealTimeEventProbe probe, QpmOptions peripherals, int noteCents,
        int seconds)
    {
        QpmMachine? machine = null;
        try
        {
            machine = QpmMachine.Attach(peripherals.RegionPath, peripherals.ResourcesDirectory);
        }
        catch (QpmUnavailableException unavailable)
        {
            Console.WriteLine();
            Console.WriteLine("  no peripheral driver: " + unavailable.Reason);
            Console.WriteLine("  the door, note and game events cannot be produced, and are "
                              + "reported as unproduced rather than skipped.");
        }

        using (machine)
        {
            Console.WriteLine();
            Console.WriteLine("  driving the machine");

            if (machine is not null)
            {
                Settle(machine);

                // The machine declares a reset keyswitch for clearing attendant conditions, and
                // an ordinary button press is swallowed while one is active. Turned and returned
                // here so that everything after it is a stimulus the machine can act on rather
                // than one it silently drops.
                Turn(machine, "reset_jkey");
                Quiet(probe, engine, TimeSpan.FromSeconds(20));

                // The game is chosen before the note is offered. A machine sitting on its game
                // select screen has no game to stake anything on, and the note that follows is
                // the credit the first round needs.
                Press(machine, "game menu");
                Await(probe, 0x8C, TimeSpan.FromSeconds(5));

                LongPollResult acceptor = probe.EnableNoteAcceptor();
                Console.WriteLine($"    $06      {acceptor.Outcome}  {Describe(acceptor)}");
                Note(machine, noteCents);
                Await(probe, 0x4F, TimeSpan.FromSeconds(10));
            }

            // Retried, because the first attempt often lands in the middle of the exception burst
            // the steps above cause and the machine answers the poll it was already answering. A
            // silent setter is not the same as a refused one and this is which.
            LongPollResult rebet = Insist(probe, on: true);
            Console.WriteLine($"  $AA on   {rebet.Outcome}  {Describe(rebet)}");

            if (machine is not null)
            {
                Press(machine, "play");
                Await(probe, 0x7E, TimeSpan.FromSeconds(10));
            }

            Console.WriteLine($"  watching for {seconds}s");
            long deadline = Environment.TickCount64 + (seconds * 1000L);
            int reported = probe.Reported.Count;
            while (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(250);
                reported = Show(probe, reported);
            }

            LongPollResult stop = probe.AutoRebet(on: false);
            Console.WriteLine($"  $AA off  {stop.Outcome}  {Describe(stop)}");
        }
    }

    /// <summary>Sends the auto-rebet command until the machine acknowledges it, or three times.</summary>
    private static LongPollResult Insist(RealTimeEventProbe probe, bool on)
    {
        LongPollResult result = probe.AutoRebet(on);
        for (int attempt = 1; attempt < 3 && result.Outcome == LongPollOutcome.Silent; attempt++)
        {
            Thread.Sleep(500);
            result = probe.AutoRebet(on);
        }

        return result;
    }

    /// <summary>Waits for the machine to stop reporting, so the next stimulus is not lost in a burst.</summary>
    private static void Quiet(RealTimeEventProbe probe, PollEngine engine, TimeSpan patience)
    {
        TimeSpan settled = engine.PollInterval * 5;
        long deadline = Environment.TickCount64 + (long)patience.TotalMilliseconds;
        int seen = probe.Reported.Count;
        int shown = seen;
        long lastChange = Environment.TickCount64;

        while (Environment.TickCount64 < deadline)
        {
            Thread.Sleep(50);
            shown = Show(probe, shown);
            int now = probe.Reported.Count;
            if (now != seen)
            {
                seen = now;
                lastChange = Environment.TickCount64;
                continue;
            }

            if (Environment.TickCount64 - lastChange >= (long)settled.TotalMilliseconds)
            {
                Console.WriteLine($"    the machine went quiet after {seen} exception(s)");
                return;
            }
        }

        Console.WriteLine($"    the machine was still reporting after "
                          + $"{patience.TotalSeconds:F0}s and {seen} exception(s); carrying on");
    }

    /// <summary>Waits for one exception, and says plainly when it never came.</summary>
    private static void Await(RealTimeEventProbe probe, byte code, TimeSpan patience)
    {
        long deadline = Environment.TickCount64 + (long)patience.TotalMilliseconds;
        int shown = probe.Reported.Count;

        while (Environment.TickCount64 < deadline)
        {
            Thread.Sleep(50);
            shown = Show(probe, shown);
            if (probe.Reported.Any(r => r.Code == code))
                return;
        }

        Console.WriteLine($"    no ${code:X2} in {patience.TotalSeconds:F0}s");
    }

    private static int Show(RealTimeEventProbe probe, int shown)
    {
        IReadOnlyList<ExceptionReport> reported = probe.Reported;
        for (int i = shown; i < reported.Count; i++)
            Console.WriteLine($"    {reported[i]}");

        return reported.Count;
    }

    /// <summary>Turns a keyswitch and returns it, which is not a press.</summary>
    private static void Turn(QpmMachine machine, string name)
    {
        if (!machine.Profile.TryFindButton(name, out EgmButton key))
        {
            Console.WriteLine($"    this machine declares no '{name}' keyswitch, so none was turned");
            return;
        }

        machine.SetButton(key, pressed: true);
        machine.SetButton(key, pressed: false);
        Console.WriteLine($"    {key.Label} turned and returned");
    }

    private static void Press(QpmMachine machine, string name)
    {
        if (!machine.Profile.TryFindButton(name, out EgmButton button))
        {
            Console.WriteLine($"    this machine declares no '{name}' button, so none was pressed");
            return;
        }

        machine.PressButton(button);
        Console.WriteLine($"    {button.Label} pressed");
    }

    /// <summary>
    /// Shuts every door the machine believes is open, which is also what makes it take a note.
    /// </summary>
    /// <remarks>
    /// A machine fresh from a cold start believes several doors are open, because it detects a
    /// door by the change in its state and a door that was already open at power-up never changed.
    /// It refuses a note while it believes one is open, and it swallows a button press for the
    /// same reason — so every stimulus after this depends on it. The door exceptions it produces
    /// are the first proof that the event format is arriving.
    /// </remarks>
    private static void Settle(QpmMachine machine)
    {
        foreach (string line in machine.Settle())
            Console.WriteLine($"    {line}");
    }

    private static void Note(QpmMachine machine, int cents)
    {
        try
        {
            machine.InsertNote(cents);
            Console.WriteLine($"    a note of {cents} cents was offered to the acceptor");
        }
        catch (QpmUnavailableException unavailable)
        {
            Console.WriteLine($"    the note could not be offered: {unavailable.Reason}");
        }
    }

    private static void PrintIntent(SasAddress address, int seconds)
    {
        Console.WriteLine($"Measuring real-time event payloads against the machine at {address}.");
        Console.WriteLine("Nothing here asserts anything: it causes events and records what came "
                          + "back.");
        Console.WriteLine();
        Console.WriteLine("DELIBERATELY SENT — commands the ordinary sweep withholds");
        foreach ((byte command, string what) in RealTimeEventPlan.DeliberatelySent)
            Console.WriteLine($"  ${command:X2}  {what}");

        Console.WriteLine();
        Console.WriteLine($"Both are turned off again at the end, and this says whether that was "
                          + $"acknowledged. Watching for {seconds}s.");
        Console.WriteLine();
    }

    private static void PrintPlan()
    {
        Console.WriteLine(
            $"{SasEventLayouts.All.Count} exceptions have a payload the machine library builds, "
            + "and this build reads all of them. What follows is what it would take to see one.");
        Console.WriteLine();
        Console.WriteLine("DELIBERATELY SENT — commands the ordinary sweep withholds");
        foreach ((byte command, string what) in RealTimeEventPlan.DeliberatelySent)
            Console.WriteLine($"  ${command:X2}  {what}");

        Console.WriteLine();
        foreach (EventReach reach in Enum.GetValues<EventReach>())
        {
            List<EventProbeEntry> group =
                RealTimeEventPlan.All.Where(e => e.Reach == reach).ToList();
            if (group.Count == 0)
                continue;

            Console.WriteLine(Heading(reach));
            foreach (EventProbeEntry entry in group)
            {
                Console.WriteLine($"  ${entry.Code:X2}  {entry.Name,-28}  {entry.How}");
                Console.WriteLine($"       raised by {entry.RaisedBy}");
            }

            Console.WriteLine();
        }

        foreach ((byte code, string why) in SasEventLayouts.NeverQueued)
            Console.WriteLine($"  ${code:X2}  no payload can be established: {why}");
    }

    private static string Heading(EventReach reach) => reach switch
    {
        EventReach.Driven => "DRIVEN — this measurement causes these",
        EventReach.NotDrivenHere => "NOT DRIVEN HERE — the machine can report these and nothing "
                                    + "here brings them about",
        _ => "NEVER RAISED — nothing in the machine or the platform above it reports these",
    };

    private static void PrintFindings(RealTimeEventProbe probe, IReadOnlyList<EventFinding> findings)
    {
        Console.WriteLine("WHAT THE MACHINE REPORTED, PAYLOAD BY PAYLOAD");
        foreach (EventFinding finding in findings)
        {
            string state = finding.Seen == 0
                ? "unproduced"
                : finding.Fits == true ? "fits      " : "DOES NOT FIT";
            Console.WriteLine(
                $"  ${finding.Code:X2}  {state}  {finding.Planned?.Name ?? SasNames.ExceptionName(finding.Code),-28}"
                + $"  {finding.Seen,3}x  {finding.Hex}");
            if (finding.Failure is not null)
                Console.WriteLine($"       {finding.Failure}");
            else if (finding.Seen == 0 && finding.Planned is { } planned)
                Console.WriteLine($"       {Why(planned)}");
        }

        IReadOnlyList<byte> plain = probe.CarriedNothing();
        Console.WriteLine();
        Console.WriteLine($"  {plain.Count} exception(s) arrived in the event format carrying "
                          + "nothing, which is what most of them do:");
        Console.WriteLine("    " + (plain.Count == 0
            ? "none"
            : string.Join(' ', plain.Select(c => $"${c:X2}"))));

        IReadOnlyList<byte> bare = probe.ArrivedAsBareCodes();
        if (bare.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  and these arrived as bare codes while reporting was meant to be "
                              + "on, which is a finding about the machine:");
            Console.WriteLine("    " + string.Join(' ', bare.Select(c => $"${c:X2}")));
        }

        int established = findings.Count(f => f.Fits == true);
        Console.WriteLine();
        Console.WriteLine($"  {established} of {SasEventLayouts.All.Count} payloads checked "
                          + "against bytes a machine sent. The rest stand on the library's own "
                          + "builder alone");
        Console.WriteLine("  and are recorded as unproduced, which is what they are.");
    }

    private static string Why(EventProbeEntry entry) => entry.Reach switch
    {
        EventReach.Driven => "this measurement drives it and none arrived, which is a finding: "
                             + entry.How,
        EventReach.NotDrivenHere => "not driven here — " + entry.How,
        _ => "never raised — " + entry.How,
    };

    private static string Describe(LongPollResult result) =>
        result.Frame.Length == 0 ? result.Detail ?? "nothing came back" : result.ToHex();

    private static RunReport ToReport(
        IReadOnlyList<EventFinding> findings,
        RealTimeEventProbe probe,
        PollEngine engine,
        ISasTransport transport)
    {
        IReadOnlyList<WireRecord> wire = engine.WireLog.Snapshot();
        var provenance = new RunProvenance(
            transport.Description, transport.Capabilities.Fidelity, StimulusBackend.Qpm,
            "A real-time event measurement asserts nothing. It turns reporting on, causes what it "
            + "can cause, and records the payloads, so an exception's layout can be checked "
            + "against bytes rather than only against source. It sends two commands the catalogue "
            + "sweep withholds and turns both off again.");

        var started = DateTimeOffset.UtcNow;
        var tests = new List<TestOutcome>(findings.Count);
        foreach (EventFinding finding in findings)
        {
            string outcome = finding.Seen == 0
                ? "unproduced: " + (finding.Planned is { } planned ? Why(planned) : "not in the plan")
                : finding.Fits == true
                    ? $"the payload read the {finding.First!.Length} byte(s) the machine sent"
                    : finding.Failure ?? "the payload did not fit";

            tests.Add(new TestOutcome
            {
                Name = $"${finding.Code:X2} {finding.Planned?.Name ?? SasNames.ExceptionName(finding.Code)}",
                Location = Origin,
                Verdict = Verdict.Skip,
                SkipReason = outcome,
                Steps = [],
                StartedAt = started,
                Provenance = provenance,

                // Every record goes on the first row and none on the rest. An event is not a
                // transaction, so there is no poll to attribute it to the way a sweep attributes a
                // reply — and splitting the log by guesswork would be worse than not splitting it.
                Wire = finding.Code == findings[0].Code ? wire : [],
            });
        }

        _ = probe;
        return new RunReport
        {
            Tests = tests,
            StartedAt = started,
            Duration = engine.Elapsed,
            Provenance = [provenance],
            Capabilities = [],
        };
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

        if (line.Value("serial") is { } serial)
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
    /// A measurement has no script, so every row points at the plan that decided what to do.
    /// </summary>
    private static SourceLocation Origin { get; } = new("real-time event plan", 0);

    private static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss");
}
