namespace SasWorkbench.Core.Scripting;

/// <summary>Turns legacy bonusing on or off at the machine's own menu.</summary>
/// <remarks>A stimulus for the reason the AFT bonusing switch is one: only a machine this build made, or a person, can take it from a script.</remarks>
public sealed record SetLegacyBonusStep(SourceLocation Location, bool On) : StimulusStep(Location)
{
    public override string RequiredCapability => "stimulus.legacy_bonus";

    public override string Describe() => $"set legacy bonus {(On ? "on" : "off")}";
}
