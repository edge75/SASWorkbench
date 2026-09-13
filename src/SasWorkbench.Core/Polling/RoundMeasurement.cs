using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Polling;

/// <summary>
/// What a round of machines cost, measured off the wire rather than worked out from a per-machine
/// figure.
/// </summary>
/// <remarks>
/// A round is measured from the general poll of the first machine to the next general poll of the
/// same machine, so everything the host did in between — every other machine's poll, every reply,
/// every wait — is inside the number. Multiplying one machine's cost by the count is an estimate;
/// this is the thing itself, and the two are reported side by side because a difference between
/// them is a finding about the host.
/// </remarks>
/// <param name="Machines">How many machines the round visits.</param>
/// <param name="Rounds">How many complete rounds were seen.</param>
/// <param name="Samples">Each round's duration, in order.</param>
/// <param name="Answered">The machines that answered at least one poll.</param>
/// <param name="Silent">The machines that answered none.</param>
public sealed record RoundMeasurement(
    int Machines,
    int Rounds,
    IReadOnlyList<TimeSpan> Samples,
    IReadOnlyList<SasAddress> Answered,
    IReadOnlyList<SasAddress> Silent)
{
    /// <summary>The shortest round seen.</summary>
    public TimeSpan Fastest => Samples.Count == 0 ? TimeSpan.Zero : Samples.Min();

    /// <summary>The middle round, which is the one to quote.</summary>
    public TimeSpan Median
    {
        get
        {
            if (Samples.Count == 0)
                return TimeSpan.Zero;

            List<TimeSpan> sorted = [.. Samples.Order()];
            return sorted[sorted.Count / 2];
        }
    }

    /// <summary>The longest round seen.</summary>
    public TimeSpan Slowest => Samples.Count == 0 ? TimeSpan.Zero : Samples.Max();

    /// <summary>The median round divided by the machines in it.</summary>
    public TimeSpan PerMachine =>
        Machines == 0 ? TimeSpan.Zero : Median / Machines;

    /// <summary>
    /// Measures the rounds in a wire log.
    /// </summary>
    /// <param name="wire">Everything the host sent and heard, in order.</param>
    /// <param name="round">The addresses the host polls, in the order it polls them.</param>
    public static RoundMeasurement Of(IReadOnlyList<WireRecord> wire, IReadOnlyList<SasAddress> round)
    {
        ArgumentNullException.ThrowIfNull(wire);
        ArgumentNullException.ThrowIfNull(round);
        ArgumentOutOfRangeException.ThrowIfZero(round.Count);

        SasAddress first = round[0];
        var starts = new List<TimeSpan>();
        var answered = new HashSet<byte>();
        SasAddress? awaiting = null;

        foreach (WireRecord record in wire)
        {
            if (record.Direction == WireDirection.Transmit)
            {
                if (GeneralPollFor(record) is not { } polled)
                    continue;

                awaiting = polled.IsSync ? null : polled;
                if (polled == first)
                    starts.Add(record.Elapsed);

                continue;
            }

            if (record.Symbols.Length == 0)
                continue;

            // The record's own address where it has one, and otherwise the machine whose poll was
            // outstanding. The second is what a reading of an older record or an import falls back
            // to; the first is what a round records, and it is the only one that survives a file
            // being filtered or reordered.
            if (record.Address is { } stated)
            {
                answered.Add(stated.Value);
                awaiting = null;
                continue;
            }

            // Anything at all coming back while a machine's poll is outstanding is that machine
            // having spoken. What it said is a question for the decoder; that it answered is the
            // only thing a round needs, and a silent address is the finding worth surfacing.
            if (awaiting is { } machine)
            {
                answered.Add(machine.Value);
                awaiting = null;
            }
        }

        var samples = new List<TimeSpan>(Math.Max(0, starts.Count - 1));
        for (int i = 1; i < starts.Count; i++)
            samples.Add(starts[i] - starts[i - 1]);

        return new RoundMeasurement(
            round.Count,
            samples.Count,
            samples,
            [.. round.Where(a => answered.Contains(a.Value))],
            [.. round.Where(a => !answered.Contains(a.Value))]);
    }

    /// <summary>The address a one-symbol general poll asks for, or null when it is not one.</summary>
    private static SasAddress? GeneralPollFor(WireRecord record)
    {
        if (record.Symbols.Length != 1)
            return null;

        SasSymbol symbol = record.Symbols[0];
        if (!symbol.Wakeup || (symbol.Data & HostMessage.GeneralPollFlag) == 0)
            return null;

        return SasAddress.Of((byte)(symbol.Data & ~HostMessage.GeneralPollFlag));
    }
}
