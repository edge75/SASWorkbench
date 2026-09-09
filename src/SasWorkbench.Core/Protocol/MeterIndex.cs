using System.Collections.Frozen;

namespace SasWorkbench.Core.Protocol;

/// <summary>Which long poll to send to read a meter, and which field of the reply holds it.</summary>
/// <param name="Name">The name a script uses.</param>
/// <param name="Command">The long poll that returns it.</param>
/// <param name="Field">The field of that reply.</param>
/// <param name="Unit">What the number means.</param>
/// <param name="Meter">
/// Which meter of the library's own catalogue this is, or null for the one name whose number the
/// machine computes from twenty-four meters rather than reading one.
/// </param>
/// <param name="ScaledByAccountingDenomination">
/// True when the library answers this command through its shared single-meter helper and the meter
/// is one that helper divides — so the number depends on the machine's accounting denomination and
/// on which of its five meter modes it is in, and another command reporting the same meter may
/// answer something else.
/// </param>
public sealed record MeterSource(
    string Name,
    byte Command,
    string Field,
    SasUnit Unit,
    SasMeterCode? Meter = null,
    bool ScaledByAccountingDenomination = false)
{
    public override string ToString() => $"{Name} <- lp ${Command:X2}.{Field}";
}

/// <summary>
/// Works out how to read each meter a script can name, from the layouts themselves.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is a hand-maintained table of "meter X comes from long poll Y". The index is
/// built from the fields each layout marks as meters, so adding a layout makes its meters
/// readable and removing one makes them honestly unreadable.
/// </para>
/// <para>
/// What it has to decide is which command to read a name from when more than one reports the same
/// meter, and that is not a matter of taste. The gaming machine library reads a meter two ways.
/// Its shared single-meter helper divides the value by the accounting denomination, for any meter
/// it does not call multigame, under two of the machine's five meter modes; every other handler
/// writes the meter as it stands. Both readings are the library's, and neither is a fault — but
/// they are different numbers, so a name has to be attached to one of them deliberately.
/// </para>
/// <para>
/// The rule, and it comes from the library's own account of why that division exists — a comment
/// saying meters expressed in credit should be divided by the denomination:
/// </para>
/// <list type="bullet">
/// <item>
/// A meter counted in <b>credits</b> is read from the command that scales it, because that
/// division is the conversion from what the machine stores to the unit the reply is supposed to
/// be in.
/// </item>
/// <item>
/// A meter counted in <b>anything else</b> — a number of notes, a number of games, a number of
/// door openings — is read from the command that does not, because dividing a count by a currency
/// unit converts nothing. The library applies that division to those meters too, which is where
/// the two commands part company.
/// </item>
/// <item>
/// Where nothing separates two commands, the reply carrying fewer other fields wins: one number is
/// cheaper to explain when it is wrong.
/// </item>
/// </list>
/// <para>
/// Applied to the sixteen names this index offered before it knew any of this, the rule chooses
/// exactly the sixteen commands they were already read from — which is checked by a test, because
/// a rule that quietly moved an existing name would change what a passing test measures.
/// </para>
/// <para>
/// A name with only one command behind it takes that one whatever it does, and says so:
/// <see cref="MeterSource.ScaledByAccountingDenomination"/> is set, and
/// <c>sasctl catalogue readable-meters</c> prints it.
/// </para>
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

    private static readonly FrozenDictionary<string, IReadOnlyList<MeterSource>> Candidates = Build();

    private static readonly FrozenDictionary<string, MeterSource> Sources =
        Candidates.ToFrozenDictionary(entry => entry.Key, entry => entry.Value[0],
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Every meter a script can read, and where it comes from.</summary>
    public static IReadOnlyCollection<MeterSource> All => Sources.Values;

    /// <summary>Finds how to read a meter by the name a script used.</summary>
    public static bool TryResolve(string? name, out MeterSource source)
    {
        source = null!;
        return Sources.TryGetValue(Resolve(name) ?? string.Empty, out source!);
    }

    /// <summary>
    /// Every command that reports one name's meter, the chosen one first.
    /// </summary>
    /// <remarks>
    /// The rest are not noise. Where a name has more than one command behind it, a reading taken
    /// from one and a reading taken from another are two answers to the same question, and a test
    /// that compares them is the only thing that finds a field at the wrong offset in a reply full
    /// of zeroes.
    /// </remarks>
    public static IReadOnlyList<MeterSource> SourcesFor(string? name) =>
        Candidates.TryGetValue(Resolve(name) ?? string.Empty, out IReadOnlyList<MeterSource>? all)
            ? all
            : [];

    /// <summary>Every name a script may use, sorted, for an error message that lists the options.</summary>
    public static IReadOnlyList<string> KnownNames() =>
        Sources.Keys.Concat(Aliases.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// True when the library answers this command's meter through the helper that divides it.
    /// </summary>
    /// <remarks>
    /// Both halves come from the generated catalogue, which reads them out of the library: which
    /// commands go through that helper, and which meters it leaves alone.
    /// </remarks>
    public static bool IsScaled(byte command, SasMeterCode? meter) =>
        meter is not null
        && SasCatalogue.MeterReads.TryGetValue(command, out SasMeterReadInfo? reads)
        && reads.ThroughSharedHandler
        && !SasCatalogue.MultigameMeters.Contains((ushort)meter.Value);

    private static string? Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        string key = name.Trim();
        return Aliases.TryGetValue(key, out string? aliased) ? aliased : key;
    }

    private static FrozenDictionary<string, IReadOnlyList<MeterSource>> Build()
    {
        var byName = new Dictionary<string, List<MeterSource>>(StringComparer.OrdinalIgnoreCase);

        foreach (SasMessageLayout layout in SasLayouts.All.Values)
        {
            // A reply that has to be asked for in a particular shape is not a meter reading: the
            // runner sends the command with nothing in it, and a per-game reply answered for no
            // particular game means something different from one answered for a game.
            if (layout.Shape != SasResponseShape.Data || layout.Request.Count > 0)
                continue;

            foreach (SasField part in layout.Response)
            {
                if (!part.IsMeter || part.IsGroup)
                    continue;

                if (!byName.TryGetValue(part.Name, out List<MeterSource>? sources))
                    byName[part.Name] = sources = [];

                sources.Add(new MeterSource(
                    part.Name, layout.Command, part.Name, part.Unit, part.Meter,
                    IsScaled(layout.Command, part.Meter)));
            }
        }

        return byName.ToFrozenDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<MeterSource>)[.. entry.Value.OrderBy(Rank)],
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Lower sorts first: the command a name is read from.</summary>
    private static (int Wanted, int Fields, byte Command) Rank(MeterSource source)
    {
        bool wantsScaling = source.Unit is SasUnit.Credits or SasUnit.Cents;
        int fields = SasLayouts.All[source.Command].Response.Count;
        return (source.ScaledByAccountingDenomination == wantsScaling ? 0 : 1, fields, source.Command);
    }
}
