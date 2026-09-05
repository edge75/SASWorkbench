namespace SasWorkbench.Core.Protocol;

/// <summary>
/// The accounting denomination: the unit every credit value reported to a host is counted in.
/// </summary>
/// <remarks>
/// <para>
/// SAS 6.02 section 16 makes this a property of the machine, not of the game a player happens to
/// have chosen, and requires that every denomination available to the player divide evenly by it.
/// Values specifically defined to be in another unit — cents, tokens, or whole game credits — are
/// unaffected, which is why a value wrongly converted here under-reports money to the host and a
/// value wrongly left alone over-reports it.
/// </para>
/// <para>
/// The table is the library's own, read from its Table C-4 array, and is held in hundredths of a
/// cent exactly as the library holds it. Codes 1B to 1F are sub-cent denominations the table
/// leaves at zero: they cannot be used as an accounting denomination, and a build that accepted
/// one would divide by zero rather than refuse.
/// </para>
/// </remarks>
public static class SasDenomination
{
    /// <summary>The lowest code, so a caller can walk the table.</summary>
    public const byte FirstCode = 0x00;

    /// <summary>The highest code the protocol defines.</summary>
    public const byte LastCode = 0x1F;

    // Hundredths of a cent per unit, indexed by Table C-4 code. Derived from the library's own
    // denomInMinUnit array, whose values are the table multiplied by one hundred.
    private static readonly int[] HundredthsOfACent =
    [
        /* 00 */ 100, /* 01 */ 100, /* 02 */ 500, /* 03 */ 1000, /* 04 */ 2500,
        /* 05 */ 5000, /* 06 */ 10000, /* 07 */ 50000, /* 08 */ 100000, /* 09 */ 200000,
        /* 0A */ 1000000, /* 0B */ 2000, /* 0C */ 20000, /* 0D */ 25000, /* 0E */ 250000,
        /* 0F */ 500000, /* 10 */ 2000000, /* 11 */ 2500000, /* 12 */ 5000000, /* 13 */ 10000000,
        /* 14 */ 20000000, /* 15 */ 25000000, /* 16 */ 50000000, /* 17 */ 200, /* 18 */ 300,
        /* 19 */ 1500, /* 1A */ 4000, /* 1B */ 0, /* 1C */ 0, /* 1D */ 0,
        /* 1E */ 0, /* 1F */ 0,
    ];

    /// <summary>
    /// How many cents one unit of this code is worth, or null when the code cannot be used as an
    /// accounting denomination.
    /// </summary>
    public static int? CentsOf(byte code)
    {
        if (code > LastCode)
            return null;

        int hundredths = HundredthsOfACent[code];

        // Sub-cent denominations exist in the table but not as accounting denominations: a unit
        // smaller than a cent cannot divide a meter held in cents.
        return hundredths == 0 || hundredths % 100 != 0 ? null : hundredths / 100;
    }

    /// <summary>The code for a whole number of cents, or null when the protocol cannot express it.</summary>
    public static byte? CodeFor(int cents)
    {
        if (cents <= 0)
            return null;

        for (byte code = 0x01; code <= LastCode; code++)
        {
            if (CentsOf(code) == cents)
                return code;
        }

        return null;
    }

    /// <summary>
    /// Whether every denomination a player may choose divides evenly by this one, as section 16
    /// requires. A value that does not is refused rather than used, because using it would report
    /// a meter in a unit no player denomination is a whole number of.
    /// </summary>
    public static bool DividesEvenly(int accountingCents, IEnumerable<int> playerDenominationsInCents)
    {
        ArgumentNullException.ThrowIfNull(playerDenominationsInCents);

        if (accountingCents <= 0)
            return false;

        foreach (int player in playerDenominationsInCents)
        {
            if (player <= 0 || player % accountingCents != 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Converts an amount held in cents into accounting units, truncating what is left over.
    /// </summary>
    /// <remarks>
    /// Truncation, never rounding: rounding up would report money the machine does not hold. A
    /// remainder is normal rather than a fault — a ticket, a note or a transfer carries any number
    /// of cents, and section 16's divisibility rule constrains only the denominations a player may
    /// choose.
    /// </remarks>
    public static long ToAccountingUnits(long cents, int accountingCents)
    {
        if (accountingCents <= 0)
            throw new ArgumentOutOfRangeException(nameof(accountingCents), accountingCents,
                "An accounting denomination is at least one cent.");

        return cents / accountingCents;
    }

    /// <summary>What is lost to truncation when an amount is converted, in cents.</summary>
    public static long RemainderOf(long cents, int accountingCents)
    {
        if (accountingCents <= 0)
            throw new ArgumentOutOfRangeException(nameof(accountingCents), accountingCents,
                "An accounting denomination is at least one cent.");

        return cents % accountingCents;
    }
}
