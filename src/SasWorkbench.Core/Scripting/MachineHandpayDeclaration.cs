namespace SasWorkbench.Core.Scripting;

/// <summary>
/// How a case assumes the machine reports and validates hand pays, written <c>machine handpay legacy</c> and
/// <c>machine handpay validation on</c>.
/// </summary>
/// <remarks>
/// A declaration in the sense of <see cref="MachineDeclaration"/>: the reporting mode is chosen at a memory reset
/// and hand pay validation is the machine's to offer, and no poll reports either. The software machine is built
/// that way and claims the capability; any other machine cannot be asked, so the case skips there.
/// </remarks>
/// <param name="Location">Where it was first declared.</param>
/// <param name="Legacy">Legacy hand pay reporting (SAS 6.02 7.8.2) rather than the hand pay queue.</param>
/// <param name="Validation">The machine validates hand pays when the host asks.</param>
public sealed record MachineHandpayDeclaration(SourceLocation Location, bool Legacy, bool Validation)
{
    /// <summary>The capability a machine reporting hand pays the legacy way carries.</summary>
    public const string LegacyCapability = "machine.legacy_handpay";

    /// <summary>The capability a machine holding a hand pay queue carries.</summary>
    public const string QueueCapability = "machine.handpay_queue";

    /// <summary>The capability carrying how many hand pays the queue holds.</summary>
    public const string QueueSizeCapability = "machine.handpay_queue_size";

    /// <summary>The capability a machine validating hand pays carries.</summary>
    public const string ValidationCapability = "machine.handpay_validation";

    /// <summary>The declaration as the requirements only a machine built this way answers.</summary>
    public IEnumerable<Requirement> AsRequirements()
    {
        if (Legacy)
            yield return new Requirement(Location, LegacyCapability);
        if (Validation)
            yield return new Requirement(Location, ValidationCapability);
    }

    public override string ToString() =>
        string.Join(", ", new[] { Legacy ? "handpay legacy" : null, Validation ? "handpay validation on" : null }.OfType<string>());
}
