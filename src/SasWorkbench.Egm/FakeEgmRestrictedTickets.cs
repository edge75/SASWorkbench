using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Tickets;

namespace SasWorkbench.Egm;

/// <summary>
/// Restricted tickets: the operator's option, the host's two $7B bits, the restricted cash-out ticket and the terms a
/// redeemed restricted ticket carries (D62).
/// </summary>
/// <remarks>
/// The protocol half is the library's: the status bits it reports and the expiration and pool it keeps from $71
/// (lp.cpp:4625). Printing one is the platform's: the operator and the host must both allow it, and without the
/// ticket-in-part toggle the host's bit 4 decides whether any restricted credit may leave on a ticket at all
/// (QSP CashoutPrinterRules.h:256, :306).
/// </remarks>
public sealed partial class FakeEgm
{
    private const ushort PrintRestrictedTickets = 0x0008;
    private const ushort ForeignRestrictedAmounts = 0x0010;

    // The operator's restricted ticket option, off as the platform ships it (QSP SasNetwork.cpp:624).
    private bool _restrictedTicketsOption;

    /// <summary>The operator allows or forbids restricted tickets.</summary>
    public void SetRestrictedTickets(bool on)
    {
        lock (_gate)
            _restrictedTicketsOption = on;
    }

    /// <summary>$7B bits 3 and 4 follow the operator's option, bit 4 with bit 3 as the platform has it without its toggle (QSP SasNetwork.cpp:624).</summary>
    private ushort RestrictedTicketStatus => _restrictedTicketsOption ? (ushort)(PrintRestrictedTickets | ForeignRestrictedAmounts) : (ushort)0;

    /// <summary>How much restricted credit may leave on a ticket now: all of it, or none (QSP RestrictedAmountForTicket).</summary>
    private long RestrictedCentsForTicket()
    {
        lock (_gate)
        {
            ushort host = _platformValidationStatus;
            bool allowed = _restrictedTicketsOption && (host & PrintRestrictedTickets) != 0 && (host & ForeignRestrictedAmounts) != 0;
            return allowed ? CurrentRestrictedCents() : 0;
        }
    }

    /// <summary>
    /// Pays the restricted credit out on a restricted ticket once no cashable credit is left, secure enhanced only;
    /// otherwise it stays on the machine as before.
    /// </summary>
    private Ticket? PayOutRestricted(string walletDirectory)
    {
        if (CashoutValidation != SasCashoutValidation.SecureEnhanced)
            return null;

        long cents = RestrictedCentsForTicket();
        if (cents <= 0 || !MayPrintValidated())
            return null;

        uint printed = (uint)Interlocked.Increment(ref _ticketsPrinted);
        var ticketNumber = (ushort)(printed % 10_000);
        ulong number;
        lock (_gate)
        {
            if (!TryStartCashout(cents, ticketNumber, out number, (byte)TicketValidationType.RestrictedPromotionalFromCashout))
                return null;
        }

        long credits = cents / AccountingCents;
        TakeRestricted(cents);
        MeterRestrictedTicketOut(credits, cents);

        var ticket = new Ticket
        {
            ValidationNumber = number,
            ValidationType = TicketValidationType.RestrictedPromotionalFromCashout,
            AmountCents = cents,
            TicketNumber = ticketNumber,
            PrintedAt = DateTime.Now,
            Location = TicketText().Location,
            Address1 = TicketText().Address1,
            Address2 = TicketText().Address2,
            Note = "printed by the software machine",
        };

        FileTicket(walletDirectory, ticket);
        CompleteCashout();
        return ticket;
    }

    /// <summary>The meters a restricted ticket moves, as the platform's meter table reads them (QSP SasNetwork_Ticket.hpp:1161 to :1170).</summary>
    private void MeterRestrictedTicketOut(long credits, long cents)
    {
        Meters.Increment(SasMeterCode.TotalSASRestrictedTicketOut_Cents, cents);
        Meters.Increment(SasMeterCode.TotalSASRestrictedTicketOut_Qty, 1);
        Meters.Increment(SasMeterCode.RestrictedTicketOut_Cents, cents);
        Meters.Increment(SasMeterCode.RestrictedTicketOut_Qty, 1);
        Meters.Increment(SasMeterCode.TotalRestrictedPromotionalTicketOut_Crd, credits);
        Meters.Increment(SasMeterCode.TotalRestrictedPromotionalTicketOut_Qty, 1);
        Meters.Increment(SasMeterCode.TotalTicketOut_Crd, credits);
    }

    /// <summary>
    /// The expiration and pool a restricted redemption carries, kept only with validation extensions and zero when left
    /// out, as the library keeps them (lp.cpp:4625, :4657).
    /// </summary>
    private (ushort Pool, byte[] Expiration) RestrictedTerms(SasDecoded command)
    {
        if (CashoutValidation == SasCashoutValidation.Standard)
            return (0, new byte[4]);

        long month = command.Number("restricted_expiration_month") ?? 0;
        long day = command.Number("restricted_expiration_day") ?? 0;
        long year = command.Number("restricted_expiration_year") ?? 0;
        var pool = (ushort)(command.Number("pool_id") ?? 0);
        return (pool, [Bcd((byte)month), Bcd((byte)day), .. BcdBytes(year, 2)]);
    }
}
