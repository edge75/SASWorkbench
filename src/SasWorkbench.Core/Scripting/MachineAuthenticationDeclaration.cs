namespace SasWorkbench.Core.Scripting;

/// <summary>
/// That a case assumes the machine takes part in component authentication, written
/// <c>machine component authentication on</c>.
/// </summary>
/// <remarks>
/// A declaration in the sense of <see cref="MachineDeclaration"/>: taking part is set when the platform starts
/// the library, and no poll can change it. The software machine is built taking part, with two components;
/// any other machine is asked off bit 4 of its second enabled-features byte and the case skips when it is clear.
/// </remarks>
/// <param name="Location">Where it was declared.</param>
public sealed record MachineAuthenticationDeclaration(SourceLocation Location)
{
    /// <summary>The capability the machine answers with, off its enabled-features reply.</summary>
    public const string Capability = "machine.component_authentication";

    /// <summary>The declaration as the requirement the machine itself answers.</summary>
    public Requirement AsRequirement() => new(Location, Capability);

    public override string ToString() => "component authentication on";
}
