using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Wire;
using SasWorkbench.Egm;

namespace SasWorkbench.Cli;

/// <summary>
/// Brings up a floor of software machines on one line and measures what a round of them costs.
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
/// </remarks>
internal static class FloorCommand
{
    public static int Execute(CommandLine line)
    {
        if (!TryCount(line, "machines", 8, out int machines)
            || !TryCount(line, "rounds", 10, out int rounds))
        {
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

        using SoftwareFloor floor = SoftwareFloor.Of(machines);

        var options = new PollEngineOptions
        {
            Address = floor.Addresses[0],
            Addresses = floor.Addresses,

            // Zero by default, so the round's cost is what is measured rather than the interval a
            // host was told to keep. A floor host with a hundred machines cannot keep a 200 ms
            // interval per machine anyway: that is twenty seconds a round.
            PollInterval = interval,
            SyncPollEvery = syncPolls ? 1 : 0,
            WireLogCapacity = Math.Max(4096, machines * rounds * 8),
        };

        using var engine = new PollEngine(floor.Host, options);

        Console.WriteLine(
            $"A floor of {machines} software machine(s) at {floor.Addresses[0]}-"
            + $"{floor.Addresses[^1]} on one multi-drop line, one host walking the round.");
        Console.WriteLine(
            syncPolls
                ? "  A synchronisation poll of address zero goes out ahead of each general poll."
                : "  No synchronisation polls: in a round, the next machine's poll is already a "
                  + "poll for another address.");
        Console.WriteLine($"  Poll interval {interval.TotalMilliseconds:F0} ms, quiet gap "
                          + $"{options.QuietGap.TotalMilliseconds:F0} ms.");
        Console.WriteLine();

        engine.Start();
        if (!WaitFor(engine, () => engine.RoundsCompleted >= rounds + 1, TimeSpan.FromMinutes(5)))
        {
            engine.Stop();
            Console.Error.WriteLine(
                $"error: only {engine.RoundsCompleted} round(s) completed. Nothing is reported, "
                + "because a round count that did not reach what was asked measures a stall "
                + "rather than a round.");
            return 2;
        }

        engine.Stop();

        IReadOnlyList<WireRecord> wire = engine.WireLog.Snapshot();
        RoundMeasurement measured = RoundMeasurement.Of(wire, floor.Addresses);
        Report(measured, engine, floor, wire);
        return measured.Silent.Count == 0 ? 0 : 1;
    }

    private static void Report(
        RoundMeasurement measured, PollEngine engine, SoftwareFloor floor,
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
                "  A machine that answered nothing is reported as a failure rather than left in a "
                + "timing table: the round still cost a round, and that machine was not in it.");
        }
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

    private static bool WaitFor(PollEngine engine, Func<bool> done, TimeSpan limit)
    {
        DateTime deadline = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < deadline)
        {
            if (done())
                return true;
            Thread.Sleep(10);
        }

        return done();
    }
}
