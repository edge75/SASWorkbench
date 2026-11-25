namespace SasWorkbench.Core.Scripting;

/// <summary>
/// Expects the ROM signature a long poll $21 asked for, or its absence, on whichever poll it arrives.
/// </summary>
/// <remarks>
/// The signature answers a later general poll, or takes a long poll's window while reporting is on and an
/// exception is queued, so this waits on everything the host has heard rather than on one reply.
/// </remarks>
public sealed record ExpectRomSignatureStep(SourceLocation Location, TimeSpan Within, bool Negated)
    : ScriptStep(Location)
{
    public override string Describe() => Negated
        ? $"expect no rom signature within {Within.TotalMilliseconds:F0}ms"
        : $"expect rom signature within {Within.TotalMilliseconds:F0}ms";
}

/// <summary>Which of the machine's two menus a person enters or leaves.</summary>
public enum MachineMenu
{
    /// <summary>The audit or display meters screens, reached with the attendant's key ($82, $83).</summary>
    Attendant,

    /// <summary>The self-test or operator screens, reached with the door open ($84, $85).</summary>
    Operator,
}

/// <summary>A person enters or leaves one of the machine's menus.</summary>
public sealed record MenuStep(SourceLocation Location, MachineMenu Menu, bool Enter) : StimulusStep(Location)
{
    /// <summary>The capability a machine whose menus a script can work offers.</summary>
    public const string Capability = "machine.audit_menu";

    public override string RequiredCapability => Capability;

    public override string Describe() =>
        $"menu {Menu.ToString().ToLowerInvariant()} {(Enter ? "enter" : "leave")}";

    public override string? Guidance => (Menu, Enter) switch
    {
        (MachineMenu.Attendant, true) => "Turn the attendant key to open the audit or display meters screens, the ones that do not need the door open.",
        (MachineMenu.Attendant, false) => "Turn the attendant key back to leave the audit screens.",
        (MachineMenu.Operator, true) => "Open the main door and enter the self-test or operator screens.",
        _ => "Leave the self-test or operator screens and close the main door.",
    };
}
