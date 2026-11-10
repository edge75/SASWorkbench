using System.Diagnostics;
using SasWorkbench.Core.Polling;

namespace SasWorkbench.Core.Scripting;

/// <summary>
/// Brings a machine that has just restarted back into step before a sequenced case polls it.
/// </summary>
/// <remarks>
/// A machine starts up, and comes out of a loop break, chirping its address and ignoring polls for
/// itself until it sees a poll to address zero (SAS 6.02 section 3.3). It stops chirping on its own
/// timer tick after that, so the host keeps sending synchronisation polls until two in a row draw
/// nothing. Only synchronisation polls are sent, so no exception is taken off the queue.
/// </remarks>
public sealed record MachineResynchronisation(bool Failed, string Summary)
{
    /// <summary>Two quiet polls this far apart cover a machine's 200 ms timer tick twice over.</summary>
    public static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(200);

    /// <summary>How long a machine may go on chirping before the step is failed.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    private const int QuietPollsNeeded = 2;

    /// <summary>Does nothing outside a sequence, where the host's own round keeps the machine in step.</summary>
    public static MachineResynchronisation? Run(PollEngine engine) => Run(engine, Limit, Spacing);

    public static MachineResynchronisation? Run(PollEngine engine, TimeSpan limit, TimeSpan spacing)
    {
        if (!engine.IsSequenced)
            return null;

        Stopwatch watch = Stopwatch.StartNew();
        int quiet = 0;
        int chirps = 0;
        int polls = 0;

        while (quiet < QuietPollsNeeded)
        {
            GeneralPollResult result = engine.Synchronise(
                purpose: "resynchronise the machine after it restarted");
            polls++;

            if (result.Outcome == GeneralPollOutcome.NotSent)
                return new MachineResynchronisation(true, result.Detail ?? "the poll was not sent");

            if (result.Answered)
            {
                quiet = 0;
                chirps += result.Frame.Length;
            }
            else
            {
                quiet++;
            }

            if (quiet < QuietPollsNeeded && watch.Elapsed >= limit)
            {
                return new MachineResynchronisation(true,
                    $"still chirping after {polls} synchronisation polls in {watch.Elapsed.TotalSeconds:F0}s");
            }

            if (quiet < QuietPollsNeeded)
                Thread.Sleep(spacing);
        }

        string heard = chirps == 0
            ? "the machine was already quiet"
            : $"{chirps} chirp byte{(chirps == 1 ? string.Empty : "s")} before it went quiet";
        return new MachineResynchronisation(false,
            $"the host resynchronised the machine with {polls} synchronisation polls; {heard}");
    }
}
