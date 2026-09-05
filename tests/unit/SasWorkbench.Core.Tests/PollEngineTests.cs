using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

public class PollEngineTests
{
    private static readonly SasAddress Address = SasAddress.Of(1);

    private static PollEngineOptions FastOptions() => new()
    {
        Address = Address,
        PollInterval = TimeSpan.FromMilliseconds(20),
        ResponseTimeout = TimeSpan.FromMilliseconds(200),
        QuietGap = TimeSpan.FromMilliseconds(15),
        LinkDownAfter = TimeSpan.FromMilliseconds(500),
    };

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

    /// <summary>
    /// The synchronisation poll is not optional. A machine that has just powered up, or that has
    /// seen a loop break, ignores every poll addressed to itself until it has seen a general poll
    /// of address zero — so an engine that skipped it would be answered by silence and would be
    /// right to be.
    /// </summary>
    [Fact]
    public void Sends_a_synchronisation_poll_before_polling_the_machine()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Start();

        using var engine = new PollEngine(bus.Host, FastOptions());
        engine.Start();

        Assert.True(WaitUntil(() => machine.Snapshot().Sync >= 2 && machine.Snapshot().General >= 2));
        Assert.Equal(0, machine.Snapshot().Misplaced);
    }

    [Fact]
    public void Reports_each_exception_a_general_poll_returns()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Exceptions.Enqueue(0x15);
        machine.Exceptions.Enqueue(0x11);
        machine.Exceptions.Enqueue(0x17);
        machine.Start();

        var reported = new List<byte>();
        using var engine = new PollEngine(bus.Host, FastOptions());
        engine.ExceptionReported += report =>
        {
            lock (reported)
                reported.Add(report.Code);
        };
        engine.Start();

        Assert.True(WaitUntil(() =>
        {
            lock (reported)
                return reported.Count >= 3;
        }));

        lock (reported)
            Assert.Equal<byte[]>([0x15, 0x11, 0x17], reported.Take(3).ToArray());
    }

    [Fact]
    public void An_empty_exception_queue_is_not_reported_as_an_exception()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Start();

        int reports = 0;
        using var engine = new PollEngine(bus.Host, FastOptions());
        engine.ExceptionReported += _ => Interlocked.Increment(ref reports);
        engine.Start();

        Assert.True(WaitUntil(() => machine.Snapshot().General >= 4));
        Assert.Equal(0, Volatile.Read(ref reports));
        Assert.Equal(LinkState.Up, engine.State);
    }

    [Fact]
    public void A_long_poll_answered_in_full_carries_its_payload_back()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        // The payload of the captured reply to long poll $1F, minus address, command and check.
        byte[] payload = Convert.FromHexString("5158303031" + "02" + "FF" + "00" + "0000"
                                               + "303031000000" + "39323130");
        machine.Replies[0x1F] = payload;
        machine.Start();

        using var engine = new PollEngine(bus.Host, FastOptions());
        engine.Start();

        LongPollResult result = engine.Execute(0x1F);

        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        Assert.True(result.Succeeded);
        Assert.Equal<byte[]>(payload, result.Payload.ToArray());

        // The whole frame, exactly as it was captured off the wire, check sequence included.
        Assert.Equal("011F5158303031" + "02" + "FF" + "00" + "0000" + "303031000000"
                     + "39323130" + "863F", result.ToHex());
    }

    [Fact]
    public void An_acknowledgement_is_not_mistaken_for_a_payload()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Acknowledge.Add(0x0E);
        machine.Start();

        using var engine = new PollEngine(bus.Host, FastOptions());
        engine.Start();

        LongPollResult result = engine.Execute(0x0E, [0x00]);

        Assert.Equal(LongPollOutcome.Acknowledged, result.Outcome);
        Assert.True(result.Succeeded);
        Assert.True(result.Payload.IsEmpty);
    }

    [Fact]
    public void A_rejection_is_reported_as_a_rejection_and_not_as_success()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Reject.Add(0x21);
        machine.Start();

        using var engine = new PollEngine(bus.Host, FastOptions());
        engine.Start();

        LongPollResult result = engine.Execute(0x21);

        Assert.Equal(LongPollOutcome.Rejected, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Detail);
    }

    [Fact]
    public void Silence_is_reported_as_silence()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Silent.Add(0x2F);
        machine.Start();

        using var engine = new PollEngine(bus.Host, FastOptions());
        engine.Start();

        LongPollResult result = engine.Execute(0x2F);

        Assert.Equal(LongPollOutcome.Silent, result.Outcome);
        Assert.Empty(result.Frame);
    }

    [Fact]
    public void A_bad_check_sequence_is_never_treated_as_an_answer()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Replies[0x1F] = [0x51, 0x58, 0x30];
        machine.Corrupt.Add(0x1F);
        machine.Start();

        using var engine = new PollEngine(bus.Host, FastOptions());
        engine.Start();

        LongPollResult result = engine.Execute(0x1F);

        Assert.Equal(LongPollOutcome.BadCheckSequence, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Equal(1, engine.Statistics.BadCheckSequences);
    }

    /// <summary>
    /// Only a host may set the wakeup bit. A machine that sets one is either broken or the host is
    /// hearing its own transmission, and both are faults worth failing a test over rather than
    /// quietly decoding the frame anyway.
    /// </summary>
    [Fact]
    public void A_wakeup_bit_from_the_machine_fails_the_poll()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Replies[0x1F] = [0x51, 0x58];
        machine.WithWakeupBit.Add(0x1F);
        machine.Start();

        using var engine = new PollEngine(bus.Host, FastOptions());
        engine.Start();

        LongPollResult result = engine.Execute(0x1F);

        Assert.Equal(LongPollOutcome.BadCheckSequence, result.Outcome);
        Assert.Contains("wakeup", result.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, engine.Statistics.WakeupBitsFromMachine);
    }

    [Fact]
    public void A_long_poll_sent_before_the_engine_starts_is_not_silently_dropped()
    {
        using var bus = new LoopbackBus();
        using var engine = new PollEngine(bus.Host, FastOptions());

        LongPollResult result = engine.Execute(0x1F);

        Assert.Equal(LongPollOutcome.NotSent, result.Outcome);
        Assert.NotNull(result.Detail);
    }

    [Fact]
    public void Every_message_is_recorded_in_both_directions()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Replies[0x1F] = [0x51, 0x58];
        machine.Start();

        using var engine = new PollEngine(bus.Host, FastOptions());
        engine.Start();
        engine.Execute(0x1F);

        Assert.True(WaitUntil(() => engine.WireLog.Snapshot().Count >= 4));
        IReadOnlyList<WireRecord> log = engine.WireLog.Snapshot();

        Assert.Contains(log, r => r.Direction == WireDirection.Transmit && r.ToHex() == "1.80");
        Assert.Contains(log, r => r.Direction == WireDirection.Transmit && r.ToHex() == "1.81");
        Assert.Contains(log, r => r.Direction == WireDirection.Transmit && r.ToHex() == "1.01 0.1F");
        Assert.Contains(log, r => r.Direction == WireDirection.Receive);
    }

    [Fact]
    public void The_link_is_reported_down_when_the_machine_stops_answering()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Start();

        using var engine = new PollEngine(bus.Host, FastOptions());
        engine.Start();

        Assert.True(WaitUntil(() => engine.State == LinkState.Up));

        machine.Dispose();

        Assert.True(WaitUntil(() => engine.State == LinkState.Down, 4000));
    }

    [Fact]
    public void Channel_one_is_the_default_and_is_carried_on_every_record()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Start();

        var options = FastOptions() with { Channel = SasChannel.Two };
        using var engine = new PollEngine(bus.Host, options);
        engine.Start();

        Assert.True(WaitUntil(() => engine.WireLog.Snapshot().Count >= 2));
        Assert.All(engine.WireLog.Snapshot(), r => Assert.Equal(SasChannel.Two, r.Channel));
    }

    [Fact]
    public void Statistics_count_what_the_link_actually_did()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Exceptions.Enqueue(0x11);
        machine.Acknowledge.Add(0x0E);
        machine.Start();

        using var engine = new PollEngine(bus.Host, FastOptions());
        engine.Start();
        engine.Execute(0x0E, [0x00]);

        Assert.True(WaitUntil(() => engine.Statistics.GeneralPolls >= 2));
        PollStatistics stats = engine.Statistics;

        Assert.True(stats.SyncPolls >= 2);
        Assert.Equal(1, stats.LongPolls);
        Assert.Equal(1, stats.Exceptions);
    }

    /// <summary>
    /// A reply that arrives after the host gave up waiting must not be handed to the poll that
    /// follows it. SAS ends a message with silence and nothing else, so a late frame cannot be told
    /// apart from the next one by looking at it: unless the line is resynchronised, every later
    /// reply is read one transaction behind for the rest of the session, which is why a single slow
    /// answer used to turn into a whole meter sweep of unreadable meters.
    /// </summary>
    /// <remarks>
    /// The lateness is staged rather than timed. A machine told to stay silent times out on the
    /// clock the engine already owns, and the overdue answer is then put on the line by hand, so
    /// what this proves does not depend on any thread being scheduled promptly. An earlier version
    /// of this test slept its way to the same place and failed roughly one run in ten under load —
    /// a timing test for a timing bug is not evidence of anything.
    /// </remarks>
    [Fact]
    public void A_reply_that_arrives_after_the_host_gave_up_is_not_handed_to_the_next_poll()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Silent.Add(0x1F);
        machine.Replies[0x2F] = [0x11, 0x22];
        machine.Start();

        using var engine = new PollEngine(bus.Host, SpacedOutOptions());
        engine.Start();
        Assert.True(WaitUntil(() => engine.State == LinkState.Up, 10000));

        Assert.Equal(LongPollOutcome.Silent, engine.Execute(0x1F).Outcome);
        PutOnTheLine(bus, LateAnswerTo(0x1F));

        LongPollResult next = engine.Execute(0x2F);

        Assert.True(
            next.Outcome == LongPollOutcome.Answered && next.Frame.Length > 1 && next.Frame[1] == 0x2F,
            $"long poll $2F came back {next.Outcome} as {next.ToHex()} - "
            + "the late reply to $1F was handed to it instead");
    }

    /// <summary>
    /// What the line is resynchronised of is counted and recorded. A host that swallowed the bytes
    /// quietly would look perfectly healthy while dropping a machine's answers on the floor.
    /// </summary>
    [Fact]
    public void Discarding_a_late_reply_is_counted_and_recorded_rather_than_done_quietly()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Silent.Add(0x1F);
        machine.Replies[0x2F] = [0x11, 0x22];
        machine.Start();

        using var engine = new PollEngine(bus.Host, SpacedOutOptions());
        engine.Start();
        Assert.True(WaitUntil(() => engine.State == LinkState.Up, 10000));

        engine.Execute(0x1F);
        byte[] late = LateAnswerTo(0x1F);
        PutOnTheLine(bus, late);
        engine.Execute(0x2F);

        Assert.True(engine.Statistics.SymbolsDiscarded >= late.Length,
            $"only {engine.Statistics.SymbolsDiscarded} of {late.Length} late symbols were counted");
        Assert.Contains(engine.WireLog.Snapshot(),
            record => record.Note is not null
                && record.Note.Contains("discard", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A command whose caller gave up waiting must not have its answer handed to the next caller.
    /// The poll thread can still be in the middle of a transaction when the wait expires, and
    /// without something to tell one transaction from another the reply that finally arrives is
    /// returned to whoever asks next — one command's answer reported under another's name, which
    /// is the same fault as the wire desync one layer up.
    /// </summary>
    [Fact]
    public void An_answer_to_an_abandoned_command_is_not_returned_to_the_next_caller()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Silent.Add(0x1F);
        machine.Replies[0x2F] = [0x11, 0x22];
        machine.Start();

        using var engine = new PollEngine(bus.Host, FastOptions() with
        {
            // Long enough that the poll thread is certainly still waiting on $1F when the caller
            // below stops waiting for it.
            ResponseTimeout = TimeSpan.FromSeconds(3),
            LinkDownAfter = TimeSpan.FromSeconds(30),
        });
        engine.Start();
        Assert.True(WaitUntil(() => engine.State == LinkState.Up, 10000));

        LongPollResult abandoned = engine.Execute(0x1F, [], TimeSpan.FromMilliseconds(200));
        Assert.Equal(LongPollOutcome.NotSent, abandoned.Outcome);

        LongPollResult next = engine.Execute(0x2F);

        Assert.Equal(0x2F, next.Command);
    }

    /// <summary>
    /// Polls far enough apart that no general poll falls between two consecutive long polls, so
    /// what the second one reads is decided by the engine rather than by a race with the clock.
    /// </summary>
    private static PollEngineOptions SpacedOutOptions() => FastOptions() with
    {
        ResponseTimeout = TimeSpan.FromMilliseconds(50),
        PollInterval = TimeSpan.FromSeconds(1),
    };

    /// <summary>A well-formed answer to <paramref name="command"/>, as a machine would have sent it.</summary>
    private static byte[] LateAnswerTo(byte command)
    {
        Span<byte> frame = stackalloc byte[HostMessage.MaxSymbols];
        frame[0] = Address.Value;
        frame[1] = command;
        frame[2] = 0x51;
        frame[3] = 0x58;
        return frame[..SasCrc.Append(frame, 4)].ToArray();
    }

    /// <summary>Puts bytes on the line from the machine's end, with no poll having asked for them.</summary>
    private static void PutOnTheLine(LoopbackBus bus, byte[] frame)
    {
        SasSymbol[] symbols = frame.Select(SasSymbol.Data8).ToArray();
        bus.Egm.Write(symbols);
    }

    [Fact]
    public void An_engine_refuses_a_link_that_cannot_carry_the_wakeup_bit()
    {
        using var transport = new EightBitTransport();

        TransportRefusedException refusal = Assert.Throws<TransportRefusedException>(
            () => new PollEngine(transport, FastOptions()));

        Assert.Contains("wakeup", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A link that has lost the ninth bit — the failure mode the design refuses.</summary>
    private sealed class EightBitTransport : ISasTransport
    {
        public string Description => "eight-bit stand-in";

        public TransportCapabilities Capabilities { get; } = new(
            TransportKind.Serial,
            CarriesWakeupBit: false,
            TransportFidelity.Unverified,
            "Eight data bits only.");

        public bool IsConnected => true;

        public void Open()
        {
        }

        public void Close()
        {
        }

        public void Write(ReadOnlySpan<SasSymbol> symbols)
        {
        }

        public int Read(Span<SasSymbol> destination, TimeSpan timeout) => 0;

        public void Dispose()
        {
        }
    }
}
