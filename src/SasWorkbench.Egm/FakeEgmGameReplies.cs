using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Egm;

/// <summary>
/// The per-game and per-denomination polls of a machine built with a game table, each as the library's handler answers.
/// </summary>
public sealed partial class FakeEgm
{
    // The player denomination the game menu stands at: the platform's game zero record is one cent (QSP SasNetwork.cpp:1258).
    private const byte MenuDenomination = 0x01;

    // The library's sentinel for no player denomination (libsas_internal.h:584).
    private const byte NoPlayerDenomination = 0x00;

    /// <summary>The reply from the game table, or null when the table has nothing to say about this command.</summary>
    private Dictionary<string, object>? GameTableValues(SasMessageLayout layout, ReadOnlySpan<byte> request) => layout.Command switch
    {
        0x1F => MachineSummary(),
        0x52 => TableGameMeters(layout, request),
        0x53 => TableGameConfiguration(layout, request),
        0x55 => new Dictionary<string, object> { ["selected_game"] = (long)SelectedGame },
        0x56 => TableEnabledGames(preamble: false, NoPlayerDenomination),
        0xB1 => new Dictionary<string, object> { ["denomination"] = CurrentPlayerDenomination() },
        0xB2 => TableEnabledPlayerDenominations(),
        0xB5 => TableExtendedGameInformation(layout, request, preamble: false, 0),
        0x2F => TableMetersForGame(request, preamble: false, 0).Values,
        0x6F or 0xAF => TableSelectedMeters(request, preamble: false, 0).Values,
        _ => null,
    };

    /// <summary>
    /// The identification reply: the largest bet and the average payback of the games a player can reach
    /// (lp.cpp:1320, libsas_internal.cpp:1080), the rest the machine's own.
    /// </summary>
    private Dictionary<string, object> MachineSummary()
    {
        (int maxBet, int payback) = MachineAggregateLocked();
        return new Dictionary<string, object>
        {
            ["game_id"] = _options.GameId,
            ["additional_id"] = _options.AdditionalId,
            ["denomination"] = DenominationCode,
            ["max_bet"] = (byte)Math.Min(maxBet, 0xFF),
            ["progressive_group"] = _options.ProgressiveGroup,
            ["game_options"] = _options.GameOptions,
            ["paytable_id"] = _options.PaytableId,
            ["base_percentage"] = payback,
        };
    }

    /// <summary>Largest bet and average payback of the reachable games, the average an integer division as the library takes it.</summary>
    private (int MaxBet, int Payback) MachineAggregateLocked()
    {
        lock (_gate)
        {
            List<MachineGame> available = AvailableGamesLocked();
            return available.Count == 0
                ? (0, 0)
                : (available.Max(game => game.MaxBet), available.Sum(game => game.Payback) / available.Count);
        }
    }

    /// <summary>Long poll $52: the four meters of one game, or the machine's for game zero; silent past the last (lp.cpp:3046).</summary>
    private Dictionary<string, object>? TableGameMeters(SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        if (GameNumber(layout, request) is not { } game)
            return NoAnswer;

        return new Dictionary<string, object>
        {
            ["game_number"] = game,
            ["total_coin_in"] = ReadGameMeter(game, 0, (ushort)SasMeterCode.TotalCoinInCredits),
            ["total_coin_out"] = ReadGameMeter(game, 0, (ushort)SasMeterCode.TotalCoinOutCredits),
            ["total_jackpot"] = ReadGameMeter(game, 0, (ushort)SasMeterCode.TotalJackpotCredits),
            ["games_played"] = ReadGameMeter(game, 0, (ushort)SasMeterCode.GamesPlayed),
        };
    }

    /// <summary>Long poll $53: one game's configuration, the accounting denomination for every game (lp.cpp:3163).</summary>
    private Dictionary<string, object>? TableGameConfiguration(SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        if (GameNumber(layout, request) is not { } number)
            return NoAnswer;

        MachineGame? game;
        lock (_gate)
            game = GameLocked(number);
        (int maxBet, int payback) = game is null ? MachineAggregateLocked() : (game.MaxBet, game.Payback);

        return new Dictionary<string, object>
        {
            ["game_number"] = number,
            ["game_id"] = _options.GameId,
            ["additional_id"] = game is null ? _options.AdditionalId : $"{game.Number:D3}",
            ["denomination"] = DenominationCode,
            ["max_bet"] = (byte)Math.Min(maxBet, 0xFF),
            ["progressive_group"] = _options.ProgressiveGroup,
            ["game_options"] = _options.GameOptions,
            ["paytable_id"] = game?.Paytable ?? _options.PaytableId,
            ["base_percentage"] = FourDigits(payback),
        };
    }

    /// <summary>
    /// Long poll $56: the games enabled at the denomination reported, which is the selected game's on a bare poll and
    /// every enabled denomination in the game menu (lp.cpp:3359, libsas_internal.h:635).
    /// </summary>
    private Dictionary<string, object> TableEnabledGames(bool preamble, byte requested)
    {
        List<int> listed;
        lock (_gate)
        {
            byte current = GameLocked(_selectedGame)?.Code ?? NoPlayerDenomination;
            byte reported = !preamble || requested == NoPlayerDenomination ? current : requested;
            listed = [.. _games
                .Where(game => game.Status && game.DenominationEnabled
                               && (reported == NoPlayerDenomination || game.Code == reported))
                .Select(game => game.Number)];
        }

        var values = new Dictionary<string, object>
        {
            ["length"] = 1 + (listed.Count * 2),
            ["count"] = (long)listed.Count,
            ["games.count"] = (long)listed.Count,
        };
        for (int i = 0; i < listed.Count; i++)
            values[$"games[{i}].game_number"] = (long)listed[i];
        return values;
    }

    /// <summary>Long poll $B1: the selected game's denomination, or the game menu's (lp.cpp:10228).</summary>
    private byte CurrentPlayerDenomination()
    {
        lock (_gate)
            return GameLocked(_selectedGame)?.Code ?? MenuDenomination;
    }

    /// <summary>Long poll $B2: every enabled denomination of every game, once, in game order (libsas_internal.cpp:1246).</summary>
    private Dictionary<string, object> TableEnabledPlayerDenominations()
    {
        List<byte> codes;
        lock (_gate)
            codes = [.. _games.Where(game => game.DenominationEnabled).Select(game => game.Code).Distinct()];

        var values = new Dictionary<string, object>
        {
            ["length"] = 1 + codes.Count,
            ["count"] = codes.Count,
            ["denominations.count"] = codes.Count,
        };
        for (int i = 0; i < codes.Count; i++)
            values[$"denominations[{i}].denomination"] = codes[i];
        return values;
    }

    /// <summary>
    /// Long poll $B5: a game's largest bet, or for game zero the largest of every game at the denomination asked
    /// (lp.cpp:10687); the machine's name for game zero, the game's for a game.
    /// </summary>
    private Dictionary<string, object>? TableExtendedGameInformation(
        SasMessageLayout layout, ReadOnlySpan<byte> request, bool preamble, byte denomination)
    {
        if (GameNumber(layout, request) is not { } number)
            return NoAnswer;

        MachineGame? game;
        int maxBet;
        lock (_gate)
        {
            game = GameLocked(number);
            maxBet = game?.MaxBet
                     ?? _games.Where(each => !preamble || denomination == 0 || each.Code == denomination)
                         .Select(each => each.MaxBet).DefaultIfEmpty(0).Max();
        }

        string name = game?.Name ?? _options.GameName;
        string paytable = game?.Paytable ?? _options.PaytableId;
        return new Dictionary<string, object>
        {
            ["length"] = 13 + name.Length + paytable.Length,
            ["game_number"] = number,
            ["max_bet"] = (long)maxBet,
            ["progressive_group"] = _options.ProgressiveGroup,
            ["progressive_levels"] = (long)SasLevels,
            ["name_length"] = name.Length,
            ["game_name"] = name,
            ["paytable_length"] = paytable.Length,
            ["paytable_name"] = paytable,
            ["wager_categories"] = 0L,
        };
    }

    /// <summary>
    /// Long polls $6F and $AF for a game or, behind the preamble, for game zero at a denomination: a game past the
    /// last is silent, and a meter the library does not keep per game or per denomination is left out (lp.cpp:4274).
    /// </summary>
    /// <returns>The values, <see cref="NoAnswer"/> for silence, or null values and a preamble error code.</returns>
    private (Dictionary<string, object>? Values, byte Error) TableSelectedMeters(ReadOnlySpan<byte> request, bool preamble, byte denomination)
    {
        if (request.Length < 5)
            return (null, MdpeImproperlyFormatted);

        long game = BcdNumber(request[3], request[4]);
        int codes = (request[2] - 2) / 2;
        if (codes < 0 || 5 + (codes * 2) > request.Length)
            return (null, MdpeImproperlyFormatted);

        if (TableRefusesGame(game, preamble, denomination) is { } refusal)
            return refusal;

        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        int entries = 0;
        int payload = 2;
        for (int i = 0; i < codes; i++)
        {
            ushort code = (ushort)(request[5 + (i * 2)] | (request[6 + (i * 2)] << 8));
            if (!IsReportableMeter(code) || (game > 0 && !IsMultigameMeter(code)) || (preamble && !IsMultidenomMeter(code)))
                continue;

            long value = ReadGameMeter(game, denomination, code);
            int size = WidthOf(code, value);
            values[$"meters[{entries}].code"] = code;
            values[$"meters[{entries}].size"] = size;
            values[$"meters[{entries}].value"] = value;
            entries++;
            payload += 3 + size;
        }

        values["length"] = payload;
        values["game_number"] = game;
        values["meters.count"] = entries;
        return (values, 0);
    }

    /// <summary>Long poll $2F, the fixed-width relative of $6F, under the same rules and at most ten meters (lp.cpp:1867).</summary>
    private (Dictionary<string, object>? Values, byte Error) TableMetersForGame(ReadOnlySpan<byte> request, bool preamble, byte denomination)
    {
        if (request.Length < 5)
            return (null, MdpeImproperlyFormatted);

        long game = BcdNumber(request[3], request[4]);
        int codes = request[2] - 2;
        if (codes < 0 || 5 + codes > request.Length)
            return (null, MdpeImproperlyFormatted);

        if (TableRefusesGame(game, preamble, denomination) is { } refusal)
            return refusal;
        if (codes > 10)
            return (preamble ? null : NoAnswer, MdpeImproperlyFormatted);

        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        int entries = 0;
        int payload = 2;
        for (int i = 0; i < codes; i++)
        {
            ushort code = request[5 + i];
            if (!IsReportableMeter(code) || (game > 0 && !IsMultigameMeter(code)) || (preamble && !IsMultidenomMeter(code)))
                continue;

            values[$"meters[{entries}].code"] = code;
            values[$"meters[{entries}].value"] = ReadGameMeter(game, denomination, code);
            entries++;
            payload += 1 + SasMeterWidths.Of(code);
        }

        values["length"] = payload;
        values["game_number"] = game;
        values["meters.count"] = entries;
        return (values, 0);
    }

    /// <summary>
    /// Silence for a game past the last on a bare poll; behind the preamble, error 04 for any game but zero or a
    /// denomination game zero does not offer (lp.cpp:4203, :4225).
    /// </summary>
    private (Dictionary<string, object>? Values, byte Error)? TableRefusesGame(long game, bool preamble, byte denomination)
    {
        if (!preamble)
            return game > GameCount ? (NoAnswer, 0) : null;

        lock (_gate)
        {
            return game != 0 || (denomination != 0 && !MachineOffersLocked(denomination))
                ? (null, MdpeNotInThatFormat)
                : null;
        }
    }

    /// <summary>A meter the library keeps per game (libsas_utils.cpp:986).</summary>
    private static bool IsMultigameMeter(ushort code) => SasCatalogue.MultigameMeters.Contains(code);

    /// <summary>A meter the library keeps per denomination (libsas_utils.cpp:1016).</summary>
    private static bool IsMultidenomMeter(ushort code) =>
        code <= 0x0002 || code is >= 0x0005 and <= 0x0007 || code is >= 0x001C and <= 0x0022;

    /// <summary>Two bytes of binary coded decimal as a number.</summary>
    private static long BcdNumber(byte high, byte low) =>
        ((high >> 4) * 1000) + ((high & 0x0F) * 100) + ((low >> 4) * 10) + (low & 0x0F);
}
