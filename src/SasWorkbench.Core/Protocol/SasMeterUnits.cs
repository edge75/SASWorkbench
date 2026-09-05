using System.Collections.Frozen;

namespace SasWorkbench.Core.Protocol;

/// <summary>What a meter's number is counted in.</summary>
public enum SasMeterUnit
{
    /// <summary>This build cannot tell, and will say so rather than assume.</summary>
    Unknown,

    /// <summary>Accounting units, so the value scales with the accounting denomination.</summary>
    Accounting,

    /// <summary>Cents, fixed by the specification whatever the accounting denomination is.</summary>
    Cents,

    /// <summary>A plain tally, unaffected by any denomination.</summary>
    Count,
}

/// <summary>
/// Which unit each meter is counted in, which decides whether the accounting denomination applies
/// to it.
/// </summary>
/// <remarks>
/// <para>
/// Taken from the names in the library's own meter table rather than written out again here. Table
/// C-7 names every meter with its unit in brackets — "(credits)", "(cents)", "(quantity)" — and
/// the specification's rule is stated in exactly those terms: values in credits are reported in
/// the accounting denomination, values defined to be in another unit are not. Deriving the map
/// from the names means it cannot drift from the library the way a second copy would.
/// </para>
/// <para>
/// A meter whose name carries no unit is <see cref="SasMeterUnit.Unknown"/>, not a guess. Getting
/// this wrong in either direction misreports money: a value wrongly divided under-reports it, and
/// a value wrongly left alone over-reports it.
/// </para>
/// </remarks>
public static class SasMeterUnits
{
    private static readonly FrozenDictionary<ushort, SasMeterUnit> Map = Build();

    /// <summary>What this meter is counted in.</summary>
    public static SasMeterUnit UnitOf(ushort code) =>
        Map.TryGetValue(code, out SasMeterUnit unit) ? unit : SasMeterUnit.Unknown;

    /// <summary>What this meter is counted in.</summary>
    public static SasMeterUnit UnitOf(SasMeterCode code) => UnitOf((ushort)code);

    /// <summary>Whether the accounting denomination applies to this meter.</summary>
    public static bool ScalesWithDenomination(ushort code) => UnitOf(code) == SasMeterUnit.Accounting;

    /// <summary>How many meters fall into each unit, so a report can say what is known.</summary>
    public static IReadOnlyDictionary<SasMeterUnit, int> Counts { get; } =
        Map.GroupBy(pair => pair.Value).ToDictionary(g => g.Key, g => g.Count());

    private static FrozenDictionary<ushort, SasMeterUnit> Build()
    {
        var map = new Dictionary<ushort, SasMeterUnit>();

        foreach ((ushort code, SasMeterInfo info) in SasCatalogue.Meters)
        {
            map[code] = Classify(info.Description);
        }

        return map.ToFrozenDictionary();
    }

    /// <summary>
    /// Reads the unit out of a meter's name. The bracketed marker is Table C-7's own notation and
    /// is taken first, wherever in the name it appears - several names carry a cross-reference
    /// after it. Where there is no marker, a name that still says "credits" is a credits meter;
    /// anything else is left unknown rather than guessed at, and an unknown meter is never scaled.
    /// </summary>
    private static SasMeterUnit Classify(string description)
    {
        if (description.Contains("(cents)", StringComparison.OrdinalIgnoreCase))
            return SasMeterUnit.Cents;

        if (description.Contains("(quantity)", StringComparison.OrdinalIgnoreCase))
            return SasMeterUnit.Count;

        if (description.Contains("(credits)", StringComparison.OrdinalIgnoreCase)
            || description.Contains("credits", StringComparison.OrdinalIgnoreCase))
        {
            return SasMeterUnit.Accounting;
        }

        return SasMeterUnit.Unknown;
    }
}
