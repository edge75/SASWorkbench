using System.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>The last legacy bonus award, as long poll $90 reports it until the host has read it.</summary>
internal readonly record struct LegacyBonusAward(byte Multiplier, long MultipliedWin, byte TaxStatus, long Bonus);

/// <summary>
/// Legacy bonusing: long polls $8A, $2E, $8B, $90 and $9A, the escrow a bonus waits in, the game
/// delay, and exception $7C.
/// </summary>
/// <remarks>
/// <para>
/// The protocol half is the machine library's, check for check (<c>lp8A_InitiateLegacyBonusPay</c>,
/// <c>lp2E_GameDelay</c>, <c>lp8B_InitiateMultipliedJackpot</c>, <c>lp90_SendLegacyBonusWinAmount</c>,
/// <c>lp9A_SendLegacyBonusMeters</c>, <c>SAS_LegacyBonusAward</c>). What the library hands to the
/// application - whether the machine is busy, when an acknowledged bonus is paid, what it moves -
/// is answered as QSP answers it (<c>AsyncLpLegacyBonus</c>, <c>ProcessLegacyBonus</c>,
/// <c>IsLegacyBonusBusyCondition</c>, <c>GameDelay</c>), reduced to what this machine has.
/// </para>
/// <para>
/// Multiplied jackpots are off unless a case declares them, as QSP never gives the library the jackpot
/// multiplier bit (SasNetwork.cpp:286); see the multiplied jackpot partial.
/// </para>
/// </remarks>
public sealed partial class FakeEgm
{
    // LEGACY_BONUS_AWARDS_BIT of the first feature byte (libsas.h:188).
    private const byte LegacyBonusAwardsBit = 0x04;

    // Deductible, non-deductible and wager match, the library's three legacy meters (libsas.h:1535).
    private const int LegacyTaxStatuses = 3;

    // The library's busy response is the address and then 00 (libsas_threads_sync.cpp:374).
    private const byte BusyResponse = 0x00;

    private bool _legacyBonus;
    private readonly long[] _legacyEscrow = new long[LegacyTaxStatuses];
    private readonly long[] _legacyMeters = new long[LegacyTaxStatuses];
    private LegacyBonusAward _lastLegacyAward;
    private int _gameDelayTenths;
    private long _gameDelayEnds;
    private readonly Dictionary<SasChannel, byte[]> _unacknowledgedLegacyReads = [];

    // The most of a game delay a stimulus waits out, and how often it looks whether the machine has stopped.
    private static readonly TimeSpan LongestGameDelayWait = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan GameDelayWaitSlice = TimeSpan.FromMilliseconds(50);

    /// <summary>True while the machine takes legacy bonuses.</summary>
    public bool LegacyBonusOn
    {
        get
        {
            lock (_gate)
                return _legacyBonus;
        }
    }

    /// <summary>Turns legacy bonusing on or off, as the operator does in the machine's network menu.</summary>
    public void SetLegacyBonus(bool on)
    {
        lock (_gate)
            _legacyBonus = on;
    }

    /// <summary>The legacy bonus bit of the first feature byte while the operator has it on.</summary>
    private byte LegacyBonusFeatureBits() => LegacyBonusOn ? LegacyBonusAwardsBit : (byte)0;

    /// <summary>
    /// Answers or silences a legacy bonus poll, and false for any other command.
    /// </summary>
    /// <remarks>
    /// Reached after the check sequence and the channel category gate, which the library applies to
    /// all of these but $9A, a command no category owns; the feature bit and address follow in its order.
    /// </remarks>
    private bool AnswerLegacyBonus(MachineLink link, byte command, ReadOnlySpan<byte> frame)
    {
        switch (command)
        {
            case (byte)SasLongPollCode.LP_INITALEGACYBONUSPAY:
                // Channel, then the feature bit, then the address (lp.cpp:8621, :8628).
                if (LegacyBonusOn && frame[0] == link.Address.Value)
                    InitiateLegacyBonus(link, frame);
                return true;

            case (byte)SasLongPollCode.LP_DELAYGAME:
                // The feature bit, the channel, the address (lp.cpp:1716, :1725).
                if (LegacyBonusOn && frame[0] == link.Address.Value)
                    DelayGame(link, frame);
                return true;

            case (byte)SasLongPollCode.LP_INITMULTIPLEJACKPOT:
                AnswerMultipliedJackpot(link, frame);
                return true;

            case (byte)SasLongPollCode.LP_SENDLEGACYBONUSWIN:
            case (byte)SasLongPollCode.LP_SENDLEGACYBONUSMETERS:
                // Silent with the bit clear (lp.cpp:8981, :9308); answered by the usual path otherwise.
                return !LegacyBonusOn || frame[0] != link.Address.Value;

            default:
                return false;
        }
    }

    /// <summary>Long poll $8A: busy, or acknowledged and handed to the platform's escrow.</summary>
    private void InitiateLegacyBonus(MachineLink link, ReadOnlySpan<byte> frame)
    {
        // Address, command, four bytes of BCD amount, the tax status and the check sequence.
        if (frame.Length < 9)
        {
            Reject(link);
            return;
        }

        // The platform's busy states answer before the acknowledgement (lp.cpp:8652).
        if (IsLegacyBonusBusy())
        {
            Send(link, [SasSymbol.Data8(link.Address.Value), SasSymbol.Data8(BusyResponse)]);
            return;
        }

        Send(link, [SasSymbol.Data8(link.Address.Value)]);

        long credits = BcdValue(frame.Slice(2, 4));
        byte taxStatus = frame[6];

        // The platform drops a tax status past wager match after the acknowledgement (SasNetwork_AftTransfer.hpp:1648).
        if (taxStatus >= LegacyTaxStatuses)
            return;

        lock (_gate)
            _legacyEscrow[taxStatus] += credits;

        PayLegacyEscrowIfReleased();
    }

    /// <summary>Long poll $2E: acknowledged, and a delay already running starts again from the new value.</summary>
    private void DelayGame(MachineLink link, ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 6)
        {
            Reject(link);
            return;
        }

        Send(link, [SasSymbol.Data8(link.Address.Value)]);

        // Binary, least significant byte first, in tenths of a second (lp.cpp:1755).
        int tenths = frame[2] | (frame[3] << 8);
        lock (_gate)
        {
            _gameDelayTenths = tenths;

            // A delay that is running is replaced, and zero ends it (QSP SasNetwork_Callbacks.hpp:775).
            if (GameDelayRunningLocked())
                _gameDelayEnds = DelayEndFrom(Stopwatch.GetTimestamp(), tenths);
        }

        PayLegacyEscrowIfReleased();
    }

    /// <summary>The platform's busy states: a door open, or the host having shut the machine down (QSP SasGameRules.h:255).</summary>
    private bool IsLegacyBonusBusy()
    {
        lock (_gate)
            return IsLegacyBonusBusyLocked();
    }

    /// <summary>The busy test with the gate held; the doors' own lock is only ever taken inside it, never around it.</summary>
    private bool IsLegacyBonusBusyLocked()
    {
        bool doorOpen;
        lock (_openDoors)
            doorOpen = _openDoors.Count > 0;

        // A peripheral or memory tilt is busy too (QSP SasGameRules.h:255, :278).
        return doorOpen || !_enabled || PeripheralTilt() || MemoryTilt();
    }

    /// <summary>True while an acknowledged bonus must stay in escrow: a hand pay, a busy state, a game, a cash out or the game delay.</summary>
    /// <remarks>A game and a printing ticket hold it as QSP's escrow does (SasNetwork_AftTransfer.hpp:1669, SasNetwork_Conditions.hpp:404).</remarks>
    private bool LegacyEscrowHeldLocked() =>
        IsLegacyBonusBusyLocked() || _handpay is not null || GameDelayRunningLocked()
        || _gameInProgress || _cashoutDue != 0;

    private bool GameDelayRunningLocked() =>
        _gameDelayEnds != 0 && Stopwatch.GetTimestamp() < _gameDelayEnds;

    private static long DelayEndFrom(long now, int tenths) =>
        tenths == 0 ? 0 : now + (long)(tenths / 10.0 * Stopwatch.Frequency);

    /// <summary>
    /// Holds the next game back until the game delay has run, as a player would be held, and gives
    /// up when the machine stops.
    /// </summary>
    /// <exception cref="InvalidOperationException">More of the delay is left than a case could sensibly wait.</exception>
    private void WaitOutGameDelay()
    {
        while (_running)
        {
            TimeSpan left;
            lock (_gate)
            {
                left = GameDelayRunningLocked()
                    ? Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), _gameDelayEnds)
                    : TimeSpan.Zero;
            }

            if (left <= TimeSpan.Zero)
                return;

            if (left > LongestGameDelayWait)
            {
                throw new InvalidOperationException(
                    $"The game delay holds the next game for {left.TotalSeconds:F0} more seconds, longer than "
                    + $"a case waits ({LongestGameDelayWait.TotalSeconds:F0} s). Long poll $2E carries tenths of a "
                    + "second in binary, least significant byte first: five seconds is 32 00.");
            }

            Thread.Sleep(left < GameDelayWaitSlice ? left : GameDelayWaitSlice);
        }
    }

    /// <summary>Starts the configured delay at game end, as QSP does after the game end exception (SasNetwork_Conditions.hpp:691).</summary>
    private void StartGameDelay()
    {
        lock (_gate)
            _gameDelayEnds = DelayEndFrom(Stopwatch.GetTimestamp(), _gameDelayTenths);

        PayLegacyEscrowIfReleased();
    }

    /// <summary>Pays an escrow the machine no longer has to hold, once a turn of the polling loop.</summary>
    private void TickLegacyBonus() => PayLegacyEscrowIfReleased();

    /// <summary>
    /// Pays every tax status with something in escrow, one award each, when nothing holds it back
    /// (QSP ProcessLegacyBonus); the test, the payment and the emptied escrow are one step under the gate.
    /// </summary>
    private void PayLegacyEscrowIfReleased()
    {
        lock (_gate)
        {
            if (Array.TrueForAll(_legacyEscrow, credits => credits == 0) || LegacyEscrowHeldLocked())
                return;

            for (byte taxStatus = 0; taxStatus < LegacyTaxStatuses; taxStatus++)
            {
                long due = _legacyEscrow[taxStatus];
                _legacyEscrow[taxStatus] = 0;
                if (due > 0)
                    PayLegacyBonusLocked(taxStatus, due);
            }
        }
    }

    /// <summary>
    /// One award, with the gate held: onto the credit meter as a machine-paid external bonus (or to an attendant above the jackpot limit), onto
    /// the legacy meter for its tax status, stored for $90 and reported by $7C.
    /// </summary>
    private void PayLegacyBonusLocked(byte taxStatus, long credits)
    {
        // An award above the jackpot limit is an external bonus hand pay, reported at once all the same (QSP SasNetwork_AftTransfer.hpp:1708).
        if (OverJackpotLimit(credits * AccountingCents))
        {
            RaiseBonusHandpay(credits * AccountingCents);
        }
        else
        {
            // A win the machine pays onto the credit meter, metered as the AFT coin-out bonus is (QSP BonusIn, MoneyInHandler.cpp:635).
            Meters.SetCurrentCredits(Meters.Read(SasMeterCode.CurrentCredits) + credits);
            Meters.Increment(SasMeterCode.TotalCoinOutCredits, credits);
            Meters.Increment(SasMeterCode.TotalMachinePaidExternalBonusWin_Crd, credits);
        }

        // A bonus from $8A has no multiplier and no multiplied win (SasNetwork_AftTransfer.hpp:1738); the platform writes the machine's
        // legacy meter alone (:1745) and the library in full manual moves no game's (libsas.cpp:2617), so no game's moves here.
        _legacyMeters[taxStatus] += credits;
        RaiseLegacyBonusAwardLocked(new LegacyBonusAward(0x00, 0, taxStatus, credits));
    }

    /// <summary>What long poll $90 answers: the last award, until a reading of it has been acknowledged.</summary>
    private Dictionary<string, object> LegacyBonusWin()
    {
        LegacyBonusAward award;
        lock (_gate)
            award = _lastLegacyAward;

        return new Dictionary<string, object>
        {
            ["multiplier"] = award.Multiplier,
            ["multiplied_win"] = award.MultipliedWin,
            ["tax_status"] = award.TaxStatus,
            ["bonus"] = award.Bonus,
        };
    }

    /// <summary>What long poll $9A answers: the machine's three legacy meters for game zero or a single game, otherwise the game's.</summary>
    private Dictionary<string, object>? LegacyBonusMeters(SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        // A game past the last is silence (lp.cpp:9330).
        if (GameNumber(layout, request) is not { } game)
            return NoAnswer;

        lock (_gate)
        {
            // A game of a multi-game machine answers its own meters, which the platform never writes (D59).
            long[] meters = HasGameTable && game > 0 ? GameLocked(game)?.LegacyMeters ?? new long[LegacyTaxStatuses] : _legacyMeters;
            return new Dictionary<string, object>
            {
                ["game_number"] = game,
                ["deductible"] = meters[0],
                ["non_deductible"] = meters[1],
                ["wager_match"] = meters[2],
            };
        }
    }

    /// <summary>Remembers a $90 reading just sent, so the next message can acknowledge it.</summary>
    private void RememberLegacyBonusReading(MachineLink link, ReadOnlySpan<byte> frame)
    {
        lock (_gate)
            _unacknowledgedLegacyReads[link.Channel] = frame.ToArray();
    }

    /// <summary>A loop break is an implied NACK, so the reading it cut off is never acknowledged.</summary>
    private void ForgetLegacyBonusReading(MachineLink link)
    {
        lock (_gate)
            _unacknowledgedLegacyReads.Remove(link.Channel);
    }

    /// <summary>
    /// Any message other than the same $90 again acknowledges the reading and clears the award, the
    /// pending action the library queues behind it (lp.cpp:9022, libsas_pending.cpp:250).
    /// </summary>
    private void NoteLegacyBonusAcknowledgement(MachineLink link, ReadOnlySpan<byte> frame)
    {
        lock (_gate)
        {
            if (!_unacknowledgedLegacyReads.TryGetValue(link.Channel, out byte[]? sent) || frame.SequenceEqual(sent))
                return;

            _unacknowledgedLegacyReads.Remove(link.Channel);
            _lastLegacyAward = default;
        }
    }
}
