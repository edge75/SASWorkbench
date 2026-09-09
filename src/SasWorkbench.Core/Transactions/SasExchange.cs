using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Transactions;

/// <summary>What the host put on the wire to open an exchange.</summary>
public enum ExchangeKind
{
    /// <summary>A general poll of address zero, which no machine answers.</summary>
    SyncPoll,

    /// <summary>A general poll of one machine, asking for its next queued exception.</summary>
    GeneralPoll,

    /// <summary>An addressed command.</summary>
    LongPoll,

    /// <summary>A machine message with no host message before it to answer.</summary>
    Orphan,
}

/// <summary>What came back.</summary>
public enum ExchangeAnswer
{
    /// <summary>Nothing at all. A fact about the machine, never dropped.</summary>
    Silent,

    /// <summary>The machine reported an exception in the plain one-byte form.</summary>
    Exception,

    /// <summary>The machine said it had nothing queued.</summary>
    NoException,

    /// <summary>The machine answered a general poll with a whole real-time event message.</summary>
    RealTimeEvent,

    /// <summary>A reply carrying data, echoing the command it answers.</summary>
    Data,

    /// <summary>The machine's own address alone: it accepted the command and has nothing to send.</summary>
    Acknowledged,

    /// <summary>The machine's address with bit 7 set: it will not do what was asked.</summary>
    Refused,

    /// <summary>The machine's address and a zero: it has the command and wants polling again.</summary>
    Busy,

    /// <summary>Bytes came back that do not stand as a message, so nothing is read out of them.</summary>
    NotAMessage,

    /// <summary>
    /// One byte with two readings and nothing in the record to choose between them.
    /// </summary>
    Ambiguous,
}

/// <summary>
/// One host message and the machine messages that answer it — the smallest unit of a session
/// that means anything on its own.
/// </summary>
/// <remarks>
/// <para>
/// A capture is a flat list of packets, and a packet on its own answers almost no question worth
/// asking. What a reply says is a fact about the poll it answers, so the exchange is the unit, and
/// building it is the first thing anything reading somebody else's traffic has to do.
/// </para>
/// <para>
/// Attribution is by <b>what a message says</b>, not by where it sits. A reply echoes the address
/// and the command it answers, so a reply that echoes neither is not attributed to whatever
/// happened to precede it — it is reported as answering nothing, and the poll before it as
/// unanswered. That is the same rule the poll engine itself was corrected to use, and for the same
/// reason: attributing by position reports one command's answer under another command's name, and
/// a splitter that did it would produce a tidy account of a session that never happened.
/// </para>
/// <para>
/// Where a message cannot be attributed by what it says — a run of bytes whose check sequence does
/// not check out has no readable command in it — the exchange records that its answer was placed
/// by position, in <see cref="Ambiguity"/>. Nothing is repaired and nothing is hidden.
/// </para>
/// </remarks>
public sealed record SasExchange
{
    /// <summary>Index within the split, so a reader can tell one from another.</summary>
    public required int Index { get; init; }

    /// <summary>What the host sent.</summary>
    public required ExchangeKind Kind { get; init; }

    /// <summary>The host message, or the unattributed machine message when this is an orphan.</summary>
    public required WireRecord Request { get; init; }

    /// <summary>The long poll, when this is one.</summary>
    public byte? Command { get; init; }

    /// <summary>The machine being addressed, when the record shows it.</summary>
    public SasAddress? Address { get; init; }

    /// <summary>What came back.</summary>
    public required ExchangeAnswer Answer { get; init; }

    /// <summary>Every machine message attributed to this exchange, in the order recorded.</summary>
    public IReadOnlyList<WireRecord> Replies { get; init; } = [];

    /// <summary>
    /// How many times the host repeated the same message before this exchange was answered.
    /// </summary>
    /// <remarks>
    /// The protocol makes a repeated long poll an implied instruction to send the answer again, so
    /// a host that repeats is retrying rather than asking twice. Collapsing the repeats into one
    /// exchange is what stops a stretch of silence reading as a dozen separate questions; the
    /// count is kept because it is the measure of how much silence there was.
    /// </remarks>
    public int Retries { get; init; }

    /// <summary>When the host message went out, on the record's own clock.</summary>
    public TimeSpan At => Request.Elapsed;

    /// <summary>Time from the host message to the first answer, when there was one.</summary>
    public TimeSpan? Latency =>
        Replies.Count == 0 ? null : Replies[0].Elapsed - Request.Elapsed;

    /// <summary>Why this exchange's attribution is not certain, when it is not.</summary>
    public string? Ambiguity { get; init; }

    /// <summary>The exception the machine reported in this exchange, when it reported one.</summary>
    /// <remarks>
    /// Only where the reading is not in doubt. A one-byte answer to a general poll is an exception
    /// code; a one-byte answer to a long poll on a machine whose address the record never showed
    /// has two readings, and this stays null rather than choosing one.
    /// </remarks>
    public byte? Exception { get; init; }

    /// <summary>True when the exchange was answered in a way the protocol provides for.</summary>
    public bool Answered => Answer is not (ExchangeAnswer.Silent or ExchangeAnswer.NotAMessage
        or ExchangeAnswer.Ambiguous);

    /// <summary>The host message's bytes, wakeup bits dropped.</summary>
    public byte[] RequestBytes => [.. Request.Symbols.Select(s => s.Data)];

    /// <summary>The first answer's bytes, or empty when there was none.</summary>
    public byte[] ReplyBytes =>
        Replies.Count == 0 ? [] : [.. Replies[0].Symbols.Select(s => s.Data)];

    public override string ToString()
    {
        string what = Kind switch
        {
            ExchangeKind.SyncPoll => "sync poll",
            ExchangeKind.GeneralPoll => $"general poll of ${Address?.Value ?? 0:X2}",
            ExchangeKind.LongPoll => $"long poll ${Command ?? 0:X2}",
            _ => "unattributed machine message",
        };

        string retries = Retries == 0 ? string.Empty : $" (+{Retries} repeat(s))";
        return $"[{At.TotalMilliseconds,9:F1}ms] {what}{retries} → {Answer}";
    }
}
