using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm;

/// <summary>
/// The coin acceptor, the hopper and the cashbox: built only when a case declares them, as the platform has neither
/// coin acceptor nor hopper (D58).
/// </summary>
/// <remarks>
/// The library only queues what the application writes and stores the hopper status it is given
/// (SAS_RegisterHopperStatus, libsas.cpp:1297), so the protocol half is the queue, long polls 4F and 2C and the
/// coin meters. The application half has no QSP counterpart and is the simplest form of what the GSA documents
/// expect, each choice citing the case it serves. A coin is worth one accounting unit.
/// </remarks>
public sealed partial class FakeEgm
{
    // GSA 4.34, 4.39: a coin-in tilt and a diverter tilt take the machine out of play until cleared.
    private static readonly Dictionary<string, PeripheralCondition> CoinConditions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tilt"] = new("coin in tilt", SasExceptionCode.CoinInTilt, SasExceptionCode.NoActivity, true),
        ["diverter"] = new("diverter malfunction", SasExceptionCode.DiverterMalfunction, SasExceptionCode.NoActivity, true),
    };

    // GSA 4.34, 4.37, 4.38, 4.53, 5.11: each fault with the status long poll 4F reports for it (libsas.h:1758).
    private static readonly Dictionary<string, (PeripheralCondition Condition, byte Status)> HopperConditions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["empty"] = (new("hopper empty", SasExceptionCode.HopperEmpty, SasExceptionCode.NoActivity, true), 0x07),
            ["flooded_optics"] = (new("hopper flooded optics", SasExceptionCode.CoinOutTilt, SasExceptionCode.NoActivity, true), 0x01),
            ["reverse_coin"] = (new("hopper reverse coin", SasExceptionCode.CoinOutTilt, SasExceptionCode.NoActivity, true), 0x02),
            ["coin_too_short"] = (new("hopper coin too short", SasExceptionCode.CoinOutTilt, SasExceptionCode.NoActivity, true), 0x03),
            ["jam"] = (new("hopper jam", SasExceptionCode.CoinOutTilt, SasExceptionCode.NoActivity, true), 0x04),
            ["runaway"] = (new("hopper runaway", SasExceptionCode.ExtraCoinPaid, SasExceptionCode.NoActivity, true), 0x05),
            ["disconnected"] = (new("hopper disconnected", SasExceptionCode.CoinOutTilt, SasExceptionCode.NoActivity, true), 0x06),
            ["full"] = (new("hopper full", SasExceptionCode.HopperFull, SasExceptionCode.NoActivity, false), 0x00),
            ["low"] = (new("hopper low", SasExceptionCode.HopperLevelLow, SasExceptionCode.NoActivity, false), 0x00),
        };

    // QSP SasNetwork_Conditions.hpp:74 and ConditionDefaults.h:71: a removed stacker raises $1B, its return $1C, and interrupts play.
    private static readonly PeripheralCondition CashboxRemovedCondition =
        new("cashbox removed", SasExceptionCode.CashboxRemoved, SasExceptionCode.CashboxInstalled, true);

    private int _hopperLevel;
    private long _hopperLimitCents;
    private bool _winsToHopper;

    // The library's sHopperInfo, as SAS_RegisterHopperStatus last stored it (libsas_internal.cpp:364 starts it at zero).
    private byte _hopperStatusStored;
    private byte _hopperPercentStored;
    private long _hopperLevelStored;

    /// <summary>How many coins the hopper holds.</summary>
    public int HopperLevel
    {
        get
        {
            lock (_gate)
                return _hopperLevel;
        }
    }

    private bool HasHopper => _options.Hopper;

    /// <summary>Called once from the constructor: the hopper as the declaration built it.</summary>
    private void StartDevices()
    {
        _hopperLevel = _options.HopperLevel;
        _hopperLimitCents = _options.HopperLimitCents;
        if (HasHopper)
            RegisterHopperStatusLocked();
    }

    /// <summary>Coins at the coin acceptor: to the hopper while it has room, otherwise to the drop; an external acceptor's always to the drop.</summary>
    /// <returns>Null when taken, otherwise why not.</returns>
    public string? InsertCoins(int coins)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(coins);
        if (_options.CoinAcceptor == CoinAcceptorKind.None)
            return "the machine has no coin acceptor";

        lock (_gate)
        {
            // GSA 6.9 to 6.12: every coin counts as coin acceptor credits; one the hopper cannot take goes to the drop.
            int toHopper = _options.CoinAcceptor == CoinAcceptorKind.Internal && HasHopper
                ? Math.Min(coins, Math.Max(0, _options.HopperCapacity - _hopperLevel))
                : 0;
            int toDrop = coins - toHopper;

            Meters.Increment(SasMeterCode.TotalCreditsFromCoinAcceptor, coins);
            if (_options.CoinAcceptor == CoinAcceptorKind.External)
                Meters.Increment(SasMeterCode.TotalCreditsFromExternalCoinAcceptor, coins);
            if (toDrop > 0)
            {
                Meters.Increment(SasMeterCode.TotalCreditsFromCoinsToDrop, toDrop);
                Meters.Increment(SasMeterCode.TotalDropIn_Crd, toDrop);
            }

            Meters.SetCurrentCredits(Meters.Read(SasMeterCode.CurrentCredits) + coins);
            if (toHopper > 0)
                SetHopperLevelLocked(_hopperLevel + toHopper);
        }

        return null;
    }

    /// <summary>Puts the coin acceptor into a fault, or clears its faults; null when done, otherwise why not.</summary>
    public string? CoinFault(string fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        if (_options.CoinAcceptor == CoinAcceptorKind.None)
            return "the machine has no coin acceptor";

        switch (fault.ToLowerInvariant())
        {
            case PeripheralFaultStep.Cleared:
                ClearConditions(CoinConditions.Values);
                return null;

            // GSA 4.35 and 4.36: a coin pulled back and a coin past the lockout are reported once, in any state.
            case "reversed":
                RaiseException(SasExceptionCode.ReverseCoinIn);
                return null;

            case "lockout":
                RaiseException(SasExceptionCode.CoinInLockoutMalfunction);
                return null;

            default:
                return CoinConditions.TryGetValue(fault, out PeripheralCondition? condition)
                    ? Activate(condition)
                    : $"the coin acceptor has no fault called '{fault}'";
        }
    }

    /// <summary>Puts the hopper into a fault, or clears its faults; null when done, otherwise why not.</summary>
    public string? HopperFault(string fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        if (!HasHopper)
            return "the machine has no hopper";

        if (fault.Equals(PeripheralFaultStep.Cleared, StringComparison.OrdinalIgnoreCase))
        {
            ClearConditions(HopperConditions.Values.Select(entry => entry.Condition));
            lock (_gate)
                RegisterHopperStatusLocked();
            return null;
        }

        if (!HopperConditions.TryGetValue(fault, out (PeripheralCondition Condition, byte Status) entry))
            return $"the hopper has no fault called '{fault}'";

        string? refusal = Activate(entry.Condition);
        lock (_gate)
            RegisterHopperStatusLocked();
        return refusal;
    }

    /// <summary>The attendant fills or empties the hopper.</summary>
    public string? SetHopperLevel(int coins)
    {
        if (!HasHopper)
            return "the machine has no hopper";
        if (coins < 0 || coins > _options.HopperCapacity)
            return $"the hopper holds between 0 and {_options.HopperCapacity} coins";

        lock (_gate)
            SetHopperLevelLocked(coins);
        return null;
    }

    /// <summary>The operator sets the hopper limit.</summary>
    public string? SetHopperLimit(long cents)
    {
        if (!HasHopper)
            return "the machine has no hopper";

        lock (_gate)
            _hopperLimitCents = cents;
        return null;
    }

    /// <summary>The operator chooses whether game wins are paid from the hopper (GSA 4.37.2, 6.8).</summary>
    public string? SetWinPayout(bool hopper)
    {
        if (!HasHopper)
            return "the machine has no hopper";

        lock (_gate)
            _winsToHopper = hopper;
        return null;
    }

    /// <summary>Removes or installs the cashbox, as the platform reports its note stacker.</summary>
    public void SetCashbox(bool removed)
    {
        if (removed)
            Activate(CashboxRemovedCondition);
        else
            ClearConditions([CashboxRemovedCondition]);
    }

    /// <summary>
    /// A cash out on a machine with a hopper: up to the hopper limit it is paid in coins, above it the hopper pays the
    /// partial pay and the attendant the rest (GSA 4.21.2, 4.37.1, 5.4.2).
    /// </summary>
    private void PayOutFromHopper()
    {
        if (HopperTilted())
            return;

        long credits = Meters.Read(SasMeterCode.CurrentCredits) - (CurrentRestrictedCents() / AccountingCents);
        if (credits <= 0)
            return;

        bool handpay;
        lock (_gate)
        {
            if (_handpay is not null)
                return;

            handpay = credits * AccountingCents > _hopperLimitCents;
            if (!handpay)
            {
                long paid = PayCoinsLocked(credits);
                Meters.Increment(SasMeterCode.TotalCancelledCredits, paid);
                Meters.SetCurrentCredits(Meters.Read(SasMeterCode.CurrentCredits) - paid);
                ClampNonrestricted();
                return;
            }

            long partial = PayCoinsLocked(Math.Min(_options.PartialPayCoins, credits));
            Meters.Increment(SasMeterCode.TotalCancelledCredits, partial);
            Meters.SetCurrentCredits(Meters.Read(SasMeterCode.CurrentCredits) - credits);
            ClampNonrestricted();
            StartHandpayLocked(new HandpayOwed(HandpayKind.CancelledCredit, (credits - partial) * AccountingCents, CancelledCreditLevel)
            {
                PartialPayUnits = partial,
            });
        }

        WithdrawLockIfUnplayable();
    }

    /// <summary>
    /// The player cancels a cash out waiting for an attendant: the credits go back on the meter and the library refuses
    /// the hand pay, writing $55 (internalHandpayRefuse, libsas_internal.cpp:5370). Anything else has nothing to cancel.
    /// </summary>
    /// <returns>False when there was nothing to cancel, which leaves the machine as it was (GSA 4.25).</returns>
    public bool CancelHandpay()
    {
        lock (_gate)
        {
            // GSA 4.25: only a cash out can be cancelled, and only before the attendant's reset.
            if (_handpay is not { Kind: HandpayKind.CancelledCredit, Bonus: false } owed || !_handpayRegistered)
                return false;

            _handpay = null;
            Meters.SetCurrentCredits(Meters.Read(SasMeterCode.CurrentCredits) + (owed.AmountCents / AccountingCents));
            foreach (MachineLink link in _links)
            {
                if (link.Handpays.Refuse(!link.AwaitingSync, out HandpayWrite write))
                    ApplyHandpayWriteLocked(link, write);
            }

            _handpayRegistered = false;
        }

        return true;
    }

    /// <summary>The partial pay the hopper makes towards a jackpot hand pay, in accounting units (GSA 5.3.2, 15.12).</summary>
    private long PayJackpotPartialLocked(long owedUnits)
    {
        if (!HasHopper || _options.PartialPayCoins == 0 || HopperTilted())
            return 0;

        long partial = PayCoinsLocked(Math.Min(_options.PartialPayCoins, owedUnits));
        Meters.Increment(SasMeterCode.TotalCoinOutCredits, partial);
        return partial;
    }

    /// <summary>A game win paid from the hopper when the operator chose it; what the hopper cannot pay goes to the credit meter (GSA 4.37.2).</summary>
    /// <returns>The part of the win left for the credit meter.</returns>
    private long PayWinFromHopperLocked(long won)
    {
        if (!HasHopper || !_winsToHopper || won <= 0)
            return won;

        return won - PayCoinsLocked(won);
    }

    /// <summary>Pays coins from the hopper, counting them as paid from it; an empty hopper pays what it has and raises $23.</summary>
    /// <returns>How many coins were paid.</returns>
    private long PayCoinsLocked(long coins)
    {
        if (coins <= 0)
            return 0;

        long paid = Math.Min(coins, _hopperLevel);
        Meters.Increment(SasMeterCode.TotalCreditsPaidFromHopper, paid);
        SetHopperLevelLocked(_hopperLevel - (int)paid);
        if (paid < coins)
            HopperFault("empty");
        return paid;
    }

    private void SetHopperLevelLocked(int coins)
    {
        _hopperLevel = coins;
        RegisterHopperStatusLocked();
    }

    /// <summary>True while a hopper fault takes the machine out of play.</summary>
    private bool HopperTilted()
    {
        lock (_conditions)
            return HopperConditions.Values.Any(entry => entry.Condition.Tilts && _conditions.ContainsKey(entry.Condition.Name));
    }

    /// <summary>
    /// The application hands the library the hopper's status, how full it is and its level; SAS_RegisterHopperStatus
    /// stores the level only when the hopper cannot measure it (libsas.cpp:1325), the opposite of what long poll 4F
    /// sends (lp.cpp:2777).
    /// </summary>
    private void RegisterHopperStatusLocked()
    {
        byte status = 0x00;
        lock (_conditions)
        {
            foreach ((PeripheralCondition condition, byte code) in HopperConditions.Values)
            {
                if (code != 0x00 && _conditions.ContainsKey(condition.Name))
                    status = code;
            }
        }

        byte percent = (byte)Math.Min(100, _hopperLevel * 100 / Math.Max(1, _options.HopperCapacity));
        _hopperStatusStored = status;
        _hopperPercentStored = percent;
        if (percent == 0xFF)
            _hopperLevelStored = _hopperLevel;
    }

    /// <summary>Long poll 4F on a machine with a hopper: status, percentage full, and the level when it is measured (lp.cpp:2773).</summary>
    private Dictionary<string, object> DeclaredHopperStatus()
    {
        lock (_gate)
        {
            var values = new Dictionary<string, object>
            {
                ["length"] = _hopperPercentStored != 0xFF ? 6 : 2,
                ["status"] = _hopperStatusStored,
                ["percent_full"] = _hopperPercentStored,
            };
            if (_hopperPercentStored != 0xFF)
                values["hopper_level"] = _hopperLevelStored;
            return values;
        }
    }

    /// <summary>Long poll 2C on a machine with a hopper: the stored level, silent when the hopper cannot measure it (lp.cpp:1589).</summary>
    private Dictionary<string, object> DeclaredHopperLevel()
    {
        lock (_gate)
        {
            return _hopperPercentStored == 0xFF
                ? NoAnswer
                : new Dictionary<string, object> { ["hopper_level"] = _hopperLevelStored };
        }
    }

    /// <summary>
    /// $A4 on a declared hopper machine, which sets its hopper limit as every game's cash-out limit (D61): game zero to
    /// the last answers, a game past it is silent (lp.cpp:9520), and the limit is in accounting units, capped at the
    /// four digits the reply holds.
    /// </summary>
    private Dictionary<string, object> CashoutLimit(SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        if (GameNumber(layout, request) is not { } game)
            return NoAnswer;

        long limit;
        lock (_gate)
            limit = _hopperLimitCents / AccountingCents;

        return new Dictionary<string, object>
        {
            ["game_number"] = game,
            ["cashout_limit"] = Math.Min(limit, 9999),
        };
    }

    /// <summary>Clears the coin acceptor, hopper and reel faults and puts the cashbox back, as <c>egm settle</c> does.</summary>
    private void ClearDeviceFaults()
    {
        ClearConditions(CoinConditions.Values);
        ClearConditions(HopperConditions.Values.Select(entry => entry.Condition));
        ClearConditions(ReelConditions.Values);
        ClearConditions([CashboxRemovedCondition]);
        if (HasHopper)
        {
            lock (_gate)
                RegisterHopperStatusLocked();
        }
    }
}
