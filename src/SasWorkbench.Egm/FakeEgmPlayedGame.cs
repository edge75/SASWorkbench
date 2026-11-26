using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Egm;

/// <summary>
/// A game played on a chosen game: the recall screen, which the platform has, and the bet-one button, which it has not
/// and which a case declares (D59).
/// </summary>
/// <remarks>
/// The recall screen is the platform's audit replay, reported through <c>SAS_RTE_GameRecallEntry</c> with the replayed
/// game's number and its replay index less one (QSP SasNetwork.cpp:1700). The bet-one button follows the GSA document
/// in its simplest form: each press commits one credit, up to the maximum bet, and is reported with $89, an event the
/// library sends only in the event format.
/// </remarks>
public sealed partial class FakeEgm
{
    /// <summary>The largest bet of a machine built with a bet-one button: $89 is sent only when it is ten or fewer.</summary>
    public const int BetButtonMaxBet = 5;

    /// <summary>How many past games the recall screen keeps.</summary>
    private const int RecalledGames = 10;

    // The game number of each game played, the last one first.
    private readonly LinkedList<int> _playedGames = new();

    // Credits committed with the bet-one button since the last game started.
    private int _committedCredits;

    /// <summary>Notes a game starting, for the recall screen; its start also uses the credits the bet button committed.</summary>
    private void NoteGameStartedLocked()
    {
        _playedGames.AddFirst(HasGameTable ? _selectedGame : 1);
        if (_playedGames.Count > RecalledGames)
            _playedGames.RemoveLast();
        _committedCredits = 0;
    }

    /// <summary>
    /// The attendant shows a past game on the recall screen, zero being the last; the platform reports the game's
    /// number and the index with $8A (QSP SasNetwork.cpp:1705, libsas.cpp:1808).
    /// </summary>
    /// <returns>Why it could not be shown, or null.</returns>
    public string? RecallGame(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        int game;
        lock (_gate)
        {
            if (index >= _playedGames.Count)
                return _playedGames.Count == 0
                    ? "no game has been played to recall"
                    : $"the recall screen holds {_playedGames.Count} game(s), so none {index} before the last";
            game = _playedGames.ElementAt(index);
        }

        RaiseEvent((byte)SasExceptionCode.GameRecallEntryDisplayed, [.. BcdBytes(game, 2), .. BcdBytes(index, 2)]);
        return null;
    }

    /// <summary>
    /// What a general poll with nothing queued answers: $1F while a wager placed with the bet-one button waits for the
    /// player to start the game, otherwise no activity (GSA 11.4).
    /// </summary>
    private byte IdleCode()
    {
        lock (_gate)
        {
            return _options.BetButton && _committedCredits > 0 && !_gameInProgress
                ? (byte)SasExceptionCode.WaitingForPlayerInput
                : (byte)SasExceptionCode.NoActivity;
        }
    }

    /// <summary>
    /// The player presses bet-one: one more credit is committed and reported with $89, while there are credits for it
    /// and the bet is below its maximum (GSA 11.21).
    /// </summary>
    /// <returns>Why the button does nothing on this machine, or null.</returns>
    public string? PressBet()
    {
        if (!_options.BetButton)
            return "the machine has no bet-one button";

        lock (_gate)
        {
            long committedUnits = (_committedCredits + 1L) * GameCreditCents / AccountingCents;
            if (_gameInProgress || _committedCredits >= BetButtonMaxBet
                || Meters.Read(SasMeterCode.CurrentCredits) < committedUnits)
            {
                return null;
            }

            _committedCredits++;
        }

        RaiseEvent((byte)SasExceptionCode.CoinOrCreditWagered, []);
        return null;
    }
}
