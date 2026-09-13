using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Sweep;

/// <summary>What one command's turn in the sweep came to.</summary>
public enum SweepOutcome
{
    /// <summary>The machine sent a reply carrying data.</summary>
    Answered,

    /// <summary>The machine acknowledged with its own address and nothing else.</summary>
    Acknowledged,

    /// <summary>The machine answered its address with bit 7 set, refusing the command.</summary>
    Rejected,

    /// <summary>Nothing came back to any form of the request. Recorded, never skipped.</summary>
    Silent,

    /// <summary>A reply came back that this build could not read as one.</summary>
    Unreadable,

    /// <summary>The sweep did not send this command, because sending it changes the machine.</summary>
    Withheld,
}

/// <summary>One attempt at one command.</summary>
/// <param name="Request">The request bytes sent after the address and the command.</param>
/// <param name="Result">What came back.</param>
public sealed record SweepAttempt(byte[] Request, LongPollResult Result);

/// <summary>Everything the sweep learned about one command.</summary>
public sealed record SweepFinding
{
    public required SweepEntry Entry { get; init; }

    public required SweepOutcome Outcome { get; init; }

    /// <summary>Every form of the request that was tried, in the order it was tried.</summary>
    public IReadOnlyList<SweepAttempt> Attempts { get; init; } = [];

    /// <summary>The attempt the machine answered, when one did.</summary>
    public SweepAttempt? Answered { get; init; }

    /// <summary>Whether this build already holds a layout for the reply.</summary>
    public bool HadLayout { get; init; }

    /// <summary>Wire records for this command's turn, so the record can be rendered.</summary>
    public IReadOnlyList<WireRecord> Wire { get; init; } = [];

    /// <summary>One sentence for the table, saying what happened rather than what it means.</summary>
    public string Detail => Outcome switch
    {
        SweepOutcome.Withheld => Entry.Reason,
        SweepOutcome.Silent =>
            $"answered nothing to {Attempts.Count} form(s) of the request",
        SweepOutcome.Rejected => "refused the command",
        SweepOutcome.Acknowledged => "acknowledged, with no data",
        SweepOutcome.Unreadable => Answered?.Result.Detail ?? "the reply could not be read",
        _ => $"{Answered?.Result.Frame.Length ?? 0} byte(s): "
             + (Answered?.Result.ToHex() ?? string.Empty),
    };
}

/// <summary>
/// Asks a machine every command the catalogue holds that can be asked, and records what came back.
/// </summary>
/// <remarks>
/// <para>
/// This exists because a coverage figure taken over traffic can only ever name the commands
/// somebody happened to send. Our own suite sends ten; the imported corpus eighteen; the
/// catalogue holds a hundred and twenty-four. The only way left to produce bytes for the rest is
/// to ask a machine for them, which is also the only way that verifies a layout rather than
/// asserting one — the reply either fits or it does not.
/// </para>
/// <para>
/// Two rules keep it honest. A command that changes the machine is not sent, by name and with a
/// reason, rather than sent in the hope that it is harmless; that is <see cref="SweepPlan"/>. And
/// a command that answers nothing is recorded as answering nothing, because a machine's silence is
/// a fact about the machine and dropping it would leave the sweep looking more complete than it is.
/// </para>
/// <para>
/// Where a command's request shape is not known, the sweep works up from the shortest form and
/// records every form it sent. That is an experiment rather than a guess: what is on the record is
/// what was sent and what came back, and the layout is still read out of the library's own handler
/// afterwards. The shortest form goes first for a reason — a machine that answers a bare poll
/// would also answer one with bytes it ignores, so trying the longer form first would establish
/// nothing.
/// </para>
/// </remarks>
public sealed class CatalogueSweep
{
    private readonly PollEngine _engine;
    private readonly Action<SweepFinding>? _progress;

    public CatalogueSweep(PollEngine engine, Action<SweepFinding>? progress = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _progress = progress;
    }

    /// <summary>Runs the whole plan and returns what every command came to.</summary>
    public IReadOnlyList<SweepFinding> Run(CancellationToken cancel = default)
    {
        var findings = new List<SweepFinding>();
        foreach (SweepEntry entry in SweepPlan.All)
        {
            cancel.ThrowIfCancellationRequested();
            SweepFinding finding = entry.Disposition == SweepDisposition.Withheld
                ? new SweepFinding
                {
                    Entry = entry,
                    Outcome = SweepOutcome.Withheld,
                    HadLayout = SasLayouts.For(entry.Command) is not null,
                }
                : Ask(entry);

            findings.Add(finding);
            _progress?.Invoke(finding);
        }

        return findings;
    }

    /// <summary>The forms of the request to try, shortest first.</summary>
    /// <remarks>
    /// <para>
    /// Almost always one form, and it is derived rather than guessed: the machine library's own
    /// receiver holds the total length of every host message it frames, so how many request bytes
    /// a command takes is a fact this build can read. A message shorter or longer than that is
    /// dropped by the receiver as incomplete, which is why working up from the shortest form is a
    /// fallback and not the method.
    /// </para>
    /// <para>
    /// A command framed by a length byte is asked for the empty case — a length of zero — because
    /// that is the only content-free form of a variable-length message. A command the receiver
    /// does not carry at all has nothing to derive from, and only there does the sweep work up:
    /// nothing, one byte, then two, with every form it sent on the record.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<byte[]> RequestLadder(SweepEntry entry)
    {
        if (entry.Request is { } stated)
            return [stated];

        // A command whose request this build has established is asked in that shape, built
        // through the encoder so its length byte is right. The receiver's byte count cannot do
        // that: it says how long the message is and not what is in it, so a length-prefixed
        // command asked by count alone goes out with a length byte that disagrees with its own
        // message - which this host would then be sending knowing it was wrong.
        if (MinimalRequest(entry.Command) is { } shaped)
        {
            // A command the receiver frames by a length byte is asked for nothing first and then
            // for one element, and both forms are kept. The reason is measured rather than
            // theoretical: the checkout this build's catalogue is generated from frames $2F by a
            // length byte, and the copy the machine under test is built from frames it at eight
            // bytes exactly — so the shortest form is answered by one of them and dropped as
            // incomplete by the other. Asking both ways costs one message and is the difference
            // between a reply and a silence that means nothing.
            return OneElementLonger(entry.Command) is { } longer ? [shaped, longer] : [shaped];
        }

        if (SasCommandFraming.RequestDataBytes(entry.Command) is { } width)
            return [new byte[width]];

        if (SasCommandFraming.IsVariableLength(entry.Command))
            return [[0x00]];

        return [[], new byte[1], new byte[2]];
    }

    /// <summary>
    /// The same request with one entry in its repeating group, or null when the command has no
    /// such group or the group's entries are not a fixed width.
    /// </summary>
    /// <remarks>
    /// Only ever a second rung on the ladder, never a replacement for the first: which of the two
    /// a machine answers is a fact about that machine's receiver, and the sweep records which one
    /// it was rather than deciding in advance.
    /// </remarks>
    public static byte[]? OneElementLonger(byte command)
    {
        SasMessageLayout? layout = SasLayouts.For(command);
        if (layout is null)
            return null;

        SasField[] groups = [.. layout.Request.Where(f => f.IsGroup)];
        if (groups.Length != 1 || groups[0].Fields.Any(m => m.Length <= 0))
            return null;

        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (SasField field in layout.Request)
        {
            if (field.IsGroup || field.BitsOfField is not null || field.PresentWhen is not null)
                continue;
            if (field.Name.Equals("length", StringComparison.OrdinalIgnoreCase))
                continue;

            values[field.Name] = 0L;
        }

        values[groups[0].Name + ".count"] = 1L;
        foreach (SasField member in groups[0].Fields)
            values[$"{groups[0].Name}[0].{member.Name}"] = 0L;

        return Encode(layout, values);
    }

    /// <summary>
    /// The shortest request this build's own layout for a command says is well formed, with every
    /// field zero, or null when the command has no established request.
    /// </summary>
    /// <remarks>
    /// Zero for every field is a deliberate choice and not a lazy one: game zero means the machine
    /// as a whole, and a validation or meter code of zero is a real code. A field this build cannot
    /// fill returns null rather than a message with a hole in it.
    /// </remarks>
    public static byte[]? MinimalRequest(byte command)
    {
        SasMessageLayout? layout = SasLayouts.For(command);
        if (layout is null || layout.Request.Count == 0)
            return null;

        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var groups = new List<SasField>();
        foreach (SasField field in layout.Request)
        {
            if (field.IsGroup)
            {
                groups.Add(field);
                values[field.Name + ".count"] = 0L;
                continue;
            }

            if (field.BitsOfField is not null || field.PresentWhen is not null)
                continue;
            if (field.Name.Equals("length", StringComparison.OrdinalIgnoreCase))
                continue;

            values[field.Name] = 0L;
        }

        byte[]? shortest = Encode(layout, values);
        if (shortest is null || groups.Count != 1)
            return shortest;

        // A repeating group asked for nothing is the shortest well-formed request, and for one
        // command it is not a request a machine will read: the receiver frames long poll F at
        // eight bytes exactly, so a message asking for no meters is dropped as incomplete and so
        // is one asking for two. Where the receiver fixes a total, the group is filled to it.
        if (SasCommandFraming.RequestDataBytes(layout.Command) is not { } framed
            || framed == shortest.Length)
        {
            return shortest;
        }

        int element = 0;
        foreach (SasField member in groups[0].Fields)
        {
            if (member.Length <= 0)
                return shortest;
            element += member.Length;
        }

        int room = framed - shortest.Length;
        if (element == 0 || room <= 0 || room % element != 0)
            return shortest;

        values[groups[0].Name + ".count"] = (long)(room / element);
        for (int index = 0; index < room / element; index++)
        {
            foreach (SasField member in groups[0].Fields)
                values[$"{groups[0].Name}[{index}].{member.Name}"] = 0L;
        }

        return Encode(layout, values) ?? shortest;
    }

    private static byte[]? Encode(SasMessageLayout layout, Dictionary<string, object> values)
    {
        try
        {
            byte[] buffer = new byte[HostMessage.MaxSymbols];
            int written = SasMessageEncoder.WriteRequestData(layout, values, buffer);
            return buffer[..written];
        }
        catch (InvalidOperationException)
        {
            // The layout needs something this build cannot supply. Saying so by falling back to
            // the receiver's count beats sending a message shaped by a guess.
            return null;
        }
    }

    private SweepFinding Ask(SweepEntry entry)
    {
        var attempts = new List<SweepAttempt>();
        SweepAttempt? answered = null;

        foreach (byte[] request in RequestLadder(entry))
        {
            LongPollResult result = _engine.Execute(
                entry.Command, request,
                purpose: $"sweep ${entry.Command:X2} with {request.Length} request byte(s)");

            var attempt = new SweepAttempt(request, result);
            attempts.Add(attempt);

            if (result.Outcome is LongPollOutcome.Answered or LongPollOutcome.Acknowledged
                or LongPollOutcome.BadCheckSequence or LongPollOutcome.WrongAddress)
            {
                answered = attempt;
                break;
            }

            // A refusal is the machine speaking, and it ends the ladder for the same reason an
            // answer does: the machine heard the command and said no to it, so a longer form of
            // the same command is asking a question that has been answered.
            if (result.Outcome == LongPollOutcome.Rejected)
            {
                answered = attempt;
                break;
            }
        }

        SweepOutcome outcome = answered?.Result.Outcome switch
        {
            LongPollOutcome.Answered => SweepOutcome.Answered,
            LongPollOutcome.Acknowledged => SweepOutcome.Acknowledged,
            LongPollOutcome.Rejected => SweepOutcome.Rejected,
            LongPollOutcome.BadCheckSequence or LongPollOutcome.WrongAddress =>
                SweepOutcome.Unreadable,
            _ => SweepOutcome.Silent,
        };

        return new SweepFinding
        {
            Entry = entry,
            Outcome = outcome,
            Attempts = attempts,
            Answered = outcome is SweepOutcome.Answered or SweepOutcome.Acknowledged
                       or SweepOutcome.Rejected or SweepOutcome.Unreadable
                ? answered
                : null,
            HadLayout = SasLayouts.For(entry.Command) is not null,
        };
    }
}
