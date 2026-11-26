using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Session;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// How a case says the machine is divided between hosts, how it moves between them, and how a
/// build with one channel refuses to pretend it has two.
/// </summary>
public class ChannelScriptTests
{
    private static ScriptTest ParseOne(string body)
    {
        ScriptDocument document = ScriptParser.Parse(
            $"test \"t\" {{\n{body}\n}}\n", "memory.sas");
        return Assert.Single(document.Tests);
    }

    [Fact]
    public void A_case_says_what_each_channel_answers_at_and_what_it_owns()
    {
        ScriptTest test = ParseOne("""
              machine channel 1 address 01
              machine channel 2 address 02 owns validation information
              host connect loopback address 01 channel 1
            """);

        Assert.NotNull(test.Channels);
        MachineChannelDeclaration channels = test.Channels;
        Assert.Equal(2, channels.Declared);
        Assert.Equal(SasAddress.Of(1), channels.Addresses[0]);
        Assert.Equal(SasAddress.Of(2), channels.Addresses[1]);
        Assert.Null(channels.Addresses[2]);
        Assert.Equal(SasChannel.Two, channels.Owners[SasCategory.Validation]);
        Assert.Equal(SasChannel.Two, channels.Owners[SasCategory.Information]);
        Assert.True(channels.TryResolve(out _, out string? refusal));
        Assert.Null(refusal);
    }

    /// <summary>
    /// A poll address of zero means the channel has none, which is how the machine stores it.
    /// </summary>
    /// <remarks>
    /// Allowed so that a script can write the configuration that has to be refused and see it
    /// refused. Rejecting it at the parse would leave the refusal untestable from a script.
    /// </remarks>
    [Fact]
    public void An_address_of_zero_says_the_channel_has_none_and_the_arrangement_is_refused()
    {
        ScriptTest test = ParseOne("""
              machine channel 1 address 01
              machine channel 2 address 00 owns validation
              host connect loopback address 01 channel 1
            """);

        Assert.NotNull(test.Channels);
        MachineChannelDeclaration channels = test.Channels;
        Assert.Equal(1, channels.Declared);
        Assert.False(channels.TryResolve(out _, out string? refusal));
        Assert.Contains("validation is on channel 2, which has no poll address", refusal);
    }

    [Fact]
    public void A_category_written_on_two_channels_stops_the_parse()
    {
        ScriptParseException error = Assert.Throws<ScriptParseException>(() => ParseOne("""
              machine channel 1 address 01 owns validation
              machine channel 2 address 02 owns validation
              host connect loopback address 01 channel 1
            """));

        Assert.Contains("already on channel 1", error.Reason);
        Assert.Contains("one channel number per category", error.Reason);
    }

    [Fact]
    public void A_channel_written_twice_stops_the_parse()
    {
        ScriptParseException error = Assert.Throws<ScriptParseException>(() => ParseOne("""
              machine channel 2 address 02
              machine channel 2 address 03
              host connect loopback address 01 channel 1
            """));

        Assert.Contains("already says what channel 2 is", error.Reason);
    }

    [Fact]
    public void A_channel_with_no_address_at_all_stops_the_parse()
    {
        ScriptParseException error = Assert.Throws<ScriptParseException>(() => ParseOne("""
              machine channel 2 owns validation
              host connect loopback address 01 channel 1
            """));

        Assert.Contains("needs 'address <hh>'", error.Reason);
        Assert.Contains("address 00", error.Reason);
    }

    [Fact]
    public void A_case_moves_the_host_to_another_channel()
    {
        ScriptTest test = ParseOne("""
              host connect loopback address 01 channel 1
              host channel 2
            """);

        UseChannelStep moved = Assert.Single(test.Steps.OfType<UseChannelStep>());
        Assert.Equal(SasChannel.Two, moved.Channel);
        Assert.Equal("use channel 2", moved.Describe());
    }

    [Fact]
    public void A_case_expects_a_command_to_be_met_with_silence()
    {
        ScriptTest test = ParseOne("""
              host connect loopback address 01 channel 1
              host expect no reply to lp 70
            """);

        ExpectNoReplyStep silence = Assert.Single(test.Steps.OfType<ExpectNoReplyStep>());
        Assert.Equal(0x70, silence.Command);
        Assert.Empty(silence.Data);
        Assert.Equal("expect no reply to lp 70", silence.Describe());
    }

    [Fact]
    public void An_unreadable_silence_expectation_says_what_it_wanted()
    {
        ScriptParseException error = Assert.Throws<ScriptParseException>(() => ParseOne("""
              host connect loopback address 01 channel 1
              host expect no reply lp 70
            """));

        Assert.Contains("host expect no reply to lp", error.Reason);
    }

    // ---- the hosts a case has open ------------------------------------------------------------

    [Fact]
    public void Moving_to_a_channel_with_no_host_is_refused_and_names_the_ones_there_are()
    {
        using var hosts = new ChannelHosts();
        using var bus = new LoopbackBus();
        hosts.Add(SasChannel.One, Engine(bus.Host, SasChannel.One), ClockBase.Session);

        Assert.False(hosts.TryUse(SasChannel.Two, out string? refusal));
        Assert.Contains("no host is connected on channel 2", refusal);
        Assert.Contains("this case has channel 1", refusal);
        Assert.Contains("host connect ... channel 2", refusal);

        // And the current channel is left where it was, so nothing runs on the wrong one.
        Assert.Equal(SasChannel.One, hosts.Current);
    }

    /// <summary>
    /// A channel that could not be opened blocks the rest of the case rather than letting it
    /// continue on the channel it was already on.
    /// </summary>
    /// <remarks>
    /// The alternative is the worst outcome available: the second half of a two-channel case runs
    /// against the first channel and passes.
    /// </remarks>
    [Fact]
    public void A_channel_that_could_not_be_reached_blocks_what_follows()
    {
        using var hosts = new ChannelHosts();
        Assert.Null(hosts.Blocked);

        hosts.Block("channel 2 has no poll address");
        Assert.Equal("channel 2 has no poll address", hosts.Blocked);

        // The first reason sticks: a later one would describe a step that never ran.
        hosts.Block("something else");
        Assert.Equal("channel 2 has no poll address", hosts.Blocked);
    }

    [Fact]
    public void The_wire_of_two_hosts_is_one_list_on_the_session_clock()
    {
        using var first = new LoopbackBus();
        using var second = new LoopbackBus();
        using var hosts = new ChannelHosts();

        PollEngine one = Engine(first.Host, SasChannel.One);
        PollEngine two = Engine(second.Host, SasChannel.Two);
        hosts.Add(SasChannel.One, one, new ClockBase(TimeSpan.FromSeconds(10)));
        hosts.Add(SasChannel.Two, two, new ClockBase(TimeSpan.FromSeconds(1)));

        one.Start();
        two.Start();
        Thread.Sleep(400);
        hosts.StopAll();

        IReadOnlyList<Diagnostics.WireRecord> wire = hosts.Wire();
        Assert.NotEmpty(wire);
        Assert.Contains(wire, record => record.Channel == SasChannel.One);
        Assert.Contains(wire, record => record.Channel == SasChannel.Two);

        // Ordered on the session's clock, not on either engine's own: the second host started
        // nine seconds earlier in session time and so comes first however its own clock reads.
        Assert.True(wire.Zip(wire.Skip(1)).All(pair => pair.First.Elapsed <= pair.Second.Elapsed));
        Assert.Equal(SasChannel.Two, wire[0].Channel);

        // Renumbered over the merged list. Two independent sequences interleaved would look to a
        // reader like a great many records had been dropped.
        Assert.Equal(Enumerable.Range(0, wire.Count).Select(i => (long)i), wire.Select(r => r.Sequence));
    }

    /// <summary>
    /// A serial port or a TCP endpoint carries one channel, so a case whose two channels both end
    /// up on one of them - the usual result of pointing a two-channel suite at a port with
    /// <c>--serial</c> - is skipped saying so. Before any port opens, because opening the second
    /// would only fail with access denied, and an error says less than the reason does. The names
    /// differ in case on purpose: COM9 and com9 are one port.
    /// </summary>
    [Theory]
    [InlineData("serial COM9", "serial com9", "serial port")]
    [InlineData("tcp 127.0.0.1:7200", "tcp 127.0.0.1:7200", "TCP endpoint")]
    public void Two_channels_on_one_link_skip_before_any_port_opens(
        string firstLink, string secondLink, string kind)
    {
        ScriptTest test = ParseOne($"""
              host connect {firstLink} address 01 channel 1
              host connect {secondLink} address 01 channel 2
            """);

        bool opened = false;
        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Environment = (_, _, _, _, _, _, _, _, _, _, _) =>
            {
                opened = true;
                throw new TransportRefusedException("the environment was asked for a link");
            },
        });

        TestOutcome outcome = runner.Run(test);

        Assert.Equal(Verdict.Skip, outcome.Verdict);
        Assert.False(opened);
        Assert.Contains($"channels 1 and 2 would both open {kind}", outcome.SkipReason);
    }

    /// <summary>
    /// The refusal is about two channels on one link and nothing wider: two channels on two ports
    /// reach the environment as before.
    /// </summary>
    [Fact]
    public void Two_channels_on_two_ports_are_not_refused_for_sharing()
    {
        ScriptTest test = ParseOne("""
              host connect serial COM9 address 01 channel 1
              host connect serial COM10 address 01 channel 2
            """);

        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Environment = (_, _, _, _, _, _, _, _, _, _, _) =>
                throw new TransportRefusedException("the environment was asked for a link"),
        });

        TestOutcome outcome = runner.Run(test);

        Assert.Equal(Verdict.Skip, outcome.Verdict);
        Assert.Equal("the environment was asked for a link", outcome.SkipReason);
    }

    private static PollEngine Engine(ISasTransport transport, SasChannel channel) =>
        new(transport, new PollEngineOptions
        {
            Address = SasAddress.Of(1),
            Channel = channel,
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromMilliseconds(20),
            LinkDownAfter = TimeSpan.FromSeconds(30),
        });
}
