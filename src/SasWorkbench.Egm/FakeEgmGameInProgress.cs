using System.Diagnostics;
using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Egm;

/// <summary>A progressive hand pay the round owes, gathered as its levels are awarded and raised at its end.</summary>
internal readonly record struct ProgressiveHandpayOwed(byte Level, byte Group, long Cents);

/// <summary>
/// A game or a cash out held open, and the jackpot limit above which a win, a progressive or a bonus
/// is paid by an attendant.
/// </summary>
/// <remarks>
/// <para>
/// None of this is the machine library's: the library is told a game started and ended and a hand
/// pay began, and the platform decides when. So every behaviour here is QSP's, in its simplest form:
/// a game in progress holds a legacy bonus (<c>AsyncLpLegacyBonus</c>, SasNetwork_AftTransfer.hpp:1669),
/// a transfer to the machine (<c>AftTransferWaitsForGameEnd</c>) and a lock, which it answers pending
/// (<c>DecideAftLockAnswer</c>); a ticket being printed holds a bonus as any condition that interrupts
/// play does (ConditionDefaults.h:65); and a win above the limit locks the machine up (SasMarket.cpp:479).
/// </para>
/// <para>
/// The platform keeps a game win limit and a progressive win limit; this machine keeps one figure for both.
/// </para>
/// </remarks>
public sealed partial class FakeEgm
{
    private bool _gameInProgress;
    private long _jackpotLimitCents;
    private long _cashoutDue;
    private string? _cashoutWallet;
    private long _roundProgressiveCents;
    private ProgressiveHandpayOwed? _roundProgressiveHandpay;

    // GLS_GAME_LOCK_PENDING, which QSP answers while a game runs (SasNetwork_AftTransfer.hpp:1090).
    private const byte LockPending = 0x40;

    /// <summary>True between a game's start and its end.</summary>
    public bool GameInProgress
    {
        get
        {
            lock (_gate)
                return _gameInProgress;
        }
    }

    /// <summary>True while a cash out started with a duration is still printing.</summary>
    public bool CashoutInProgress
    {
        get
        {
            lock (_gate)
                return _cashoutDue != 0;
        }
    }

    /// <summary>Sets the jackpot limit in cents, as an operator would; zero removes it.</summary>
    public void SetJackpotLimit(long cents)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cents);
        lock (_gate)
            _jackpotLimitCents = cents;
    }

    /// <summary>Whether an award of this many cents is above the jackpot limit; the market's test is strictly greater (QSP SasMarket.cpp:479).</summary>
    private bool OverJackpotLimit(long cents)
    {
        lock (_gate)
            return _jackpotLimitCents > 0 && cents > _jackpotLimitCents;
    }

    /// <summary>Whether a game win of this many accounting units goes to an attendant (QSP NvramHandler.cpp:1316).</summary>
    private bool WinIsHandpaid(long creditsHeld, long won) =>
        won > 0 && (WinPassesCreditLimit(creditsHeld, won) || OverJackpotLimit(won * AccountingCents));

    private void ThrowIfGameInProgress()
    {
        if (_gameInProgress)
            throw new InvalidOperationException("a game is already running; end it with 'egm game end' first");
        if (_cashoutDue != 0)
            throw new InvalidOperationException("a cash out is still under way");
    }

    /// <summary>Starts one game and leaves it running: the wager is taken and the game start reported.</summary>
    /// <exception cref="InvalidOperationException">A game or a cash out is running, the host holds a lock, or the credits do not cover the bet.</exception>
    public void StartGame(long betCredits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(betCredits);
        WaitOutGameDelay();

        lock (_gate)
        {
            ThrowIfGameInProgress();
            if (_lockStatus == Locked)
                throw new InvalidOperationException("the host has locked the machine for a transfer, so no game can start");

            Wager(1, betCredits, 0);
            _gameInProgress = true;
            RaiseGameStarted(betCredits);
        }
    }

    /// <summary>Ends the running game with its win, then releases what waited for it.</summary>
    /// <exception cref="InvalidOperationException">No game is running, or the win needs a hand pay while one is held.</exception>
    public void EndGame(long winCredits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(winCredits);
        lock (_gate)
        {
            if (!_gameInProgress)
                throw new InvalidOperationException("no game is running; start one with 'egm game start'");
            if (_handpay is not null && WinIsHandpaid(Meters.Read(SasMeterCode.CurrentCredits), WonUnits(winCredits)))
                throw new InvalidOperationException("the win passes the credit or jackpot limit, and the machine already holds a hand pay");

            EndRound(1, winCredits, 0);
        }
    }

    /// <summary>
    /// Presses collect and keeps the cash out printing for <paramref name="lasting"/>; the ticket is
    /// paid then, and a bonus sent meanwhile waits for it (QSP eCashTicketOut interrupts play, ConditionDefaults.h:65).
    /// </summary>
    /// <exception cref="InvalidOperationException">A game or another cash out is running.</exception>
    public void CashoutLasting(string walletDirectory, TimeSpan lasting)
    {
        lock (_gate)
        {
            ThrowIfGameInProgress();
            _cashoutWallet = walletDirectory;
            _cashoutDue = Stopwatch.GetTimestamp() + Ticks(lasting);
        }

        WithdrawLockIfUnplayable();
    }

    /// <summary>Finishes a cash out whose printing time is up, once a turn of the polling loop.</summary>
    private void TickCashout()
    {
        string? wallet;
        lock (_gate)
        {
            if (_cashoutDue == 0 || Stopwatch.GetTimestamp() < _cashoutDue)
                return;

            wallet = _cashoutWallet;
            _cashoutWallet = null;
            _cashoutDue = 0;
        }

        if (wallet is not null)
            Cashout(wallet);
        PayLegacyEscrowIfReleased();
    }

    /// <summary>A lock asked for during a game is held pending, and taken once the game ends (QSP DecideAftLockAnswer).</summary>
    private void ReleaseLockPendingOnGameEnd()
    {
        lock (_gate)
        {
            if (_lockStatus == LockPending && IsPlayable())
                LockGame();
        }
    }

    /// <summary>
    /// Weighs one progressive award against the jackpot limit by everything the round has won on its
    /// levels (QSP BookProgressiveWin, NvramHandler.cpp:1516), and keeps it for the round's hand pay when it is over.
    /// </summary>
    /// <returns>True when the award is paid by hand.</returns>
    private bool ProgressiveIsHandpaid(byte level, long cents, bool sas)
    {
        lock (_gate)
        {
            _roundProgressiveCents += cents;
            if (!OverJackpotLimit(_roundProgressiveCents))
                return false;

            // Group and level of the highest contributor, a SAS level with the game's group (libsas_wrapper.cpp:3438).
            byte group = sas ? ProgressiveGroup : (byte)0;
            _roundProgressiveHandpay = _roundProgressiveHandpay is { } owed
                ? new ProgressiveHandpayOwed(
                    Math.Min(owed.Level, level), owed.Level <= level ? owed.Group : group, owed.Cents + cents)
                : new ProgressiveHandpayOwed(level, group, cents);
            return true;
        }
    }

    /// <summary>Locks the machine up for the progressives the round won above the limit, joined to a hand pay the round raised for its own win.</summary>
    private void RaiseRoundProgressiveHandpay()
    {
        lock (_gate)
        {
            if (_roundProgressiveHandpay is not { } owed)
                return;

            _roundProgressiveHandpay = null;

            // One lockup for everything the round won (QSP NvramHandler.cpp:1294); the amount is then in cents (libsas_internal.cpp:4899).
            _handpay = _handpay is { Reset: false } held
                ? held with
                {
                    AmountCents = held.AmountCents + owed.Cents,
                    Level = owed.Level,
                    Group = owed.Group,
                    ProgressiveCents = held.ProgressiveCents + owed.Cents,
                    ReadAcknowledged = false,
                }
                : new HandpayOwed(Core.Scripting.HandpayKind.Jackpot, owed.Cents, owed.Level, ReadAcknowledged: false)
                {
                    Group = owed.Group,
                    ProgressiveCents = owed.Cents,
                };
            _handpayIssuedAt = Stopwatch.GetTimestamp();
        }

        Queue((byte)SasExceptionCode.HandpayIsPending, [], priority: true);
        WithdrawLockIfUnplayable();
    }
}
