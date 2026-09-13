using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Recording;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Session;
using SasWorkbench.Core.Transactions;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Cli;

/// <summary>
/// Turns a session somebody drove into a script that drives it again.
/// </summary>
/// <remarks>
/// <para>
/// The window records what a person did and emits the script directly. This command is the same
/// emitter over a session already on disk, and it exists for two reasons: it is how the emitter is
/// exercised without a person, and it is the only way to get a script out of a recording nobody
/// here made.
/// </para>
/// <para>
/// What it can recover from a file is narrower than what the window records, and the difference is
/// the point. A host's own messages are things the host chose, so they come back as steps. A
/// <b>stimulus is not in the bytes at all</b> — nothing in a recording says a person opened a door
/// or fed a note, only that the machine reported one — so every stimulus is reported as
/// unrecoverable rather than guessed at from the exceptions that followed it. A script that
/// inferred "somebody must have opened the slot door" and wrote a step for it would assert its way
/// to a pass on a machine whose door nobody touched.
/// </para>
/// </remarks>
internal static class RecordCommand
{
    public static int Execute(CommandLine line)
    {
        if (line.Flag("caveats"))
        {
            Caveats();
            return 0;
        }

        if (line.Arguments.Count == 0)
        {
            Console.Error.WriteLine(
                "error: 'record' needs a run written with --report jsonl, or a capture. "
                + "Try 'sasctl record --caveats'.");
            return 2;
        }

        string path = line.Arguments[0];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"error: there is no file at '{path}'.");
            return 2;
        }

        SasAddress? address = null;
        if (line.Value("address") is { } given)
        {
            if (!SasAddress.TryParse(given, out SasAddress parsed) || parsed.IsSync)
            {
                Console.Error.WriteLine(
                    $"error: '{given}' is not a machine address. Expected 01 to 7F.");
                return 2;
            }

            address = parsed;
        }

        if (!RecordedSessions.TryRead(path, null, out IReadOnlyList<RecordedSession> sessions,
                out string source))
        {
            return 2;
        }

        string? only = line.Value("test");
        IReadOnlyList<RecordedSession> chosen = only is null
            ? sessions
            : [.. sessions.Where(s => s.Name.Contains(only, StringComparison.OrdinalIgnoreCase))];

        if (chosen.Count == 0)
        {
            Console.Error.WriteLine(
                $"error: no session in '{path}' carries '{only}'. It has: "
                + string.Join(", ", sessions.Take(10).Select(s => s.Name)));
            return 2;
        }

        Console.WriteLine($"RECORDED IN {path}");
        Console.WriteLine($"  read as      {source}");
        Console.WriteLine($"  sessions     {chosen.Count}");
        Console.WriteLine();

        var written = new List<string>();
        foreach (RecordedSession session in chosen)
        {
            EmittedScript emitted = Emit(session, address, line.Value("name") ?? session.Name);
            Report(session, emitted);

            if (line.Value("out") is { } directory)
                written.Add(Write(directory, session, emitted, chosen.Count));
        }

        if (written.Count > 0)
        {
            Console.WriteLine();
            foreach (string file in written)
                Console.WriteLine($"  wrote {file}");
            Console.WriteLine();
            Console.WriteLine("  Run it, and compare the verdict against the session it came from. "
                              + "A recorded script that");
            Console.WriteLine("  passes for the wrong reason is worse than no recorder, which is "
                              + "why every step it");
            Console.WriteLine("  could not write is above rather than absent.");
        }

        return 0;
    }

    /// <summary>
    /// Builds a recording out of a session's own packets.
    /// </summary>
    /// <remarks>
    /// The host's messages are the actions, because the host chose them: a long poll in a recording
    /// is a thing somebody asked for. The general and synchronisation polls are the poll loop
    /// rather than anybody's decision, so they become the poll interval and nothing else. Anything
    /// the machine did is left where it is — it is the evidence the emitter reads to decide what
    /// may be asserted, and never a step.
    /// </remarks>
    private static EmittedScript Emit(RecordedSession session, SasAddress? address, string name)
    {
        TransactionSplit split = TransactionSplitter.Split(session.Records, address);
        SasAddress machine = address
                             ?? split.Address
                             ?? SasAddress.Of(1);

        var log = new SessionLog();
        var recorder = new SessionRecorder(log);
        var actions = new List<RecordedAction>();

        TimeSpan first = session.Records.Count == 0 ? TimeSpan.Zero : session.Records[0].Elapsed;
        actions.Add(new RecordedAction(
            first, SasChannel.One, "connect to the machine this session was recorded against",
            new ConnectStep(
                SourceLocation.None, ScriptTransport.Loopback, string.Empty, machine,
                SasChannel.One)));

        if (Interval(split) is { } interval)
        {
            actions.Add(new RecordedAction(
                first, SasChannel.One, $"poll every {interval.TotalMilliseconds:F0}ms",
                new PollEveryStep(SourceLocation.None, interval)));
        }

        foreach (SasExchange exchange in split.Exchanges)
        {
            if (exchange.Kind != ExchangeKind.LongPoll || exchange.Command is not { } command)
                continue;

            //  The address, the command and - where the command requires one - the check
            //  sequence are framing rather than data. Keeping the sequence would put two bytes
            //  into the step that the engine computes for itself, and the replayed message would
            //  carry a check sequence over a check sequence.
            byte[] request = exchange.RequestBytes;
            int tail = SasCommandFraming.RequiresCheckSequence(command) && request.Length > 4
                ? 2
                : 0;
            byte[] data = request.Length > 2 + tail ? request[2..^tail] : [];

            actions.Add(new RecordedAction(
                exchange.At, exchange.Request.Channel, $"send long poll ${command:X2}",
                new SendLongPollStep(SourceLocation.None, command, data)));
        }

        //  A stimulus is not in the bytes. Every episode that opened on a machine event is one
        //  something did to the machine, and this says so once per episode rather than inventing
        //  a step for it.
        foreach (SasEpisode episode in split.Episodes.Where(e => e.OpenedBy is not null))
        {
            actions.Add(new RecordedAction(
                episode.At, SasChannel.One,
                $"whatever caused {episode.Rule.Name}"
                + (episode.Key.Length == 0 ? string.Empty : $" {episode.Key}"),
                Step: null,
                Refused: "nothing in a recording says what acted on the machine, only that the "
                         + $"machine reported ${episode.OpenedBy:X2}. A step guessed from that "
                         + "would drive a machine nobody drove."));
        }

        var recorded = new Recorded
        {
            From = first,
            To = session.Records.Count == 0 ? TimeSpan.Zero : session.Records[^1].Elapsed,
            Actions = [.. actions.OrderBy(a => a.At)],
            Events = [.. Events(session.Records)],
        };

        _ = recorder;
        return ScriptEmitter.Emit(recorded, name);
    }

    /// <summary>The wire, as session events, so the emitter reads one shape whatever the source.</summary>
    private static IEnumerable<SessionEvent> Events(IReadOnlyList<WireRecord> records) =>
        records.Select((record, index) => new SessionEvent(
            index,
            record.Direction == WireDirection.Transmit
                ? SessionLane.Transmit
                : SessionLane.Receive,
            record.Channel,
            record.Elapsed,
            record.ToHex())
        {
            Symbols = record.Symbols,
            Note = record.Note,
        });

    /// <summary>
    /// How often this session's host polled, taken from the gaps between its own polls.
    /// </summary>
    /// <remarks>
    /// The commonest gap between one kind of poll, rounded to ten milliseconds, and not the mean
    /// or the median. A poll loop skips a general poll in any cycle a long poll took, so a session
    /// with long polls in it has a mean and a median around twice the interval; and a cycle is two
    /// polls thirty milliseconds apart, so measuring across both kinds reads that gap as the
    /// interval. Both were measured wrong before this. The commonest gap between the polls that
    /// come once per cycle is the interval whatever else the session did. Null where there were
    /// too few to say, and the script then leaves the interval to the run.
    /// </remarks>
    private static TimeSpan? Interval(TransactionSplit split)
    {
        //  One kind of poll, and one per cycle. A cycle is a synchronisation poll and then a
        //  general poll thirty milliseconds later, so measuring across both kinds reads the gap
        //  inside a cycle as the interval - which is how this first read a two-hundred
        //  millisecond loop as thirty.
        ExchangeKind cycle = split.Exchanges.Any(e => e.Kind == ExchangeKind.SyncPoll)
            ? ExchangeKind.SyncPoll
            : ExchangeKind.GeneralPoll;

        TimeSpan[] polls = [.. split.Exchanges.Where(e => e.Kind == cycle).Select(e => e.At)];

        int[] gaps =
        [
            .. polls.Zip(polls.Skip(1), (before, after) => after - before)
                .Where(gap => gap > TimeSpan.Zero)
                .Select(gap => (int)Math.Round(gap.TotalMilliseconds / 10) * 10)
                .Where(ms => ms > 0),
        ];

        if (gaps.Length < 4)
            return null;

        int commonest = gaps.GroupBy(ms => ms).OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key).First().Key;

        return TimeSpan.FromMilliseconds(commonest);
    }

    private static void Report(RecordedSession session, EmittedScript emitted)
    {
        Console.WriteLine($"  {session.Name}");
        Console.WriteLine($"    packets      {session.Records.Count:N0}");
        Console.WriteLine($"    episodes     {emitted.Episodes.Count}");
        Console.WriteLine($"    steps        {emitted.Steps}");
        Console.WriteLine($"    assertions   {emitted.Assertions}");
        Console.WriteLine($"    caveats      {emitted.Caveats.Count}");

        foreach (CaveatKind kind in emitted.Caveats.Select(c => c.Kind).Distinct())
        {
            int count = emitted.Caveats.Count(c => c.Kind == kind);
            Console.WriteLine($"      {count,4}  {RecordingCaveat.Describe(kind)}");
        }

        Console.WriteLine();
    }

    private static string Write(
        string directory, RecordedSession session, EmittedScript emitted, int sessions)
    {
        Directory.CreateDirectory(directory);
        string file = Path.Combine(
            directory,
            sessions == 1 ? "recorded.sas" : $"{Safe(session.Name)}.sas");

        File.WriteAllText(file, emitted.Script);
        return file;
    }

    private static string Safe(string name)
    {
        char[] cleaned = [.. name.Select(c =>
            char.IsLetterOrDigit(c) || c is '.' or '-' ? char.ToLowerInvariant(c) : '-')];

        return string.Join('-', new string(cleaned).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>What a recording cannot capture, said once and in one place.</summary>
    private static void Caveats()
    {
        Console.WriteLine("WHAT A RECORDING CANNOT CAPTURE");
        Console.WriteLine();
        Console.WriteLine("  A recorded script that passes for the wrong reason is worse than no");
        Console.WriteLine("  recorder at all, so each of these is reported rather than written out");
        Console.WriteLine("  as a step that would do something else.");
        Console.WriteLine();

        foreach (CaveatKind kind in Enum.GetValues<CaveatKind>())
        {
            Console.WriteLine($"  {Name(kind)}");
            foreach (string wrapped in Wrap(RecordingCaveat.Describe(kind), 72))
                Console.WriteLine($"      {wrapped}");
            Console.WriteLine();
        }

        Console.WriteLine("WHAT IS WRITTEN INSTEAD");
        Console.WriteLine();
        Console.WriteLine("  a field the machine chose      nothing, and the value is listed above");
        Console.WriteLine("  a ticket's own number          compared against the ticket the printer");
        Console.WriteLine("                                 filed, which is the only form of that");
        Console.WriteLine("                                 question that means anything");
        Console.WriteLine("  a person's hesitation          nothing at all");
        Console.WriteLine("  a wait for the machine         an expectation with a window: what was");
        Console.WriteLine("                                 measured, times "
                          + $"{ScriptEmitter.WindowFactor:F0}, floored at "
                          + $"{ScriptEmitter.ShortestWindow.TotalSeconds:F0}s");
        Console.WriteLine("  a step in an ambiguous split   the step, with the split's own reason");
        Console.WriteLine("                                 for not being able to tell it from");
        Console.WriteLine("                                 another of its kind");
        Console.WriteLine();
        Console.WriteLine("  A field is the machine's settings or its condition according to the");
        Console.WriteLine("  same table the replay classifier uses, where a name in neither list is");
        Console.WriteLine("  reported rather than assumed harmless.");
    }

    private static string Name(CaveatKind kind) => kind switch
    {
        CaveatKind.UnwiredStimulus => "a stimulus nobody wired",
        CaveatKind.MachineChosenValue => "a value the machine chose",
        CaveatKind.HumanHesitation => "a person's hesitation",
        CaveatKind.AmbiguousEpisode => "a step whose transaction is a guess",
        CaveatKind.NotExpressible => "something with no step for it",
        _ => kind.ToString(),
    };

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new System.Text.StringBuilder();
        foreach (string word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + word.Length + 1 > width)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
                line.Append(' ');
            line.Append(word);
        }

        if (line.Length > 0)
            yield return line.ToString();
    }
}
