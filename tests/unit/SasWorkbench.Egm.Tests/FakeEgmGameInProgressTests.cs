using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// A game or a cash out held open, and the jackpot limit, as QSP decides them: a game holds a legacy
/// bonus and a lock (AsyncLpLegacyBonus, DecideAftLockAnswer), a printing ticket holds a bonus
/// (eCashTicketOut interrupts play), and a win, a progressive or a bonus above the limit locks the
/// machine up for an attendant (SasMarket IsLimitReached, BookProgressiveWin, ProcessLegacyBonus).
/// Every case runs with the host in a sequence, so nothing reaches the line the test did not send.
/// </summary>
public sealed class FakeEgmGameInProgressTests : IDisposable
{
    private const byte HandpayPending = 0x51;
    private const byte HandpayReset = 0x52;
    private const byte ProgressiveWin = 0x54;
    private const byte LevelHit = 0x56;
    private const byte GameLocked = 0x6F;
    private const byte LegacyBonusAwarded = 0x7C;
    private const byte GameStarted = 0x7E;
    private const byte GameEnded = 0x7F;

    private readonly LoopbackBus _bus = new();
    private readonly string _wallet = Path.Combine(Path.GetTempPath(), "sasworkbench-gip-" + Guid.NewGuid().ToString("N"));
    private FakeEgm? _machine;
    private PollEngine? _host;

    private FakeEgm Machine => _machine!;

    private PollEngine Host => _host!;

    /// <summary>A one-cent machine with non-SAS level 3, and in group 1 with SAS level 1 only when asked, as that level must be kept broadcast.</summary>
    private void Start(bool sas = false)
    {
        _machine = new FakeEgm(_bus.Egm, new FakeEgmOptions
        {
            ProgressiveGroup = sas ? (byte)1 : (byte)0,
            SasProgressiveLevels = sas ? 0b001u : 0u,
            NonSasProgressiveLevels = 0b100,
            Denomination = 0x01,
        });
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

        // Level 1 broadcast, so the machine is out of its progressive lockup, and a note to play with.
        if (sas)
            Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x80, [0x01, 0x01, .. Bcd(500, 5)]).Outcome);
        Machine.InsertBill(1000);
        Drain();
    }

    public void Dispose()
    {
        _host?.Dispose();
        _machine?.Dispose();
        _bus.Dispose();
        if (Directory.Exists(_wallet))
            Directory.Delete(_wallet, recursive: true);
    }

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

    private void Award(long credits, byte taxStatus) =>
        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x8A, [.. Bcd(credits, 4), taxStatus]).Outcome);

    private SasDecoded Read(byte command, byte[]? data = null)
    {
        LongPollResult result = Host.Execute(command, data ?? []);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(command)!, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return decoded;
    }

    private SasDecoded Lock(string hex) => Read(0x74, Convert.FromHexString(hex));

    private long Meter(SasMeterCode code) => Machine.Meters.Read(code);

    private byte Poll()
    {
        GeneralPollResult result = Host.PollGenerally();
        Assert.True(result.Answered, $"the general poll went unanswered: {result.Outcome}");
        return result.Code;
    }

    /// <summary>Drains everything queued, acknowledging each, and returns the codes in order.</summary>
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

    /// <summary>Reads the hand pay and lets the next poll acknowledge it, then resets it and drains $52.</summary>
    private SasDecoded ReadAndReset()
    {
        SasDecoded handpay = Read(0x1B);
        Host.Synchronise();
        Assert.True(Machine.ResetHandpay());
        Assert.Contains(HandpayReset, Drain());
        return handpay;
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
    public void A_game_held_open_reports_its_start_and_its_end_only_when_ended()
    {
        Start();
        long credits = Meter(SasMeterCode.CurrentCredits);

        Machine.StartGame(2);
        Assert.True(Machine.GameInProgress);
        // A game credit is a dollar and the accounting unit a cent.
        Assert.Equal(credits - 200, Meter(SasMeterCode.CurrentCredits));
        Assert.Equal([GameStarted], Drain());

        Machine.EndGame(5);
        Assert.False(Machine.GameInProgress);
        Assert.Equal(credits - 200 + 500, Meter(SasMeterCode.CurrentCredits));
        Assert.Equal([GameEnded], Drain());
    }

    [Fact]
    public void A_second_game_or_a_play_cannot_start_while_one_runs()
    {
        Start();
        Machine.StartGame(1);

        Assert.Throws<InvalidOperationException>(() => Machine.StartGame(1));
        Assert.Throws<InvalidOperationException>(() => Machine.PlayGames(1, 1));
        Machine.EndGame(0);
        Assert.Throws<InvalidOperationException>(() => Machine.EndGame(0));
    }

    [Fact]
    public void A_legacy_bonus_sent_during_a_game_is_paid_after_its_end()
    {
        // QSP escrows while eGameInProgress holds (SasNetwork_AftTransfer.hpp:1669) and pays at game end (SasNetwork_Conditions.hpp:697).
        Start();
        Machine.StartGame(1);
        Drain();
        long credits = Meter(SasMeterCode.CurrentCredits);

        Award(3, 0x01);
        Assert.Equal(credits, Meter(SasMeterCode.CurrentCredits));
        Assert.DoesNotContain(LegacyBonusAwarded, Drain());

        Machine.EndGame(0);
        Assert.Equal(credits + 3, Meter(SasMeterCode.CurrentCredits));
        Assert.Equal([GameEnded, LegacyBonusAwarded], Drain());
    }

    [Fact]
    public void A_lock_asked_for_during_a_game_is_pending_and_taken_at_its_end()
    {
        // QSP answers GLS_GAME_LOCK_PENDING while a game runs (DecideAftLockAnswer) and locks once it ends.
        Start();
        Machine.StartGame(1);
        Drain();

        Assert.Equal(0x40, Lock("000F9999").Number("game_lock_status"));
        Assert.DoesNotContain(GameLocked, Drain());

        Machine.EndGame(0);
        Assert.Equal(0x00, Lock("FF000000").Number("game_lock_status"));
        Assert.Contains(GameLocked, Drain());
    }

    [Fact]
    public void A_lock_cancelled_during_a_game_is_not_taken_at_its_end()
    {
        Start();
        Machine.StartGame(1);
        Assert.Equal(0x40, Lock("000F9999").Number("game_lock_status"));
        Assert.Equal(0xFF, Lock("80000000").Number("game_lock_status"));

        Machine.EndGame(0);
        Assert.Equal(0xFF, Lock("FF000000").Number("game_lock_status"));
    }

    [Fact]
    public void A_cash_out_that_lasts_holds_a_bonus_until_its_ticket_is_printed()
    {
        // eCashTicketOut interrupts play (QSP ConditionDefaults.h:65), so the escrow waits for it.
        Start();
        Machine.CashoutLasting(_wallet, TimeSpan.FromMilliseconds(600));
        Assert.True(Machine.CashoutInProgress);

        Award(3, 0x01);
        Assert.Equal(1000, Meter(SasMeterCode.CurrentCredits));

        Assert.True(Wait(() => !Machine.CashoutInProgress), "the cash out never finished");
        Assert.True(Wait(() => Meter(SasMeterCode.CurrentCredits) == 3), "the bonus was not paid after the cash out");
        Assert.Equal(1000, Meter(SasMeterCode.TotalCashableTicketOut_Cents));
    }

    [Fact]
    public void A_game_win_above_the_jackpot_limit_is_a_hand_pay_in_accounting_units()
    {
        Start();
        Machine.SetJackpotLimit(400);
        long credits = Meter(SasMeterCode.CurrentCredits);

        Machine.PlayGames(1, 1, 5);

        Assert.Equal(credits - 100, Meter(SasMeterCode.CurrentCredits));
        Assert.Contains(HandpayPending, Drain());
        SasDecoded handpay = ReadAndReset();
        Assert.Equal(0, handpay.Number("level"));
        Assert.Equal(500, handpay.Number("amount"));
        Assert.Equal(500, Meter(SasMeterCode.TotalAttendantPaidPaytableWin_Crd));
        Assert.Equal(500, Meter(SasMeterCode.TotalJackpotCredits));
    }

    [Fact]
    public void A_win_at_the_limit_is_paid_by_the_machine()
    {
        // The market's test is strictly greater (QSP SasMarket.cpp:479).
        Start();
        Machine.SetJackpotLimit(500);

        Machine.PlayGames(1, 1, 5);

        Assert.DoesNotContain(HandpayPending, Drain());
        Assert.Equal(500, Meter(SasMeterCode.TotalMachinePaidPaytableWin_Crd));
    }

    [Fact]
    public void A_legacy_bonus_above_the_limit_is_an_external_bonus_hand_pay_reported_at_once()
    {
        // ProcessLegacyBonus hands BonusIn a hand pay and still reports the award (SasNetwork_AftTransfer.hpp:1708, :1753).
        Start();
        Machine.SetJackpotLimit(400);

        Award(500, 0x00);

        Assert.Equal(1000, Meter(SasMeterCode.CurrentCredits));
        List<byte> codes = Drain();
        Assert.Contains(HandpayPending, codes);
        Assert.Contains(LegacyBonusAwarded, codes);
        Assert.Equal(500, ReadAndReset().Number("amount"));
        Assert.Equal(500, Meter(SasMeterCode.TotalAttendantPaidExternalBonusWin_Crd));
        Assert.Equal(0, Meter(SasMeterCode.TotalMachinePaidExternalBonusWin_Crd));
    }

    [Fact]
    public void A_non_sas_progressive_above_the_limit_is_a_hand_pay_in_cents_with_group_zero_and_no_54()
    {
        // A level the machine runs is queued without $54 (libsas_wrapper.cpp:3867) and handed to sasHandpayStart with group 00.
        Start();
        Machine.SetJackpotLimit(400);

        Machine.WinProgressive([3], 1);

        List<byte> codes = Drain();
        Assert.Contains(HandpayPending, codes);
        Assert.DoesNotContain(ProgressiveWin, codes);
        SasDecoded handpay = ReadAndReset();
        Assert.Equal(0, handpay.Number("progressive_group"));
        Assert.Equal(3, handpay.Number("level"));
        Assert.Equal(1000, handpay.Number("amount"));
        Assert.Equal(1000, Meter(SasMeterCode.TotalAttendantPaidProgressiveWin_Crd));
        Assert.Equal(0, Meter(SasMeterCode.TotalMachinePaidProgressiveWin_Crd));
        Assert.Equal(0, Meter(SasMeterCode.TotalAttendantPaidPaytableWin_Crd));
    }

    [Fact]
    public void A_sas_progressive_above_the_limit_is_announced_and_paid_by_hand_with_its_group()
    {
        Start(sas: true);
        Machine.SetJackpotLimit(400);

        Machine.WinProgressive([1], 1);

        List<byte> codes = Drain();
        Assert.Contains(LevelHit, codes);
        Assert.Contains(HandpayPending, codes);
        Assert.DoesNotContain(ProgressiveWin, codes);
        SasDecoded handpay = ReadAndReset();
        Assert.Equal(1, handpay.Number("progressive_group"));
        Assert.Equal(1, handpay.Number("level"));
        Assert.Equal(500, handpay.Number("amount"));
    }

    [Fact]
    public void A_progressive_and_a_game_jackpot_in_one_game_are_one_hand_pay_in_cents()
    {
        Start();
        Machine.SetJackpotLimit(400);

        Machine.WinProgressive([3], 1, winCredits: 5);

        SasDecoded handpay = ReadAndReset();
        Assert.Equal(3, handpay.Number("level"));
        Assert.Equal(1500, handpay.Number("amount"));
        Assert.Equal(1000, Meter(SasMeterCode.TotalAttendantPaidProgressiveWin_Crd));
        Assert.Equal(500, Meter(SasMeterCode.TotalAttendantPaidPaytableWin_Crd));
    }
}
