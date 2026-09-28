using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

public class PollEngineTests
{
    private static readonly SasAddress Address = SasAddress.Of(1);

    /// <summary>
    /// A quick poll loop with a patient host. The patience is deliberately far longer than any
    /// machine here takes to answer: these tests are about what the engine does with a reply, and
    /// a machine whose thread is not scheduled for a while is the one thing four test assemblies
    /// running at once reliably produce. The two tests that are about a clock set their own.
    /// </summary>
    private static PollEngineOptions FastOptions() => new()
    {
        Address = Address,
        PollInterval = TimeSpan.FromMilliseconds(20),
        ResponseTimeout = TimeSpan.FromSeconds(2),
        QuietGap = TimeSpan.FromMilliseconds(15),
        LinkDownAfter = TimeSpan.FromSeconds(5),
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

        // Waiting on a count would be waiting on the wrong thing: a long poll submitted this early
        // can be served before the first general poll, so four records can arrive without one.
        static bool Sent(IReadOnlyList<WireRecord> log, string hex) => log.Any(
            r => r.Direction == WireDirection.Transmit && r.ToHex() == hex);

        Assert.True(WaitUntil(() =>
        {
            IReadOnlyList<WireRecord> so_far = engine.WireLog.Snapshot();
            return Sent(so_far, "1.80") && Sent(so_far, "1.81") && Sent(so_far, "1.01 0.1F")
                   && so_far.Any(r => r.Direction == WireDirection.Receive);
        }));
    }

    [Fact]
    public void The_link_is_reported_down_when_the_machine_stops_answering()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Start();

        // This one is about the clock, so it keeps a short one of its own.
        using var engine = new PollEngine(bus.Host, FastOptions() with
        {
            LinkDownAfter = TimeSpan.FromMilliseconds(500),
        });
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

        Assert.Equal(LongPollOutcome.Silent,
            engine.Execute(0x1F, [], LongEnoughToBeReached).Outcome);
        PutOnTheLine(bus, AnswerTo(0x1F));

        LongPollResult next = engine.Execute(0x2F, [], LongEnoughToBeReached);

        Assert.True(
            next.Outcome == LongPollOutcome.Answered && next.Frame.Length > 1 && next.Frame[1] == 0x2F,
            $"long poll $2F came back {next.Outcome} as {next.ToHex()} - "
            + "the late reply to $1F was handed to it instead"
            // The counters and the tail of the wire: what went wrong on a link is not something a
            // reader can reconstruct from the name of an outcome.
            + $"\n{engine.Statistics}\n"
            + string.Join('\n', engine.WireLog.Snapshot().TakeLast(20)));
    }

    /// <summary>
    /// Draining the line before a poll goes out cannot catch an answer that arrives after the
    /// drain, and a reply echoes the command it answers - so a reply echoing a different one is
    /// not this poll's answer, whatever else is true of it. Without that rule the drain is the
    /// only defence and the drain has a window it cannot close, which is how one command's answer
    /// used to be reported under another command's name.
    /// </summary>
    /// <remarks>
    /// Staged rather than timed, at both ends: the line hands back prepared messages one per read
    /// with a silence after each, so what this proves depends on the order the bytes arrive in and
    /// not on any thread being scheduled promptly.
    /// </remarks>
    [Fact]
    public void A_reply_echoing_another_command_is_not_this_polls_answer()
    {
        using var bus = new LoopbackBus();
        var line = new ScriptedLine(bus.Host, 0x2F, AnswerTo(0x1F));

        using var engine = new PollEngine(line, SpacedOutOptions());
        engine.Start();

        LongPollResult next = engine.Execute(0x2F, [], LongEnoughToBeReached);

        Assert.Equal(LongPollOutcome.Silent, next.Outcome);
        Assert.Contains("discarded", next.Detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Discarding the message that cannot be this poll's answer is not the end of the transaction:
    /// the machine still has the rest of its time to answer, and the answer it then gives is the
    /// one the caller asked for. A host that gave up at the first foreign byte would turn one late
    /// reply into a silence for every poll behind it.
    /// </summary>
    [Fact]
    public void The_answer_that_follows_a_discarded_message_is_still_read()
    {
        using var bus = new LoopbackBus();
        var line = new ScriptedLine(bus.Host, 0x2F, AnswerTo(0x1F), AnswerTo(0x2F));

        using var engine = new PollEngine(line, SpacedOutOptions());
        engine.Start();

        LongPollResult next = engine.Execute(0x2F, [], LongEnoughToBeReached);

        Assert.Equal(LongPollOutcome.Answered, next.Outcome);
        Assert.Equal(0x2F, next.Frame[1]);
        Assert.True(engine.Statistics.SymbolsDiscarded >= AnswerTo(0x1F).Length,
            $"only {engine.Statistics.SymbolsDiscarded} discarded symbols were counted");
    }

    /// <summary>
    /// A machine reporting real-time events answers a long poll with a queued event rather than
    /// with the poll's own reply, and the event must survive that. A host reading the second byte
    /// as an echoed command cannot see one at all: the message is counted as an answer to some
    /// earlier poll, discarded, and the long poll reported silent — so the event is lost, not
    /// misreported, which is the one failure this project exists to refuse.
    /// </summary>
    /// <remarks>
    /// Staged the way the one-behind tests stage lateness: the line hands back one prepared
    /// message with a silence after it, so what this proves is the order the bytes arrive in and
    /// not any thread being scheduled promptly. The poll is deliberately not a success — the
    /// caller never got the reply it asked for — and deliberately not retried, because repeating
    /// the same poll is an implied NACK and would only fetch the same event again.
    /// </remarks>
    [Fact]
    public void An_event_answering_a_long_poll_is_kept_and_the_poll_is_not_a_success()
    {
        using var bus = new LoopbackBus();
        var line = new ScriptedLine(bus.Host, 0x1F, EventInsteadOfReply(0x7E, [0x01, 0x00]));

        var reported = new List<ExceptionReport>();
        using var engine = new PollEngine(line, SpacedOutOptions());
        engine.ExceptionReported += reported.Add;
        engine.Start();

        LongPollResult result = engine.Execute(0x1F, [], LongEnoughToBeReached);

        Assert.Equal(LongPollOutcome.AnsweredWithEvent, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Contains("drain", result.Detail, StringComparison.OrdinalIgnoreCase);

        ExceptionReport kept = Assert.Single(reported);
        Assert.Equal(0x7E, kept.Code);
        Assert.Equal<byte[]>([0x01, 0x00], kept.Data);
        Assert.True(kept.IsRealTimeEvent);
        Assert.Equal(Address, kept.Address);
    }

    /// <summary>
    /// The sequenced mode exists because what is on the line between two messages is not
    /// incidental - it is the protocol. A machine learns the host heard its last reply from the
    /// next poll it sees, so a host running a round on a timer and prefixing a broadcast to every
    /// message cannot present the sequence an implied-acknowledgement case describes: section 3.19
    /// makes that broadcast an acknowledgement in its own right.
    /// </summary>
    [Fact]
    public void In_a_sequence_the_line_carries_only_what_was_named()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Replies[0x1F] = [0x11, 0x22];
        machine.Start();

        using var engine = new PollEngine(bus.Host, FastOptions());
        engine.BeginSequence();
        engine.Start();

        // Long enough that a host polling on a timer would have sent a dozen of each.
        Thread.Sleep(300);
        Assert.Equal(0, engine.Statistics.GeneralPolls);
        Assert.Equal(0, engine.Statistics.SyncPolls);
        Assert.Equal(0, engine.Statistics.LongPolls);

        Assert.Equal(GeneralPollOutcome.NoActivity, engine.PollGenerally().Outcome);
        Assert.Equal(LongPollOutcome.Answered, engine.Execute(0x1F).Outcome);

        PollStatistics stats = engine.Statistics;
        Assert.Equal(1, stats.GeneralPolls);
        Assert.Equal(1, stats.LongPolls);

        // The one that matters: no synchronisation poll went out on its own, ahead of the long
        // poll or anywhere else.
        Assert.Equal(0, stats.SyncPolls);
        Assert.Equal([0x1F], machine.CommandsSeen);
    }

    /// <summary>
    /// A general poll a case names draws one exception and says which, in the order the machine
    /// queued them. Watching the exception stream instead would say the same codes arrived and not
    /// which poll drew each, which is exactly what these cases are about.
    /// </summary>
    [Fact]
    public void A_named_general_poll_says_what_that_one_poll_drew()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Exceptions.Enqueue(0x11);
        machine.Exceptions.Enqueue(0x12);
        machine.Start();

        using var engine = new PollEngine(bus.Host, FastOptions());
        engine.BeginSequence();
        engine.Start();

        GeneralPollResult first = engine.PollGenerally();
        GeneralPollResult second = engine.PollGenerally();
        GeneralPollResult third = engine.PollGenerally();

        Assert.Equal(GeneralPollOutcome.Exception, first.Outcome);
        Assert.Equal(0x11, first.Code);
        Assert.Equal(GeneralPollOutcome.Exception, second.Outcome);
        Assert.Equal(0x12, second.Code);

        // Not a silence: the machine answered and said it has nothing, which is a statement.
        Assert.Equal(GeneralPollOutcome.NoActivity, third.Outcome);
        Assert.Equal(Address, third.Address);
    }

    /// <summary>
    /// The broadcast to address 00 is the message section 3.19 turns on, so a case has to be able
    /// to place one itself. No machine answers it, and one that did would be a finding - so the
    /// line is listened to rather than assumed quiet. Outside a sequence the engine owns this
    /// message, and a second one placed by hand is refused rather than silently doubled.
    /// </summary>
    [Fact]
    public void A_broadcast_is_placed_by_hand_only_inside_a_sequence_and_no_machine_answers_it()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Start();

        using var engine = new PollEngine(bus.Host, FastOptions());
        engine.Start();

        GeneralPollResult refused = engine.Synchronise();
        Assert.Equal(GeneralPollOutcome.NotSent, refused.Outcome);
        Assert.Contains("sequence", refused.Detail, StringComparison.OrdinalIgnoreCase);

        engine.BeginSequence();
        GeneralPollResult broadcast = engine.Synchronise();

        Assert.Equal(GeneralPollOutcome.Broadcast, broadcast.Outcome);
        Assert.False(broadcast.Answered);
        Assert.True(engine.Statistics.SyncPolls >= 1);
    }

    /// <summary>
    /// The mode is given back, so a case can establish communication the ordinary way, drive an
    /// exact sequence, and then let the round resume.
    /// </summary>
    [Fact]
    public void Ending_a_sequence_gives_the_line_back_to_the_timer()
    {
        using var bus = new LoopbackBus();
        using var machine = new ScriptedMachine(bus.Egm, Address);
        machine.Start();

        using var engine = new PollEngine(bus.Host, FastOptions());
        engine.BeginSequence();
        engine.Start();
        Thread.Sleep(200);
        Assert.Equal(0, engine.Statistics.GeneralPolls);

        engine.EndSequence();
        Assert.True(WaitUntil(() => engine.Statistics.GeneralPolls >= 3));
        Assert.True(engine.Statistics.SyncPolls >= 1);
    }

    /// <summary>
    /// A busy response is the one reply to a long poll that echoes no command: the machine's
    /// address and a zero, with no check sequence. Read off the gaming machine library's own
    /// sender rather than reasoned about. Treating it as a message about some other poll would
    /// throw the machine's answer away and report a silence instead.
    /// </summary>
    [Fact]
    public void A_busy_response_is_the_machines_answer_and_not_a_message_about_another_poll()
    {
        using var bus = new LoopbackBus();
        var line = new ScriptedLine(bus.Host, 0x2F, [Address.Value, 0x00]);

        using var engine = new PollEngine(line, SpacedOutOptions());
        engine.Start();

        LongPollResult next = engine.Execute(0x2F, [], LongEnoughToBeReached);

        Assert.Equal(LongPollOutcome.Answered, next.Outcome);
        Assert.Equal([Address.Value, 0x00], next.Frame);
    }

    /// <summary>
    /// The same fault runs the other way. A general poll is answered by a single byte, so an
    /// earlier long poll's reply landing in a general poll's window is not an exception - and
    /// reading its first byte as one invents a machine event that never happened, which is worse
    /// than an unreadable meter because somebody would go looking for the door that opened.
    /// </summary>
    [Fact]
    public void A_late_long_poll_reply_does_not_become_an_exception()
    {
        using var bus = new LoopbackBus();
        var line = new ScriptedLine(
            bus.Host, (byte)(Address.Value | HostMessage.GeneralPollFlag), AnswerTo(0x1F));

        using var engine = new PollEngine(line, FastOptions());
        var reported = new List<byte>();
        engine.ExceptionReported += report =>
        {
            lock (reported)
                reported.Add(report.Code);
        };
        engine.Start();

        Assert.True(WaitUntil(() => engine.Statistics.SymbolsDiscarded >= AnswerTo(0x1F).Length));

        lock (reported)
            Assert.Empty(reported);
    }

    /// <summary>
    /// A general poll answered in the real-time event format is longer than one byte and carries a
    /// fixed identifier where a long poll's reply carries its command. This build does not decode
    /// that format, so the honest answer is to name it: discarding it would drop a machine event,
    /// and reading its address byte as an exception code would invent one.
    /// </summary>
    [Fact]
    public void A_general_poll_answered_in_the_real_time_event_format_is_named_rather_than_guessed()
    {
        using var bus = new LoopbackBus();
        byte[] realTimeEvent = [Address.Value, HostMessage.RealTimeEventIdentifier, 0x11, 0x00, 0x00];
        var line = new ScriptedLine(
            bus.Host, (byte)(Address.Value | HostMessage.GeneralPollFlag), realTimeEvent);

        using var engine = new PollEngine(line, FastOptions());
        var reported = new List<byte>();
        engine.ExceptionReported += report =>
        {
            lock (reported)
                reported.Add(report.Code);
        };
        engine.Start();

        Assert.True(WaitUntil(() => engine.WireLog.Snapshot().Any(record =>
            record.Note?.Contains("real-time event", StringComparison.OrdinalIgnoreCase) == true)));

        lock (reported)
            Assert.Empty(reported);
        Assert.Equal(0, engine.Statistics.SymbolsDiscarded);
    }

    /// <summary>
    /// A line that hands back prepared messages, one per read, with a silence after each - which
    /// is how a machine's replies actually reach a host, and what makes an arrival order testable
    /// without staging it on a clock. The messages are queued when a chosen poll is transmitted,
    /// so they land after the line has been resynchronised: the one window draining cannot close.
    /// </summary>
    private sealed class ScriptedLine(ISasTransport inner, byte trigger, params byte[][] messages)
        : ISasTransport
    {
        private readonly Queue<byte[]> _queued = new();
        private bool _silenceNext;

        public string Description => inner.Description;

        public TransportCapabilities Capabilities => inner.Capabilities;

        public bool IsConnected => inner.IsConnected;

        public void Open() => inner.Open();

        public void Close() => inner.Close();

        public void Write(ReadOnlySpan<SasSymbol> symbols)
        {
            bool matches = symbols.Length >= 2
                ? symbols[1].Data == trigger
                : symbols.Length == 1 && symbols[0].Data == trigger;

            inner.Write(symbols);
            if (!matches)
                return;

            lock (_queued)
            {
                foreach (byte[] message in messages)
                    _queued.Enqueue(message);
            }
        }

        public int Read(Span<SasSymbol> destination, TimeSpan timeout)
        {
            byte[] message;
            lock (_queued)
            {
                // Silence is SAS's only frame delimiter, so one has to follow each message or the
                // next one is read as its continuation.
                if (_silenceNext)
                {
                    _silenceNext = false;
                    return 0;
                }

                if (_queued.Count == 0)
                    return inner.Read(destination, timeout);

                message = _queued.Dequeue();
                _silenceNext = true;
            }

            for (int i = 0; i < message.Length; i++)
                destination[i] = SasSymbol.Data8(message[i]);
            return message.Length;
        }

        public void Dispose() => inner.Dispose();
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

        engine.Execute(0x1F, [], LongEnoughToBeReached);
        byte[] late = AnswerTo(0x1F);
        PutOnTheLine(bus, late);
        engine.Execute(0x2F, [], LongEnoughToBeReached);

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
        PollInterval = TimeSpan.FromSeconds(1),
    };

    /// <summary>
    /// How long the tests below wait for the poll thread to reach their command. Spaced-out polling
    /// and a patient host together make the wait a caller gets by default too tight to rely on —
    /// and how long a caller waits is not what any of these tests is about, so they say.
    /// </summary>
    private static readonly TimeSpan LongEnoughToBeReached = TimeSpan.FromSeconds(10);

    /// <summary>A well-formed answer to <paramref name="command"/>, as a machine would have sent it.</summary>
    private static byte[] AnswerTo(byte command)
    {
        Span<byte> frame = stackalloc byte[HostMessage.MaxSymbols];
        frame[0] = Address.Value;
        frame[1] = command;
        frame[2] = 0x51;
        frame[3] = 0x58;
        return frame[..SasCrc.Append(frame, 4)].ToArray();
    }

    /// <summary>The real-time event format, as a machine sends it in a long poll's window.</summary>
    private static byte[] EventInsteadOfReply(byte code, byte[] carried)
    {
        Span<byte> frame = stackalloc byte[HostMessage.MaxSymbols];
        frame[0] = Address.Value;
        frame[1] = SasEventLayouts.Marker;
        frame[2] = code;
        carried.CopyTo(frame[SasEventLayouts.HeaderLength..]);
        return frame[..SasCrc.Append(frame, SasEventLayouts.HeaderLength + carried.Length)].ToArray();
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
