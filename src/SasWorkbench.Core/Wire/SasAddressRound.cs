namespace SasWorkbench.Core.Wire;

/// <summary>
/// Reads a round of machines written the short way: <c>01-64</c>, <c>01,05,09</c>, or both.
/// </summary>
/// <remarks>
/// <para>
/// A hundred machines cannot be named on a hundred lines and still be a script somebody reads, so
/// a round is written as ranges and lists. The same reader serves the command line and the script
/// language, because two readers of the same notation drift and the first sign of it is a case
/// that polls a different floor than the one its author wrote down.
/// </para>
/// <para>
/// Each address is read by <see cref="SasAddress.TryParse"/>, so the convention is the one used
/// everywhere else in this product and in every capture: a bare two-digit number is hexadecimal,
/// so <c>01-64</c> is machines 1 to 100 and <c>1-100</c> is the same round written in decimal.
/// </para>
/// <para>
/// Duplicates are refused rather than collapsed. A round that names one machine twice polls it
/// twice and costs more than its author thinks, and silently removing the second is a decision
/// about the arrangement made on the author's behalf.
/// </para>
/// </remarks>
public static class SasAddressRound
{
    /// <summary>The most machines a round may hold, which is every address SAS defines.</summary>
    public const int MaxMachines = SasAddress.MaxValue;

    /// <summary>
    /// Reads a round, or says why what was written is not one.
    /// </summary>
    /// <param name="text">The round, such as <c>01-08</c> or <c>01,05,20-24</c>.</param>
    /// <param name="round">The machines, in the order the round visits them.</param>
    /// <param name="error">Why it could not be read, when it could not.</param>
    public static bool TryParse(string? text, out IReadOnlyList<SasAddress> round, out string? error)
    {
        round = [];
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "a round names no machines. Expected something like 01-08 or 01,05,09.";
            return false;
        }

        var machines = new List<SasAddress>();
        var seen = new HashSet<byte>();

        foreach (string part in text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            string piece = part.Trim();
            int dash = piece.IndexOf('-', StringComparison.Ordinal);

            if (dash < 0)
            {
                if (!TryOne(piece, out SasAddress one, out error))
                    return false;
                if (!Add(machines, seen, one, out error))
                    return false;
                continue;
            }

            if (!TryOne(piece[..dash], out SasAddress from, out error)
                || !TryOne(piece[(dash + 1)..], out SasAddress to, out error))
            {
                return false;
            }

            if (to.Value < from.Value)
            {
                error = $"'{piece}' runs backwards. A round is written low to high, so a host "
                        + "polls the machines in the order the range names them.";
                return false;
            }

            for (int value = from.Value; value <= to.Value; value++)
            {
                if (!Add(machines, seen, SasAddress.Of((byte)value), out error))
                    return false;
            }
        }

        if (machines.Count == 0)
        {
            error = "a round names no machines. Expected something like 01-08 or 01,05,09.";
            return false;
        }

        round = machines;
        return true;
    }

    private static bool TryOne(string text, out SasAddress address, out string? error)
    {
        error = null;
        if (!SasAddress.TryParse(text, out address))
        {
            error = $"'{text.Trim()}' is not a poll address. Expected 01 to 7F.";
            return false;
        }

        if (address.IsSync)
        {
            // Address zero is the synchronisation address. A round containing it would poll a
            // machine that cannot exist, and the silence would read as a machine that went away.
            error = "address 00 is the synchronisation address, which no machine answers to, so "
                    + "it cannot be a machine in a round.";
            return false;
        }

        return true;
    }

    private static bool Add(
        List<SasAddress> machines, HashSet<byte> seen, SasAddress address, out string? error)
    {
        error = null;
        if (!seen.Add(address.Value))
        {
            error = $"${address} is named twice. A round that polls one machine twice costs more "
                    + "than it reads, so the repeat is refused rather than removed.";
            return false;
        }

        machines.Add(address);
        return true;
    }

    /// <summary>Writes a round the short way, so what is printed can be pasted back in.</summary>
    public static string Describe(IReadOnlyList<SasAddress> round)
    {
        ArgumentNullException.ThrowIfNull(round);
        if (round.Count == 0)
            return string.Empty;

        var parts = new List<string>();
        int start = 0;

        for (int i = 1; i <= round.Count; i++)
        {
            bool consecutive = i < round.Count && round[i].Value == round[i - 1].Value + 1;
            if (consecutive)
                continue;

            parts.Add(i - start == 1
                ? $"{round[start]}"
                : $"{round[start]}-{round[i - 1]}");
            start = i;
        }

        return string.Join(",", parts);
    }
}
