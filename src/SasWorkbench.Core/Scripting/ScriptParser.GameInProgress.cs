using System.Globalization;

namespace SasWorkbench.Core.Scripting;

/// <summary>A game or a cash out held open, and the jackpot limit.</summary>
public static partial class ScriptParser
{
    private const string GameUsage = "expected: egm game start [bet=<credits>], or egm game end [win=<credits>]";

    /// <summary>egm game start [bet=n] and egm game end [win=n].</summary>
    private static StimulusStep ReadGame(string[] words, SourceLocation location)
    {
        if (words.Length is < 3 or > 4)
            throw new ScriptParseException(location, GameUsage);

        string verb = words[2].ToLowerInvariant();
        string setting = verb switch
        {
            "start" => "bet",
            "end" => "win",
            _ => throw new ScriptParseException(location, GameUsage),
        };

        long credits = 0;
        if (words.Length == 4)
        {
            string[] pair = words[3].Split('=', 2);
            if (pair.Length != 2 || !pair[0].Equals(setting, StringComparison.OrdinalIgnoreCase)
                || !long.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out credits))
            {
                throw new ScriptParseException(location, GameUsage);
            }
        }

        return verb == "start" ? new StartGameStep(location, credits) : new EndGameStep(location, credits);
    }

    /// <summary>egm press collect lasting &lt;duration&gt;.</summary>
    private static SlowCashoutStep ReadSlowCashout(string[] words, SourceLocation location)
    {
        if (words.Length != 5 || !words[2].Equals("collect", StringComparison.OrdinalIgnoreCase))
            throw new ScriptParseException(location, "expected: egm press <button>, or egm press collect lasting <duration>");

        TimeSpan lasting = ParseDuration(words[4], location);
        if (lasting <= TimeSpan.Zero)
            throw new ScriptParseException(location, "a cash out must last some time");
        return new SlowCashoutStep(location, lasting);
    }

    /// <summary>egm set jackpot limit &lt;cents&gt;.</summary>
    private static SetJackpotLimitStep ReadJackpotLimit(string[] words, SourceLocation location)
    {
        if (words.Length != 5
            || !words[3].Equals("limit", StringComparison.OrdinalIgnoreCase)
            || !long.TryParse(words[4], NumberStyles.None, CultureInfo.InvariantCulture, out long cents))
        {
            throw new ScriptParseException(location, "expected: egm set jackpot limit <cents>");
        }

        return new SetJackpotLimitStep(location, cents);
    }
}
