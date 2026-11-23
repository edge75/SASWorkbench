using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The software machine's progressives, against the machine library's lp80, lp83 to lp87,
/// sasSetProgressiveLevelWin, internalDequeueProgressiveWins and internalProgressiveBroadcastTimeout,
/// with QSP's announcement, award and lockup. Every case runs with the host in a sequence, so nothing
/// reaches the line the test did not send.
/// </summary>
public sealed class FakeEgmProgressivesTests : IDisposable
{
    private const byte GameStarted = 0x7E;
    private const byte GameEnded = 0x7F;
    private const byte NoBroadcast = 0x53;
    private const byte ProgressiveWin = 0x54;
    private const byte LevelHit = 0x56;

    private readonly LoopbackBus _bus = new();
    private FakeEgm? _machine;
    private PollEngine? _host;

    private FakeEgm Machine => _machine!;

    private PollEngine Host => _host!;

    /// <summary>A machine in group 1 with SAS levels 1 and 2 and non-SAS level 3, unless told otherwise.</summary>
    private void Start(byte group = 1, uint sas = 0b011, uint nonSas = 0b100)
    {
        _machine = new FakeEgm(_bus.Egm, new FakeEgmOptions
        {
            ProgressiveGroup = group,
            SasProgressiveLevels = sas,
            NonSasProgressiveLevels = nonSas,
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
    }

    public void Dispose()
    {
        _host?.Dispose();
        _machine?.Dispose();
        _bus.Dispose();
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

    private LongPollResult Send80(byte group, byte level, long cents, SasAddress? to = null) =>
        Host.Execute(0x80, [group, level, .. Bcd(cents, 5)], to: to);

    /// <summary>Both SAS levels broadcast, so the machine is out of its lockup.</summary>
    private void BroadcastBoth(long first = 500, long second = 300)
    {
        Assert.Equal(LongPollOutcome.Acknowledged, Send80(1, 1, first).Outcome);
        Assert.Equal(LongPollOutcome.Acknowledged, Send80(1, 2, second).Outcome);
    }

    private SasDecoded Read(byte command, byte[]? data = null)
    {
        LongPollResult result = Host.Execute(command, data ?? []);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(command)!, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return decoded;
    }

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

    /// <summary>A note to stake the game with, its exception drained, then the game that wins.</summary>
    private void Win(params int[] levels)
    {
        Machine.InsertBill(1000);
        Drain();
        Machine.WinProgressive(levels, 1);
    }

    [Fact]
    public void A_single_level_for_its_own_group_is_acknowledged_and_another_group_or_level_zero_refused()
    {
        // lp.cpp:7878 refuses level 0, :7903 a group mismatch, both with a NACK at the own address.
        Start();

        Assert.Equal(LongPollOutcome.Acknowledged, Send80(1, 1, 500).Outcome);
        Assert.Equal(LongPollOutcome.Rejected, Send80(0, 1, 500).Outcome);
        Assert.Equal(LongPollOutcome.Rejected, Send80(2, 1, 500).Outcome);
        Assert.Equal(LongPollOutcome.Rejected, Send80(1, 0, 500).Outcome);
        Assert.Equal(LongPollOutcome.Silent, Send80(1, 1, 500, SasAddress.Sync).Outcome);
    }

    [Fact]
    public void Several_levels_for_another_group_are_met_with_silence_even_at_the_own_address()
    {
        // The NACK at lp.cpp:8395 is commented out (REET-13).
        Start();

        byte[] mine = [0x0D, 0x01, 0x01, .. Bcd(500, 5), 0x02, .. Bcd(300, 5)];
        byte[] theirs = [0x0D, 0x02, 0x01, .. Bcd(500, 5), 0x02, .. Bcd(300, 5)];
        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x86, mine).Outcome);
        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x86, theirs).Outcome);
    }

    [Fact]
    public void A_level_still_waiting_for_its_broadcast_cannot_be_won()
    {
        // QSP locks the machine up while a linked level waits for a broadcast.
        Start();
        Assert.Equal(LongPollOutcome.Acknowledged, Send80(1, 1, 500).Outcome);

        Machine.InsertBill(1000);
        Assert.Throws<InvalidOperationException>(() => Machine.WinProgressive([1], 1));
    }

    [Fact]
    public void A_sas_win_is_announced_paid_and_read_once_from_the_queue()
    {
        Start();
        BroadcastBoth();
        Drain();

        Win(1);

        // Game start, the level hit at the win, the payment at the award, game end (QSP SendHitOnly then $54); the broadcasts may lapse meanwhile.
        Assert.Equal([GameStarted, LevelHit, ProgressiveWin, GameEnded], Drain().Where(code => code != NoBroadcast));
        Assert.Equal(500, Machine.Meters.Read(SasMeterCode.TotalMachinePaidProgressiveWin_Crd));
        Assert.Equal(500, Machine.Meters.Read(SasMeterCode.TotalCoinOutCredits));
        // One game credit is a dollar by default, so the one-credit stake is a hundred one-cent units.
        Assert.Equal(1000 - 100 + 500, Machine.Meters.Read(SasMeterCode.CurrentCredits));

        SasDecoded last = Read(0x84);
        Assert.Equal(1, last.Number("progressive_group"));
        Assert.Equal(1, last.Number("level"));
        Assert.Equal(500, last.Number("amount"));

        SasDecoded oldest = Read(0x85);
        Assert.Equal(1, oldest.Number("progressive_group"));
        Assert.Equal(500, oldest.Number("amount"));

        // A repeat is the same reading; anything else acknowledges it and dequeues the win.
        Assert.Equal(500, Read(0x85).Number("amount"));
        Host.Synchronise();
        Assert.Equal(0, Read(0x85).Number("level"));

        // $84 is not consumed (lp.cpp:8120).
        Assert.Equal(500, Read(0x84).Number("amount"));
        Assert.Equal(500, Read(0x83, [0x00, 0x00]).Number("cumulative_progressive_win"));
    }

    [Fact]
    public void A_non_sas_win_raises_54_alone_and_reports_group_zero_with_its_level()
    {
        // No $56 for group zero (libsas_wrapper.cpp:3874); $84 gives the hit level (lp.cpp:8128).
        Start(group: 0, sas: 0);
        Drain();

        Win(3);

        Assert.Equal([GameStarted, ProgressiveWin, GameEnded], Drain());
        SasDecoded last = Read(0x84);
        Assert.Equal(0, last.Number("progressive_group"));
        Assert.Equal(3, last.Number("level"));
        Assert.Equal(1000, last.Number("amount"));
        Assert.Equal(0, Read(0x85).Number("level"));
    }

    [Fact]
    public void Reading_one_of_two_wins_raises_56_again_for_the_other()
    {
        // libsas_pending.cpp:368 reissues $56 as soon as a win is left behind.
        Start();
        BroadcastBoth();

        Win(1, 2);
        Drain();

        Assert.Equal(1, Read(0x85).Number("level"));
        Assert.Contains(LevelHit, Drain());
        Assert.Equal(2, Read(0x85).Number("level"));
        Assert.DoesNotContain(LevelHit, Drain());
    }

    [Fact]
    public void Every_win_comes_back_at_once_and_the_queue_empties()
    {
        Start();
        BroadcastBoth(1500, 1000);

        Win(1, 2);
        Drain();

        SasDecoded all = Read(0x87);
        Assert.Equal(0x0E, all.Number("length"));
        Assert.Equal(2, all.Number("count"));
        Assert.Equal(1500, all.Number("levels[0].amount"));
        Assert.Equal(1000, all.Number("levels[1].amount"));

        Host.Synchronise();
        Assert.Equal(0, Read(0x87).Number("count"));
        Assert.Equal(0, Read(0x85).Number("level"));
    }

    [Fact]
    public void A_lost_broadcast_is_reported_once_and_not_every_five_seconds()
    {
        // internalProgressiveBroadcastTimeout latches the report until a broadcast arrives again.
        Start();
        BroadcastBoth();
        Drain();

        Thread.Sleep(TimeSpan.FromSeconds(5.5));
        Assert.Equal([NoBroadcast], Drain());

        Thread.Sleep(TimeSpan.FromSeconds(5.5));
        Assert.Empty(Drain());
    }

    [Fact]
    public void The_machine_publishes_its_group_its_sas_levels_and_the_several_wins_bit()
    {
        Start(sas: 0b1111, nonSas: 0);

        Assert.Equal(1, Read(0x1F).Number("progressive_group"));
        SasDecoded game = Read(0xB5, [0x00, 0x00]);
        Assert.Equal(1, game.Number("progressive_group"));
        Assert.Equal(0x0F, game.Number("progressive_levels"));

        // QSP sets the multiple SAS progressive win bit (SasNetwork.cpp:306).
        Assert.Equal(0x02, Read(0xA0, [0x00, 0x00]).Number("feature_codes_3"));
    }

    [Fact]
    public void A_power_cycle_keeps_the_queue_and_a_memory_clear_empties_it()
    {
        Start();
        BroadcastBoth();
        Win(1);
        Drain();

        Machine.PowerCycle();
        Assert.Contains(LevelHit, Drain());
        Assert.Equal(1, Read(0x85).Number("level"));

        Machine.ClearMemory();
        Host.Synchronise();
        Assert.Equal(0, Read(0x85).Number("level"));
        Assert.Equal(0, Read(0x84).Number("level"));
    }
}
