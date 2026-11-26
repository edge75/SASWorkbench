using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The coin acceptor, hopper, reels and cashbox a case can declare (D58). The protocol half is the library's queue
/// and its hopper and reel polls; the application half is the simplest form of what the GSA documents expect, since
/// the platform has none of these devices. Every case runs with the host in a sequence.
/// </summary>
public sealed class FakeEgmDevicesTests : IDisposable
{
    private readonly LoopbackBus _bus = new();
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
        Assert.True(_host.BeginSequence(), "the round did not hand the line over");
    }

    // The default unit is five cents, so a note of 500 cents is 100 coins.
    private static FakeEgmOptions Devices(bool partialPay = false) => new()
    {
        CoinAcceptor = CoinAcceptorKind.Internal,
        Hopper = true,
        HopperLevel = 500,
        Reels = true,
        PartialPayCoins = partialPay ? 10 : 0,
    };

    public void Dispose()
    {
        _host?.Dispose();
        _machine?.Dispose();
        _bus.Dispose();
    }

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

    private byte Poll()
    {
        GeneralPollResult result = Host.PollGenerally();
        Assert.True(result.Answered, $"the general poll went unanswered: {result.Outcome}");
        return result.Code;
    }

    private SasDecoded Ask(byte command, params byte[] data)
    {
        LongPollResult result = Host.Execute(command, data);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(command)!, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return decoded;
    }

    private long Meter(SasMeterCode code) => Machine.Meters.Read(code);

    [Fact]
    public void A_machine_built_without_devices_refuses_every_device_stimulus()
    {
        Start(new FakeEgmOptions());

        Assert.NotNull(Machine.InsertCoins(1));
        Assert.NotNull(Machine.CoinFault("tilt"));
        Assert.NotNull(Machine.HopperFault("empty"));
        Assert.NotNull(Machine.ReelFault("tilt"));
        Assert.NotNull(Machine.SetHopperLimit(100));
        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x8F, []).Outcome);
    }

    [Fact]
    public void Coins_go_to_the_hopper_while_it_has_room_and_then_to_the_drop()
    {
        Start(Devices() with { HopperLevel = 998 });

        Assert.Null(Machine.InsertCoins(5));

        Assert.Equal(5, Meter(SasMeterCode.TotalCreditsFromCoinAcceptor));
        Assert.Equal(3, Meter(SasMeterCode.TotalCreditsFromCoinsToDrop));
        Assert.Equal(1000, Machine.HopperLevel);
        Assert.Equal(5, Meter(SasMeterCode.CurrentCredits));
    }

    [Fact]
    public void An_external_acceptor_sends_every_coin_to_the_drop_on_its_own_meter()
    {
        Start(new FakeEgmOptions { CoinAcceptor = CoinAcceptorKind.External });

        Assert.Null(Machine.InsertCoins(4));

        Assert.Equal(4, Meter(SasMeterCode.TotalCreditsFromExternalCoinAcceptor));
        Assert.Equal(4, Meter(SasMeterCode.TotalDropIn_Crd));
    }

    [Fact]
    public void Coin_hopper_and_reel_faults_raise_their_codes()
    {
        Start(Devices());

        Assert.Null(Machine.CoinFault("tilt"));
        Assert.Null(Machine.CoinFault("reversed"));
        Assert.Null(Machine.CoinFault("lockout"));
        Assert.Null(Machine.CoinFault("diverter"));
        Assert.Null(Machine.HopperFault("jam"));
        Assert.Null(Machine.HopperFault("runaway"));
        Assert.Null(Machine.HopperFault("full"));
        Assert.Null(Machine.HopperFault("low"));
        Assert.Null(Machine.ReelFault("reel_3"));
        Assert.Null(Machine.ReelFault("disconnected"));
        Machine.SetCashbox(removed: true);
        Machine.SetCashbox(removed: false);

        Assert.Equal(new byte[] { 0x21, 0x2D, 0x79, 0x25, 0x22, 0x24, 0x80, 0x81, 0x43, 0x46, 0x1B, 0x1C }, Drain());
    }

    [Fact]
    public void The_hopper_status_poll_reports_each_fault_and_how_full_it_is_but_never_the_count()
    {
        // SAS_RegisterHopperStatus keeps the level only when the hopper cannot measure it (libsas.cpp:1325).
        Start(Devices());

        SasDecoded idle = Ask(0x4F);
        Assert.Equal(6, idle.Number("length"));
        Assert.Equal(0, idle.Number("status"));
        Assert.Equal(50, idle.Number("percent_full"));
        Assert.Equal(0, idle.Number("hopper_level"));

        Assert.Null(Machine.HopperFault("coin_too_short"));
        Assert.Equal(3, Ask(0x4F).Number("status"));
        Assert.Null(Machine.HopperFault("cleared"));
        Assert.Equal(0, Ask(0x4F).Number("status"));
    }

    [Fact]
    public void A_cash_out_under_the_hopper_limit_is_paid_in_coins()
    {
        Start(Devices());
        Assert.True(Machine.InsertBill(500));

        Machine.Cashout(Path.GetTempPath());

        Assert.Equal(100, Meter(SasMeterCode.TotalCreditsPaidFromHopper));
        Assert.Equal(0, Meter(SasMeterCode.CurrentCredits));
        Assert.False(Machine.HandpayPending);
    }

    [Fact]
    public void An_empty_hopper_pays_what_it_has_and_raises_hopper_empty()
    {
        Start(Devices() with { HopperLevel = 3 });
        Assert.True(Machine.InsertBill(100));
        Drain();

        Machine.Cashout(Path.GetTempPath());

        Assert.Equal(3, Meter(SasMeterCode.TotalCreditsPaidFromHopper));
        Assert.Equal(17, Meter(SasMeterCode.CurrentCredits));
        Assert.Equal(new byte[] { 0x23 }, Drain());
    }

    [Fact]
    public void A_cash_out_over_the_limit_pays_the_partial_pay_and_owes_the_rest_by_hand()
    {
        Start(Devices(partialPay: true) with { HopperLimitCents = 1000 });
        Assert.True(Machine.InsertBill(2000));

        Machine.Cashout(Path.GetTempPath());

        Assert.True(Machine.HandpayPending);
        Assert.Equal(10, Meter(SasMeterCode.TotalCreditsPaidFromHopper));
        SasDecoded read = Ask(0x1B);
        Assert.Equal(0x80, read.Number("level"));
        Assert.Equal(390, read.Number("amount"));
        Assert.Equal(10, read.Number("partial_pay"));
    }

    [Fact]
    public void A_cancelled_cash_out_raises_55_returns_the_credits_and_cannot_be_cancelled_twice()
    {
        // internalHandpayRefuse writes $55 while the front entry is not yet reset (libsas_internal.cpp:5477).
        Start(Devices() with { HopperLimitCents = 1000 });
        Assert.True(Machine.InsertBill(2000));
        Machine.Cashout(Path.GetTempPath());
        Assert.Contains((byte)0x51, Drain());

        Assert.True(Machine.CancelHandpay());
        Assert.False(Machine.CancelHandpay());

        Assert.Equal(new byte[] { 0x55 }, Drain());
        Assert.Equal(400, Meter(SasMeterCode.CurrentCredits));
        Assert.False(Machine.HandpayPending);
    }

    [Fact]
    public void A_game_on_a_reel_machine_registers_its_stops_and_pads_the_rest()
    {
        Start(Devices());
        Assert.True(Machine.InsertBill(2000));

        Machine.PlayGames(1, 1);

        SasDecoded stops = Ask(0x8F);
        for (int reel = 1; reel <= 5; reel++)
            Assert.NotEqual(0xFF, stops.Number($"reel_{reel}"));
        for (int reel = 6; reel <= 9; reel++)
            Assert.Equal(0xFF, stops.Number($"reel_{reel}"));
    }

    [Fact]
    public void A_win_paid_from_the_hopper_leaves_the_credit_meter_alone()
    {
        Start(Devices());
        Assert.True(Machine.InsertBill(2000));
        Assert.Null(Machine.SetWinPayout(hopper: true));

        // A game credit is a dollar, twenty coins: the bet takes twenty off the meter and the win comes as coins.
        Machine.PlayGames(1, 1, 1);

        Assert.Equal(380, Meter(SasMeterCode.CurrentCredits));
        Assert.Equal(20, Meter(SasMeterCode.TotalCreditsPaidFromHopper));
        Assert.Equal(20, Meter(SasMeterCode.TotalCoinOutCredits));
    }
}
