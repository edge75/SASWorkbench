using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Egm;

/// <summary>
/// The machine's configuration that chapter 16 leans on: transfers on or off, the credit limit and the
/// transfer limit published from it, and restricted credit with its pool and expiration.
/// </summary>
/// <remarks>
/// <para>
/// The library keeps the restricted pool and expiration itself and checks them (lp.cpp:5459,
/// libsas_internal.cpp:3275 and :5546, calculateExpirationDate in libsas_utils.cpp:614); this machine
/// keeps them the same way. The rest is the platform's, in its simplest form: the transfer limit is the
/// room left under the credit limit (QSP <c>CashlessInHeadroom</c>), a win past the credit limit is a
/// hand pay (QSP <c>CombinedAwardIsHandpaid</c>), restricted credit is wagered first (QSP
/// NvramHandler.cpp:947), and it never leaves on a cash-out ticket.
/// </para>
/// </remarks>
public sealed partial class FakeEgm
{
    private bool _aftOn;
    private long _creditLimitCents;

    // The part of the credit meter that is restricted promotional credit, in cents, with the pool and expiration it carries.
    private long _restrictedCents;
    private ushort _restrictedPool;
    private byte[] _restrictedExpiration = new byte[4];

    /// <summary>True while the machine is configured to perform funds transfers.</summary>
    public bool AftOn
    {
        get
        {
            lock (_gate)
                return _aftOn;
        }
    }

    /// <summary>
    /// Turns funds transfer on or off, as an attendant would in the machine's configuration.
    /// </summary>
    /// <remarks>A change of AFT configuration cancels any registration and lets go of a lock, as QSP's AftConfigurationChanged does.</remarks>
    public void SetAft(bool on)
    {
        lock (_gate)
        {
            if (_aftOn == on)
                return;

            _aftOn = on;
            UnlockGame();
            CancelRegistration();
        }
    }

    /// <summary>The credit meter ceiling in cents, zero when there is none.</summary>
    public long CreditLimitCents
    {
        get
        {
            lock (_gate)
                return _creditLimitCents;
        }
    }

    /// <summary>Sets the credit meter ceiling, as an attendant would in the machine's configuration; zero removes it.</summary>
    public void SetCreditLimit(long cents)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cents);
        lock (_gate)
            _creditLimitCents = cents;

        // An operator's save on a page other than the network one reports $3C (QSP SasNetwork.cpp:747, SasRules.h:391).
        QueueOrdinary((byte)SasExceptionCode.OperatorChangedOptions);
    }

    /// <summary>
    /// The transfer limit the platform publishes and the library checks a transfer against: the configured
    /// maximum, never more than the room left under the credit limit (QSP AftTransferLimitToCreditMeter).
    /// </summary>
    private long TransferLimitCents()
    {
        long limit = _options.AftTransferLimitCents;
        long ceiling;
        lock (_gate)
            ceiling = _creditLimitCents;
        if (ceiling == 0)
            return limit;

        long held = Meters.Read(SasMeterCode.CurrentCredits) * AccountingCents;
        return Math.Min(limit, Math.Max(0, ceiling - held));
    }

    /// <summary>Whether a win of this many accounting units, on the meter as it stands, goes past the credit limit (QSP CombinedAwardIsHandpaid).</summary>
    private bool WinPassesCreditLimit(long creditsAfterWager, long won)
    {
        long ceiling;
        lock (_gate)
            ceiling = _creditLimitCents;
        if (ceiling == 0 || won == 0)
            return false;

        long held = creditsAfterWager * AccountingCents;
        return held > ceiling || won * AccountingCents > ceiling - held;
    }

    /// <summary>The restricted part of the credit meter, in cents, never more than the meter holds.</summary>
    private long CurrentRestrictedCents()
    {
        lock (_gate)
            return Math.Min(_restrictedCents, Meters.Read(SasMeterCode.CurrentCredits) * AccountingCents);
    }

    /// <summary>The pool the restricted balance belongs to, zero while there is none.</summary>
    private ushort RestrictedPool()
    {
        lock (_gate)
            return CurrentRestrictedCents() == 0 ? (ushort)0 : _restrictedPool;
    }

    /// <summary>The restricted balance's expiration as its eight BCD digits read, zero while there is none.</summary>
    private long RestrictedExpirationValue()
    {
        lock (_gate)
            return CurrentRestrictedCents() == 0 ? 0 : BcdValue(_restrictedExpiration);
    }

    /// <summary>Puts restricted cents on the meter, keeping the pool and the combined expiration (libsas_internal.cpp:3275).</summary>
    /// <returns>The expiration the balance now carries, which the completed transfer reports.</returns>
    private byte[] AddRestricted(long cents, ushort pool, byte[] expiration)
    {
        lock (_gate)
        {
            long held = CurrentRestrictedCents();
            _restrictedExpiration = held == 0 ? (byte[])expiration.Clone() : CombineExpirations(_restrictedExpiration, expiration);
            _restrictedPool = pool;
            _restrictedCents = held + cents;
            Meters.SetCurrentCredits(Meters.Read(SasMeterCode.CurrentCredits) + (cents / AccountingCents));
            PublishRestricted();
            return (byte[])_restrictedExpiration.Clone();
        }
    }

    /// <summary>Takes restricted cents off the meter, the credit meter with them.</summary>
    private void TakeRestricted(long cents)
    {
        lock (_gate)
        {
            _restrictedCents = CurrentRestrictedCents() - cents;
            Meters.SetCurrentCredits(Meters.Read(SasMeterCode.CurrentCredits) - (cents / AccountingCents));
            PublishRestricted();
        }
    }

    /// <summary>Takes a wager from the restricted part first, and returns how many credits of it that was.</summary>
    /// <remarks>Called before the credit meter goes down by the wager.</remarks>
    private long PlayRestricted(long credits)
    {
        lock (_gate)
        {
            long held = CurrentRestrictedCents();
            long played = Math.Min(credits, held / AccountingCents);
            _restrictedCents = held - (played * AccountingCents);
            return played;
        }
    }

    /// <summary>
    /// Keeps meter 001B with the restricted balance, and gives an empty balance's pool and expiration back to the
    /// default, as sasSetCurrentAmounts does (libsas_internal.cpp:5546).
    /// </summary>
    private void PublishRestricted()
    {
        lock (_gate)
        {
            _restrictedCents = Math.Min(_restrictedCents, Meters.Read(SasMeterCode.CurrentCredits) * AccountingCents);
            if (_restrictedCents == 0)
            {
                _restrictedPool = 0;
                _restrictedExpiration = new byte[4];
            }

            Meters.SetCurrentRestrictedCredits(_restrictedCents / AccountingCents);
        }
    }

    /// <summary>
    /// Two expirations as calculateExpirationDate combines them, branch for branch (libsas_utils.cpp:614): the later
    /// of two dates, the count of days over a date, whichever is set when the other is zero; its days-and-days branch
    /// tests the day byte, so two counts of days keep the one held.
    /// </summary>
    internal static byte[] CombineExpirations(byte[] held, byte[] incoming)
    {
        (long month1, long day1, long year1) = Parts(held);
        (long month2, long day2, long year2) = Parts(incoming);

        if (month1 != 0 && day1 != 0 && month2 != 0 && day2 != 0)
            return CompareDates(held, incoming) >= 0 ? (byte[])held.Clone() : (byte[])incoming.Clone();
        if (month1 == 0 && day1 == 0 && month2 == 0 && day2 != 0)
            return Days(Math.Max(year1, year2));
        if (day1 == 0 && month1 == 0 && year1 == 0)
            return (byte[])incoming.Clone();
        if (day2 == 0 && month2 == 0 && year2 == 0)
            return (byte[])held.Clone();
        return Days(month1 != 0 ? year2 : year1);

        static (long Month, long Day, long Year) Parts(byte[] date) =>
            (BcdValue(date.AsSpan(0, 1)), BcdValue(date.AsSpan(1, 1)), BcdValue(date.AsSpan(2, 2)));

        static int CompareDates(byte[] a, byte[] b)
        {
            (long ma, long da, long ya) = Parts(a);
            (long mb, long db, long yb) = Parts(b);
            return (ya, ma, da).CompareTo((yb, mb, db));
        }

        static byte[] Days(long count) => [0x00, 0x00, .. BcdBytes(count, 2)];
    }
}
