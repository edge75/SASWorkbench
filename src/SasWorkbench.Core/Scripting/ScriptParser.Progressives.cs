using System.Globalization;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Scripting;

/// <summary>The progressive declaration, the background broadcast and the progressive win.</summary>
public static partial class ScriptParser
{
    private const string ProgressiveUsage =
        "expected: machine progressive sas group <1-255> levels <list>, or machine progressive "
        + "non_sas levels <list>, where a list is written 1-4 or 1,3";

    /// <summary>Reads one half of the progressive declaration; a case may declare both halves, on two lines.</summary>
    private static MachineProgressiveDeclaration ReadMachineProgressive(
        MachineProgressiveDeclaration? already, string[] words, SourceLocation location)
    {
        bool sas = words.Length == 7 && words[2].Equals("sas", StringComparison.OrdinalIgnoreCase)
                   && words[3].Equals("group", StringComparison.OrdinalIgnoreCase)
                   && words[5].Equals("levels", StringComparison.OrdinalIgnoreCase);
        bool nonSas = words.Length == 5 && words[2].Equals("non_sas", StringComparison.OrdinalIgnoreCase)
                      && words[3].Equals("levels", StringComparison.OrdinalIgnoreCase);
        if (!sas && !nonSas)
            throw new ScriptParseException(location, ProgressiveUsage);

        if (!MachineProgressiveDeclaration.TryParseLevels(words[^1], out uint levels))
            throw new ScriptParseException(location, $"'{words[^1]}' is not a list of levels. {ProgressiveUsage}");

        byte group = 0;
        if (sas && (!byte.TryParse(words[4], NumberStyles.None, CultureInfo.InvariantCulture, out group) || group == 0))
        {
            throw new ScriptParseException(location,
                $"'{words[4]}' is not a SAS progressive group. Group 00 is reserved for progressives no SAS host runs, so a group is 1 to 255");
        }

        MachineProgressiveDeclaration current = already ?? new MachineProgressiveDeclaration(location, 0, 0, 0);
        if (sas ? current.SasLevels != 0 : current.NonSasLevels != 0)
            throw new ScriptParseException(location, "this case already declares those progressive levels");

        if (((sas ? current.NonSasLevels : current.SasLevels) & levels) != 0)
            throw new ScriptParseException(location, "a level is either one a host feeds or one the machine runs, not both");

        return sas
            ? current with { Group = group, SasLevels = levels }
            : current with { NonSasLevels = levels };
    }

    /// <summary>Reads <c>host broadcast lp 80|86 ... every &lt;duration&gt;</c> or <c>host broadcast stop</c>.</summary>
    private static ScriptStep ReadBroadcast(string[] words, SourceLocation location)
    {
        const string Usage = "expected: host broadcast lp 80|86 data <hex>|with <field>=<value> ... every <duration>, or host broadcast stop";

        if (words.Length == 3 && words[2].Equals("stop", StringComparison.OrdinalIgnoreCase))
            return new StopBroadcastsStep(location);

        if (words.Length < 6 || !words[^2].Equals("every", StringComparison.OrdinalIgnoreCase))
            throw new ScriptParseException(location, Usage);

        TimeSpan every = ParseDuration(words[^1], location);
        if (every < TimeSpan.FromMilliseconds(200))
            throw new ScriptParseException(location, "a broadcast more often than every 200ms would leave the line no room for anything else");

        SendLongPollStep send = ReadSend(["host", "send", .. words[2..^2]], location);
        if (send.To is not null || send.When is not null || SasLayouts.For(send.Command)?.Broadcast != true)
            throw new ScriptParseException(location, $"{Usage}. A broadcast goes to address 00 and is only a command that may be broadcast");

        return new BroadcastStep(location, send with { To = SasAddress.Sync }, every);
    }

    /// <summary>Reads <c>host expect answer to lp &lt;hh&gt; [data &lt;hex&gt;]</c>, worded as the silence it rules out is.</summary>
    private static ExpectAnswerStep ReadExpectAnswer(string[] words, SourceLocation location)
    {
        ExpectNoReplyStep silence = ReadExpectNoReply(["host", "expect", "no", "reply", .. words[3..]], location);
        return new ExpectAnswerStep(location, silence.Command, silence.Data);
    }

    /// <summary>Reads <c>egm progressive win level=&lt;n&gt; [bet=&lt;credits&gt;]</c>.</summary>
    private static ProgressiveWinStep ReadProgressiveWin(string[] words, SourceLocation location)
    {
        const string Usage = "expected: egm progressive win level=<1-32>[,<1-32>...] [bet=<credits>]";

        if (words.Length < 4 || !words[2].Equals("win", StringComparison.OrdinalIgnoreCase))
            throw new ScriptParseException(location, Usage);

        var levels = new List<int>();
        long bet = 1;
        foreach (string word in words[3..])
        {
            string[] pair = word.Split('=', 2);
            if (pair.Length == 2 && pair[0].Equals("level", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string part in pair[1].Split(','))
                {
                    if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int level)
                        || level is < 1 or > 32 || levels.Contains(level))
                    {
                        throw new ScriptParseException(location, $"'{part}' is not a level, or names one twice. {Usage}");
                    }

                    levels.Add(level);
                }

                continue;
            }

            if (pair.Length != 2 || !pair[0].Equals("bet", StringComparison.OrdinalIgnoreCase)
                || !long.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out bet) || bet < 1)
            {
                throw new ScriptParseException(location, $"'{word}' is not a progressive win setting. {Usage}");
            }
        }

        if (levels.Count == 0)
            throw new ScriptParseException(location, Usage);

        return new ProgressiveWinStep(location, levels, bet);
    }
}
