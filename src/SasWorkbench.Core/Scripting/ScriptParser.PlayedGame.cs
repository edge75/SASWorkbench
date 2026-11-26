using System.Globalization;

namespace SasWorkbench.Core.Scripting;

/// <summary>The bet button, the game recall screen and the busy reply (D59).</summary>
public static partial class ScriptParser
{
    /// <summary>Reads <c>egm game recall &lt;index&gt;</c>, zero being the last game played.</summary>
    private static GameRecallStep ReadGameRecall(string[] words, SourceLocation location)
    {
        if (words.Length != 4
            || !int.TryParse(words[3], NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            || index > 9999)
        {
            throw new ScriptParseException(location, "expected: egm game recall <index>, zero being the last game played");
        }

        return new GameRecallStep(location, index);
    }

    /// <summary>Reads <c>host expect busy reply to lp &lt;hh&gt; [data &lt;hex&gt;]</c>.</summary>
    private static ExpectBusyReplyStep ReadExpectBusyReply(string[] words, SourceLocation location)
    {
        const string Usage = "expected: host expect busy reply to lp <hh> [data <hex>]";
        if (words.Length is not (7 or 9)
            || !words[3].Equals("reply", StringComparison.OrdinalIgnoreCase)
            || !words[4].Equals("to", StringComparison.OrdinalIgnoreCase)
            || !words[5].Equals("lp", StringComparison.OrdinalIgnoreCase))
        {
            throw new ScriptParseException(location, Usage);
        }

        if (!SasWorkbench.Core.Protocol.SasNames.TryParseCode(words[6], out byte command))
            throw new ScriptParseException(location, $"'{words[6]}' is not a long poll code");

        byte[] data = [];
        if (words.Length == 9)
        {
            if (!words[7].Equals("data", StringComparison.OrdinalIgnoreCase))
                throw new ScriptParseException(location, Usage);
            data = ParseHex(words[8], location);
        }

        return new ExpectBusyReplyStep(location, command, data);
    }
}
