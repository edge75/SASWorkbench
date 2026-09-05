namespace SasWorkbench.Core.Tickets;

/// <summary>
/// One ticket: the thing a machine prints and a machine accepts.
/// </summary>
/// <remarks>
/// <para>
/// There is exactly one ticket model. It is written and read in two encodings — a key/value text
/// file in the shape of a classic <c>.ini</c>, and JSON — and a ticket converted from one to the
/// other and back is the same ticket. Both encodings are produced from the single field table in
/// <see cref="TicketField"/>, so the two cannot drift apart by being edited separately.
/// </para>
/// <para>
/// Most fields come from the library's own cashout structure, so that what a test asserts on and
/// what the machine printed are the same quantities. Three do not, and are marked below: they
/// describe the ticket as a <em>file</em> rather than as a protocol structure, and exist so that
/// the negative cases — a ticket from another machine, one already redeemed — can be written by
/// hand rather than manufactured by a special code path.
/// </para>
/// <para>
/// <see cref="AmountCents"/> is named for its unit on purpose. The amount is in cents, which is
/// established by the library metering it into the cent-denominated ticket-in meters; it is not a
/// credit value, and it is not scaled by the accounting denomination.
/// </para>
/// </remarks>
public sealed record Ticket
{
    /// <summary>The number printed as the barcode, and quoted back by a host that redeems it.</summary>
    public ulong ValidationNumber { get; init; }

    /// <summary>What kind of ticket this is.</summary>
    public TicketValidationType ValidationType { get; init; } = TicketValidationType.CashableFromCashout;

    /// <summary>The ticket's face value, in cents.</summary>
    public long AmountCents { get; init; }

    /// <summary>The machine's own sequence number for the ticket.</summary>
    public uint TicketNumber { get; init; }

    /// <summary>The pool a restricted ticket belongs to; zero when it belongs to none.</summary>
    public ushort PoolId { get; init; }

    /// <summary>The day after which the ticket is no longer redeemable, or null when it never expires.</summary>
    public DateOnly? Expiration { get; init; }

    /// <summary>The validation system that issued the number, as reported to a host.</summary>
    public byte ValidationSystemId { get; init; }

    /// <summary>When the machine validated and printed it.</summary>
    public DateTime? PrintedAt { get; init; }

    /// <summary>The casino or location line printed on the ticket.</summary>
    public string Location { get; init; } = string.Empty;

    /// <summary>The first address line printed on the ticket.</summary>
    public string Address1 { get; init; } = string.Empty;

    /// <summary>The second address line printed on the ticket.</summary>
    public string Address2 { get; init; } = string.Empty;

    /// <summary>
    /// Which machine printed it. Not a field of the protocol's cashout structure — it is here so a
    /// ticket from a foreign machine can be written by hand and offered to this one.
    /// </summary>
    public uint AssetNumber { get; init; }

    /// <summary>
    /// Whether this ticket has already been paid. Not a field of the protocol's cashout structure —
    /// it is here so the already-redeemed case can be written by hand rather than staged by
    /// redeeming a ticket twice.
    /// </summary>
    public bool Redeemed { get; init; }

    /// <summary>
    /// Free text a person can leave on a hand-written ticket saying what it is for. Carried through
    /// both encodings untouched and never interpreted.
    /// </summary>
    public string Note { get; init; } = string.Empty;
}
