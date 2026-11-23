using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The software machine's legacy bonusing, against the machine library's lp8A_InitiateLegacyBonusPay,
/// lp2E_GameDelay, lp8B_InitiateMultipliedJackpot, lp90_SendLegacyBonusWinAmount,
/// lp9A_SendLegacyBonusMeters and SAS_LegacyBonusAward, with the platform's escrow, busy states and
/// game delay as QSP has them. Every case runs with the host in a sequence, so nothing reaches the
/// line the test did not send.
/// </summary>
public sealed partial class FakeEgmLegacyBonusTests : IDisposable
{
    private const byte LegacyBonusAwarded = 0x7C;
    private const byte LegacyBonusBit = 0x04;
    private const byte JackpotMultiplierBit = 0x01;

    private static readonly SasMessageLayout FeaturesLayout = SasLayouts.For(0xA0)!;
    private static readonly SasMessageLayout WinLayout = SasLayouts.For(0x90)!;
    private static readonly SasMessageLayout MetersLayout = SasLayouts.For(0x9A)!;

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

    /// <summary>A legacy bonus of so many accounting credits, BCD, with its tax status byte.</summary>
    private static byte[] Bonus(long credits, byte taxStatus) => [.. Bcd(credits, 4), taxStatus];

    private static byte[] Bcd(long value, int bytes)
    {
        byte[] digits = new byte[bytes];
        for (int i = bytes - 1; i >= 0; i--)
        {
            digits[i] = (byte)((value % 10) | (((value / 10) % 10) << 4));
            value /= 100;
        }

        return digits;
    }

    private LongPollResult Send8A(long credits, byte taxStatus) => Host.Execute(0x8A, Bonus(credits, taxStatus));

    private void Award(long credits, byte taxStatus) =>
        Assert.Equal(LongPollOutcome.Acknowledged, Send8A(credits, taxStatus).Outcome);

    private SasDecoded ReadWin(bool withCrc = false)
    {
        LongPollResult result = Host.Execute(0x90, [], checkSequence: withCrc);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(WinLayout, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return decoded;
    }

    private SasDecoded ReadLegacyMeters()
    {
        LongPollResult result = Host.Execute(0x9A, [0x00, 0x00]);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(MetersLayout, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return decoded;
    }

    private long FeatureCodes1() =>
        SasMessageDecoder.DecodeResponse(FeaturesLayout, Host.Execute(0xA0, [0x00, 0x00]).Frame)
            .Number("feature_codes_1")!.Value;

    private byte Poll()
    {
        GeneralPollResult result = Host.PollGenerally();
        Assert.True(result.Answered, $"the general poll went unanswered: {result.Outcome}");
        return result.Code;
    }

    /// <summary>Drains everything queued, acknowledging each with a long poll in between, and returns the codes in order.</summary>
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

    private static bool Wait(Func<bool> condition, int milliseconds = 3000)
    {
        DateTime until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until)
        {
            if (condition())
                return true;
            Thread.Sleep(20);
        }

        return condition();
    }

    [Fact]
    public void Legacy_bonusing_is_on_by_default_and_multiplied_jackpots_are_not_offered()
    {
        // QSP stores legacy bonusing on after a memory clear (SasRules.h:442) and never gives the
        // library the jackpot multiplier bit (SasNetwork.cpp:286), so the feature byte carries
        // bit 2 and not bit 0.
        Start();

        long features = FeatureCodes1();

        Assert.Equal(LegacyBonusBit, features & LegacyBonusBit);
        Assert.Equal(0, features & JackpotMultiplierBit);
        Assert.True(Machine.LegacyBonusOn);
    }

    [Fact]
    public void The_operator_switch_clears_the_bit_and_every_legacy_bonus_poll_goes_silent()
    {
        // Each handler reads the legacy bonus feature bit before it sends anything (lp.cpp:1716,
        // :8628, :8981, :9308), so with the switch off all four polls draw silence, not a refusal.
        Start();
        Machine.SetLegacyBonus(false);

        Assert.Equal(0, FeatureCodes1() & LegacyBonusBit);
        Assert.Equal(LongPollOutcome.Silent, Send8A(5, 0x00).Outcome);
        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x2E, [0x32, 0x00]).Outcome);
        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x90, []).Outcome);
        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x9A, [0x00, 0x00]).Outcome);
    }

    [Fact]
    public void A_multiplied_jackpot_is_always_silent_because_the_multiplier_bit_is_never_set()
    {
        // lp.cpp:8691 returns without sending unless the jackpot multiplier bit is set as well.
        Start();

        LongPollResult result = Host.Execute(0x8B, Convert.FromHexString("0000000099999999020000"));

        Assert.Equal(LongPollOutcome.Silent, result.Outcome);
    }

    [Fact]
    public void An_idle_machine_pays_a_bonus_at_once_as_a_machine_paid_external_bonus()
    {
        Start();
        long credits = Machine.Meters.Read(SasMeterCode.CurrentCredits);
        long coinIn = Machine.Meters.Read(SasMeterCode.TotalCoinInCredits);

        Award(5, 0x00);

        Assert.Equal(credits + 5, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.Equal(5, Machine.Meters.Read(SasMeterCode.TotalMachinePaidExternalBonusWin_Crd));
        Assert.Equal(5, Machine.Meters.Read(SasMeterCode.TotalCoinOutCredits));
        Assert.Equal(coinIn, Machine.Meters.Read(SasMeterCode.TotalCoinInCredits));
        Assert.Contains(LegacyBonusAwarded, Drain());
    }

    [Fact]
    public void The_award_is_read_back_with_90_and_cleared_once_the_reply_is_acknowledged()
    {
        // SAS_LegacyBonusAward stores the award (libsas.cpp:2594) and lp90 queues its reset behind
        // the host's acknowledgement (lp.cpp:9022), so a repeat of the poll reads it again.
        Start();
        Award(5, 0x01);

        SasDecoded first = ReadWin();
        Assert.Equal(0, first.Number("multiplier"));
        Assert.Equal(0, first.Number("multiplied_win"));
        Assert.Equal(1, first.Number("tax_status"));
        Assert.Equal(5, first.Number("bonus"));

        Assert.Equal(5, ReadWin().Number("bonus"));

        Host.Synchronise();
        SasDecoded cleared = ReadWin();
        Assert.Equal(0, cleared.Number("tax_status"));
        Assert.Equal(0, cleared.Number("bonus"));
    }

    [Fact]
    public void A_repeated_90_with_a_crc_resends_the_award_and_a_different_poll_clears_it()
    {
        // With a CRC the request is longer than the two bytes its reply shares with it, so the repeat
        // must be compared with what the host sent, as libsas compares command and CRC
        // (libsas_threads.cpp:964); the reset waits for a different message (lp.cpp:9022).
        Start();
        Award(5, 0x01);

        Assert.Equal(5, ReadWin(withCrc: true).Number("bonus"));
        Assert.Equal(5, ReadWin(withCrc: true).Number("bonus"));
        Assert.Equal(5, ReadWin(withCrc: true).Number("bonus"));

        Host.Synchronise();
        Assert.Equal(0, ReadWin(withCrc: true).Number("bonus"));
    }

    [Fact]
    public void Each_tax_status_lands_on_its_own_legacy_bonus_meter()
    {
        Start();

        Award(5, 0x00);
        Award(6, 0x01);
        Award(7, 0x02);

        SasDecoded meters = ReadLegacyMeters();
        Assert.Equal(5, meters.Number("deductible"));
        Assert.Equal(6, meters.Number("non_deductible"));
        Assert.Equal(7, meters.Number("wager_match"));
    }

    [Fact]
    public void Legacy_bonus_meters_for_a_game_the_machine_does_not_have_are_silent()
    {
        // lp.cpp:9330 returns without sending for a game past the last.
        Start();

        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x9A, [0x00, 0x02]).Outcome);
    }

    [Fact]
    public void With_reporting_on_the_bonus_event_carries_multiplier_win_tax_status_and_bonus()
    {
        Start();
        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x0E, [0x01]).Outcome);
        Drain();

        Award(3, 0x01);
        Host.Synchronise();
        GeneralPollResult result = Host.PollGenerally();

        Assert.True(result.IsRealTimeEvent);
        Assert.Equal(LegacyBonusAwarded, result.Code);
        Assert.Equal(Convert.FromHexString("00000000000100000003"), result.Data);
    }

    [Fact]
    public void A_bonus_with_no_amount_or_an_unknown_tax_status_is_acknowledged_and_pays_nothing()
    {
        // The library acknowledges before the platform looks at it; QSP drops a tax status past
        // wager match and pays only a non-zero escrow (SasNetwork_AftTransfer.hpp:1648, :1699).
        Start();

        Award(0, 0x00);
        Award(5, 0x03);

        Assert.Equal(0, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.DoesNotContain(LegacyBonusAwarded, Drain());
    }
}
