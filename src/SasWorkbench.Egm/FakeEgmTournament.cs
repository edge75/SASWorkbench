using System.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>
/// Host-controlled tournaments, which the platform has not and a case declares with <c>machine tournament on</c> (D60):
/// long poll $8C starts or ends a session, and $95 to $99 read its four meters.
/// </summary>
/// <remarks>
/// <para>
/// The protocol half is the machine library's: <c>lp8C_EnterExitTournamentMode</c> tests the address, the game number
/// and the game's tournament bit, acknowledges, and hands the five fields to the application (lp.cpp:8747); the meter
/// polls answer from four counts per game that only the application writes (lp.cpp:206, :9206, libsas.cpp:1477).
/// QSP has no tournaments, so the application half follows the GSA document in its simplest form:
/// </para>
/// <list type="bullet">
/// <item>A session asked for zeroes its game's four meters at once, and is entered at the next game that starts while
/// the machine is free: no game, door, tilt, hand pay, cash out or menu holds it (GSA 18.8).</item>
/// <item>A session with credits plays on them, not on the player's: the credit meter and the accounting meters stay
/// still, and the session ends when they are spent or a bet is more than is left. One without credits plays on the
/// player's credits as any game does.</item>
/// <item>The time, when there is one, runs from the session's first game; a game starting after it has run out is
/// outside the session.</item>
/// <item>Game zero counts every game; any other number only that game's rounds. Credits are accounting units.</item>
/// <item>The pulses byte is acknowledged and changes nothing; a restart ends the session, a memory clear its meters too.</item>
/// </list>
/// </remarks>
public sealed partial class FakeEgm
{
    // TOURNAMENT_BIT of the first feature byte (libsas.h:189).
    private const byte TournamentBit = 0x08;

    // Games played, games won, credits wagered and credits won, in the library's order (libsas.h:1526).
    private const int TournamentMeterCount = 4;

    private readonly Dictionary<long, long[]> _tournamentMeters = [];
    private TournamentSession? _tournament;

    // Whether the round now running is in the session, and whether the session's own credits pay it.
    private TournamentRound _tournamentRound;

    private enum TournamentRound
    {
        Outside,
        OnPlayerCredits,
        OnSessionCredits,
    }

    /// <summary>A session the host asked for: its game, its time in seconds, its credits and, once entered, what is left.</summary>
    private sealed class TournamentSession(long game, int seconds, long credits)
    {
        public long Game { get; } = game;

        public int Seconds { get; } = seconds;

        public long Credits { get; } = credits;

        public bool Entered { get; set; }

        public long Balance { get; set; } = credits;

        public long FirstGameAt { get; set; }

        public bool TimeIsUp(long now) =>
            Seconds > 0 && FirstGameAt != 0 && Stopwatch.GetElapsedTime(FirstGameAt, now) >= TimeSpan.FromSeconds(Seconds);
    }

    /// <summary>The tournament bit, set on a machine built with tournaments.</summary>
    private byte TournamentFeatureBits() => _options.Tournament ? TournamentBit : (byte)0;

    /// <summary>Answers or silences $8C, and false for any other command.</summary>
    private bool AnswerTournament(MachineLink link, byte command, ReadOnlySpan<byte> frame)
    {
        if (command != (byte)SasLongPollCode.LP_ENTEREXITTROURNAMENT)
            return false;

        // The address first (lp.cpp:8768).
        if (frame[0] != link.Address.Value)
            return true;

        // Address, command, game, minutes, seconds, four bytes of credits, pulses and the check sequence.
        if (frame.Length < 13)
        {
            Reject(link);
            return true;
        }

        // A game past the last or not configured, then the game's tournament bit: silence (lp.cpp:8786, :8791, :8809).
        long game = BcdValue(frame.Slice(2, 2));
        if (game > GameCount || TournamentFeatureBits() == 0)
            return true;

        // No busy response: the library's test is commented out (lp.cpp:8779).
        Send(link, [SasSymbol.Data8(link.Address.Value)]);

        int seconds = (int)((BcdValue(frame.Slice(4, 1)) * 60) + BcdValue(frame.Slice(5, 1)));
        long credits = BcdValue(frame.Slice(6, 4));
        lock (_gate)
            AskForTournamentLocked(game, seconds, credits);
        return true;
    }

    /// <summary>Zero time and zero credits ends the session; anything else replaces it with one waiting to be entered.</summary>
    private void AskForTournamentLocked(long game, int seconds, long credits)
    {
        if (seconds == 0 && credits == 0)
        {
            _tournament = null;
            return;
        }

        _tournament = new TournamentSession(game, seconds, credits);
        _tournamentMeters[game] = new long[TournamentMeterCount];
    }

    /// <summary>Long polls $95 to $99: silent for a game past the last or with tournaments off (lp.cpp:239, :251).</summary>
    private Dictionary<string, object>? TournamentMeters(SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        if (GameNumber(layout, request) is not { } game || TournamentFeatureBits() == 0)
            return NoAnswer;

        long[] meters;
        lock (_gate)
            meters = _tournamentMeters.TryGetValue(game, out long[]? kept) ? [.. kept] : new long[TournamentMeterCount];

        string[] names = ["games_played", "games_won", "credits_wagered", "credits_won"];
        var values = new Dictionary<string, object> { ["game_number"] = game };
        for (int meter = 0; meter < TournamentMeterCount; meter++)
        {
            if (layout.Command == (byte)SasLongPollCode.LP_SENDMETERS95_98
                || layout.Command - (byte)SasLongPollCode.LP_SENDTOURNAMENTGAMEPLAYED == meter)
            {
                values[names[meter]] = meters[meter];
            }
        }

        return values;
    }

    /// <summary>
    /// Decides whether the rounds about to start are in the session, entering one that waits if the machine is free, and
    /// counts their wager; true when the session's credits pay it, so the credit meter and accounting meters stay still.
    /// </summary>
    private bool StartTournamentRoundsLocked(int games, long wagered)
    {
        _tournamentRound = TournamentRound.Outside;
        if (_tournament is not { } session)
            return false;

        long now = Stopwatch.GetTimestamp();
        if (!session.Entered)
        {
            if (TournamentHeldLocked())
                return false;
            session.Entered = true;
        }

        // Out of time, or out of credits for this bet: the session is over and this game is the player's.
        if (session.TimeIsUp(now) || (session.Credits > 0 && session.Balance < wagered))
        {
            _tournament = null;
            return false;
        }

        if (session.Game != 0 && session.Game != (HasGameTable ? _selectedGame : 1))
            return false;

        if (session.FirstGameAt == 0)
            session.FirstGameAt = now;

        long[] meters = TournamentMetersLocked(session.Game);
        meters[0] += games;
        meters[2] += wagered;
        if (session.Credits == 0)
        {
            _tournamentRound = TournamentRound.OnPlayerCredits;
            return false;
        }

        session.Balance -= wagered;
        _tournamentRound = TournamentRound.OnSessionCredits;
        return true;
    }

    /// <summary>Counts the win of a round in the session; true when it goes back to the session's credits rather than the player's.</summary>
    private bool EndTournamentRoundLocked(long won)
    {
        TournamentRound round = _tournamentRound;
        _tournamentRound = TournamentRound.Outside;
        if (round == TournamentRound.Outside || _tournament is not { } session)
            return false;

        long[] meters = TournamentMetersLocked(session.Game);
        if (won > 0)
        {
            meters[1]++;
            meters[3] += won;
        }

        if (round != TournamentRound.OnSessionCredits)
            return false;

        session.Balance += won;
        if (session.Balance <= 0)
            _tournament = null;
        return true;
    }

    /// <summary>What keeps a session asked for from being entered: a game, a busy state, a hand pay, a cash out or a menu.</summary>
    private bool TournamentHeldLocked() =>
        IsLegacyBonusBusyLocked() || _handpay is not null || _gameInProgress || _cashoutDue != 0
        || _attendantMenuOpen || _operatorMenuOpen;

    private long[] TournamentMetersLocked(long game)
    {
        if (!_tournamentMeters.TryGetValue(game, out long[]? meters))
            _tournamentMeters[game] = meters = new long[TournamentMeterCount];
        return meters;
    }

    /// <summary>A restart ends the session; the library's meters are kept.</summary>
    private void RestartTournamentLocked()
    {
        _tournament = null;
        _tournamentRound = TournamentRound.Outside;
    }

    /// <summary>A memory clear ends the session and zeroes its meters.</summary>
    private void ClearTournamentLocked()
    {
        RestartTournamentLocked();
        _tournamentMeters.Clear();
    }
}
