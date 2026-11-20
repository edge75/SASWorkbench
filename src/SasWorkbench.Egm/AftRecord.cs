namespace SasWorkbench.Egm;

/// <summary>Where the current funds transfer has got to, as the machine library's state machine names it.</summary>
internal enum AftCycle
{
    None,
    Pending,
    PendingPartial,
    Updated,
    Completed,
}

/// <summary>Whether the current transfer is one of no amount, which the library keeps apart from the history.</summary>
internal enum AftZeroAmount
{
    NonZero,
    Pending,
    Completed,
}

/// <summary>One transaction slot: what the host asked for and what the machine answered, as the library stores both.</summary>
/// <remarks>
/// Mutable on purpose, because it mirrors the library's slot array, which is written field by field as
/// a transfer moves on; every copy handed out of a slot is taken with <see cref="Copy"/>.
/// </remarks>
internal sealed class AftRecord
{
    public byte TransferCode { get; set; }

    public byte TransferType { get; set; }

    public byte RequestFlags { get; set; }

    public byte ResponseFlags { get; set; }

    public long RequestedCashable { get; set; }

    public long RequestedRestricted { get; set; }

    public long RequestedNonrestricted { get; set; }

    public long Cashable { get; set; }

    public long Restricted { get; set; }

    public long Nonrestricted { get; set; }

    public byte[] TransactionId { get; set; } = [];

    /// <summary>The expiration exactly as the host sent it, four bytes of binary coded decimal.</summary>
    public byte[] Expiration { get; set; } = new byte[4];

    public ushort PoolId { get; set; }

    public byte TransferStatus { get; set; }

    public byte ReceiptStatus { get; set; }

    /// <summary>When the transfer reached a final state; null while it has not, which the reply sends as zeros.</summary>
    public DateTime? CompletedAt { get; set; }

    public AftMeter CashableMeter { get; set; }

    public AftMeter RestrictedMeter { get; set; }

    public AftMeter NonrestrictedMeter { get; set; }

    /// <summary>What the host gave the receipt, kept for the receipt the platform prints.</summary>
    public AftReceiptData Receipt { get; set; } = AftReceiptData.None;

    public AftRecord Copy() => (AftRecord)MemberwiseClone();
}

/// <summary>A cumulative transfer meter as the reply carries it: its value and how many bytes it takes.</summary>
internal readonly record struct AftMeter(long Value, int Size);
