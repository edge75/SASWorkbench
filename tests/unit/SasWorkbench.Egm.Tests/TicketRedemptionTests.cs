using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Tickets;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The redemption cycle on its own, with no wire under it.
/// </summary>
/// <remarks>
/// Worth testing here rather than only through a script because most of what can go wrong in a
/// redemption is an ordering question — a verdict arriving before the machine was asked, a second
/// ticket going in while the first is unresolved — and each of those is one call here and a
/// paragraph of scripting otherwise.
/// </remarks>
public class TicketRedemptionTests
{
    private static Ticket Cashout(ulong validation = 123456789012345678, long cents = 2000) => new()
    {
        ValidationNumber = validation,
        AmountCents = cents,
    };

    [Fact]
    public void AFreshMachineIsHoldingNothing()
    {
        var redemption = new TicketRedemption();

        Assert.Equal(TicketRedemptionState.Idle, redemption.State);
        Assert.Equal(SasTicketStatus.TICKET_STATUS_NO_TICKET_IN_ESCROW, redemption.Ask());
    }

    [Fact]
    public void AnInsertedTicketIsHeldUntilTheHostAsks()
    {
        var redemption = new TicketRedemption();

        Assert.True(redemption.Insert(Cashout()));
        Assert.Equal(TicketRedemptionState.Held, redemption.State);
        Assert.Equal(SasTicketStatus.TICKET_STATUS_TICKET_IN_ESCROW, redemption.Ask());
        Assert.Equal(TicketRedemptionState.Asked, redemption.State);
    }

    // A machine with a document in escrow has nowhere to put a second one.
    [Fact]
    public void ASecondTicketIsTurnedAwayWhileTheFirstIsUnresolved()
    {
        var redemption = new TicketRedemption();
        redemption.Insert(Cashout(111));

        Assert.False(redemption.Insert(Cashout(222)));
        Assert.Equal(111ul, redemption.ValidationNumber);
    }

    // A host that answers before it has asked is answering about nothing. The machine says so
    // rather than paying: $FF before any cycle has run, $C0 once one has.
    [Fact]
    public void AVerdictBeforeTheQuestionAdvancesNothing()
    {
        var redemption = new TicketRedemption();
        redemption.Insert(Cashout());

        Assert.False(redemption.Redeem(
            SasTicketTransferCode.TTC_VALID_CASHABLE_TICKET, 2000, 123456789012345678));
        Assert.Equal(SasTicketMachineStatus.GMSC_NO_VALIDATION_INFORMATION_AVAILABLE,
            redemption.MachineStatus);
        Assert.Equal(TicketRedemptionState.Held, redemption.State);
    }

    [Fact]
    public void AnAcceptedTicketIsPendingBeforeItIsRedeemed()
    {
        var redemption = new TicketRedemption();
        redemption.Insert(Cashout());
        redemption.Ask();

        Assert.True(redemption.Redeem(
            SasTicketTransferCode.TTC_VALID_CASHABLE_TICKET, 2000, 123456789012345678));
        Assert.Equal(SasTicketMachineStatus.GMSC_TICKET_REDEMPTION_PENDING, redemption.MachineStatus);
        Assert.Equal(2000, redemption.AmountCents);

        redemption.Settle(SasTicketTransferCode.TTC_VALID_CASHABLE_TICKET);
        Assert.Equal(SasTicketMachineStatus.GMSC_CASHABLE_TICKET_REDEEMED, redemption.MachineStatus);
    }

    [Fact]
    public void ARedemptionForAnotherNumberIsRefusedAndTheTicketGoesBack()
    {
        var redemption = new TicketRedemption();
        redemption.Insert(Cashout(111));
        redemption.Ask();

        Assert.False(redemption.Redeem(SasTicketTransferCode.TTC_VALID_CASHABLE_TICKET, 2000, 222));
        Assert.Equal(SasTicketMachineStatus.GMSC_VALIDATION_NUMBER_DOES_NOT_MATCH,
            redemption.MachineStatus);
        Assert.Equal(TicketRedemptionState.Idle, redemption.State);
        Assert.Null(redemption.Held);
    }

    // Nine reasons a host can give for refusing, and the machine reports one status for all of
    // them: it was told no, and it does not know which of the host's records said so.
    [Theory]
    [InlineData(SasTicketTransferCode.TTC_UNABLE_TO_VALIDATE)]
    [InlineData(SasTicketTransferCode.TTC_TICKET_ALREADY_REDEEMED)]
    [InlineData(SasTicketTransferCode.TTC_TICKET_EXPIRED)]
    public void EveryRefusalReportsTheSameStatus(SasTicketTransferCode code)
    {
        var redemption = new TicketRedemption();
        redemption.Insert(Cashout());
        redemption.Ask();

        Assert.False(redemption.Redeem(code, 2000, 123456789012345678));
        Assert.Equal(SasTicketMachineStatus.GMSC_TICKET_REJECTED_BY_HOST_OR_UNKNOWN,
            redemption.MachineStatus);
    }

    [Fact]
    public void AnAmountOfNothingIsNotAValidTransferAmount()
    {
        var redemption = new TicketRedemption();
        redemption.Insert(Cashout());
        redemption.Ask();

        Assert.False(redemption.Redeem(
            SasTicketTransferCode.TTC_VALID_CASHABLE_TICKET, 0, 123456789012345678));
        Assert.Equal(SasTicketMachineStatus.GMSC_NOT_A_VALID_TRANSFER_AMOUNT,
            redemption.MachineStatus);
    }

    // The cycle stays open until the host has read the outcome. A machine that closed it on
    // sending would free the acceptor for a second ticket on the strength of a reply that may
    // never have arrived.
    [Fact]
    public void TheCycleClosesOnlyWhenTheOutcomeIsRead()
    {
        var redemption = new TicketRedemption();
        redemption.Insert(Cashout());
        redemption.Ask();
        redemption.Redeem(SasTicketTransferCode.TTC_VALID_CASHABLE_TICKET, 2000, 123456789012345678);

        redemption.CloseIfRead();
        Assert.Equal(TicketRedemptionState.Redeeming, redemption.State);

        redemption.Settle(SasTicketTransferCode.TTC_VALID_CASHABLE_TICKET);
        Assert.False(redemption.Insert(Cashout(999)));

        redemption.CloseIfRead();
        Assert.Equal(TicketRedemptionState.Idle, redemption.State);
        Assert.True(redemption.Insert(Cashout(999)));
    }

    [Fact]
    public void AVerdictAfterACycleHasRunIsNotCompatibleWithIt()
    {
        var redemption = new TicketRedemption();
        redemption.Insert(Cashout());
        redemption.Ask();
        redemption.Redeem(SasTicketTransferCode.TTC_VALID_CASHABLE_TICKET, 2000, 123456789012345678);
        redemption.Settle(SasTicketTransferCode.TTC_VALID_CASHABLE_TICKET);
        redemption.CloseIfRead();

        Assert.False(redemption.Redeem(
            SasTicketTransferCode.TTC_VALID_CASHABLE_TICKET, 2000, 123456789012345678));
        Assert.Equal(SasTicketMachineStatus.GMSC_NOT_COMPATIBLE_WITH_CURRENT_REDEMPTION_CYCLE,
            redemption.MachineStatus);
    }

    [Fact]
    public void OnlyTheTwoHighestStatusesAnswerWithoutATicket()
    {
        var redemption = new TicketRedemption();
        Assert.False(redemption.StatusCarriesTicket);

        redemption.Insert(Cashout());
        redemption.Ask();
        Assert.True(redemption.StatusCarriesTicket);
    }
}
