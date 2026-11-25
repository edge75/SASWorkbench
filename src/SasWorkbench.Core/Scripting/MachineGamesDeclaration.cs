namespace SasWorkbench.Core.Scripting;

/// <summary>
/// The games a case assumes the machine holds, each at its player denomination, written
/// <c>machine games 1c 2c 5c 10c</c>.
/// </summary>
/// <remarks>
/// A declaration in the sense of <see cref="MachineDeclaration"/>: which games a machine holds and at what
/// denominations is set in its configuration, and no poll changes it. The software machine is built with these games,
/// numbered from one in the order written; any other machine is asked how many games ($51) and player denominations
/// ($B2) it has, and the case skips when there is only one.
/// </remarks>
/// <param name="Location">Where it was declared.</param>
/// <param name="DenominationsInCents">Each game's player denomination, game one first.</param>
public sealed record MachineGamesDeclaration(SourceLocation Location, IReadOnlyList<int> DenominationsInCents)
{
    /// <summary>The declaration as the requirements the machine itself answers.</summary>
    public IEnumerable<Requirement> AsRequirements()
    {
        if (DenominationsInCents.Count > 1)
            yield return new Requirement(Location, CapabilityProbe.MultipleGamesCapability);
        if (DenominationsInCents.Distinct().Count() > 1)
            yield return new Requirement(Location, CapabilityProbe.MultipleDenominationsCapability);
    }

    public override string ToString() =>
        "games " + string.Join(' ', DenominationsInCents.Select(MachineDeclaration.Describe));
}
