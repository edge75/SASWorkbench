using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The software machine's line: the loop-break chirp (libsas_threads.cpp:2410, :2548), a poll whose
/// address byte has no wakeup bit (libsas_uart.cpp:488), the poll address the operator sets (QSP
/// SasNetwork.cpp:331, :753), and $7F as lp7F_ReceiveDateAndTime takes it. The host runs in a
/// sequence so nothing reaches the line the test did not send, and the loop break comes after one
/// second rather than five so the tests stay short.
/// </summary>
public sealed class FakeEgmLineTests : IDisposable
{
    private const byte OperatorChangedOptions = 0x3C;

    private static readonly TimeSpan LoopBreak = TimeSpan.FromSeconds(1);
    private static readonly SasMessageLayout ClockLayout = SasLayouts.For(0x7E)!;

    private readonly LoopbackBus _bus = new();
    private FakeEgm? _machine;
    private PollEngine? _host;

    private FakeEgm Machine => _machine!;

    private PollEngine Host => _host!;

    private void Start()
    {
        _machine = new FakeEgm(_bus.Egm, new FakeEgmOptions { LinkLossAfter = LoopBreak });
        _machine.Start();
        _host = new PollEngine(_bus.Host, new PollEngineOptions
        {
            Address = SasAddress.Of(1),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromMilliseconds(300),
            QuietGap = TimeSpan.FromMilliseconds(15),
            LinkDownAfter = TimeSpan.FromSeconds(30),
        });
        _host.Start();
        Assert.True(_host.BeginSequence(), "the round did not hand the line over");
        Assert.Equal(LongPollOutcome.Answered, Host.Execute(0x1F).Outcome);
    }

    public void Dispose()
    {
        _host?.Dispose();
        _machine?.Dispose();
        _bus.Dispose();
    }

    private static bool IsChirp(HeardMessage message, byte address) =>
        message.Wakeup && message.Frame.Length == 1 && message.Frame[0] == address;

    private DateTime ReadClock()
    {
        LongPollResult result = Host.Execute(0x7E);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(ClockLayout, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        int Field(string name) => (int)decoded.Number(name)!.Value;
        return new DateTime(Field("year"), Field("month"), Field("day"), Field("hour"), Field("minute"), Field("second"));
    }

    [Fact]
    public void A_quiet_line_draws_the_address_with_the_wakeup_bit_every_200_ms()
    {
        Start();

        IReadOnlyList<HeardMessage> early = Host.Listen(TimeSpan.FromMilliseconds(700))!;
        Assert.Empty(early);

        IReadOnlyList<HeardMessage> heard = Host.Listen(TimeSpan.FromSeconds(2), m => m.Count >= 4)!;
        Assert.Equal(4, heard.Count);
        Assert.All(heard, m => Assert.True(IsChirp(m, 0x01), Convert.ToHexString(m.Frame)));
        for (int i = 1; i < heard.Count; i++)
        {
            double gap = (heard[i].At - heard[i - 1].At).TotalMilliseconds;
            Assert.InRange(gap, 120, 300);
        }
    }

    [Fact]
    public void A_poll_to_any_address_stops_the_chirp_and_it_starts_again_after_another_silence()
    {
        Start();
        Assert.NotEmpty(Host.Listen(TimeSpan.FromSeconds(2), m => m.Count >= 1)!);

        // A stranger's poll is heard by the machine and answered by nobody.
        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x1F, to: SasAddress.Of(0x7F)).Outcome);
        Assert.Empty(Host.Listen(TimeSpan.FromMilliseconds(400))!);
        Assert.NotEmpty(Host.Listen(TimeSpan.FromSeconds(1), m => m.Count >= 1)!);

        // Its own poll stops it too, and is not answered until a synchronisation poll.
        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x1F).Outcome);
        Assert.Empty(Host.Listen(TimeSpan.FromMilliseconds(400))!);
        Assert.NotEmpty(Host.Listen(TimeSpan.FromSeconds(1), m => m.Count >= 1)!);

        Host.Synchronise();
        Assert.Equal(LongPollOutcome.Answered, Host.Execute(0x1F).Outcome);
    }

    [Fact]
    public void A_poll_without_its_wakeup_bit_is_ignored_and_the_same_poll_with_it_is_answered()
    {
        Start();

        LongPollResult bare = Host.Execute(0x1F, withoutWakeup: true);
        Assert.Equal(LongPollOutcome.Silent, bare.Outcome);
        Assert.Equal(LongPollOutcome.Answered, Host.Execute(0x1F).Outcome);
    }

    [Fact]
    public void A_data_poll_to_address_00_is_answered_by_nobody()
    {
        Start();

        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x1F, to: SasAddress.Sync).Outcome);
    }

    [Fact]
    public void A_new_address_is_answered_after_a_synchronisation_poll_and_reports_the_change()
    {
        Start();

        Assert.Null(Machine.SetPollAddress(SasAddress.Of(0x26)));
        Assert.Equal(SasAddress.Of(0x26), Machine.Address);

        // Its own polls wait for a synchronisation poll at the new address.
        Assert.Null(Host.Readdress(SasAddress.Of(0x26)));
        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x1F).Outcome);
        Host.Synchronise();
        GeneralPollResult change = Host.PollGenerally();
        Assert.Equal(GeneralPollOutcome.Exception, change.Outcome);
        Assert.Equal(OperatorChangedOptions, change.Code);
        Assert.Equal(LongPollOutcome.Answered, Host.Execute(0x1F).Outcome);

        // The old address is nobody's now.
        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x1F, to: SasAddress.Of(1)).Outcome);
    }

    [Fact]
    public void Saving_the_same_address_restarts_nothing()
    {
        Start();

        Assert.Null(Machine.SetPollAddress(SasAddress.Of(1)));
        Assert.Equal(GeneralPollOutcome.NoActivity, Host.PollGenerally().Outcome);
        Assert.Equal(LongPollOutcome.Answered, Host.Execute(0x1F).Outcome);
    }

    [Fact]
    public void At_address_00_the_machine_hears_nothing_and_never_chirps()
    {
        Start();

        Assert.Null(Machine.SetPollAddress(SasAddress.Sync));
        Host.Synchronise();
        Assert.Equal(GeneralPollOutcome.Silent, Host.PollGenerally().Outcome);
        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x1F).Outcome);
        Assert.Empty(Host.Listen(TimeSpan.FromSeconds(2))!);
        Assert.Equal(0, Machine.Chirps);
    }

    [Fact]
    public void The_clock_set_by_7F_is_acknowledged_and_read_back_by_7E()
    {
        Start();

        LongPollResult set = Host.Execute(0x7F, [0x06, 0x15, 0x20, 0x31, 0x10, 0x30, 0x00]);
        Assert.Equal(LongPollOutcome.Acknowledged, set.Outcome);

        DateTime now = ReadClock();
        Assert.InRange(now, new DateTime(2031, 6, 15, 10, 30, 0), new DateTime(2031, 6, 15, 10, 30, 10));
    }

    [Fact]
    public void The_clock_sent_to_00_is_taken_without_an_answer()
    {
        Start();

        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x7F, [0x03, 0x01, 0x20, 0x30, 0x09, 0x15, 0x00], to: SasAddress.Sync).Outcome);
        Assert.Equal(new DateTime(2030, 3, 1), ReadClock().Date);
    }

    [Fact]
    public void A_field_out_of_range_is_refused_and_a_date_no_calendar_has_is_acknowledged_but_not_taken()
    {
        Start();
        DateTime before = Machine.Now;

        Assert.Equal(LongPollOutcome.Rejected, Host.Execute(0x7F, [0x13, 0x01, 0x20, 0x30, 0x09, 0x15, 0x00]).Outcome);
        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x7F, [0x02, 0x30, 0x20, 0x30, 0x09, 0x15, 0x00]).Outcome);

        Assert.InRange(Machine.Now - before, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }
}
