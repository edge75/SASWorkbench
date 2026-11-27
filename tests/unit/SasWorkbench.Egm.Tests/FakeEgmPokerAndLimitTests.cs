using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Tickets;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// What a case declares beyond the platform (D61): a draw poker game and its hand, a hopper machine's cash-out limit,
/// and hand pay receipts.
/// </summary>
public sealed class FakeEgmPokerAndLimitTests : IDisposable
{
    private readonly LoopbackBus _bus = new();
    private readonly string _wallet = Path.Combine(Path.GetTempPath(), "sasworkbench-receipts-" + Guid.NewGuid().ToString("N"));
    private FakeEgm? _machine;
    private PollEngine? _host;

    private FakeEgm Machine => _machine!;

    private PollEngine Host => _host!;

    private void Start(FakeEgmOptions options)
    {
        _machine = new FakeEgm(_bus.Egm, options);
        _machine.Start();
        _host = new PollEngine(_bus.Host, new PollEngineOptions
        {
            Address = SasAddress.Of(1),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(1),
            QuietGap = TimeSpan.FromMilliseconds(15),
            LinkDownAfter = TimeSpan.FromSeconds(30),
        });
        _host.Start();
    }

    public void Dispose()
    {
        _host?.Dispose();
        _machine?.Dispose();
        _bus.Dispose();
        if (Directory.Exists(_wallet))
            Directory.Delete(_wallet, recursive: true);
    }

    private SasDecoded Answer(byte command, byte[] data)
    {
        LongPollResult result = Host.Execute(command, data);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(command)!, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return decoded;
    }

    private byte[] Hand(out long handType)
    {
        SasDecoded decoded = Answer(0x8E, []);
        handType = decoded.Number("hand_type")!.Value;
        return [.. Enumerable.Range(1, 5).Select(i => (byte)decoded.Number($"card_{i}")!.Value)];
    }

    [Fact]
    public void A_machine_without_poker_leaves_the_card_poll_unanswered()
    {
        Start(new FakeEgmOptions());

        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x8E, []).Outcome);
        Assert.NotNull(Machine.DrawCards());
    }

    [Fact]
    public void A_poker_machine_reports_the_dealt_hand_then_the_final_one_with_the_held_cards_kept()
    {
        Start(new FakeEgmOptions { Poker = true });
        Assert.True(Machine.InsertBill(2000));
        Assert.NotNull(Machine.HoldCard(1, true));

        Machine.StartGame(1);
        Assert.Equal(FakeEgm.DealtHand.ToArray(), Hand(out long dealt));
        Assert.Equal(0, dealt);

        Assert.Null(Machine.HoldCard(1, true));
        Assert.Null(Machine.HoldCard(3, true));
        Assert.Null(Machine.DrawCards());
        byte[] final = Hand(out long type);
        Assert.Equal(1, type);
        Assert.Equal(
            new byte[] { FakeEgm.DealtHand[0], FakeEgm.DrawnCards[0], FakeEgm.DealtHand[2], FakeEgm.DrawnCards[1], FakeEgm.DrawnCards[2] },
            final);

        // Drawn already, so another draw is refused; the game's end leaves the hand as it is.
        Assert.NotNull(Machine.DrawCards());
        Machine.EndGame(0);
        Assert.Equal(final, Hand(out _));
    }

    [Fact]
    public void A_hand_not_drawn_is_final_when_the_game_ends()
    {
        Start(new FakeEgmOptions { Poker = true });
        Assert.True(Machine.InsertBill(2000));

        Machine.StartGame(1);
        Machine.EndGame(0);

        Assert.Equal(FakeEgm.DrawnCards.ToArray(), Hand(out long type));
        Assert.Equal(1, type);
    }

    [Fact]
    public void A_hopper_machine_reports_its_hopper_limit_for_each_game_and_nothing_past_the_last()
    {
        // Five cents to the unit: a fifty-dollar limit is a thousand units.
        Start(new FakeEgmOptions { Hopper = true, HopperLimitCents = 5000, AccountingDenomination = 0x02 });

        SasDecoded machine = Answer(0xA4, [0x00, 0x00]);
        Assert.Equal(0, machine.Number("game_number"));
        Assert.Equal(1000, machine.Number("cashout_limit"));
        Assert.Equal(1, Answer(0xA4, [0x00, 0x01]).Number("game_number"));
        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0xA4, [0x00, 0x02]).Outcome);
    }

    [Fact]
    public void A_machine_without_a_hopper_has_no_cash_out_limit()
    {
        Start(new FakeEgmOptions());

        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0xA4, [0x00, 0x00]).Outcome);
    }

    [Fact]
    public void A_machine_with_receipts_offers_the_bit_and_prints_one_when_a_hand_pay_is_reset()
    {
        Start(new FakeEgmOptions
        {
            HandpayReceipts = true,
            CashoutValidation = SasCashoutValidation.SecureEnhanced,
            WalletDirectory = _wallet,
        });
        Assert.Equal(LongPollOutcome.Answered, Host.Execute(0x4C, [1, 0, 0, 1, 0, 0]).Outcome);

        // Control and status both bit 1: the reply keeps it, as the machine offers it.
        long status = Answer(0x7B, Convert.FromHexString("080200020000000000")).Number("current_validation_status")!.Value;
        Assert.Equal(0x0002, status & 0x0002);

        Machine.RaiseHandpay(Core.Scripting.HandpayKind.CancelledCredit, 2000);
        Assert.True(Machine.ResetHandpay());

        Ticket receipt = Assert.Single(Directory.GetFiles(_wallet).Select(TicketFile.Read));
        Assert.Equal(TicketValidationType.CancelledCreditHandpayReceiptPrinted, receipt.ValidationType);
        Assert.Equal(2000, receipt.AmountCents);
    }
}
