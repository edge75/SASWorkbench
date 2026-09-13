using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// One software machine serving two hosts, and what each of them is allowed to see.
/// </summary>
/// <remarks>
/// Both ends here are our own code, so a pass says the host and the machine agree about the rule
/// and not that a real machine implements it. What makes the rule worth having anyway is that it
/// is generated out of the gaming machine library rather than written here: the gate on a command,
/// the filter on an exception and the mask on the features poll all come from the library's own
/// source, so this fixture behaves the way the thing under test behaves, or the generator is what
/// is wrong.
/// </remarks>
public class TwoChannelMachineTests
{
    private static readonly SasAddress First = SasAddress.Of(1);
    private static readonly SasAddress Second = SasAddress.Of(2);

    /// <summary>A machine with validation on channel two, and a host on each channel.</summary>
    private sealed class Split : IDisposable
    {
        private readonly LoopbackBus _one = new();
        private readonly LoopbackBus _two = new();

        public Split()
        {
            Assert.True(SasChannelPlan.TryCreate(
                [First, Second, null, null],
                new Dictionary<SasCategory, SasChannel> { [SasCategory.Validation] = SasChannel.Two },
                out SasChannelPlan plan,
                out _));

            Machine = new FakeEgm(_one.Egm, new FakeEgmOptions { Address = First, Channels = plan });
            Machine.Attach(_two.Egm, SasChannel.Two);
            Machine.Start();

            HostOne = Engine(_one.Host, First, SasChannel.One);
            HostTwo = Engine(_two.Host, Second, SasChannel.Two);
            HostOne.Start();
            HostTwo.Start();
        }

        public FakeEgm Machine { get; }

        public PollEngine HostOne { get; }

        public PollEngine HostTwo { get; }

        public void Dispose()
        {
            HostOne.Dispose();
            HostTwo.Dispose();
            Machine.Dispose();
            _one.Dispose();
            _two.Dispose();
        }

        private static PollEngine Engine(ISasTransport transport, SasAddress address, SasChannel channel) =>
            new(transport, new PollEngineOptions
            {
                Address = address,
                Channel = channel,
                PollInterval = TimeSpan.FromMilliseconds(20),
                ResponseTimeout = TimeSpan.FromMilliseconds(200),
                LinkDownAfter = TimeSpan.FromSeconds(30),
            });
    }

    [Fact]
    public void One_machine_answers_both_hosts_at_their_own_addresses()
    {
        using var split = new Split();

        Assert.Equal([SasChannel.One, SasChannel.Two], split.Machine.OpenChannels);

        LongPollResult one = split.HostOne.Execute(0x1F);
        LongPollResult two = split.HostTwo.Execute(0x1F);

        Assert.True(one.Succeeded, one.ToString());
        Assert.True(two.Succeeded, two.ToString());

        // The address byte differs because each channel answers at its own; everything after it is
        // the same machine describing itself.
        Assert.Equal(First.Value, one.Frame[0]);
        Assert.Equal(Second.Value, two.Frame[0]);
        Assert.Equal(one.Frame[1..^2], two.Frame[1..^2]);
    }

    [Fact]
    public void A_command_in_a_category_this_channel_lacks_is_met_with_silence_not_a_refusal()
    {
        using var split = new Split();

        LongPollResult refused = split.HostOne.Execute(0x70);
        Assert.Equal(LongPollOutcome.Silent, refused.Outcome);

        LongPollResult answered = split.HostTwo.Execute(0x70);
        Assert.True(answered.Succeeded, answered.ToString());
    }

    /// <summary>
    /// The finding, on a wire: the cashout ticket poll is gated on nothing, so both hosts read it.
    /// </summary>
    [Fact]
    public void A_command_in_no_category_is_answered_on_both_channels()
    {
        using var split = new Split();

        Assert.True(split.HostOne.Execute(0x3D).Succeeded);
        Assert.True(split.HostTwo.Execute(0x3D).Succeeded);
    }

    /// <summary>
    /// Both hosts are told about a printed ticket, and each reads its own copy of it.
    /// </summary>
    /// <remarks>
    /// The watchers are attached before the exception is raised, because the hosts are already
    /// polling: reading the machine's queue depth afterwards measured whichever host got there
    /// first and said nothing about the other.
    /// </remarks>
    [Fact]
    public void An_exception_in_no_category_reaches_both_channels_and_each_reads_its_own_copy()
    {
        using var split = new Split();
        using var onOne = new Listener(split.HostOne);
        using var onTwo = new Listener(split.HostTwo);

        split.Machine.RaiseException(SasExceptionCode.CashoutTicketPrinted);

        Assert.True(onOne.Waits((byte)SasExceptionCode.CashoutTicketPrinted));
        Assert.True(onTwo.Waits((byte)SasExceptionCode.CashoutTicketPrinted));
    }

    [Fact]
    public void An_exception_in_a_category_reaches_only_the_channel_that_owns_it()
    {
        using var split = new Split();
        using var onOne = new Listener(split.HostOne);
        using var onTwo = new Listener(split.HostTwo);

        split.Machine.RaiseException(SasExceptionCode.TicketInserted);

        Assert.True(onTwo.Waits((byte)SasExceptionCode.TicketInserted));

        // The other host is never told, so the machine holds nothing for it either.
        Assert.False(onOne.Waits((byte)SasExceptionCode.TicketInserted));
        Assert.Equal(0, split.Machine.PendingExceptionsOn(SasChannel.One));
    }

    [Fact]
    public void The_features_reply_narrows_to_what_the_answering_channel_owns()
    {
        using var split = new Split();
        var options = new FakeEgmOptions { CashoutValidation = SasCashoutValidation.SecureEnhanced };

        using var bus = new LoopbackBus();
        Assert.True(SasChannelPlan.TryCreate(
            [First, Second, null, null],
            new Dictionary<SasCategory, SasChannel> { [SasCategory.Validation] = SasChannel.Two },
            out SasChannelPlan plan,
            out _));

        using var machine = new FakeEgm(bus.Egm, options with { Address = First, Channels = plan });
        using var second = new LoopbackBus();
        machine.Attach(second.Egm, SasChannel.Two);
        machine.Start();

        using PollEngine one = Host(bus.Host, First, SasChannel.One);
        using PollEngine two = Host(second.Host, Second, SasChannel.Two);
        one.Start();
        two.Start();

        //  The machine performs secure enhanced validation, so the channel that owns validation
        //  reports the style and the redemption bit; the channel that does not must report neither.
        SasDecoded onTwo = Features(two);
        Assert.Equal((long)SasCashoutValidation.SecureEnhanced, onTwo.Number("validation_style"));
        Assert.Equal(1, onTwo.Number("ticket_redemption"));

        SasDecoded onOne = Features(one);
        Assert.Equal(0, onOne.Number("validation_style"));
        Assert.Equal(0, onOne.Number("ticket_redemption"));

        static PollEngine Host(ISasTransport transport, SasAddress address, SasChannel channel) =>
            new(transport, new PollEngineOptions
            {
                Address = address,
                Channel = channel,
                PollInterval = TimeSpan.FromMilliseconds(20),
                ResponseTimeout = TimeSpan.FromMilliseconds(200),
                LinkDownAfter = TimeSpan.FromSeconds(30),
            });

        static SasDecoded Features(PollEngine host)
        {
            LongPollResult reply = host.Execute(0xA0, [0x00, 0x00]);
            Assert.True(reply.Succeeded, reply.ToString());
            return SasMessageDecoder.DecodeResponse(SasLayouts.For(0xA0)!, reply.Frame);
        }
    }

    [Fact]
    public void A_channel_the_arrangement_gives_no_address_cannot_be_attached()
    {
        using var bus = new LoopbackBus();
        using var machine = new FakeEgm(bus.Egm, new FakeEgmOptions { Address = First });
        using var second = new LoopbackBus();

        TransportRefusedException refused = Assert.Throws<TransportRefusedException>(
            () => machine.Attach(second.Egm, SasChannel.Two));

        Assert.Contains("channel 2 has no poll address", refused.Message);
        Assert.Contains("not a channel this machine opens", refused.Message);
    }

    [Fact]
    public void A_channel_already_open_cannot_be_attached_twice()
    {
        using var bus = new LoopbackBus();
        using var machine = new FakeEgm(bus.Egm, new FakeEgmOptions { Address = First });
        using var again = new LoopbackBus();

        TransportRefusedException refused = Assert.Throws<TransportRefusedException>(
            () => machine.Attach(again.Egm, SasChannel.One));

        Assert.Contains("already open", refused.Message);
    }

    [Fact]
    public void A_machine_given_an_arrangement_no_machine_could_hold_carries_the_refusal()
    {
        using var bus = new LoopbackBus();
        Assert.False(SasChannelPlan.TryCreate(
            [First, null, null, null],
            new Dictionary<SasCategory, SasChannel> { [SasCategory.Validation] = SasChannel.Two },
            out _,
            out string? refusal));

        using var machine = new FakeEgm(
            bus.Egm, new FakeEgmOptions { Address = First, ChannelRefusal = refusal });

        // Built on its one channel, with the refusal on the record rather than repaired in silence.
        Assert.Equal(1, machine.Channels.OpenCount);
        Assert.Contains("no poll address", machine.ChannelRefusal);
    }

    /// <summary>
    /// Asserting the silence of a command a bare poll cannot reach is refused, with the reason.
    /// </summary>
    /// <remarks>
    /// The machine's receiver frames <c>$4D</c> at five bytes and drops a shorter one as
    /// incomplete before any handler sees it, so its silence is the framing. A case asserting it
    /// would read as the channel gate working and prove nothing — which is what two cases written
    /// for this wave did until the framing lengths were checked.
    /// </remarks>
    [Fact]
    public void Asserting_a_silence_that_would_only_be_the_framing_is_refused()
    {
        ScriptDocument document = ScriptParser.Parse(
            "test \"t\" {\n"
            + "  host connect loopback address 01\n"
            + "  host poll every 20ms\n"
            + "  host expect no reply to lp 4D\n"
            + "  host expect no reply to lp 70\n"
            + "}\n",
            "silence.sas");

        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = SoftwareMachineEnvironment.For(),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(5),
        });

        TestOutcome outcome = runner.Run(document.Tests[0]);

        StepResult refused = outcome.Steps.Single(
            step => step.Description.Contains("lp 4D", StringComparison.Ordinal));
        Assert.Equal(Verdict.Fail, refused.Verdict);
        Assert.Contains("drops a short $4D as incomplete", refused.Reason);
        Assert.Contains("Give the command its data", refused.Reason);

        //  The one a bare poll does reach is asserted for real, and on this machine — one channel
        //  owning everything — it answers. So the step fails, and it fails for the other reason:
        //  the machine replied. Telling those two failures apart is the whole point of the guard.
        StepResult answered = outcome.Steps.Single(
            step => step.Description.Contains("lp 70", StringComparison.Ordinal));
        Assert.Equal(Verdict.Fail, answered.Verdict);
        Assert.Contains("Answered", answered.Actual);
        Assert.DoesNotContain("incomplete", answered.Actual);
    }

    /// <summary>Collects the exceptions one host is told about, from before they are raised.</summary>
    private sealed class Listener : IDisposable
    {
        private readonly PollEngine _host;
        private readonly Lock _gate = new();
        private readonly List<byte> _seen = [];

        public Listener(PollEngine host)
        {
            _host = host;
            _host.ExceptionReported += Watch;
        }

        /// <summary>Whether that code arrived within a second.</summary>
        public bool Waits(byte code)
        {
            long deadline = Environment.TickCount64 + 1000;
            while (Environment.TickCount64 < deadline)
            {
                lock (_gate)
                {
                    if (_seen.Contains(code))
                        return true;
                }

                Thread.Sleep(10);
            }

            return false;
        }

        public void Dispose() => _host.ExceptionReported -= Watch;

        private void Watch(ExceptionReport report)
        {
            lock (_gate)
                _seen.Add(report.Code);
        }
    }
}
