using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Scripting;

/// <summary>
/// The validation style a case assumes the machine performs, written <c>machine validation &lt;style&gt;</c>.
/// </summary>
/// <remarks>
/// A declaration in the sense of <see cref="MachineDeclaration"/>: the software machine is built
/// performing it, and any other machine is asked over the link and the case skips when it answers
/// another style. The style is set at the machine and no poll can change it, so this is the only
/// honest way for a case to choose one.
/// </remarks>
/// <param name="Location">Where it was declared.</param>
/// <param name="Style">The style the machine is assumed to perform.</param>
public sealed record MachineValidationDeclaration(SourceLocation Location, SasCashoutValidation Style)
{
    /// <summary>The capability the machine answers with, as <c>egm.validation</c> is asked today.</summary>
    public const string Capability = "egm.validation";

    /// <summary>The style in the words a requirement and a report use.</summary>
    public string Assumed => SasNames.Underscore(Style.ToString());

    /// <summary>The declaration as the requirement the machine itself answers.</summary>
    public Requirement AsRequirement() => new(Location, Capability, Assumed);

    /// <summary>Reads a style as a script writes it; there is no fourth style on the wire, so 'none' is not one.</summary>
    public static bool TryParse(string word, out SasCashoutValidation style)
    {
        style = word.ToLowerInvariant() switch
        {
            "standard" => SasCashoutValidation.Standard,
            "secure_enhanced" => SasCashoutValidation.SecureEnhanced,
            "system" => SasCashoutValidation.System,
            _ => SasCashoutValidation.Reserved,
        };
        return style != SasCashoutValidation.Reserved;
    }

    public override string ToString() => $"validation {Assumed}";
}
