using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Polling;

/// <summary>What became of a general poll, or of a broadcast to the synchronisation address.</summary>
public enum GeneralPollOutcome
{
    /// <summary>The machine reported an exception.</summary>
    Exception,

    /// <summary>The machine answered that it has nothing to report.</summary>
    NoActivity,

    /// <summary>Nothing came back before the timeout.</summary>
    Silent,

    /// <summary>
    /// A message came back that could not be read as an answer to a general poll — a real-time
    /// event whose marker, address or check sequence did not hold up.
    /// </summary>
    Unreadable,

    /// <summary>The machine answered its own address with bit 7 set, refusing the poll.</summary>
    Rejected,

    /// <summary>
    /// A broadcast to address 00, which no machine answers. Silence here is the correct answer
    /// and is not reported as a machine failing to speak.
    /// </summary>
    Broadcast,

    /// <summary>The engine was not running, or the poll could not be put on the link.</summary>
    NotSent,
}

/// <summary>
/// The result of one general poll a caller asked for by name.
/// </summary>
/// <remarks>
/// The timer-driven round does not produce these — it reports what it draws through
/// <see cref="PollEngine.ExceptionReported"/> and keeps no per-poll record. This exists for the
/// sequenced mode, where a case names every message on the line and has to be able to say what
/// each one drew, in order, rather than watching a stream and hoping the order held.
/// </remarks>
/// <param name="Address">Which address the poll carried.</param>
/// <param name="Channel">Which channel carried it.</param>
/// <param name="Outcome">What became of it.</param>
/// <param name="Code">
/// The exception code the machine reported. Zero means it reported no activity, which is a
/// statement and not an absence.
/// </param>
/// <param name="Frame">Every byte that came back, so a test can assert on the wire.</param>
/// <param name="Latency">Time from the last symbol sent to the first symbol received.</param>
/// <param name="Detail">One sentence naming the problem, when there was one.</param>
public sealed record GeneralPollResult(
    SasAddress Address,
    SasChannel Channel,
    GeneralPollOutcome Outcome,
    byte Code,
    byte[] Frame,
    TimeSpan Latency,
    string? Detail = null)
{
    /// <summary>True when the machine said something, whatever it said.</summary>
    public bool Answered => Frame.Length > 0;

    /// <summary>True when the machine answered in the real-time event format.</summary>
    public bool IsRealTimeEvent { get; init; }

    /// <summary>What the exception carried after its code, when it came in the event format.</summary>
    public byte[]? Data { get; init; }

    /// <summary>A single line for a report or a log.</summary>
    public override string ToString()
    {
        string frame = Frame.Length == 0 ? "-" : Convert.ToHexString(Frame);
        string code = Outcome == GeneralPollOutcome.Exception ? $" ${Code:X2}" : string.Empty;
        string detail = Detail is null ? string.Empty : $" — {Detail}";
        return $"GP ${Address} ch{(int)Channel} {Outcome}{code} {frame} "
               + $"({Latency.TotalMilliseconds:F1}ms){detail}";
    }
}
