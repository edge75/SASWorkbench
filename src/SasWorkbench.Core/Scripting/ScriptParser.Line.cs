namespace SasWorkbench.Core.Scripting;

/// <summary>The line itself: chirps, a poll without its wakeup bit, the poll address, and repeats in time.</summary>
public static partial class ScriptParser
{
    /// <summary>True when a send line ends 'without wakeup'.</summary>
    private static bool EndsWithoutWakeup(string[] words) =>
        words.Length > 4
        && words[^2].Equals("without", StringComparison.OrdinalIgnoreCase)
        && words[^1].Equals("wakeup", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads 'host send lp &lt;hh&gt; ... without wakeup'.</summary>
    private static SendWithoutWakeupStep ReadSendWithoutWakeup(string[] words, SourceLocation location)
    {
        SendLongPollStep poll = ReadSend(words[..^2], location);
        if (poll.To is not null || poll.When is not null)
            throw new ScriptParseException(location, "a poll without its wakeup bit goes to the machine itself, unconditionally");
        return new SendWithoutWakeupStep(location, poll);
    }

    /// <summary>True when a send line ends 'with bad crc'.</summary>
    private static bool EndsWithBadCrc(string[] words) =>
        words.Length > 5
        && words[^3].Equals("with", StringComparison.OrdinalIgnoreCase)
        && words[^2].Equals("bad", StringComparison.OrdinalIgnoreCase)
        && words[^1].Equals("crc", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads 'host send lp &lt;hh&gt; ... with bad crc', only for a poll that carries a check sequence.</summary>
    private static SendBadCrcStep ReadSendBadCrc(string[] words, SourceLocation location)
    {
        SendLongPollStep poll = ReadSend(words[..^3], location);
        if (poll.To is not null || poll.When is not null)
            throw new ScriptParseException(location, "a poll with a broken check sequence goes to the machine itself, unconditionally");
        if (poll.Data.Length == 0 && !Protocol.SasCommandFraming.RequiresCheckSequence(poll.Command))
            throw new ScriptParseException(location, $"long poll ${poll.Command:X2} carries no check sequence to break");
        return new SendBadCrcStep(location, poll);
    }

    /// <summary>Reads 'host poll address &lt;aa&gt;'.</summary>
    private static FollowAddressStep ReadFollowAddress(string[] words, SourceLocation location)
    {
        if (words.Length != 4 || !Wire.SasAddress.TryParse(words[3], out Wire.SasAddress address) || address.IsSync)
            throw new ScriptParseException(location, "expected: host poll address <01-7F>");
        return new FollowAddressStep(location, address);
    }

    /// <summary>Reads 'egm set address &lt;aa&gt;', 00 included: a machine may be set to it.</summary>
    private static SetPollAddressStep ReadSetPollAddress(string[] words, SourceLocation location)
    {
        if (words.Length != 4 || !Wire.SasAddress.TryParse(words[3], out Wire.SasAddress address))
            throw new ScriptParseException(location, "expected: egm set address <00-7F>");
        return new SetPollAddressStep(location, address);
    }

    /// <summary>Reads 'host expect chirp within &lt;d&gt;' and 'host expect no chirp for &lt;d&gt;'.</summary>
    private static ExpectChirpStep ReadExpectChirp(string[] words, SourceLocation location, bool negated)
    {
        int at = negated ? 4 : 3;
        string keyword = negated ? "for" : "within";
        if (words.Length != at + 2 || !words[at].Equals(keyword, StringComparison.OrdinalIgnoreCase))
        {
            throw new ScriptParseException(location,
                "expected: host expect chirp within <duration>, or host expect no chirp for <duration>");
        }

        return new ExpectChirpStep(location, ParseDuration(words[at + 1], location), negated);
    }

    /// <summary>Reads 'host expect exception &lt;name&gt; every &lt;duration&gt;'.</summary>
    private static ExpectExceptionRepeatStep ReadExpectRepeat(string[] words, SourceLocation location)
    {
        if (words.Length != 6)
            throw new ScriptParseException(location, "expected: host expect exception <name> every <duration>");

        if (!Protocol.SasNames.TryResolveException(words[3], out byte code))
            throw new ScriptParseException(location, $"'{words[3]}' is not an exception this build knows");

        return new ExpectExceptionRepeatStep(
            location, code, Protocol.SasNames.ExceptionName(code), ParseDuration(words[5], location));
    }

    /// <summary>True when an expect line is one of this file's.</summary>
    private static ScriptStep? ReadExpectLine(string[] words, SourceLocation location)
    {
        if (words.Length >= 3 && words[2].Equals("chirp", StringComparison.OrdinalIgnoreCase))
            return ReadExpectChirp(words, location, negated: false);

        if (words.Length >= 4 && words[2].Equals("no", StringComparison.OrdinalIgnoreCase)
            && words[3].Equals("chirp", StringComparison.OrdinalIgnoreCase))
        {
            return ReadExpectChirp(words, location, negated: true);
        }

        if (words.Length >= 5 && words[2].Equals("exception", StringComparison.OrdinalIgnoreCase)
            && words[4].Equals("every", StringComparison.OrdinalIgnoreCase))
        {
            return ReadExpectRepeat(words, location);
        }

        return null;
    }
}
