using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Polling;

/// <summary>What became of a long poll.</summary>
public enum LongPollOutcome
{
    /// <summary>A reply arrived and, where one was present, its check sequence was good.</summary>
    Answered,

    /// <summary>The machine acknowledged a command that returns no data.</summary>
    Acknowledged,

    /// <summary>
    /// The machine rejected the command. It answers its own address with bit 7 set to say so —
    /// usually a bad check sequence or a command it will not honour in its current state.
    /// </summary>
    Rejected,

    /// <summary>Nothing came back before the timeout.</summary>
    Silent,

    /// <summary>A reply arrived but its check sequence did not match.</summary>
    BadCheckSequence,

    /// <summary>A reply arrived addressed to a different machine.</summary>
    WrongAddress,

    /// <summary>The engine was not running, or the link had no peer.</summary>
    NotSent,

    /// <summary>
    /// The machine answered with a queued real-time event instead of this poll's own reply, which
    /// it does while event reporting is on and an exception is queued.
    /// </summary>
    /// <remarks>
    /// Deliberately not a success: the event is kept and reported, but the caller did not get the
    /// reply it asked for, and the host does not ask again — repeating the same poll is an implied
    /// NACK and would only fetch the same event. Last in the enum because these values are written
    /// to reports by number, and a run recorded before this existed must still read back the same.
    /// </remarks>
    AnsweredWithEvent,
}

/// <summary>
/// The result of one long poll: everything a test needs in order to assert, and everything a
/// report needs in order to explain a failure.
/// </summary>
/// <param name="Command">The command that was sent.</param>
/// <param name="Channel">Which channel carried it.</param>
/// <param name="Outcome">What became of it.</param>
/// <param name="Frame">
/// Every byte that came back, address and check sequence included, so a test can assert on the
/// wire rather than on our interpretation of it.
/// </param>
/// <param name="Latency">Time from the last symbol sent to the first symbol received.</param>
/// <param name="Detail">One sentence naming the problem, when there was one.</param>
public sealed record LongPollResult(
    byte Command,
    SasChannel Channel,
    LongPollOutcome Outcome,
    byte[] Frame,
    TimeSpan Latency,
    string? Detail = null)
{
    /// <summary>True when the machine answered in a usable way.</summary>
    public bool Succeeded => Outcome is LongPollOutcome.Answered or LongPollOutcome.Acknowledged;

    /// <summary>
    /// The reply with the address, the echoed command and the check sequence removed — the part
    /// that carries meaning. Empty when there is none.
    /// </summary>
    public ReadOnlySpan<byte> Payload
    {
        get
        {
            // address + command + at least one payload byte + two check bytes
            if (Outcome != LongPollOutcome.Answered || Frame.Length < 5)
                return default;
            return Frame.AsSpan(2, Frame.Length - 4);
        }
    }

    /// <summary>Renders the reply the way the captures do.</summary>
    public string ToHex() => Convert.ToHexString(Frame);

    /// <summary>A single line for a report or a log.</summary>
    public override string ToString()
    {
        string frame = Frame.Length == 0 ? "-" : ToHex();
        string detail = Detail is null ? string.Empty : $" — {Detail}";
        return $"LP ${Command:X2} ch{(int)Channel} {Outcome} {frame} "
               + $"({Latency.TotalMilliseconds:F1}ms){detail}";
    }
}

/// <summary>
/// One exception reported by a machine in answer to a general poll.
/// </summary>
/// <param name="Code">The exception code. Zero means the queue is empty and is never reported.</param>
/// <param name="Channel">Which channel it arrived on.</param>
/// <param name="Elapsed">Time since the engine started.</param>
/// <param name="Sequence">Monotonic index within the run.</param>
/// <param name="Data">
/// What the exception carried after its code, when the machine was reporting in the real-time
/// event format. Null when it answered with the bare code, which is a different thing from an
/// empty payload: most exceptions carry nothing even in the event format, and those arrive with an
/// empty array rather than with null.
/// </param>
public sealed record ExceptionReport(
    byte Code, SasChannel Channel, TimeSpan Elapsed, long Sequence, byte[]? Data = null)
{
    /// <summary>
    /// The machine that raised it, or null where this build cannot say which.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A general poll is answered with a bare exception code and no address in it at all, so which
    /// machine sent one is answerable only from the poll it answered. The engine knows that, and
    /// carries it here — without it, an assertion on one machine of a floor is satisfied by any
    /// machine on the line raising the same code.
    /// </para>
    /// <para>
    /// Nullable with the same hard rule the wire record has: null means <em>not known</em>, never
    /// <em>machine one</em>. A report read back from an older run has none, and filling it in
    /// would attribute a whole floor's exceptions to whichever machine happened to be first.
    /// </para>
    /// </remarks>
    public SasAddress? Address { get; init; }

    /// <summary>True when the machine reported this in the real-time event format.</summary>
    public bool IsRealTimeEvent => Data is not null;

    public override string ToString()
    {
        string carried = Data is { Length: > 0 }
            ? " carrying " + Convert.ToHexString(Data)
            : string.Empty;
        string from = Address is { } address ? $" ${address}" : string.Empty;
        return $"exception ${Code:X2} ch{(int)Channel}{from} at "
               + $"{Elapsed.TotalMilliseconds:F0}ms{carried}";
    }
}
