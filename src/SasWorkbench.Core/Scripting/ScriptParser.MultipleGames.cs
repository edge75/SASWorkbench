using System.Globalization;

namespace SasWorkbench.Core.Scripting;

/// <summary>The games a machine holds, the player's choice among them, and the operator's switch for each.</summary>
public static partial class ScriptParser
{
    private const string SelectUsage = "expected: egm select game <number>, or egm select denomination <cents>c";

    /// <summary>
    /// Whether a field belongs to the base reply of the last preamble sent, which a preamble reply carries decoded
    /// under the base command's own names, its length byte as <c>base_length</c>.
    /// </summary>
    private static bool PreambleBaseHasField(TestBuilder test, byte command, string fieldName)
    {
        if (command != SasWorkbench.Core.Protocol.SasLayouts.PreambleCommand
            || test.Steps.OfType<SendLongPollStep>().LastOrDefault(send => send.Command == command) is not { Data.Length: >= 3 } sent
            || SasWorkbench.Core.Protocol.SasLayouts.For(sent.Data[2]) is not { } inner)
        {
            return false;
        }

        return fieldName.Equals("base_length", StringComparison.OrdinalIgnoreCase)
            ? HasField(inner.Response, "length")
            : !fieldName.Equals("length", StringComparison.OrdinalIgnoreCase) && HasField(inner.Response, fieldName);
    }

    /// <summary>Reads 'machine games &lt;cents&gt;c [&lt;cents&gt;c ...]', one denomination per game.</summary>
    private static void ReadMachineGames(TestBuilder test, string[] words, SourceLocation location)
    {
        if (words.Length < 3)
            throw new ScriptParseException(location, "expected: machine games <cents>c [<cents>c ...], one per game");

        if (test.Games is not null)
            throw new ScriptParseException(location, "this case already declares the machine's games");

        var denominations = new List<int>();
        foreach (string word in words[2..])
        {
            if (!TryReadCents(word, out int cents) || !MachineDeclaration.TryFromCents(cents, out _))
            {
                throw new ScriptParseException(location,
                    $"'{word}' is not a denomination a game can be played at. Expected a number of cents such as '5c'");
            }

            denominations.Add(cents);
        }

        test.Games = new MachineGamesDeclaration(location, denominations);
    }

    /// <summary>Reads 'egm select game &lt;n&gt;' and 'egm select denomination &lt;cents&gt;c'.</summary>
    private static StimulusStep ReadSelect(string[] words, SourceLocation location)
    {
        if (words.Length != 4)
            throw new ScriptParseException(location, SelectUsage);

        if (words[2].Equals("game", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(words[3], NumberStyles.None, CultureInfo.InvariantCulture, out int game) || game > 9999)
                throw new ScriptParseException(location, $"'{words[3]}' is not a game number; zero opens the game menu");
            return new SelectGameStep(location, game);
        }

        if (words[2].Equals("denomination", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadCents(words[3], out int cents))
                throw new ScriptParseException(location, $"'{words[3]}' is not a denomination. Expected a number of cents such as '5c'");
            return new SelectDenominationStep(location, cents);
        }

        throw new ScriptParseException(location, SelectUsage);
    }

    /// <summary>Reads 'egm set game &lt;n&gt; on|off', the operator's switch for one game.</summary>
    private static SetGameEnabledStep ReadSetGame(string[] words, SourceLocation location)
    {
        if (words.Length != 5
            || !int.TryParse(words[3], NumberStyles.None, CultureInfo.InvariantCulture, out int game)
            || game is < 1 or > 9999
            || words[4].ToLowerInvariant() is not ("on" or "off"))
        {
            throw new ScriptParseException(location, "expected: egm set game <number> on|off");
        }

        return new SetGameEnabledStep(location, game, words[4].Equals("on", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>The player chooses a game, or opens the game menu with game zero.</summary>
public sealed record SelectGameStep(SourceLocation Location, int Game) : StimulusStep(Location)
{
    public override string RequiredCapability => "stimulus.select_game";

    public override string Describe() => Game == 0 ? "select the game menu" : $"select game {Game}";

    public override string? Guidance =>
        Game == 0 ? "Open the game selection menu, so no game is selected." : $"Choose game {Game} on the player screen.";
}

/// <summary>The player chooses a denomination, which on a machine of single-denomination games chooses the game.</summary>
public sealed record SelectDenominationStep(SourceLocation Location, int Cents) : StimulusStep(Location)
{
    public override string RequiredCapability => "stimulus.select_denomination";

    public override string Describe() => $"select denomination {MachineDeclaration.Describe(Cents)}";

    public override string? Guidance =>
        $"Choose the {MachineDeclaration.Describe(Cents)} denomination on the player screen, choosing a game offered at it.";
}

/// <summary>The operator switches one game on or off in the machine's configuration.</summary>
public sealed record SetGameEnabledStep(SourceLocation Location, int Game, bool Enabled) : StimulusStep(Location)
{
    public override string RequiredCapability => "machine.enable_games";

    public override string Describe() => $"set game {Game} {(Enabled ? "on" : "off")}";

    public override string? Guidance =>
        $"In the operator's menu, {(Enabled ? "enable" : "disable")} game {Game}.";
}
