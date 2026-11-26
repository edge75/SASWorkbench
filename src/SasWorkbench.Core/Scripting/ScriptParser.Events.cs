using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Scripting;

/// <summary>What a poll placed by a case drew, and in which format.</summary>
public static partial class ScriptParser
{
    /// <summary>Where 'draws' stands in a long poll line, or -1.</summary>
    private static int DrawsAt(string[] words) =>
        words.Length > 4 && words[2].Equals("lp", StringComparison.OrdinalIgnoreCase)
            ? Array.FindIndex(words, 4, w => w.Equals("draws", StringComparison.OrdinalIgnoreCase))
            : -1;

    /// <summary>Reads 'host send lp &lt;hh&gt; [data ...] draws event &lt;name&gt; [or silence]'.</summary>
    private static SendLongPollDrawsEventStep ReadSendDrawsEvent(string[] words, SourceLocation location)
    {
        int at = DrawsAt(words);
        SendLongPollStep poll = ReadSend(words[..at], location);
        if (poll.To is not null || poll.When is not null)
            throw new ScriptParseException(location, "a long poll that draws an event goes to the machine itself, unconditionally");

        (DrawFormat format, string what, bool orSilence) = ReadDraws(words[(at + 1)..], location);
        if (format != DrawFormat.Event)
        {
            throw new ScriptParseException(location,
                "a long poll draws an exception only as a real-time event: expected: host send lp <code> [data <hex>] "
                + "draws event <exception> [or silence]");
        }

        byte code = ResolveDrawnException(what, location);
        return new SendLongPollDrawsEventStep(location, poll, code, SasNames.ExceptionName(code), orSilence);
    }

    /// <summary>Reads what follows 'draws': an optional format, the name, and an optional 'or silence'.</summary>
    private static (DrawFormat Format, string What, bool OrSilence) ReadDraws(string[] tail, SourceLocation location)
    {
        bool orSilence = tail.Length >= 3
            && tail[^2].Equals("or", StringComparison.OrdinalIgnoreCase)
            && tail[^1].Equals("silence", StringComparison.OrdinalIgnoreCase);
        if (orSilence)
            tail = tail[..^2];

        DrawFormat format = DrawFormat.Any;
        if (tail.Length == 2 && tail[0].Equals("event", StringComparison.OrdinalIgnoreCase))
            format = DrawFormat.Event;
        else if (tail.Length == 2 && tail[0].Equals("bare", StringComparison.OrdinalIgnoreCase))
            format = DrawFormat.Bare;
        else if (tail.Length != 1)
            throw new ScriptParseException(location, "expected after 'draws': [event|bare] <exception> [or silence]");

        return (format, tail[^1], orSilence);
    }

    /// <summary>The code of an exception a poll is expected to draw.</summary>
    private static byte ResolveDrawnException(string what, SourceLocation location)
    {
        if (SasNames.TryResolveException(what, out byte code))
            return code;

        throw new ScriptParseException(location,
            $"'{what}' is not an exception this build knows. Write its name, such as "
            + "slot_door_opened, or its code, such as $11 - or 'nothing' for a machine "
            + "reporting no activity, or 'silence' for no answer at all");
    }
}
