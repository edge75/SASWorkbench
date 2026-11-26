using System.Collections.Frozen;
using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Egm;

/// <summary>
/// A machine built with several games, each at its own player denomination, as the platform configures them.
/// </summary>
/// <remarks>
/// Without <see cref="FakeEgmOptions.GameDenominationsInCents"/> the machine keeps its single game and answers every
/// per-game poll as it always has. With it, each game is configured the way the platform configures the library
/// (QSP SasNetwork.cpp:1243), the player chooses among them, and the meters a game moves are charged to the game
/// selected at the time, the platform's per-game block (QSP SasNetwork_Meters.hpp:1345).
/// </remarks>
public sealed partial class FakeEgm
{
    // The meters the platform keeps per game (QSP SasNetwork_Meters.hpp:350, has_game_meters); the payback is worked out.
    private static readonly FrozenSet<ushort> PlatformGameMeters = new ushort[]
    {
        0x0000, 0x0001, 0x0002, 0x0005, 0x0006, 0x0007,
        0x001C, 0x001D, 0x001E, 0x001F, 0x0020, 0x0021, 0x0022,
    }.ToFrozenSet();

    private readonly List<MachineGame> _games = [];

    // The game the player is in; zero while the game menu is open.
    private int _selectedGame;

    // The game last reported with $8C, so a selection is reported once, when it changes (QSP ExceptionReporting.h:150).
    private int _reportedSelectedGame;

    /// <summary>True when the machine was built with a game table rather than its single game.</summary>
    private bool HasGameTable => _games.Count > 0;

    /// <summary>How many games the machine implements.</summary>
    private int GameCount => HasGameTable ? _games.Count : (int)Games;

    /// <summary>The game the player is in, or zero in the game menu.</summary>
    public int SelectedGame
    {
        get
        {
            lock (_gate)
                return HasGameTable ? _selectedGame : 0;
        }
    }

    /// <summary>Builds the game table, game one selected, as the platform starts in its first game.</summary>
    private void BuildGames()
    {
        IReadOnlyList<int> denominations = _options.GameDenominationsInCents;
        for (int i = 0; i < denominations.Count; i++)
        {
            // The demo's alternating bet factors and paybacks, so the games differ (QSP_DemoOpenGL GameManager.cpp:161, :175).
            int number = i + 1;
            _games.Add(new MachineGame(number, denominations[i], number % 2 == 1 ? 500 : 250, number % 2 == 1 ? 9200 : 9250));
        }

        if (!HasGameTable)
            return;

        _selectedGame = 1;
        _reportedSelectedGame = 1;
        Meters.Incremented = ChargeSelectedGame;
    }

    /// <summary>Charges a meter's movement to the selected game when the platform keeps that meter per game.</summary>
    private void ChargeSelectedGame(ushort meter, long amount)
    {
        if (!PlatformGameMeters.Contains(meter))
            return;

        MachineGame? game;
        lock (_gate)
            game = GameLocked(_selectedGame);
        game?.Meters.Increment((SasMeterCode)meter, amount);
    }

    /// <summary>The game with this number, or null.</summary>
    private MachineGame? GameLocked(long number) =>
        number >= 1 && number <= _games.Count ? _games[(int)number - 1] : null;

    /// <summary>What one game credit is worth: the selected game's denomination, or the configured value on a single-game machine.</summary>
    private int GameCreditCents
    {
        get
        {
            lock (_gate)
                return GameLocked(_selectedGame)?.Cents ?? _options.CentsPerGameCredit;
        }
    }

    /// <summary>The largest bet of the game being played, in its credits.</summary>
    private long PlayedMaxBet
    {
        get
        {
            lock (_gate)
                return GameLocked(_selectedGame)?.MaxBet ?? _options.MaxBet;
        }
    }

    /// <summary>
    /// The player chooses a game, or opens the game menu with zero; a change is reported with $8C, as the platform does.
    /// </summary>
    /// <returns>Why the choice could not be made, or null.</returns>
    public string? SelectGame(int number)
    {
        lock (_gate)
        {
            if (!HasGameTable)
                return "this machine holds a single game and has no game menu";
            if (number != 0 && GameLocked(number) is not { } game)
                return $"this machine has no game {number}";
            if (number != 0 && !GameLocked(number)!.Available)
                return $"game {number} is not offered to the player";
            if (_gameInProgress)
                return "a game is in progress";
            _selectedGame = number;
        }

        ReportSelectedGame();
        return null;
    }

    /// <summary>The player chooses a denomination, which on this machine is choosing the first game offered at it.</summary>
    /// <returns>Why the choice could not be made, or null.</returns>
    public string? SelectDenomination(int cents)
    {
        MachineGame? game;
        lock (_gate)
            game = _games.FirstOrDefault(candidate => candidate.Cents == cents && candidate.Available);

        return game is null
            ? $"no game on this machine is offered at {cents} cents"
            : SelectGame(game.Number);
    }

    /// <summary>The operator offers a game or withdraws it, which the platform tells the library as its denomination (QSP SasNetwork.cpp:1407).</summary>
    /// <returns>Why the change could not be made, or null.</returns>
    public string? SetGameEnabled(int number, bool enabled)
    {
        lock (_gate)
        {
            if (GameLocked(number) is not { } game)
                return HasGameTable ? $"this machine has no game {number}" : "this machine holds a single game";
            game.OperatorEnabled = enabled;
        }

        return null;
    }

    /// <summary>Reports the selected game once it differs from the one last reported, its number in two BCD digits.</summary>
    private void ReportSelectedGame()
    {
        int selected;
        lock (_gate)
        {
            if (_selectedGame == _reportedSelectedGame)
                return;
            selected = _reportedSelectedGame = _selectedGame;
        }

        // SAS_RTE_GameSelected (libsas.cpp:1777), called by the platform on a change (QSP SasNetwork.cpp:1491).
        RaiseEvent((byte)SasExceptionCode.GameSelected, BcdBytes(selected, 2));
    }

    /// <summary>
    /// A meter as the platform's provider answers it: the machine's own for game zero at no denomination, a game's own
    /// block for a game, and the sum of every game at a denomination for game zero there (QSP SasNetwork_Meters.hpp:1345).
    /// </summary>
    private long ReadGameMeter(long game, byte denomination, ushort meter)
    {
        lock (_gate)
        {
            if (!HasGameTable || !PlatformGameMeters.Contains(meter) || (game == 0 && denomination == 0))
                return Meters.Read(meter);

            if (game > 0)
                return GameLocked(game)?.Meters.Read(meter) ?? 0;

            return _games.Where(each => each.Code == denomination).Sum(each => each.Meters.Read(meter));
        }
    }

    /// <summary>Forgets every game's meters, as a memory clear forgets the machine's.</summary>
    private void ClearGameMeters()
    {
        lock (_gate)
        {
            foreach (MachineGame game in _games)
            {
                game.Meters.ClearAll();
                Array.Clear(game.LegacyMeters);
            }
        }
    }

    /// <summary>The games a player can reach, as the library counts them for $1F and $53 (libsas_internal.cpp:1087).</summary>
    private List<MachineGame> AvailableGamesLocked() => [.. _games.Where(game => game.Available)];

    /// <summary>The denominations game zero answers for: every denomination of every game (libsas_internal.cpp:1468).</summary>
    private bool MachineOffersLocked(byte denomination) =>
        denomination != 0 && _games.Any(game => game.Code == denomination);
}
