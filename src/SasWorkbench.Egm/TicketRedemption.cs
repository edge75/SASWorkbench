using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Tickets;

namespace SasWorkbench.Egm;

/// <summary>Where a ticket redemption has got to.</summary>
/// <remarks>
/// The states are the library's, in our words. They matter to a test because the machine's answer
/// to the two redemption long polls depends on which one it is in, and because the same long poll
/// asked twice is a different question the second time.
/// </remarks>
public enum TicketRedemptionState
{
    /// <summary>Nothing is in the acceptor and no cycle is open.</summary>
    Idle,

    /// <summary>A ticket is held, and the host has not asked about it yet.</summary>
    Held,

    /// <summary>The host has read what is held and owes an answer.</summary>
    Asked,

    /// <summary>The host said yes and the machine is taking the ticket.</summary>
    Redeeming,

    /// <summary>The money has moved; the outcome is waiting to be read.</summary>
    Settled,
}

/// <summary>
/// One ticket redemption, from the ticket going in to the host reading what became of it.
/// </summary>
/// <remarks>
/// <para>
/// Kept apart from the machine itself because it is a state machine and nothing else: it holds no
/// meters and moves no money. What it decides is which status byte the machine answers with, and
/// the machine acts on that. The two being separable is what makes the awkward cases — a redeem
/// for a number the machine is not holding, a status asked for before anything happened — testable
/// without a wire.
/// </para>
/// <para>
/// It is a reduction of the library's cycle, not a copy of it. Two tickets in flight at once, the
/// resend rule that makes an identical command a status request, and the buffer of past
/// redemptions are all absent, and a test that needs one of those belongs against a real machine.
/// </para>
/// </remarks>
public sealed class TicketRedemption
{
    // True once a cycle has run, which is what separates "nothing has ever happened" from "that
    // command does not belong to what is happening now".
    private bool _everRedeemed;

    /// <summary>The ticket in the acceptor, or null when none is.</summary>
    public Ticket? Held { get; private set; }

    /// <summary>Where the cycle has got to.</summary>
    public TicketRedemptionState State { get; private set; } = TicketRedemptionState.Idle;

    /// <summary>What the machine last answered, or would answer now.</summary>
    public SasTicketMachineStatus MachineStatus { get; private set; } =
        SasTicketMachineStatus.GMSC_NO_VALIDATION_INFORMATION_AVAILABLE;

    /// <summary>What the host said the ticket was worth, in cents.</summary>
    public long AmountCents { get; private set; }

    /// <summary>
    /// The number the machine reports for this cycle, zero when it holds nothing.
    /// </summary>
    /// <remarks>
    /// The barcode read as a number, not the ticket's validation number on its own. The machine
    /// knows only what the validator read off the strip.
    /// </remarks>
    public ulong ValidationNumber => Held?.BarcodeNumber ?? 0;

    /// <summary>True when the status carries an amount and a validation number with it.</summary>
    /// <remarks>
    /// The two statuses at or above $C0 answer with the status byte alone. Everything below it
    /// belongs to a cycle the machine knows about and can quote back.
    /// </remarks>
    public bool StatusCarriesTicket => (byte)MachineStatus
        < (byte)SasTicketMachineStatus.GMSC_NOT_COMPATIBLE_WITH_CURRENT_REDEMPTION_CYCLE;

    /// <summary>Puts a ticket into the acceptor.</summary>
    /// <returns>False when one is already in flight, so the second is turned away.</returns>
    public bool Insert(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        if (State != TicketRedemptionState.Idle)
            return false;

        Held = ticket;
        AmountCents = 0;
        State = TicketRedemptionState.Held;
        MachineStatus = SasTicketMachineStatus.GMSC_WAITING_FOR_LONG_POLL_71;
        return true;
    }

    /// <summary>Answers what the machine is holding, and obliges the host to say what to do.</summary>
    /// <remarks>
    /// Reading this poll is what turns a held ticket into a cycle the host owes a verdict on, so it
    /// is a state change rather than a read. Asking twice says the same thing again, which is what
    /// a host retrying a lost reply needs.
    /// </remarks>
    public SasTicketStatus Ask()
    {
        if (State is not (TicketRedemptionState.Held or TicketRedemptionState.Asked))
            return SasTicketStatus.TICKET_STATUS_NO_TICKET_IN_ESCROW;

        State = TicketRedemptionState.Asked;
        MachineStatus = SasTicketMachineStatus.GMSC_WAITING_FOR_LONG_POLL_71;
        return SasTicketStatus.TICKET_STATUS_TICKET_IN_ESCROW;
    }

    /// <summary>Takes the host's verdict, and says whether the money is now the machine's to move.</summary>
    /// <param name="code">What the host decided.</param>
    /// <param name="amountCents">What the host says the ticket is worth.</param>
    /// <param name="validationNumber">The number the host is redeeming.</param>
    /// <returns>True when this call accepted the ticket, so the caller must move the money.</returns>
    public bool Redeem(SasTicketTransferCode code, long amountCents, ulong validationNumber)
    {
        if (code == SasTicketTransferCode.TTC_REQUEST_FOR_CURRENT_TICKET_STATUS)
            return false;

        if (State != TicketRedemptionState.Asked)
        {
            // A verdict on a cycle that is not open. $C0 says exactly that: not a refusal of the
            // ticket, but a statement that this command belongs to nothing in progress, so nothing
            // is advanced by it. Before the first cycle of all there is $FF, which says less.
            MachineStatus = _everRedeemed
                ? SasTicketMachineStatus.GMSC_NOT_COMPATIBLE_WITH_CURRENT_REDEMPTION_CYCLE
                : SasTicketMachineStatus.GMSC_NO_VALIDATION_INFORMATION_AVAILABLE;
            return false;
        }

        if (!IsAccepting(code))
        {
            // Every code from $80 up is the host refusing, and the machine reports one status for
            // all of them: it does not know which of the host's reasons applied, only that the
            // ticket is going back out.
            MachineStatus = code >= SasTicketTransferCode.TTC_UNABLE_TO_VALIDATE
                ? SasTicketMachineStatus.GMSC_TICKET_REJECTED_BY_HOST_OR_UNKNOWN
                : SasTicketMachineStatus.GMSC_NOT_A_VALID_TRANSFER_FUNCTION;
            Return();
            return false;
        }

        if (Held is null || validationNumber != Held.BarcodeNumber)
        {
            MachineStatus = SasTicketMachineStatus.GMSC_VALIDATION_NUMBER_DOES_NOT_MATCH;
            Return();
            return false;
        }

        if (amountCents <= 0)
        {
            MachineStatus = SasTicketMachineStatus.GMSC_NOT_A_VALID_TRANSFER_AMOUNT;
            Return();
            return false;
        }

        AmountCents = amountCents;
        MachineStatus = SasTicketMachineStatus.GMSC_TICKET_REDEMPTION_PENDING;
        State = TicketRedemptionState.Redeeming;
        return true;
    }

    /// <summary>Records that the money has moved, which is what the host reads next.</summary>
    public void Settle(SasTicketTransferCode code)
    {
        MachineStatus = code switch
        {
            SasTicketTransferCode.TTC_VALID_RESTRICTED_PROMOTIONAL_TICKET =>
                SasTicketMachineStatus.GMSC_RESTRICTED_PROMOTIONAL_TICKET_REDEEMED,
            SasTicketTransferCode.TTC_VALID_NONRESTRICTED_PROMOTIONAL_TICKET =>
                SasTicketMachineStatus.GMSC_NONRESTRICTED_PROMOTIONAL_TICKET_REDEEMED,
            _ => SasTicketMachineStatus.GMSC_CASHABLE_TICKET_REDEEMED,
        };

        State = TicketRedemptionState.Settled;
        _everRedeemed = true;
    }

    /// <summary>Closes the cycle once the host has read the outcome, freeing the acceptor.</summary>
    /// <remarks>
    /// Only a status request closes it, and only once the money has moved. A host that never asks
    /// leaves the cycle open, which is the truthful model: the machine cannot know its answer
    /// arrived, and a second ticket must not go in on the assumption that it did.
    /// </remarks>
    public void CloseIfRead()
    {
        if (State != TicketRedemptionState.Settled)
            return;

        Held = null;
        State = TicketRedemptionState.Idle;
    }

    private static bool IsAccepting(SasTicketTransferCode code) =>
        code is SasTicketTransferCode.TTC_VALID_CASHABLE_TICKET
            or SasTicketTransferCode.TTC_VALID_RESTRICTED_PROMOTIONAL_TICKET
            or SasTicketTransferCode.TTC_VALID_NONRESTRICTED_PROMOTIONAL_TICKET;

    /// <summary>The ticket comes back out, and the cycle is over.</summary>
    private void Return()
    {
        Held = null;
        AmountCents = 0;
        State = TicketRedemptionState.Idle;
        _everRedeemed = true;
    }
}
