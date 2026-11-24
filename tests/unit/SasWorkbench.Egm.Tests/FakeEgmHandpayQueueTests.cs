using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The library's hand pay queue on the software machine: a hand pay reset before the host read it stays queued
/// behind the next one, the readings come out oldest first, a full queue loses its oldest, legacy reporting sends
/// $52 on the reset and keeps the reading live, and a validated hand pay is announced with $3E. Every case runs in
/// a sequence, so nothing reaches the line uninvited.
/// </summary>
public sealed class FakeEgmHandpayQueueTests : IDisposable
{
    private const byte HandpayPending = 0x51;
    private const byte HandpayReset = 0x52;
    private const byte HandpayValidated = 0x3E;

    private readonly LoopbackBus _bus = new();
    private FakeEgm? _machine;
    private PollEngine? _host;

    private FakeEgm Machine => _machine!;

    private PollEngine Host => _host!;

    private void Start(FakeEgmOptions? options = null)
    {
        _machine = new FakeEgm(_bus.Egm, options ?? new FakeEgmOptions());
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
        Assert.True(_host.BeginSequence(), "the round did not hand the line over");
    }

    public void Dispose()
    {
        _host?.Dispose();
        _machine?.Dispose();
        _bus.Dispose();
    }

    private byte Poll()
    {
        GeneralPollResult result = Host.PollGenerally();
        Assert.True(result.Answered, $"the general poll went unanswered: {result.Outcome}");
        return result.Code;
    }

    /// <summary>Every exception queued, each acknowledged by the synchronisation poll after it.</summary>
    private List<byte> Drain()
    {
        var codes = new List<byte>();
        Host.Synchronise();
        for (byte code = Poll(); code != 0x00; code = Poll())
        {
            codes.Add(code);
            Host.Synchronise();
        }

        return codes;
    }

    /// <summary>Reads long poll 1B, returning its level and amount, then acknowledges the reading with a broadcast.</summary>
    private (byte Level, long Amount) Read()
    {
        LongPollResult result = Host.Execute(0x1B);
        Assert.True(result.Succeeded, $"long poll 1B went unanswered: {result.Outcome}");
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x1B)!, result.Frame);
        Host.Synchronise();
        return ((byte)decoded.Number("level")!.Value, decoded.Number("amount")!.Value);
    }

    private long Units(long cents) => cents / Machine.AccountingCents;

    /// <summary>Polls, acknowledging each answer, until hand pay pending arrives or the time is up.</summary>
    private bool PendingWithin(TimeSpan limit)
    {
        DateTime until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until)
        {
            Host.Synchronise();
            if (Poll() == HandpayPending)
            {
                Host.Synchronise();
                return true;
            }

            Thread.Sleep(200);
        }

        return false;
    }

    [Fact]
    public void A_hand_pay_reset_before_it_was_read_stays_queued_behind_the_next()
    {
        // libsas_internal.cpp:4834 refuses only a hand pay not yet reset; one reset and unread stays in the queue.
        Start();
        Machine.RaiseHandpay(HandpayKind.CancelledCredit, 2000);
        // The note's own events follow the hand pay, which goes ahead of them.
        Assert.Equal(HandpayPending, Drain()[0]);
        Assert.True(Machine.ResetHandpay());

        Machine.RaiseHandpay(HandpayKind.Jackpot, 150000);
        Assert.True(Machine.HandpayPending);
        Assert.Equal([HandpayPending], Drain());

        // The older is read first, and its $52 follows. The newer was marked announced when its own $51 was
        // acknowledged (libsas_internal.cpp:4994), so the fifteen-second timer is what announces it again.
        Assert.Equal((0x80, Units(2000)), Read());
        Assert.Equal([HandpayReset], Drain());
        Assert.True(PendingWithin(TimeSpan.FromSeconds(20)), "the newer hand pay was not announced again");
        Assert.Equal((0x00, Units(150000)), Read());
        Assert.Equal((0, 0L), Read());

        // The newer is reset after its reading was acknowledged, so its $52 follows at once.
        Assert.True(Machine.ResetHandpay());
        Assert.Equal([HandpayReset], Drain());
    }

    [Fact]
    public void A_full_queue_loses_its_oldest_hand_pay()
    {
        // libsas_internal.cpp:4855 moves the queue up and overwrites the oldest of sixteen.
        Start();
        for (int i = 1; i <= 17; i++)
        {
            Machine.RaiseHandpay(HandpayKind.Jackpot, i * 100000L);
            Assert.True(Machine.ResetHandpay());
        }

        Assert.Equal((0x00, Units(200000)), Read());
    }

    [Fact]
    public void Legacy_reporting_sends_hand_pay_was_reset_on_the_reset_and_keeps_the_reading_live()
    {
        // SAS 6.02 7.8.2 as the library has it: $52 on the reset whether or not read (libsas_internal.cpp:5310),
        // and an acknowledged reading still reported while locked up (lp.cpp:1026).
        Start(new FakeEgmOptions { LegacyHandpayReporting = true });
        Machine.RaiseHandpay(HandpayKind.CancelledCredit, 2000);
        // The note's own events follow the hand pay, which goes ahead of them.
        Assert.Equal(HandpayPending, Drain()[0]);

        Assert.Equal((0x80, Units(2000)), Read());
        Assert.Equal((0x80, Units(2000)), Read());

        Assert.True(Machine.ResetHandpay());
        Assert.Equal([HandpayReset], Drain());
        Assert.Equal((0, 0L), Read());
    }

    [Fact]
    public void Legacy_reporting_sends_hand_pay_was_reset_even_when_nothing_read_it()
    {
        Start(new FakeEgmOptions { LegacyHandpayReporting = true });
        Machine.RaiseHandpay(HandpayKind.Jackpot, 150000);
        Assert.Equal([HandpayPending], Drain());

        Assert.True(Machine.ResetHandpay());
        Assert.Equal([HandpayReset], Drain());
        Assert.Equal((0, 0L), Read());
    }

    [Fact]
    public void A_hand_pay_awaiting_its_reading_is_announced_again_after_a_power_cycle_in_either_mode()
    {
        // libsas_internal.cpp:887 writes $51 again at power up whatever the reporting mode.
        foreach (bool legacy in new[] { false, true })
        {
            using var bus = new LoopbackBus();
            using var machine = new FakeEgm(bus.Egm, new FakeEgmOptions { LegacyHandpayReporting = legacy });
            machine.Start();
            using var host = new PollEngine(bus.Host, new PollEngineOptions
            {
                Address = SasAddress.Of(1),
                PollInterval = TimeSpan.FromMilliseconds(20),
                ResponseTimeout = TimeSpan.FromSeconds(1),
                QuietGap = TimeSpan.FromMilliseconds(15),
                LinkDownAfter = TimeSpan.FromSeconds(30),
            });
            host.Start();
            Assert.True(host.BeginSequence());

            machine.RaiseHandpay(HandpayKind.Jackpot, 150000);
            host.Synchronise();
            Assert.Equal(HandpayPending, host.PollGenerally().Code);
            host.Synchronise();

            machine.PowerCycle();
            host.Synchronise();
            Assert.Equal(HandpayPending, host.PollGenerally().Code);
        }
    }

    [Fact]
    public void A_validated_hand_pay_is_announced_with_hand_pay_validated_and_read_with_4D()
    {
        // libsas_internal.cpp:5928: a hand pay record with no receipt is $3E on a secure enhanced machine.
        Start(new FakeEgmOptions { CashoutValidation = SasCashoutValidation.SecureEnhanced, HandpayValidation = true });
        Assert.Equal(LongPollOutcome.Answered, Host.Execute(0x4C, [1, 0, 0, 1, 0, 0]).Outcome);
        SasDecoded status = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x7B)!,
            Host.Execute(0x7B, [0x08, 0x04, 0x00, 0x04, 0x00, 0, 0, 0, 0]).Frame);
        Assert.Equal(0x8025L, status.Number("current_validation_status"));
        Drain();

        Machine.RaiseHandpay(HandpayKind.CancelledCredit, 2000);
        // The note's own events follow the hand pay, which goes ahead of them.
        Assert.Equal(HandpayPending, Drain()[0]);
        Assert.Equal((0x80, Units(2000)), Read());
        Assert.True(Machine.ResetHandpay());
        // $3E ranks ahead of $52 in the library's queue (internalGetExceptionPriority).
        Assert.Equal([HandpayValidated, HandpayReset], Drain());

        LongPollResult record = Host.Execute(0x4D, [0x00]);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x4D)!, record.Frame);
        Assert.Equal(0x40L, decoded.Number("validation_type"));
        Assert.Equal(2000L, decoded.Number("amount"));
        Assert.Equal(1L, Machine.Meters.Read(SasMeterCode.ValidatedCancelledCreditHandpayNoReceipt_Qty));
    }

    [Fact]
    public void A_machine_built_without_hand_pay_validation_keeps_bit_2_clear()
    {
        // QSP clears the bit, so the library reports it unsupported (QSP SasNetwork.cpp:617).
        Start(new FakeEgmOptions { CashoutValidation = SasCashoutValidation.SecureEnhanced });
        Assert.Equal(LongPollOutcome.Answered, Host.Execute(0x4C, [1, 0, 0, 1, 0, 0]).Outcome);
        SasDecoded status = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x7B)!,
            Host.Execute(0x7B, [0x08, 0x04, 0x00, 0x04, 0x00, 0, 0, 0, 0]).Frame);
        Assert.Equal(0x8021L, status.Number("current_validation_status"));

        Machine.RaiseHandpay(HandpayKind.CancelledCredit, 2000);
        Drain();
        Read();
        Assert.True(Machine.ResetHandpay());
        Assert.DoesNotContain(HandpayValidated, Drain());
    }
}
