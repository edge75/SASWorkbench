using System.Globalization;

namespace SasWorkbench.Core.Scripting;

/// <summary>
/// The progressive levels a case assumes the machine is configured with, written
/// <c>machine progressive sas group &lt;g&gt; levels &lt;list&gt;</c> and
/// <c>machine progressive non_sas levels &lt;list&gt;</c>.
/// </summary>
/// <remarks>
/// A declaration in the sense of <see cref="MachineDeclaration"/>: the group is set in the machine's
/// own menu and which levels a host feeds is the game's, so no poll can change either. The software
/// machine is built with them; any other machine is asked for its group with long poll $1F and the
/// case skips when it reports another. A non-SAS level reports group zero everywhere, so no poll can
/// tell one is there and that half is never asked.
/// </remarks>
/// <param name="Location">Where it was declared.</param>
/// <param name="Group">The SAS progressive group, zero when the case declares no SAS levels.</param>
/// <param name="SasLevels">The levels a host feeds, bit 0 for level 1.</param>
/// <param name="NonSasLevels">The levels the machine runs itself, bit 0 for level 1.</param>
public sealed record MachineProgressiveDeclaration(
    SourceLocation Location, byte Group, uint SasLevels, uint NonSasLevels)
{
    /// <summary>The capability a machine enrolled in a SAS progressive group answers with, its value the group.</summary>
    public const string SasCapability = "machine.sas_progressives";

    /// <summary>The capability a machine with levels of its own carries; only a machine this build made can claim it.</summary>
    public const string NonSasCapability = "machine.progressives";

    /// <summary>The SAS half as a requirement the machine answers, or null where the case declares none.</summary>
    public Requirement? AsRequirement() =>
        Group == 0 ? null : new Requirement(Location, SasCapability, Group.ToString(CultureInfo.InvariantCulture));

    /// <summary>Reads <c>1-4</c>, <c>1,3</c> or a mix of the two into a level mask, levels one to thirty-two.</summary>
    public static bool TryParseLevels(string text, out uint mask)
    {
        mask = 0;
        foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] range = part.Split('-');
            if (range.Length > 2
                || !int.TryParse(range[0], NumberStyles.None, CultureInfo.InvariantCulture, out int first)
                || !int.TryParse(range[^1], NumberStyles.None, CultureInfo.InvariantCulture, out int last)
                || first < 1 || last > 32 || first > last)
            {
                return false;
            }

            for (int level = first; level <= last; level++)
                mask |= 1u << (level - 1);
        }

        return mask != 0;
    }

    /// <summary>A level mask as a script writes it.</summary>
    public static string DescribeLevels(uint mask) =>
        string.Join(',', Enumerable.Range(1, 32).Where(level => (mask & (1u << (level - 1))) != 0));

    public override string ToString()
    {
        var parts = new List<string>();
        if (SasLevels != 0)
            parts.Add($"progressive sas group {Group} levels {DescribeLevels(SasLevels)}");
        if (NonSasLevels != 0)
            parts.Add($"progressive non_sas levels {DescribeLevels(NonSasLevels)}");
        return string.Join("; ", parts);
    }
}
