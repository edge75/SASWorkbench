using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Diagnostics;

/// <summary>Whose messages a search looks at.</summary>
public enum TraceSide
{
    /// <summary>Both ends of the link.</summary>
    Both,

    /// <summary>Only what the host put on the wire.</summary>
    Host,

    /// <summary>Only what the gaming machine answered.</summary>
    Machine,
}

/// <summary>
/// What a message on the wire turned out to be, worked out from its own bytes.
/// </summary>
/// <param name="Kind">Which of the three host messages it is, when the host sent it.</param>
/// <param name="Command">The long poll command, for a host command or a machine's reply to one.</param>
/// <param name="Exception">A single byte the machine answered a general poll with.</param>
public readonly record struct TraceSubject(
    HostMessageKind? Kind = null,
    byte? Command = null,
    byte? Exception = null);

/// <summary>
/// A search over recorded messages: a long poll, an exception, a kind of poll, or free text,
/// narrowed to one end of the link or to both.
/// </summary>
/// <remarks>
/// The one thing here that is genuinely ambiguous is a single byte from the machine. A general
/// poll is answered with an exception code, and a long poll may be answered with the machine's
/// own address as an acknowledgement, so a one-byte reply of <c>$4A</c> is exception 4A on a
/// machine at any other address and an acknowledgement on the machine at address 4A. Give
/// <see cref="Address"/> when it is known and the two are told apart; leave it out and a search
/// for an exception may also find an acknowledgement, which is stated rather than hidden.
/// </remarks>
public sealed record TraceQuery
{
    /// <summary>Find a long poll by command, in either direction.</summary>
    public byte? LongPoll { get; init; }

    /// <summary>Find an exception the machine reported.</summary>
    public byte? Exception { get; init; }

    /// <summary>Find polls of one kind.</summary>
    public HostMessageKind? Poll { get; init; }

    /// <summary>Which end of the link to look at.</summary>
    public TraceSide Side { get; init; } = TraceSide.Both;

    /// <summary>Find a message whose rendered symbols or note contain this text.</summary>
    public string? Text { get; init; }

    /// <summary>The machine's poll address, which tells an acknowledgement from an exception.</summary>
    public SasAddress? Address { get; init; }

    /// <summary>True when nothing has been asked for, so every message matches.</summary>
    public bool IsEmpty =>
        LongPoll is null && Exception is null && Poll is null
        && string.IsNullOrWhiteSpace(Text) && Side == TraceSide.Both;

    /// <summary>Whether this message is one the search was looking for.</summary>
    public bool Matches(WireDirection direction, ReadOnlySpan<SasSymbol> symbols, string? note)
    {
        if (!SideAllows(direction))
            return false;

        if (IsEmpty)
            return true;

        TraceSubject subject = Classify(direction, symbols, Address);

        // Every term given has to hold. A search for long poll 2F on the machine's side means the
        // reply to 2F, not "either a 2F somewhere or anything the machine said".
        if (LongPoll is { } command && subject.Command != command)
            return false;

        if (Exception is { } code && subject.Exception != code)
            return false;

        if (Poll is { } kind && subject.Kind != kind)
            return false;

        if (!string.IsNullOrWhiteSpace(Text) && !ContainsText(symbols, note, Text))
            return false;

        return true;
    }

    /// <summary>
    /// Works out what a message is from its own bytes rather than from the note beside it, so a
    /// record written by something that noted nothing is still searchable.
    /// </summary>
    public static TraceSubject Classify(
        WireDirection direction, ReadOnlySpan<SasSymbol> symbols, SasAddress? address = null)
    {
        if (symbols.Length == 0)
            return default;

        if (direction == WireDirection.Transmit)
        {
            byte first = symbols[0].Data;
            if (symbols.Length == 1)
            {
                return (first & HostMessage.GeneralPollFlag) != 0
                    ? new TraceSubject(Kind: first == HostMessage.GeneralPollFlag
                        ? HostMessageKind.SyncPoll
                        : HostMessageKind.GeneralPoll)
                    : default;
            }

            return new TraceSubject(HostMessageKind.LongPoll, Command: symbols[1].Data);
        }

        // From the machine. One byte is an exception code, unless it is this machine
        // acknowledging or rejecting, which is only knowable when the address is.
        if (symbols.Length == 1)
        {
            byte value = symbols[0].Data;
            if (address is { } known
                && (value == known.Value || value == (byte)(known.Value | HostMessage.GeneralPollFlag)))
            {
                return default;
            }

            return new TraceSubject(Exception: value);
        }

        // A reply of two or more echoes the address and then the command it is answering.
        return new TraceSubject(Command: symbols[1].Data);
    }

    /// <summary>
    /// Reads a search written as one line, which is what a box in a window gets typed into.
    /// </summary>
    /// <remarks>
    /// Deliberately small: <c>lp 2F</c>, <c>exception 4A</c> or its name, <c>general</c>,
    /// <c>sync</c>, <c>long</c>, and anything else taken as text to look for. A term that does not
    /// parse is text rather than an error, because a search box that rejects what was typed is
    /// worse than one that looks for it.
    /// </remarks>
    public static TraceQuery Parse(string? text, TraceSide side = TraceSide.Both)
    {
        var query = new TraceQuery { Side = side };
        if (string.IsNullOrWhiteSpace(text))
            return query;

        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i].ToLowerInvariant();
            string? next = i + 1 < words.Length ? words[i + 1] : null;

            switch (word)
            {
                case "lp" or "longpoll" when next is not null && SasNames.TryParseCode(next, out byte lp):
                    query = query with { LongPoll = lp };
                    i++;
                    continue;

                case "exc" or "exception" when next is not null && SasNames.TryResolveException(next, out byte ex):
                    query = query with { Exception = ex };
                    i++;
                    continue;

                case "sync":
                    query = query with { Poll = HostMessageKind.SyncPoll };
                    continue;

                case "general":
                    query = query with { Poll = HostMessageKind.GeneralPoll };
                    continue;

                case "long":
                    query = query with { Poll = HostMessageKind.LongPoll };
                    continue;
            }

            // A bare exception name, such as 'slot_door_opened', is a search for that exception.
            // A bare code is not: '2F' is far more likely to be someone looking for those two
            // characters in the symbols than for exception 2F, and 'exc 2F' says the other thing.
            if (!SasNames.TryParseCode(words[i], out _)
                && SasNames.TryResolveException(words[i], out byte named))
            {
                query = query with { Exception = named };
                continue;
            }

            query = query with
            {
                Text = query.Text is null ? words[i] : $"{query.Text} {words[i]}",
            };
        }

        return query;
    }

    /// <summary>One line describing what was searched for, for a report or a header.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (LongPoll is { } lp)
            parts.Add($"long poll ${lp:X2}");
        if (Exception is { } ex)
            parts.Add($"exception ${ex:X2} {SasNames.ExceptionName(ex)}");
        if (Poll is { } kind)
            parts.Add(kind switch
            {
                HostMessageKind.SyncPoll => "synchronisation polls",
                HostMessageKind.GeneralPoll => "general polls",
                _ => "long polls",
            });
        if (!string.IsNullOrWhiteSpace(Text))
            parts.Add($"text '{Text}'");
        if (parts.Count == 0)
            parts.Add("everything");

        string where = Side switch
        {
            TraceSide.Host => " from the host",
            TraceSide.Machine => " from the machine",
            _ => string.Empty,
        };

        return string.Join(" and ", parts) + where;
    }

    private bool SideAllows(WireDirection direction) => Side switch
    {
        TraceSide.Host => direction == WireDirection.Transmit,
        TraceSide.Machine => direction == WireDirection.Receive,
        _ => true,
    };

    private static bool ContainsText(ReadOnlySpan<SasSymbol> symbols, string? note, string text)
    {
        if (note is not null && note.Contains(text, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (SasSymbol symbol in symbols)
        {
            if (symbol.ToString().Contains(text, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
