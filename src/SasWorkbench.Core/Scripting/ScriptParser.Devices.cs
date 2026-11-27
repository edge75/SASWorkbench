using System.Globalization;

namespace SasWorkbench.Core.Scripting;

/// <summary>The coin acceptor, the hopper, the reels and the cashbox: their declarations and their stimuli (D58).</summary>
public static partial class ScriptParser
{
    private const string DevicesUsage =
        "expected: machine coin acceptor on|external, machine hopper on, machine reels on, machine partial pay on, machine bet button on, machine tournament on, machine poker on, machine memory faults on, machine period meters on, or machine credit key on. "
        + "A machine without them is the default, so none has an 'off'";

    /// <summary>Reads one device declaration, each at most once; a partial pay brings the hopper that pays it.</summary>
    private static MachineDevicesDeclaration ReadMachineDevices(
        MachineDevicesDeclaration? earlier, string[] words, SourceLocation location)
    {
        string[] rest = [.. words[1..].Select(word => word.ToLowerInvariant())];
        MachineDevicesDeclaration current = earlier ?? new MachineDevicesDeclaration(location, CoinAcceptorKind.None, false, false, false);

        switch (rest)
        {
            case ["coin", "acceptor", "on" or "external"] when current.CoinAcceptor == CoinAcceptorKind.None:
                return current with { CoinAcceptor = rest[2] == "on" ? CoinAcceptorKind.Internal : CoinAcceptorKind.External };

            case ["hopper", "on"] when !current.Hopper:
                return current with { Hopper = true };

            case ["reels", "on"] when !current.Reels:
                return current with { Reels = true };

            case ["partial", "pay", "on"] when !current.PartialPay:
                return current with { PartialPay = true, Hopper = true };

            case ["bet", "button", "on"] when !current.BetButton:
                return current with { BetButton = true };

            case ["tournament", "on"] when !current.Tournament:
                return current with { Tournament = true };

            case ["poker", "on"] when !current.Poker:
                return current with { Poker = true };

            case ["memory", "faults", "on"] when !current.MemoryFaults:
                return current with { MemoryFaults = true };

            case ["period", "meters", "on"] when !current.PeriodMeters:
                return current with { PeriodMeters = true };

            case ["credit", "key", "on"] when !current.CreditKey:
                return current with { CreditKey = true };

            case ["coin", "acceptor", "on" or "external"]:
            case ["hopper", "on"]:
            case ["reels", "on"]:
            case ["partial", "pay", "on"]:
            case ["bet", "button", "on"]:
            case ["tournament", "on"]:
            case ["poker", "on"]:
            case ["memory", "faults", "on"]:
            case ["period", "meters", "on"]:
            case ["credit", "key", "on"]:
                throw new ScriptParseException(location, "this case already declares that");

            default:
                throw new ScriptParseException(location, DevicesUsage);
        }
    }

    /// <summary>Reads <c>egm poker hold|release &lt;1-5&gt;</c> or <c>egm poker draw</c>.</summary>
    private static PokerStep ReadPokerStep(string[] words, SourceLocation location)
    {
        string action = words.Length > 2 ? words[2].ToLowerInvariant() : string.Empty;

        if (action == "draw" && words.Length == 3)
            return new PokerStep(location, PokerAction.Draw, 0);

        if (action is "hold" or "release" && words.Length == 4
            && int.TryParse(words[3], NumberStyles.None, CultureInfo.InvariantCulture, out int position)
            && position is >= 1 and <= 5)
        {
            return new PokerStep(location, action == "hold" ? PokerAction.Hold : PokerAction.Release, position);
        }

        throw new ScriptParseException(location, "expected: egm poker hold <1-5>, egm poker release <1-5>, or egm poker draw");
    }

    /// <summary>
    /// Reads <c>egm coin insert &lt;n&gt;</c>, <c>egm coin|hopper|reel fault &lt;kind&gt;</c>,
    /// <c>egm hopper level &lt;coins&gt;</c> and <c>egm cashbox removed|installed</c>.
    /// </summary>
    private static StimulusStep ReadDeviceStep(string[] words, SourceLocation location)
    {
        string device = words[1].ToLowerInvariant();

        if (device == "cashbox")
        {
            if (words.Length != 3 || words[2].ToLowerInvariant() is not ("removed" or "installed"))
                throw new ScriptParseException(location, "expected: egm cashbox removed|installed");
            return new CashboxStep(location, words[2].Equals("removed", StringComparison.OrdinalIgnoreCase));
        }

        if (device == "coin" && words.Length >= 3 && words[2].Equals("insert", StringComparison.OrdinalIgnoreCase))
        {
            if (words.Length != 4 || !int.TryParse(words[3], NumberStyles.None, CultureInfo.InvariantCulture, out int coins) || coins <= 0)
                throw new ScriptParseException(location, "expected: egm coin insert <number of coins>");
            return new CoinInsertStep(location, coins);
        }

        if (device == "hopper" && words.Length >= 3 && words[2].Equals("level", StringComparison.OrdinalIgnoreCase))
        {
            if (words.Length != 4 || !int.TryParse(words[3], NumberStyles.None, CultureInfo.InvariantCulture, out int level))
                throw new ScriptParseException(location, "expected: egm hopper level <number of coins>");
            return new HopperLevelStep(location, level);
        }

        CoinDevice kind = device switch
        {
            "coin" => CoinDevice.Coin,
            "hopper" => CoinDevice.Hopper,
            _ => CoinDevice.Reel,
        };

        IReadOnlyList<string> faults = DeviceFaultStep.Faults[kind];
        string expected = $"expected: egm {device} fault <kind>, where the kind is one of " + string.Join(", ", faults)
                          + (kind switch
                          {
                              CoinDevice.Coin => "; or egm coin insert <n>",
                              CoinDevice.Hopper => "; or egm hopper level <coins>",
                              _ => string.Empty,
                          });

        if (words.Length != 4 || !words[2].Equals("fault", StringComparison.OrdinalIgnoreCase))
            throw new ScriptParseException(location, expected);

        string fault = words[3].ToLowerInvariant();
        if (!faults.Contains(fault))
            throw new ScriptParseException(location, $"'{words[3]}' is not a {device} fault. {expected}");

        return new DeviceFaultStep(location, kind, fault);
    }

    /// <summary>Reads <c>egm set hopper limit &lt;cents&gt;</c> or <c>egm set win payout hopper|credits</c>.</summary>
    private static StimulusStep ReadDeviceSetter(string[] words, SourceLocation location)
    {
        if (words[2].Equals("hopper", StringComparison.OrdinalIgnoreCase))
        {
            if (words.Length != 5 || !words[3].Equals("limit", StringComparison.OrdinalIgnoreCase)
                || !long.TryParse(words[4], NumberStyles.None, CultureInfo.InvariantCulture, out long cents))
            {
                throw new ScriptParseException(location, "expected: egm set hopper limit <cents>");
            }

            return new SetHopperLimitStep(location, cents);
        }

        if (words.Length != 5 || !words[3].Equals("payout", StringComparison.OrdinalIgnoreCase)
            || words[4].ToLowerInvariant() is not ("hopper" or "credits"))
        {
            throw new ScriptParseException(location, "expected: egm set win payout hopper|credits");
        }

        return new SetWinPayoutStep(location, words[4].Equals("hopper", StringComparison.OrdinalIgnoreCase));
    }
}
