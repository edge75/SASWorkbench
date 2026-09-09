using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Transactions;

namespace SasWorkbench.Core.Replay;

/// <summary>One field the two machines answered differently.</summary>
/// <param name="Path">How a script would name it.</param>
/// <param name="Recorded">What the recording holds.</param>
/// <param name="Live">What the machine answered.</param>
/// <param name="Cause">Which kind of difference it is, and why.</param>
public sealed record ReplayFieldDiff(string Path, string Recorded, string Live, FieldCause Cause)
{
    public override string ToString() =>
        $"{Path}: recorded {Recorded}, answered {Live}  — {Cause.Cause}, {Cause.Why}";
}

/// <summary>
/// What replaying one recorded request against a machine came to.
/// </summary>
public sealed record ReplayFinding
{
    /// <summary>The long poll, when the recorded exchange was one.</summary>
    public byte? Command { get; init; }

    /// <summary>The request bytes after the address and the command, as the recording holds them.</summary>
    public byte[] Request { get; init; } = [];

    /// <summary>
    /// How many exchanges in the recording sent this exact request, so a reader knows whether one
    /// row stands for one poll or for a thousand.
    /// </summary>
    public required int Occurrences { get; init; }

    /// <summary>What the recording says came back.</summary>
    public byte[] RecordedReply { get; init; } = [];

    /// <summary>What the recording's answer was.</summary>
    public required ExchangeAnswer RecordedAnswer { get; init; }

    /// <summary>What the machine answered now. Empty when nothing did.</summary>
    public byte[] LiveReply { get; init; } = [];

    /// <summary>What became of the live poll.</summary>
    public LongPollOutcome LiveOutcome { get; init; }

    /// <summary>Which of the causes this difference is.</summary>
    public required ReplayCause Cause { get; init; }

    /// <summary>One sentence saying what the difference means, in a report's own voice.</summary>
    public required string Explanation { get; init; }

    /// <summary>The fields that differed, when both replies could be decoded.</summary>
    public IReadOnlyList<ReplayFieldDiff> Fields { get; init; } = [];

    /// <summary>
    /// Fields measured to move between two reads of the live machine, so they are state whatever
    /// any table says.
    /// </summary>
    public IReadOnlyList<string> MovedOnTheMachine { get; init; } = [];

    /// <summary>Fields the recording itself shows moving between two answers to this request.</summary>
    public IReadOnlyList<string> MovedInTheRecording { get; init; } = [];

    /// <summary>Fields this build classifies neither way, which is the gap in the table.</summary>
    public IReadOnlyList<string> Unclassified { get; init; } = [];

    /// <summary>The wire records the replay itself produced, for the run's record.</summary>
    public IReadOnlyList<WireRecord> Wire { get; init; } = [];

    /// <summary>True when the difference is one nothing here explains.</summary>
    public bool IsFinding => Cause == ReplayCause.Defect;

    public override string ToString() =>
        $"${Command ?? 0:X2} ×{Occurrences}  {Cause}  {Explanation}";
}
