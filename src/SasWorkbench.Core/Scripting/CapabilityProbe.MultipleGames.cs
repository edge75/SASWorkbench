using System.Globalization;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Scripting;

/// <summary>Multiple games and multiple player denominations, read off the machine's own replies.</summary>
public static partial class CapabilityProbe
{
    /// <summary>The capability a machine holding more than one game answers with, its value the count.</summary>
    public const string MultipleGamesCapability = "machine.multiple_games";

    /// <summary>The capability a machine offering more than one player denomination answers with, its value the count.</summary>
    public const string MultipleDenominationsCapability = "machine.multiple_denominations";

    /// <summary>Reads the number of games implemented off long poll $51; more than one is a multi-game machine.</summary>
    private static string? AskMultipleGames(PollEngine engine, Dictionary<string, string?> values) =>
        AskCount(engine, values, MultipleGamesCapability, 0x51, "games", "games implemented");

    /// <summary>
    /// Reads the enabled player denominations off long poll $B2, which the library answers only on a machine that
    /// sets the multi-denomination extensions bit; more than one is a multi-denomination machine.
    /// </summary>
    private static string? AskMultipleDenominations(PollEngine engine, Dictionary<string, string?> values) =>
        AskCount(engine, values, MultipleDenominationsCapability, 0xB2, "count", "enabled player denominations");

    /// <summary>Sets a capability from a count in one reply when it is above one, or says why it cannot.</summary>
    private static string? AskCount(
        PollEngine engine, Dictionary<string, string?> values, string capability, byte command, string field, string what)
    {
        SasMessageLayout layout = SasLayouts.For(command)!;

        if (Ask(engine, layout, []) is not { } result)
        {
            return $"long poll ${command:X2} could not be put on the link, so the machine's {what} were never asked"
                   + Because(engine);
        }

        if (!result.Succeeded)
        {
            return $"the machine did not answer long poll ${command:X2} ({result.Outcome}), so it reports no {what}";
        }

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, result.Frame);
        if (decoded.Number(field) is not { } count)
        {
            return $"the machine's ${command:X2} reply could not be decoded: "
                   + (decoded.Failure ?? $"it carries no {field}");
        }

        if (count < 2)
            return $"the machine reports {count} {what} in its ${command:X2} reply";

        values[capability] = count.ToString(CultureInfo.InvariantCulture);
        return null;
    }
}
