using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Session;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The interactive session, which is what the window drives. It goes through the same engine,
/// layouts and decoder a script run does, so anything a person can do by hand is something a
/// test can express — and these tests are what keeps that true.
/// </summary>
public class HostSessionTests
{
    private static HostSessionSpec Spec() => new(
        ScriptTransport.Loopback,
        string.Empty,
        SasAddress.Of(1),
        SasChannel.One,
        TimeSpan.FromMilliseconds(20));

    private static bool WaitUntil(Func<bool> condition, int milliseconds = 3000)
    {
        long deadline = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(5);
        }

        return condition();
    }

    [Fact]
    public void A_session_comes_up_and_the_machine_answers()
    {
        using HostSession session = HostSession.Open(Spec(), SoftwareMachinePeer.For());

        Assert.True(WaitUntil(() => session.State == LinkState.Up));
        Assert.True(session.Statistics.SyncPolls > 0);
        Assert.True(session.Statistics.GeneralPolls > 0);
    }

    [Fact]
    public void A_long_poll_comes_back_decoded()
    {
        using HostSession session = HostSession.Open(Spec(), SoftwareMachinePeer.For());

        (LongPollResult result, SasDecoded? decoded) = session.SendAndDecode(0x1F);

        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        Assert.NotNull(decoded);
        Assert.Equal("QX", decoded.Text("game_id"));
    }

    /// <summary>
    /// A reply this build cannot decode comes back undecoded rather than half-guessed, and the
    /// caller can still see the bytes.
    /// </summary>
    [Fact]
    public void A_reply_with_no_layout_comes_back_undecoded_but_not_hidden()
    {
        using HostSession session = HostSession.Open(Spec(), SoftwareMachinePeer.For());

        (LongPollResult result, SasDecoded? decoded) = session.SendAndDecode(0x72, [0x01]);

        Assert.Null(decoded);
        Assert.Equal(LongPollOutcome.Rejected, result.Outcome);
        Assert.NotEmpty(result.Frame);
    }

    [Fact]
    public void Every_readable_meter_is_read_and_none_is_silently_left_out()
    {
        using HostSession session = HostSession.Open(Spec(), SoftwareMachinePeer.For());

        IReadOnlyList<MeterReading> readings = session.ReadMeters();

        Assert.Equal(MeterIndex.All.Count, readings.Count);
        Assert.All(readings, reading => Assert.True(
            reading.Value is not null || reading.Problem is not null,
            $"{reading.Name} came back with neither a value nor a reason"));
    }

    /// <summary>
    /// Every meter the index offers must actually be readable from this machine. A meter that is
    /// offered and then always fails is a modelling mistake, not an environment problem, and it is
    /// worth failing a test over rather than letting it turn into noise in the meters view.
    /// </summary>
    [Fact]
    public void Every_meter_the_index_offers_can_actually_be_read()
    {
        using HostSession session = HostSession.Open(Spec(), SoftwareMachinePeer.For());

        MeterReading[] unreadable = session.ReadMeters().Where(r => r.Value is null).ToArray();

        Assert.True(unreadable.Length == 0,
            "these meters are offered but cannot be read: "
            + string.Join(", ", unreadable.Select(r => $"{r.Name} ({r.Problem})")));
    }

    [Fact]
    public void Stimulus_applied_to_the_machine_shows_up_in_the_meters()
    {
        FakeEgm? machine = null;
        using HostSession session = HostSession.Open(
            Spec(), SoftwareMachinePeer.For(started: m => machine = m));

        Assert.NotNull(machine);
        machine.InsertBill(2000);

        MeterReading value = session.ReadMeters(["total_bill_in"]).Single();

        Assert.Equal("total_bill_value", value.Name);
        Assert.Equal(20, value.Value);
    }

    [Fact]
    public void Exceptions_reach_a_watcher()
    {
        FakeEgm? machine = null;
        using HostSession session = HostSession.Open(
            Spec(), SoftwareMachinePeer.For(started: m => machine = m));

        var seen = new List<byte>();
        session.ExceptionReported += report =>
        {
            lock (seen)
                seen.Add(report.Code);
        };

        Assert.NotNull(machine);
        machine.OpenSlotDoor();

        Assert.True(WaitUntil(() =>
        {
            lock (seen)
                return seen.Contains((byte)SasExceptionCode.SlotDoorOpened);
        }));
    }

    [Fact]
    public void The_session_says_what_the_link_is_worth()
    {
        using HostSession session = HostSession.Open(Spec(), SoftwareMachinePeer.For());

        TransportCapabilities caps = session.Capabilities;

        Assert.Equal(TransportKind.Loopback, caps.Kind);
        Assert.True(caps.CarriesWakeupBit);
        Assert.NotNull(caps.Caveat);
    }

    /// <summary>
    /// A loopback link with nobody on the far end is refused rather than opened. An empty link
    /// that polls forever and never hears anything looks exactly like a broken machine.
    /// </summary>
    [Fact]
    public void A_loopback_link_with_no_peer_is_refused()
    {
        TransportRefusedException refused =
            Assert.Throws<TransportRefusedException>(() => HostSession.Open(Spec()));

        Assert.Contains("peer", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_endpoint_that_is_not_an_endpoint_is_refused()
    {
        var spec = new HostSessionSpec(ScriptTransport.Tcp, "not-an-endpoint", SasAddress.Of(1));

        Assert.Throws<TransportRefusedException>(() => HostSession.Open(spec));
    }

    [Fact]
    public void The_wire_log_records_both_directions()
    {
        using HostSession session = HostSession.Open(Spec(), SoftwareMachinePeer.For());
        session.Send(0x1F);

        Assert.True(WaitUntil(() => session.WireLog.Snapshot().Count >= 4));
        Assert.Contains(session.WireLog.Snapshot(), r => r.ToHex() == "1.01 0.1F");
    }
}

public class MeterModellingTests
{
    /// <summary>
    /// A field measured in credits is not automatically a meter. The amount owed on a pending
    /// handpay is a status that only exists while one is outstanding, and offering it as a meter
    /// gives a test a reading that fails for reasons unrelated to what it is checking.
    /// </summary>
    [Theory]
    [InlineData("amount")]
    [InlineData("partial_pay")]
    [InlineData("session_game_win")]
    [InlineData("session_game_pay")]
    [InlineData("progressive_group")]
    public void A_handpay_status_field_is_not_offered_as_a_meter(string field)
    {
        Assert.False(MeterIndex.TryResolve(field, out _), $"'{field}' should not be a meter");
    }

    [Theory]
    [InlineData("month")]
    [InlineData("hour")]
    [InlineData("max_bet")]
    [InlineData("denomination")]
    public void A_configuration_or_clock_field_is_not_offered_as_a_meter(string field)
    {
        Assert.False(MeterIndex.TryResolve(field, out _), $"'{field}' should not be a meter");
    }

    [Theory]
    [InlineData("total_coin_in")]
    [InlineData("games_played")]
    [InlineData("current_credits")]
    [InlineData("bills_20")]
    [InlineData("total_bill_value")]
    public void A_real_meter_is_offered(string field)
    {
        Assert.True(MeterIndex.TryResolve(field, out _), $"'{field}' should be a meter");
    }

    /// <summary>
    /// The hopper level is the case that made the rule explicit. It reads like a meter and is
    /// counted in coins, but the library keeps no meter code for it — it reads the figure from the
    /// device — so it is machine state, asserted on as a field of long poll $2C.
    /// </summary>
    [Fact]
    public void The_hopper_level_is_machine_state_and_not_a_meter()
    {
        Assert.False(MeterIndex.TryResolve("hopper_level", out _));

        SasMessageLayout layout = SasLayouts.For(0x2C)!;
        Assert.Equal("hopper_level", layout.Response.Single().Name);
        Assert.False(layout.Response.Single().IsMeter);
    }

    [Fact]
    public void Every_offered_meter_is_marked_as_one_in_its_layout()
    {
        foreach (MeterSource source in MeterIndex.All)
        {
            SasMessageLayout layout = SasLayouts.For(source.Command)!;
            SasField field = layout.Response.Single(f => f.Name == source.Field);
            Assert.True(field.IsMeter, $"{source.Name} is offered but its field is not a meter");
        }
    }
}
