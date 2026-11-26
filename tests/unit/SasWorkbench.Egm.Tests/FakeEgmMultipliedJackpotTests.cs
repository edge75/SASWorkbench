using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The software machine built multiplying jackpots: long poll $8B against the library's
/// lp8B_InitiateMultipliedJackpot, and the award against SAS_LegacyBonusAward. The application half is
/// this build's own (D52), as the platform has none. A game credit and an accounting credit are both five
/// cents here, so every figure is in credits. The host stays in a sequence, so nothing reaches the line
/// the test did not send.
/// </summary>
public sealed class FakeEgmMultipliedJackpotTests : IDisposable
{
    private const byte LegacyBonusAwarded = 0x7C;
    private const byte HandpayWasReset = 0x52;

    private static readonly SasMessageLayout FeaturesLayout = SasLayouts.For(0xA0)!;
    private static readonly SasMessageLayout MetersLayout = SasLayouts.For(0x9A)!;

    private readonly LoopbackBus _bus = new();
    private FakeEgm? _machine;
    private PollEngine? _host;

    private FakeEgm Machine => _machine!;

    private PollEngine Host => _host!;

    private void Start(FakeEgmOptions? options = null)
    {
        _machine = new FakeEgm(_bus.Egm, options ?? new FakeEgmOptions { MultipliedJackpot = true, CentsPerGameCredit = 5 });
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

        // Real-time reporting on, so each event arrives with its data, and credits to play with.
        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x0E, [0x01]).Outcome);
        Machine.InsertBill(2000);
        Drain();
    }

    public void Dispose()
    {
        _host?.Dispose();
        _machine?.Dispose();
        _bus.Dispose();
    }

    /// <summary>Sends $8B with the range, the multiplier byte, enable and wager type, as hex.</summary>
    private LongPollResult Send8B(string hex) => Host.Execute(0x8B, Convert.FromHexString(hex));

    private void Enable(string hex) => Assert.Equal(LongPollOutcome.Acknowledged, Send8B(hex).Outcome);

    private long FeatureCodes1() =>
        SasMessageDecoder.DecodeResponse(FeaturesLayout, Host.Execute(0xA0, [0x00, 0x00]).Frame)
            .Number("feature_codes_1")!.Value;

    private SasDecoded ReadLegacyMeters()
    {
        LongPollResult result = Host.Execute(0x9A, [0x00, 0x00]);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        return SasMessageDecoder.DecodeResponse(MetersLayout, result.Frame);
    }

    /// <summary>Drains every event, acknowledging each, and returns code and data in order.</summary>
    private List<(byte Code, byte[] Data)> Drain()
    {
        var events = new List<(byte, byte[])>();
        Host.Synchronise();
        for (GeneralPollResult result = Host.PollGenerally(); result.Answered && result.Code != 0x00; result = Host.PollGenerally())
        {
            events.Add((result.Code, result.Data?.ToArray() ?? []));
            Host.Synchronise();
        }

        return events;
    }

    private byte[]? AwardIn(List<(byte Code, byte[] Data)> events) =>
        events.Where(e => e.Code == LegacyBonusAwarded).Select(e => e.Data).FirstOrDefault();

    [Fact]
    public void A_machine_built_with_it_reports_the_multiplier_bit_and_loses_it_with_legacy_bonusing()
    {
        // lp8B needs both bits (lp.cpp:8689, :8691); this machine reports the multiplier only beside legacy bonusing.
        Start();
        Assert.Equal(0x05, FeatureCodes1() & 0x05);

        Machine.SetLegacyBonus(false);

        Assert.Equal(0, FeatureCodes1() & 0x05);
        Assert.Equal(LongPollOutcome.Silent, Send8B("0000000099999999020000").Outcome);
    }

    [Fact]
    public void A_multiplied_win_pays_the_extra_part_as_a_deductible_legacy_bonus()
    {
        Start();
        Enable("0000000099999999030000");
        long credits = Machine.Meters.Read(SasMeterCode.CurrentCredits);

        Machine.PlayGames(1, betCredits: 1, winCredits: 10);

        // Multiplier 03, the extra part twenty, tax status 00, no bonus (libsas.cpp:2637 to :2643).
        Assert.Equal(Convert.FromHexString("03000000200000000000"), AwardIn(Drain()));
        Assert.Equal(credits - 1 + 30, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.Equal(30, Machine.Meters.Read(SasMeterCode.TotalCoinOutCredits));
        Assert.Equal(20, Machine.Meters.Read(SasMeterCode.TotalMachinePaidExternalBonusWin_Crd));
        Assert.Equal(20, ReadLegacyMeters().Number("deductible"));
    }

    [Fact]
    public void Bit_seven_puts_the_extra_part_on_the_non_deductible_meter_and_leaves_the_tax_status_byte_clear()
    {
        Start();
        Enable("0000000099999999840000");

        Machine.PlayGames(1, betCredits: 1, winCredits: 10);

        Assert.Equal(Convert.FromHexString("84000000300000000000"), AwardIn(Drain()));
        SasDecoded meters = ReadLegacyMeters();
        Assert.Equal(30, meters.Number("non_deductible"));
        Assert.Equal(0, meters.Number("deductible"));
    }

    [Fact]
    public void A_win_outside_the_inclusive_range_or_below_max_bet_is_paid_as_it_is()
    {
        Start(new FakeEgmOptions { MultipliedJackpot = true, CentsPerGameCredit = 5, MaxBet = 5 });

        Enable("0000000500000009020000");
        Machine.PlayGames(1, betCredits: 1, winCredits: 4);
        Machine.PlayGames(1, betCredits: 1, winCredits: 10);
        Assert.Null(AwardIn(Drain()));

        Machine.PlayGames(1, betCredits: 1, winCredits: 9);
        Assert.NotNull(AwardIn(Drain()));

        Enable("0000000099999999020001");
        Machine.PlayGames(1, betCredits: 4, winCredits: 10);
        Assert.Null(AwardIn(Drain()));
        Machine.PlayGames(1, betCredits: 5, winCredits: 10);
        Assert.NotNull(AwardIn(Drain()));
    }

    [Fact]
    public void A_change_sent_during_a_game_waits_for_the_next_one()
    {
        Start();
        Enable("0000000099999999030000");

        Machine.StartGame(1);
        Drain();
        Enable("0000000000000000010100");
        Machine.EndGame(10);
        Assert.Equal(0x03, AwardIn(Drain())![0]);

        Machine.PlayGames(1, betCredits: 1, winCredits: 10);
        Assert.Null(AwardIn(Drain()));
    }

    [Fact]
    public void A_command_the_machine_cannot_apply_is_acknowledged_and_changes_nothing()
    {
        // lp8B forwards the fields unchecked (lp.cpp:8729); a multiplier past ten is the application's to drop.
        Start();

        Enable("00000000999999990B0000");
        Enable("0000000999999999000000");
        Enable("0000000099999999020002");
        Machine.PlayGames(1, betCredits: 1, winCredits: 10);

        Assert.Null(AwardIn(Drain()));
    }

    [Fact]
    public void A_power_cycle_switches_it_off()
    {
        Start();
        Enable("0000000099999999020000");

        Machine.PowerCycle();
        Host.Synchronise();
        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x0E, [0x01]).Outcome);
        Drain();
        Machine.PlayGames(1, betCredits: 1, winCredits: 10);

        Assert.Null(AwardIn(Drain()));
    }

    [Fact]
    public void A_lost_link_switches_it_off()
    {
        Start(new FakeEgmOptions
        {
            MultipliedJackpot = true, CentsPerGameCredit = 5, LinkLossAfter = TimeSpan.FromSeconds(2),
        });
        Host.Synchronise();
        Enable("0000000099999999020000");

        // Nothing is sent while the host holds the line, so the machine hears silence past the loop break.
        Thread.Sleep(4500);
        Drain();
        Machine.PlayGames(1, betCredits: 1, winCredits: 10);
        Assert.Null(AwardIn(Drain()));

        // Enabled again once the link is back, the next win is multiplied.
        Enable("0000000099999999020000");
        Machine.PlayGames(1, betCredits: 1, winCredits: 10);
        Assert.NotNull(AwardIn(Drain()));
    }

    [Fact]
    public void A_hand_paid_win_takes_the_extra_part_and_its_award_follows_the_reset()
    {
        Start();
        Machine.SetJackpotLimit(5000);
        Assert.Contains(Drain(), e => e.Code == 0x3C);
        Enable("0000000099999999030000");

        Machine.PlayGames(1, betCredits: 1, winCredits: 2000);
        Assert.Null(AwardIn(Drain()));
        Assert.Equal(LongPollOutcome.Answered, Host.Execute(0x1B, []).Outcome);
        Host.Synchronise();

        Assert.True(Machine.ResetHandpay());

        List<(byte Code, byte[] Data)> events = Drain();
        int reset = events.FindIndex(e => e.Code == HandpayWasReset);
        int award = events.FindIndex(e => e.Code == LegacyBonusAwarded);
        Assert.True(reset >= 0 && award > reset, "the award did not follow hand pay was reset");
        Assert.Equal(Convert.FromHexString("03000040000000000000"), events[award].Data);
        Assert.Equal(4000, Machine.Meters.Read(SasMeterCode.TotalAttendantPaidExternalBonusWin_Crd));
        Assert.Equal(2000, Machine.Meters.Read(SasMeterCode.TotalAttendantPaidPaytableWin_Crd));
        Assert.Equal(0, Machine.Meters.Read(SasMeterCode.TotalMachinePaidExternalBonusWin_Crd));
        Assert.Equal(4000, ReadLegacyMeters().Number("deductible"));
    }

    [Fact]
    public void A_bonus_held_for_the_game_end_joins_the_multiplied_award()
    {
        Start();
        Enable("0000000099999999020000");

        Machine.StartGame(1);
        Drain();
        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x8A, Convert.FromHexString("0000000500")).Outcome);
        Machine.EndGame(10);

        List<(byte Code, byte[] Data)> events = Drain();
        Assert.Single(events, e => e.Code == LegacyBonusAwarded);
        Assert.Equal(Convert.FromHexString("02000000100000000005"), AwardIn(events));
    }
}
