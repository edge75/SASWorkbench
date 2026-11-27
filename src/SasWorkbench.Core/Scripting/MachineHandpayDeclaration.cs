namespace SasWorkbench.Core.Scripting;

/// <summary>
/// How a case assumes the machine reports, validates and prints receipts for hand pays, written
/// <c>machine handpay legacy</c>, <c>machine handpay validation on</c> and <c>machine handpay receipts on</c>.
/// </summary>
/// <remarks>
/// A declaration in the sense of <see cref="MachineDeclaration"/>: the reporting mode is chosen at a memory reset
/// and hand pay validation is the machine's to offer, and no poll reports either. The software machine is built
/// that way and claims the capability; any other machine cannot be asked, so the case skips there.
/// </remarks>
/// <param name="Location">Where it was first declared.</param>
/// <param name="Legacy">Legacy hand pay reporting (SAS 6.02 7.8.2) rather than the hand pay queue.</param>
/// <param name="Validation">The machine validates hand pays when the host asks.</param>
/// <param name="Receipts">The machine prints a hand pay receipt when the host asks (D61).</param>
public sealed record MachineHandpayDeclaration(SourceLocation Location, bool Legacy, bool Validation, bool Receipts = false)
{
    /// <summary>The capability a machine printing hand pay receipts carries.</summary>
    public const string ReceiptsCapability = "machine.handpay_receipts";

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
        if (Receipts)
            yield return new Requirement(Location, ReceiptsCapability);
    }

    public override string ToString() =>
        string.Join(", ", new[]
        {
            Legacy ? "handpay legacy" : null,
            Validation ? "handpay validation on" : null,
            Receipts ? "handpay receipts on" : null,
        }.OfType<string>());
}
