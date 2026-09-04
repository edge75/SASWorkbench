using System.Collections.Frozen;

namespace SasWorkbench.Core.Protocol;

/// <summary>Which long poll to send to read a meter, and which field of the reply holds it.</summary>
/// <param name="Name">The name a script uses.</param>
/// <param name="Command">The long poll that returns it.</param>
/// <param name="Field">The field of that reply.</param>
/// <param name="Unit">What the number means.</param>
public sealed record MeterSource(string Name, byte Command, string Field, SasUnit Unit)
{
    public override string ToString() => $"{Name} <- lp ${Command:X2}.{Field}";
}

/// <summary>
/// Works out how to read each meter a script can name, from the layouts themselves.
/// </summary>
/// <remarks>
/// Nothing here is a hand-maintained table of "meter X comes from long poll Y". The index is
/// built by looking at what every known layout returns, so adding a layout makes its meters
/// readable and removing one makes them honestly unreadable. A dedicated single-meter command is
/// preferred over a bulk one, because a reply carrying one number is cheaper to explain when it
/// is wrong.
/// </remarks>
public static class MeterIndex
{
    /// <summary>
    /// Names that read better in a test than the field names do. Each maps onto a field that
    /// already exists, so no alias can invent a meter.
    /// </summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["total_bill_in"] = "total_bill_value",
        ["coin_in"] = "total_coin_in",
        ["coin_out"] = "total_coin_out",
        ["credits"] = "current_credits",
        ["jackpot"] = "total_jackpot",
    };

    private static readonly FrozenDictionary<string, MeterSource> Sources = Build();

    /// <summary>Every meter a script can read, and where it comes from.</summary>
    public static IReadOnlyCollection<MeterSource> All => Sources.Values;

    /// <summary>Finds how to read a meter by the name a script used.</summary>
    public static bool TryResolve(string? name, out MeterSource source)
    {
        source = null!;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string key = name.Trim();
        if (Aliases.TryGetValue(key, out string? aliased))
            key = aliased;

        return Sources.TryGetValue(key, out source!);
    }

    /// <summary>Every name a script may use, sorted, for an error message that lists the options.</summary>
    public static IReadOnlyList<string> KnownNames() =>
        Sources.Keys.Concat(Aliases.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    private static FrozenDictionary<string, MeterSource> Build()
    {
        var byName = new Dictionary<string, MeterSource>(StringComparer.OrdinalIgnoreCase);

        // Two passes, so a dedicated single-field command always wins over a bulk one.
        foreach (bool dedicatedOnly in (bool[])[true, false])
        {
            foreach (SasMessageLayout layout in SasLayouts.All.Values)
            {
                if (layout.Shape != SasResponseShape.Data)
                    continue;

                bool dedicated = layout.Response.Count == 1;
                if (dedicated != dedicatedOnly)
                    continue;

                foreach (SasField part in layout.Response)
                {
                    if (part.IsGroup || part.Unit is SasUnit.None or SasUnit.Code)
                        continue;

                    byName.TryAdd(part.Name, new MeterSource(
                        part.Name, layout.Command, part.Name, part.Unit));
                }
            }
        }

        return byName.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
