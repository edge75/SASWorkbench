using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Scripting;

/// <summary>
/// The configuration a case assumes the machine is already in.
/// </summary>
/// <remarks>
/// <para>
/// A declaration is not a requirement and not a stimulus. Section 16 makes the accounting
/// denomination a property of the machine that a memory clear establishes, so a case written
/// against ten centavos is meaningless on a machine in force at one cent — but it is meaningless
/// for an environmental reason, not because the build is wrong, and the honest report is a skip
/// naming what the machine is actually in force at.
/// </para>
/// <para>
/// Against the software machine the declaration builds a peer in that configuration, which is why
/// it has to be read before the link opens rather than checked after it. Against any other machine
/// it configures nothing at all: the value is read back off the wire and compared, so a case can
/// never quietly reconfigure a machine somebody else is looking at.
/// </para>
/// </remarks>
/// <param name="Location">Where it was declared.</param>
/// <param name="Code">The Table C-4 code the machine is assumed to publish.</param>
/// <param name="Cents">What one accounting unit is assumed to be worth.</param>
/// <param name="PlayerDenominationsInCents">
/// The denominations the player may choose, which section 16 requires to divide evenly by the
/// accounting denomination. Empty when the case does not say.
/// </param>
public sealed record MachineDeclaration(
    SourceLocation Location,
    byte Code,
    int Cents,
    IReadOnlyList<int> PlayerDenominationsInCents)
{
    /// <summary>
    /// The capability the machine is asked for, and answers with, over the link.
    /// </summary>
    public const string Capability = "egm.accounting_denomination";

    /// <summary>
    /// How a denomination is written in a skip, a report and a script, so all three say the same
    /// words. Always cents, because a Table C-4 code names a unit only to somebody holding the
    /// table.
    /// </summary>
    public static string Describe(int cents) => $"{cents}c";

    /// <summary>What this declaration assumes, in the one form everything else uses.</summary>
    public string Assumed => Describe(Cents);

    /// <summary>
    /// The declaration as a requirement the machine itself answers, so an unmet one is skipped by
    /// the same path and with the same wording as every other capability the machine is asked for.
    /// </summary>
    public Requirement AsRequirement() => new(Location, Capability, Assumed);

    public override string ToString() =>
        PlayerDenominationsInCents.Count == 0
            ? $"accounting denomination {Assumed} (code ${Code:X2})"
            : $"accounting denomination {Assumed} (code ${Code:X2}), player denominations "
              + string.Join(' ', PlayerDenominationsInCents.Select(Describe));

    /// <summary>
    /// Reads a denomination written as a number of cents, and refuses one the protocol cannot
    /// express as an accounting unit rather than letting a script name a unit no machine can be in.
    /// </summary>
    public static bool TryFromCents(int cents, out byte code)
    {
        if (SasDenomination.CodeFor(cents) is { } found)
        {
            code = found;
            return true;
        }

        code = 0;
        return false;
    }
}
