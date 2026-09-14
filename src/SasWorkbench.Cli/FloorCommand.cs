using System.Net;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Reporting;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Session;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;
using SasWorkbench.Egm;

namespace SasWorkbench.Cli;

/// <summary>
/// Polls a round of machines and measures what the round costs — against a floor of software
/// machines this build brings up, or against a real link.
/// </summary>
/// <remarks>
/// <para>
/// The measurement this milestone exists for. A per-machine figure multiplied by a machine count
/// is an estimate; a round is a thing that can be timed, and the two are printed together so a
/// difference between them is visible rather than assumed away.
/// </para>
/// <para>
/// One poll engine walking a round, not one engine per machine, because a multi-drop line carries
/// one transaction at a time. Several engines would measure several lines, which is a different
/// number and a flattering one.
/// </para>
/// <para>
/// <c>--channels</c> is the exception that proves it, and it is several lines on purpose. A
/// channel is a port in the machine, so a second host is a second wire; the two rounds are then
/// measured together to see whether they interleave, as independent wires should, or cost each
/// other anything. Reported per channel, never summed — a sum would read as one host doing twice
/// the work.
/// </para>
/// </remarks>
internal static class FloorCommand
{
    public static int Execute(CommandLine line)
    {
        if (!TryCount(line, "machines", 8, out int machines)
            || !TryCount(line, "rounds", 10, out int rounds)
            || !TryCount(line, "channels", 1, out int channels))
        {
            return 2;
        }

        if (channels is < 1 or > SasChannels.Count)
        {
            Console.Error.WriteLine(
                $"error: --channels is 1 to {SasChannels.Count}; a machine has that many ports.");
            return 2;
        }

        if (channels > 1 && (line.Value("tcp") is not null || line.Value("serial") is not null))
        {
            Console.Error.WriteLine(
                "error: --channels brings up a line per channel, which only the software floor "
                + "can do. A real machine's second port is a second link, named on its own.");
            return 2;
        }

        if (machines < 1 || machines > SasAddress.MaxValue)
        {
            Console.Error.WriteLine(
                $"error: --machines is 1 to {SasAddress.MaxValue}; SAS has no room for more.");
            return 2;
        }

        if (rounds < 2)
        {
            Console.Error.WriteLine(
                "error: --rounds is at least 2. A round is measured between two polls of the same "
                + "machine, so one round is no measurement at all.");
            return 2;
        }

        TimeSpan interval = line.Value("poll") is { } poll
            ? ScriptParser.ParseDuration(poll, SourceLocation.None)
            : TimeSpan.Zero;
        bool syncPolls = !line.Flag("no-sync-polls");

        // Read before anything can go wrong, so a run that stops early still reports the flag as
        // recognised rather than telling the operator they mistyped something they did not.
        string? directory = line.Value("out");

        //  Either a floor this build brings up, or a real link with a round named on the command
        //  line. The measurement is the same either way; what differs is what a result is worth,
        //  which is why the two are labelled differently and never merged into one sentence.
        SoftwareFloor? floor = null;
        ISasTransport link;
        IReadOnlyList<SasAddress> round;
        string what;

        if (line.Value("tcp") is not null || line.Value("serial") is not null)
        {
            if (!TryRound(line, out round) || !TryLink(line, out link))
                return 2;

            what = $"{link.Description}, a round of {SasAddressRound.Describe(round)}";
        }
        else
        {
            floor = Bring(machines, channels);
            link = floor.Host;
            round = floor.Addresses;
            what = $"a floor of {machines} software machine(s) at "
                   + $"{SasAddressRound.Describe(round)} on "
                   + (channels == 1 ? "one multi-drop line" : $"{channels} multi-drop lines");
        }

        if (floor is not null && channels > 1)
        {
            using (floor)
                return Lines(floor, rounds, interval, syncPolls, what, directory);
        }

        using (floor)
        {
            var options = new PollEngineOptions
            {
                Address = round[0],
                Addresses = round,

                // Zero by default, so the round's cost is what is measured rather than the interval
                // a host was told to keep. A floor host with a hundred machines cannot keep a
                // 200 ms interval per machine anyway: that is twenty seconds a round.
                PollInterval = interval,
                SyncPollEvery = syncPolls ? 1 : 0,
                WireLogCapacity = Math.Max(4096, round.Count * rounds * 8),
            };

            using var engine = new PollEngine(link, options);

            Console.WriteLine($"{what}, one host walking the round.");
            Console.WriteLine(
                syncPolls
                    ? "  A synchronisation poll of address zero goes out ahead of each general poll."
                    : "  No synchronisation polls: in a round, the next machine's poll is already a "
                      + "poll for another address.");
            Console.WriteLine($"  Poll interval {interval.TotalMilliseconds:F0} ms, quiet gap "
                              + $"{options.QuietGap.TotalMilliseconds:F0} ms.");

            if (floor is null)
            {
                Console.WriteLine(
                    "  This is a real link. An address in the round with no machine at it answers "
                    + "nothing, which is reported as silent and is not a fault.");
            }

            Console.WriteLine();

            engine.Start();
            if (!WaitForRounds(engine, rounds + 1))
            {
                engine.Stop();
                Console.Error.WriteLine(
                    $"error: only {engine.RoundsCompleted} round(s) completed, and none has "
                    + $"finished in {StallSeconds} seconds. Nothing is reported, because a round "
                    + "count that stopped rising measures a stall rather than a round.");
                return 2;
            }

            engine.Stop();

            IReadOnlyList<WireRecord> wire = engine.WireLog.Snapshot();
            RoundMeasurement measured = RoundMeasurement.Of(wire, round);
            Report(measured, engine, floor, wire);

            if (directory is not null)
                Write(directory, engine, measured, round, syncPolls, link);

            // A real link is being asked a question rather than checked against an expectation: a
            // silent address there is the answer on one of the runs the handoff asks for, so it is
            // reported and not turned into a failure.
            return floor is not null && measured.Silent.Count > 0 ? 1 : 0;
        }
    }

    private static void Report(
        RoundMeasurement measured, PollEngine engine, SoftwareFloor? floor,
        IReadOnlyList<WireRecord> wire)
    {
        Console.WriteLine("THE ROUND, MEASURED");
        Console.WriteLine($"  machines in the round        {measured.Machines}");
        Console.WriteLine($"  complete rounds timed        {measured.Rounds}");
        Console.WriteLine($"  fastest round                {measured.Fastest.TotalMilliseconds,8:F1} ms");
        Console.WriteLine($"  median round                 {measured.Median.TotalMilliseconds,8:F1} ms");
        Console.WriteLine($"  slowest round                {measured.Slowest.TotalMilliseconds,8:F1} ms");
        Console.WriteLine($"  the median, per machine      {measured.PerMachine.TotalMilliseconds,8:F2} ms");
        Console.WriteLine();

        if (measured.Machines == 100)
        {
            Console.WriteLine("A HUNDRED MACHINES");
            Console.WriteLine(
                $"  {measured.Median.TotalSeconds,8:F2} s   measured, not extrapolated: this round "
                + "is the hundred.");
        }
        else
        {
            Console.WriteLine("WHAT A HUNDRED WOULD COST, FROM THIS MEASUREMENT");
            Console.WriteLine(
                $"  {100 * measured.PerMachine.TotalSeconds,8:F2} s   the measured per-machine "
                + "cost, times a hundred");
            Console.WriteLine(
                "  An estimate, and said so: a round of a hundred has not been measured here, "
                + $"only a round of {measured.Machines}.");
            Console.WriteLine(
                "  Run with --machines 100 to measure it instead of multiplying it.");
        }

        Console.WriteLine();

        PollStatistics statistics = engine.Statistics;
        Console.WriteLine("WHAT WENT OVER THE LINE");
        Console.WriteLine($"  general polls                {statistics.GeneralPolls}");
        Console.WriteLine($"  synchronisation polls        {statistics.SyncPolls}");
        Console.WriteLine($"  silences                     {statistics.Silences}");
        Console.WriteLine($"  messages recorded            {wire.Count}");
        if (floor is not null)
            Console.WriteLine($"  symbols lost on the line     {floor.Dropped}");

        Console.WriteLine();

        Console.WriteLine("WHICH MACHINES ANSWERED");
        Console.WriteLine($"  answered at least one poll   {measured.Answered.Count} of {measured.Machines}");
        if (measured.Silent.Count > 0)
        {
            // A silent machine is the one failure a clean-looking round can hide: the round still
            // takes a round's time, and one machine was never really there.
            Console.WriteLine(
                "  never answered               "
                + string.Join(" ", measured.Silent.Select(a => $"${a}")));
            Console.WriteLine();
            Console.WriteLine(
                floor is not null
                    ? "  A machine that answered nothing is reported as a failure rather than left "
                      + "in a timing table: the round still cost a round, and that machine was not "
                      + "in it."
                    : "  On a real link a silent address may simply be an address with no machine "
                      + "at it, which is an answer and not a fault. What it is not is a machine "
                      + "that answered.");
        }
    }

    /// <summary>
    /// Brings up a floor of machines, each answering on every channel asked for.
    /// </summary>
    /// <remarks>
    /// The same address on every channel, because an address is unique on a line and not on a
    /// floor: machine $25 is $25 on both of its ports, and a hundred machines on two channels
    /// could not be addressed any other way — two hundred ports, and SAS has 127 addresses.
    /// </remarks>
    private static SoftwareFloor Bring(int machines, int channels)
    {
        if (channels == 1)
            return SoftwareFloor.Of(machines);

        var plans = new List<SasChannelPlan>(machines);
        for (int i = 0; i < machines; i++)
        {
            var addresses = new SasAddress?[SasChannels.Count];
            for (int c = 0; c < channels; c++)
                addresses[c] = SasAddress.Of((byte)(1 + i));

            // Ownership is left where it falls, on channel one. This measures a round, and a
            // general poll is answered on every channel a machine opens whatever owns what.
            if (!SasChannelPlan.TryCreate(addresses, new Dictionary<SasCategory, SasChannel>(),
                    out SasChannelPlan plan, out string? refusal))
            {
                throw new InvalidOperationException(refusal);
            }

            plans.Add(plan);
        }

        return SoftwareFloor.Of(plans);
    }

    /// <summary>
    /// Measures a round on every line of the floor at once, and reports each one on its own.
    /// </summary>
    /// <remarks>
    /// The question this answers is whether two hosts on two wires cost each other anything. They
    /// share a process and a machine at each end and nothing else, so the prediction is that each
    /// round costs what it costs alone — and the way to find out is to run them together and
    /// compare against the single-line figure rather than to reason about it.
    /// </remarks>
    private static int Lines(
        SoftwareFloor floor, int rounds, TimeSpan interval, bool syncPolls, string what,
        string? directory)
    {
        IReadOnlyList<SasChannel> channels = floor.Channels;
        IReadOnlyList<SasAddress> round = floor.Addresses;
        var engines = new List<PollEngine>();

        //  One clock for the whole session, so the two lines' records can be put in one stream.
        //  Each engine measures from its own start, and they do not start at the same instant —
        //  merging their own elapsed times would report one line's traffic as a few milliseconds
        //  earlier or later than it was, for no reason other than the order they were started in.
        var session = new SessionClock();
        var bases = new List<ClockBase>();

        Console.WriteLine($"{what}, one host on each.");
        Console.WriteLine($"  Poll interval {interval.TotalMilliseconds:F0} ms, "
                          + $"synchronisation polls {(syncPolls ? "on" : "off")}.");
        Console.WriteLine(
            "  The lines are independent wires, so the rounds should interleave rather than "
            + "contend. Each is reported on its own; they are never summed.");
        Console.WriteLine();

        try
        {
            foreach (SasChannel channel in channels)
            {
                engines.Add(new PollEngine(floor.HostFor(channel)!, new PollEngineOptions
                {
                    Address = round[0],
                    Addresses = floor.RoundOf(channel),
                    Channel = channel,
                    PollInterval = interval,
                    SyncPollEvery = syncPolls ? 1 : 0,
                    WireLogCapacity = Math.Max(4096, round.Count * rounds * 8),
                }));
            }

            //  Started as close together as this can manage, so the rounds genuinely overlap.
            //  A line that started after the other had finished would measure two runs in
            //  sequence and report them as two rounds that did not interfere.
            foreach (PollEngine engine in engines)
            {
                bases.Add(session.Mark());
                engine.Start();
            }

            foreach (PollEngine engine in engines)
            {
                if (WaitForRounds(engine, rounds + 1))
                    continue;

                Console.Error.WriteLine(
                    $"error: channel {(int)engine.Channel} completed only "
                    + $"{engine.RoundsCompleted} round(s) and has stopped making progress.");
                return 2;
            }

            foreach (PollEngine engine in engines)
                engine.Stop();

            var measurements = new List<(PollEngine Engine, RoundMeasurement Measured)>();
            foreach (PollEngine engine in engines)
            {
                measurements.Add((engine,
                    RoundMeasurement.Of(engine.WireLog.Snapshot(), floor.RoundOf(engine.Channel))));
            }

            Console.WriteLine("EACH LINE'S ROUND, MEASURED WHILE THE OTHERS WERE RUNNING");
            Console.WriteLine("  channel   rounds    fastest     median    slowest   per machine");
            foreach ((PollEngine engine, RoundMeasurement measured) in measurements)
            {
                Console.WriteLine(
                    $"  {(int)engine.Channel,7}   {measured.Rounds,6}   "
                    + $"{measured.Fastest.TotalMilliseconds,7:F1}    "
                    + $"{measured.Median.TotalMilliseconds,7:F1}    "
                    + $"{measured.Slowest.TotalMilliseconds,7:F1}   "
                    + $"{measured.PerMachine.TotalMilliseconds,8:F2} ms");
            }

            Console.WriteLine();
            Console.WriteLine("WHICH MACHINES ANSWERED, PER LINE");
            int silent = 0;
            foreach ((PollEngine engine, RoundMeasurement measured) in measurements)
            {
                silent += measured.Silent.Count;
                Console.WriteLine(
                    $"  channel {(int)engine.Channel}   {measured.Answered.Count} of "
                    + $"{measured.Machines}"
                    + (measured.Silent.Count == 0
                        ? string.Empty
                        : "   never answered: "
                          + string.Join(" ", measured.Silent.Select(a => $"${a}"))));
            }

            Console.WriteLine();
            Console.WriteLine($"  symbols lost across every line   {floor.Dropped}");

            if (directory is not null)
                WriteLines(directory, floor, engines, bases, session, measurements, syncPolls);

            return silent == 0 ? 0 : 1;
        }
        finally
        {
            foreach (PollEngine engine in engines)
                engine.Dispose();
        }
    }

    /// <summary>
    /// Writes one record for the whole floor: both lines' traffic in one stream, each message
    /// carrying the host it belongs to and the machine it was to or from.
    /// </summary>
    /// <remarks>
    /// One file and not one per line, for the reason D23 gives: the ordering between what one host
    /// sent and what the other heard is the thing a floor run is about, and two files cannot be
    /// interleaved afterwards without a shared clock. The sequence numbers are each engine's own,
    /// so they are renumbered over the merged list — two independent sequences interleaved would
    /// read as a great deal having been dropped.
    /// </remarks>
    private static void WriteLines(
        string directory,
        SoftwareFloor floor,
        IReadOnlyList<PollEngine> engines,
        IReadOnlyList<ClockBase> bases,
        SessionClock session,
        IReadOnlyList<(PollEngine Engine, RoundMeasurement Measured)> measurements,
        bool syncPolls)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "floor.jsonl");

        List<WireRecord> wire =
        [
            .. engines
                .Select((engine, index) => (Engine: engine, Base: bases[index]))
                .SelectMany(host => host.Engine.WireLog.Snapshot()
                    .Select(record => record with { Elapsed = host.Base.Reconcile(record.Elapsed) }))
                .OrderBy(record => record.Elapsed)
                .Select((record, index) => record with { Sequence = index }),
        ];

        var report = new RunReport
        {
            StartedAt = session.StartedAt,
            Duration = session.Now,
            Provenance =
            [
                new RunProvenance(
                    $"multi-drop loopback, {floor.Machines.Count} machine(s) on "
                    + $"{floor.Channels.Count} line(s)",
                    TransportFidelity.Proven,
                    StimulusBackend.None,
                    "One host per line, each walking its own round, "
                    + (syncPolls
                        ? "with a synchronisation poll ahead of each general poll."
                        : "with no synchronisation polls at all.")
                    + " It asserts nothing beyond which machines answered.")
                {
                    Channels = floor.Machines[0].Channels.Describe(),
                    ChannelsRead = true,
                    Round = string.Join(' ', floor.Channels.Select(channel =>
                        $"{(int)channel}={SasAddressRound.Describe(floor.RoundOf(channel))}")),
                },
            ],
            Tests =
            [
                new TestOutcome
                {
                    Name = $"a floor of {floor.Machines.Count} machine(s) behind "
                           + $"{floor.Channels.Count} host(s)",
                    Location = Origin,
                    Verdict = measurements.All(m => m.Measured.Silent.Count == 0)
                        ? Verdict.Pass
                        : Verdict.Fail,
                    StartedAt = session.StartedAt,
                    Duration = session.Now,
                    Steps =
                    [
                        .. measurements.Select(m => new StepResult(
                            Origin,
                            $"channel {(int)m.Engine.Channel}: the median round",
                            m.Measured.Silent.Count == 0 ? Verdict.Pass : Verdict.Fail,
                            Expected: string.Join(" ", m.Engine.Round.Select(a => $"${a}")),
                            Actual: $"{m.Measured.Median.TotalMilliseconds:F1} ms over "
                                    + $"{m.Measured.Rounds} round(s), "
                                    + $"{m.Measured.Answered.Count} machine(s) answering")
                        {
                            IsAssertion = true,
                        }),
                    ],
                    Wire = wire,
                },
            ],
        };

        File.WriteAllText(path, new JsonLinesReportWriter().Render(report));
        Console.WriteLine();
        Console.WriteLine($"  wrote {path}  ({wire.Count} wire record(s))");
    }

    /// <summary>Writes the round's own record, so an answer can be read again later.</summary>
    private static void Write(
        string directory, PollEngine engine, RoundMeasurement measured,
        IReadOnlyList<SasAddress> round, bool syncPolls, ISasTransport link)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "round.jsonl");

        var report = new RunReport
        {
            StartedAt = DateTimeOffset.UtcNow - engine.Elapsed,
            Duration = engine.Elapsed,
            Provenance =
            [
                new RunProvenance(
                    link.Description, link.Capabilities.Fidelity, StimulusBackend.None,
                    $"A round of {SasAddressRound.Describe(round)}, "
                    + (syncPolls
                        ? "with a synchronisation poll ahead of each general poll."
                        : "with no synchronisation polls at all.")
                    + " It asserts nothing beyond which machines answered: it polls a round and "
                    + "records what came back.")
                {
                    Channels = SasChannelPlan.Only(engine.Channel, round[0]).Describe(),
                },
            ],
            Tests =
            [
                new TestOutcome
                {
                    Name = $"a round of {round.Count} machine(s), "
                           + (syncPolls ? "synchronised" : "unsynchronised"),
                    Location = Origin,
                    Verdict = measured.Silent.Count == 0 ? Verdict.Pass : Verdict.Fail,
                    StartedAt = DateTimeOffset.UtcNow - engine.Elapsed,
                    Duration = engine.Elapsed,
                    Steps =
                    [
                        new StepResult(Origin, "the median round", Verdict.Pass,
                            Expected: null,
                            Actual: $"{measured.Median.TotalMilliseconds:F1} ms over "
                                    + $"{measured.Rounds} round(s)"),
                        new StepResult(Origin, "which machines answered",
                            measured.Silent.Count == 0 ? Verdict.Pass : Verdict.Fail,
                            Expected: string.Join(" ", round.Select(a => $"${a}")),
                            Actual: string.Join(" ", measured.Answered.Select(a => $"${a}")),
                            Reason: measured.Silent.Count == 0
                                ? null
                                : "silent: " + string.Join(" ", measured.Silent.Select(a => $"${a}")))
                        {
                            IsAssertion = true,
                        },
                    ],
                    Wire = engine.WireLog.Snapshot(),
                },
            ],
        };

        File.WriteAllText(path, new JsonLinesReportWriter().Render(report));
        Console.WriteLine();
        Console.WriteLine($"  wrote {path}");
    }

    /// <summary>A round has no script, so its rows point at the command rather than at a line.</summary>
    private static SourceLocation Origin { get; } = new("sasctl floor", 0);

    /// <summary>Reads the round a real link is to be polled over.</summary>
    private static bool TryRound(CommandLine line, out IReadOnlyList<SasAddress> round)
    {
        string text = line.Value("addresses") ?? line.Value("address") ?? "01";
        if (SasAddressRound.TryParse(text, out round, out string? why))
            return true;

        Console.Error.WriteLine($"error: {why}");
        return false;
    }

    private static bool TryLink(CommandLine line, out ISasTransport link)
    {
        link = null!;

        if (line.Value("serial") is { } port)
        {
            link = new SerialTransport(new SerialTransportOptions(port));
            return true;
        }

        string endpoint = line.Value("tcp")!;
        if (!TcpTransportOptions.TryParseEndpoint(endpoint, out IPEndPoint parsed))
        {
            Console.Error.WriteLine(
                $"error: '{endpoint}' is not an endpoint. Expected something like 127.0.0.1:9760.");
            return false;
        }

        link = new TcpTransport(new TcpTransportOptions(
            parsed, line.Flag("dial") ? TcpRole.Connect : TcpRole.Listen));
        return true;
    }

    /// <summary>Reads a count, refusing a value that is not one rather than falling back.</summary>
    private static bool TryCount(CommandLine line, string name, int fallback, out int value)
    {
        value = fallback;
        if (line.Value(name) is not { } text)
            return true;

        if (int.TryParse(text, out value))
            return true;

        Console.Error.WriteLine($"error: --{name} expects a whole number, and '{text}' is not one.");
        return false;
    }

    /// <summary>How long a round may take before the host is called stalled rather than slow.</summary>
    private const int StallSeconds = 60;

    /// <summary>
    /// Waits for a number of rounds, and gives up only when the count stops rising.
    /// </summary>
    /// <remarks>
    /// A deadline on the whole run would call a long floor session a failure for being long — a
    /// hundred machines at six seconds a round is ten minutes for a hundred rounds, and that is
    /// the measurement working rather than failing. What is not acceptable is a host that has
    /// stopped making progress, which is what this waits for instead.
    /// </remarks>
    private static bool WaitForRounds(PollEngine engine, long wanted)
    {
        long seen = engine.RoundsCompleted;
        DateTime lastProgress = DateTime.UtcNow;

        while (engine.RoundsCompleted < wanted)
        {
            Thread.Sleep(20);

            long now = engine.RoundsCompleted;
            if (now != seen)
            {
                seen = now;
                lastProgress = DateTime.UtcNow;
                continue;
            }

            if (DateTime.UtcNow - lastProgress > TimeSpan.FromSeconds(StallSeconds))
                return false;
        }

        return true;
    }
}
