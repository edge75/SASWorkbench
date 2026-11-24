namespace SasWorkbench.Core.Scripting;

/// <summary>
/// That a case assumes the machine multiplies jackpots, written <c>machine multiplied jackpot on</c>.
/// </summary>
/// <remarks>
/// A declaration in the sense of <see cref="MachineDeclaration"/>: the jackpot multiplier is set when the platform
/// starts the library, and no poll can change it. The software machine is built multiplying jackpots; any other
/// machine is asked off bit 0 of its first enabled-features byte and the case skips when it is clear.
/// </remarks>
/// <param name="Location">Where it was declared.</param>
public sealed record MachineMultipliedJackpotDeclaration(SourceLocation Location)
{
    /// <summary>The capability the machine answers with, off its enabled-features reply.</summary>
    public const string Capability = CapabilityProbe.MultipliedJackpotCapability;

    /// <summary>The declaration as the requirement the machine itself answers.</summary>
    public Requirement AsRequirement() => new(Location, Capability);

    public override string ToString() => "multiplied jackpot on";
}
