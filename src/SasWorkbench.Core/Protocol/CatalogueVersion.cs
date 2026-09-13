namespace SasWorkbench.Core.Protocol;

/// <summary>
/// Which catalogue a run was read against, in one line.
/// </summary>
/// <remarks>
/// Counted rather than versioned. The catalogue is generated from the gaming machine library's own
/// headers and carries no version of its own, so a number written by hand here would be a claim
/// nobody maintains. What the counts do say is the thing that matters when reading an old record:
/// whether this build knows the same protocol the run was recorded against, and if not, in which
/// direction it differs.
/// </remarks>
public static class CatalogueVersion
{
    /// <summary>The catalogue this build carries, as a line for a record's header.</summary>
    public static string Current { get; } =
        $"{SasCatalogue.LongPolls.Count} long polls, "
        + $"{SasCatalogue.Exceptions.Count} exceptions, "
        + $"{SasCatalogue.Meters.Count} meters";
}
