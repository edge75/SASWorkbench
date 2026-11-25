namespace SasWorkbench.Core.Scripting;

/// <summary>The ROM signature and the machine's menus.</summary>
public static partial class ScriptParser
{
    /// <summary>Reads 'host expect rom signature within &lt;d&gt;' and 'host expect no rom signature within &lt;d&gt;'.</summary>
    private static ExpectRomSignatureStep? ReadExpectRomSignature(string[] words, SourceLocation location)
    {
        bool negated = words.Length >= 3 && words[2].Equals("no", StringComparison.OrdinalIgnoreCase);
        int at = negated ? 3 : 2;
        if (words.Length < at + 2
            || !words[at].Equals("rom", StringComparison.OrdinalIgnoreCase)
            || !words[at + 1].Equals("signature", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (words.Length != at + 4 || !words[at + 2].Equals("within", StringComparison.OrdinalIgnoreCase))
        {
            throw new ScriptParseException(location,
                "expected: host expect rom signature within <duration>, or host expect no rom signature within <duration>");
        }

        return new ExpectRomSignatureStep(location, ParseDuration(words[at + 3], location), negated);
    }

    /// <summary>Reads 'egm menu attendant|operator enter|leave'.</summary>
    private static MenuStep ReadMenu(string[] words, SourceLocation location)
    {
        MachineMenu? menu = words.Length == 4 ? words[2].ToLowerInvariant() switch
        {
            "attendant" => MachineMenu.Attendant,
            "operator" => MachineMenu.Operator,
            _ => null,
        } : null;

        bool? enter = words.Length == 4 ? words[3].ToLowerInvariant() switch
        {
            "enter" => true,
            "leave" => false,
            _ => null,
        } : null;

        if (menu is not { } which || enter is not { } entering)
            throw new ScriptParseException(location, "expected: egm menu attendant|operator enter|leave");

        return new MenuStep(location, which, entering);
    }
}
