namespace SasWorkbench.Core.Polling;

/// <summary>How the link is doing.</summary>
public enum LinkState
{
    /// <summary>No peer is attached, or nothing has been heard for longer than the link-down time.</summary>
    Down,

    /// <summary>
    /// Polling has started but the machine has not answered yet. This is the expected state
    /// straight after power-up or a loop break, and it is not a fault: a machine deliberately
    /// ignores polls addressed to itself until it has resynchronised.
    /// </summary>
    Synchronising,

    /// <summary>The machine is answering.</summary>
    Up,
}

/// <summary>Counters for a run, so a report can say what the link actually did.</summary>
public sealed record PollStatistics(
    long SyncPolls,
    long GeneralPolls,
    long LongPolls,
    long Exceptions,
    long Silences,
    long BadCheckSequences,
    long Rejections,
    long WakeupBitsFromMachine,
    long SymbolsDiscarded)
{
    public static PollStatistics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
}
