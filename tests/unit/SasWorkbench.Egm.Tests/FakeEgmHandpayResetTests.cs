using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The host clearing a hand pay with $94 and choosing the credit meter with $A8, as the platform allows (D61): the
/// library's codes, the reset ID long poll 1B reports, and which meters each kind of reset moves.
/// </summary>
public sealed class FakeEgmHandpayResetTests : IDisposable
{
    private readonly LoopbackBus _bus = new();
    private FakeEgm? _machine;
    private PollEngine? _host;

    private FakeEgm Machine => _machine!;

    private PollEngine Host => _host!;

    private void Start(FakeEgmOptions? options = null)
    {
        // One cent to the accounting unit, so a meter moves by the cents of the hand pay.
        _machine = new FakeEgm(_bus.Egm, options ?? new FakeEgmOptions { AccountingDenomination = 0x01 });
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
    }

    private long Field(byte command, byte[] data, string field)
    {
        LongPollResult result = Host.Execute(command, data);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(command)!, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return decoded.Number(field)!.Value;
    }

    private long RemoteReset() => Field(0x94, [], "reset_code");

    private long ResetMethod(byte method) => Field(0xA8, [method], "ack_code");

    private long ResetId() => Field(0x1B, [], "reset_id");

    private long Meter(SasMeterCode code) => Machine.Meters.Read(code);

    [Fact]
    public void With_no_hand_pay_both_polls_say_there_is_none()
    {
        Start();

        Assert.Equal(2, RemoteReset());
        Assert.Equal(2, ResetMethod(0x01));
    }

    [Fact]
    public void A_method_other_than_standard_credit_meter_or_celebration_is_refused()
    {
        Start();

        Assert.Equal(LongPollOutcome.Rejected, Host.Execute(0xA8, [0x02]).Outcome);
    }

    [Fact]
    public void A_jackpot_cleared_from_the_host_without_A8_goes_to_the_jackpot_meter()
    {
        Start();
        Machine.RaiseHandpay(HandpayKind.Jackpot, 150000);
        long credits = Meter(SasMeterCode.CurrentCredits);

        Assert.Equal(0, RemoteReset());

        Assert.False(Machine.HandpayPending);
        Assert.Equal(150000, Meter(SasMeterCode.TotalJackpotCredits));
        Assert.Equal(0, Meter(SasMeterCode.TotalCoinOutCredits));
        Assert.Equal(credits, Meter(SasMeterCode.CurrentCredits));
        Assert.Equal(2, RemoteReset());
    }

    [Fact]
    public void A_jackpot_the_host_sends_to_the_credit_meter_moves_credits_and_coin_out_and_not_the_jackpot_meter()
    {
        Start();
        Machine.RaiseHandpay(HandpayKind.Jackpot, 150000);

        Assert.Equal(1, ResetId());
        Assert.Equal(0, ResetMethod(0x01));
        Assert.Equal(0, RemoteReset());

        Assert.Equal(150000, Meter(SasMeterCode.CurrentCredits));
        Assert.Equal(150000, Meter(SasMeterCode.TotalCoinOutCredits));
        Assert.Equal(150000, Meter(SasMeterCode.TotalMachinePaidPaytableWin_Crd));
        Assert.Equal(0, Meter(SasMeterCode.TotalJackpotCredits));
    }

    [Fact]
    public void The_choice_holds_for_one_hand_pay_only_and_the_last_one_sent_wins()
    {
        Start();
        Machine.RaiseHandpay(HandpayKind.Jackpot, 1000);
        Assert.Equal(0, ResetMethod(0x01));
        Assert.Equal(0, RemoteReset());

        // No $A8 this time: a standard reset.
        Machine.RaiseHandpay(HandpayKind.Jackpot, 1000);
        Assert.Equal(0, RemoteReset());
        Assert.Equal(1000, Meter(SasMeterCode.TotalJackpotCredits));

        // Credit meter and then standard: standard.
        Machine.RaiseHandpay(HandpayKind.Jackpot, 1000);
        Assert.Equal(0, ResetMethod(0x01));
        Assert.Equal(0, ResetMethod(0x00));
        Assert.Equal(0, RemoteReset());
        Assert.Equal(2000, Meter(SasMeterCode.TotalJackpotCredits));
        Assert.Equal(1000, Meter(SasMeterCode.CurrentCredits));
    }

    [Fact]
    public void A_jackpot_past_the_credit_limit_offers_only_the_standard_reset()
    {
        Start();
        Machine.SetCreditLimit(100000);
        Machine.RaiseHandpay(HandpayKind.Jackpot, 150000);

        Assert.Equal(0, ResetId());
        Assert.Equal(1, ResetMethod(0x01));
        Assert.Equal(0, RemoteReset());
        Assert.Equal(150000, Meter(SasMeterCode.TotalJackpotCredits));
        Assert.Equal(0, Meter(SasMeterCode.CurrentCredits));
    }

    [Fact]
    public void A_cancelled_credit_hand_pay_cleared_from_the_host_is_a_hand_paid_cancelled_credit()
    {
        Start();
        Machine.RaiseHandpay(HandpayKind.CancelledCredit, 2000);

        Assert.Equal(0, ResetId());
        Assert.Equal(1, ResetMethod(0x01));
        Assert.Equal(0, RemoteReset());
        Assert.Equal(2000, Meter(SasMeterCode.TotalHandPaidCancelledCredits));
        Assert.Equal(0, Meter(SasMeterCode.TotalJackpotCredits));
        Assert.Equal(0, Meter(SasMeterCode.TotalCoinOutCredits));
    }

    [Fact]
    public void The_sound_switches_are_acknowledged()
    {
        Start();

        foreach (byte command in new byte[] { 0x03, 0x04, 0x05 })
            Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(command, []).Outcome);
    }
}
