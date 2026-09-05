namespace SasWorkbench.Core.Tickets;

/// <summary>
/// What a ticket is, and how it came to exist.
/// </summary>
/// <remarks>
/// The codes are the library's own validation-type values, which a machine reports when a host
/// asks it about a ticket. The names here are ours; the numbers are the wire's, so a ticket
/// written by hand can name a type without anybody having to remember that a debit ticket from a
/// transfer is 0x04.
/// </remarks>
public enum TicketValidationType : byte
{
    /// <summary>A cashable ticket the machine printed for a cashout or a win.</summary>
    CashableFromCashout = 0x00,

    /// <summary>A restricted promotional ticket printed for a cashout.</summary>
    RestrictedPromotionalFromCashout = 0x01,

    /// <summary>A cashable ticket printed as the result of a funds transfer.</summary>
    CashableFromTransfer = 0x02,

    /// <summary>A restricted ticket printed as the result of a funds transfer.</summary>
    RestrictedFromTransfer = 0x03,

    /// <summary>A debit ticket printed as the result of a funds transfer.</summary>
    DebitFromTransfer = 0x04,

    /// <summary>A cancelled-credit handpay for which a receipt was printed.</summary>
    CancelledCreditHandpayReceiptPrinted = 0x10,

    /// <summary>A jackpot handpay for which a receipt was printed.</summary>
    JackpotHandpayReceiptPrinted = 0x20,

    /// <summary>A cancelled-credit handpay with no receipt.</summary>
    CancelledCreditHandpayNoReceipt = 0x40,

    /// <summary>A jackpot handpay with no receipt.</summary>
    JackpotHandpayNoReceipt = 0x60,

    /// <summary>A cashable ticket that has been redeemed.</summary>
    CashableRedeemed = 0x80,

    /// <summary>A restricted promotional ticket that has been redeemed.</summary>
    RestrictedPromotionalRedeemed = 0x81,

    /// <summary>A nonrestricted promotional ticket that has been redeemed.</summary>
    NonrestrictedPromotionalRedeemed = 0x82,
}
