using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The software machine's hand pay against what chapters 3 and 4 test on a real machine: hand pay
/// pending is a priority exception and goes ahead of everything queued, long poll 1B carries the
/// amount until the next message acknowledges the reading and zeros after, and the attendant's
/// reset reports $52. Every case runs in a sequence, so nothing reaches the line uninvited.
/// </summary>
public sealed class FakeEgmHandpayTests : IDisposable
{
    private static readonly SasAddress Address = SasAddress.Of(1);

    private const byte HandpayPending = 0x51;
    private const byte HandpayReset = 0x52;

    private readonly LoopbackBus _bus = new();
    private readonly FakeEgm _machine;
    private readonly PollEngine _host;

    public FakeEgmHandpayTests()
    {
        _machine = new FakeEgm(_bus.Egm);
        _machine.Start();
        _host = new PollEngine(_bus.Host, new PollEngineOptions
        {
            Address = Address,
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(1),
            QuietGap = TimeSpan.FromMilliseconds(15),
            LinkDownAfter = TimeSpan.FromSeconds(30),
        });
        _host.Start();
        Assert.True(_host.BeginSequence(), "the round did not hand the line over");
    }

    public void Dispose()
    {
        _host.Dispose();
        _machine.Dispose();
        _bus.Dispose();
    }

    private byte Poll()
    {
        GeneralPollResult result = _host.PollGenerally();
        Assert.True(result.Answered, $"the general poll went unanswered: {result.Outcome}");
        return result.Code;
    }

    /// <summary>Reads long poll 1B and returns its level byte and its five-byte BCD amount, in accounting units.</summary>
    private (byte Level, long Amount) ReadHandpay()
    {
        LongPollResult result = _host.Execute(0x1B);
        Assert.True(result.Succeeded, $"long poll 1B went unanswered: {result.Outcome}");
        byte[] frame = result.Frame.ToArray();
        long amount = 0;
        for (int i = 4; i < 9; i++)
            amount = (amount * 100) + ((frame[i] >> 4) * 10) + (frame[i] & 0x0F);
        return (frame[3], amount);
    }

    /// <summary>
    /// Two doors are queued first, and hand pay pending still comes out ahead of both - the shape
    /// GSA 4.11 asks of every priority exception.
    /// </summary>
    [Fact]
    public void Hand_pay_pending_goes_ahead_of_what_was_queued()
    {
        _machine.RaiseException(0x11);
        _machine.RaiseException(0x12);
        _machine.RaiseHandpay(HandpayKind.CancelledCredit, 2000);

        _host.Synchronise();
        Assert.Equal(HandpayPending, Poll());
        _host.Synchronise();
        Assert.Equal(0x11, Poll());
    }

    /// <summary>
    /// GSA 3.16: the first reading carries the amount; the same 1B again is a refusal and carries it
    /// again; any other message acknowledges it, and the next reading is all zeros.
    /// </summary>
    [Fact]
    public void A_hand_pay_reading_is_zero_once_another_message_acknowledges_it()
    {
        _machine.RaiseHandpay(HandpayKind.CancelledCredit, 2000);

        (byte level, long amount) = ReadHandpay();
        Assert.Equal(0x80, level);
        Assert.Equal(2000 / _machine.AccountingCents, amount);

        Assert.Equal(2000 / _machine.AccountingCents, ReadHandpay().Amount);

        Assert.True(_host.Execute(0x1F).Succeeded);
        Assert.Equal((0, 0L), ReadHandpay());
        Assert.True(_machine.HandpayPending, "acknowledging the reading must not pay the hand pay");
    }

    /// <summary>A broadcast acknowledges the reading too, which is what GSA 3.19 turns on.</summary>
    [Fact]
    public void A_broadcast_acknowledges_a_hand_pay_reading()
    {
        _machine.RaiseHandpay(HandpayKind.Jackpot, 150000);

        (byte level, long amount) = ReadHandpay();
        Assert.Equal(0x00, level);
        Assert.Equal(150000 / _machine.AccountingCents, amount);

        _host.Synchronise();
        Assert.Equal(0L, ReadHandpay().Amount);
    }

    /// <summary>
    /// Once the reading has been acknowledged, the reset reports $52, clears the hand pay, and counts
    /// what the attendant paid.
    /// </summary>
    [Fact]
    public void The_reset_reports_hand_pay_was_reset_and_moves_the_meter()
    {
        _machine.RaiseHandpay(HandpayKind.CancelledCredit, 2000);
        _host.Synchronise();
        Assert.Equal(HandpayPending, Poll());
        Assert.NotEqual(0L, ReadHandpay().Amount);
        Assert.True(_host.Execute(0x1F).Succeeded);

        Assert.True(_machine.ResetHandpay());
        Assert.False(_machine.HandpayPending);
        Assert.False(_machine.ResetHandpay(), "a second reset has nothing to reset");

        _host.Synchronise();
        Assert.Equal(HandpayReset, Poll());
        Assert.Equal(2000 / _machine.AccountingCents,
            _machine.Meters.Read(SasMeterCode.TotalHandPaidCancelledCredits));
    }

    /// <summary>
    /// SAS 6.02 7.8.1 and GSA 4.23: a hand pay reset before its reading is acknowledged sends no $52.
    /// The machine leaves the lockup and the meter moves, but the reading still carries the amount.
    /// $52 comes only once that reading has been taken and acknowledged.
    /// </summary>
    [Fact]
    public void A_reset_before_the_reading_is_acknowledged_holds_back_hand_pay_was_reset()
    {
        _machine.RaiseHandpay(HandpayKind.CancelledCredit, 2000);
        _host.Synchronise();
        Assert.Equal(HandpayPending, Poll());

        Assert.True(_machine.ResetHandpay());
        Assert.False(_machine.HandpayPending, "the attendant's reset ends the lockup");
        Assert.False(_machine.ResetHandpay(), "a second reset has nothing to reset");
        Assert.Equal(2000 / _machine.AccountingCents,
            _machine.Meters.Read(SasMeterCode.TotalHandPaidCancelledCredits));

        _host.Synchronise();
        Assert.NotEqual(HandpayReset, Poll());

        Assert.Equal(2000 / _machine.AccountingCents, ReadHandpay().Amount);
        Assert.True(_host.Execute(0x1F).Succeeded);
        _host.Synchronise();
        Assert.Equal(HandpayReset, Poll());
        Assert.Equal((0, 0L), ReadHandpay());
    }

    /// <summary>The machine holds one hand pay at a time and says so rather than overwrite it.</summary>
    [Fact]
    public void A_second_hand_pay_is_refused_while_one_is_pending()
    {
        _machine.RaiseHandpay(HandpayKind.Jackpot, 100000);
        Assert.Throws<InvalidOperationException>(() => _machine.RaiseHandpay(HandpayKind.Jackpot, 100000));
    }
}
