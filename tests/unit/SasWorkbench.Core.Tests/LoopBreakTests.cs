using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// A machine that receives no address byte for five seconds enters a loop break and chirps its own
/// address every 200 ms until it is synchronised again; it does the same as it starts up. On the
/// board, a sequenced case sent nothing while a person answered a prompt, the machine chirped, and
/// the case then read the chirps as a machine answering a broadcast and its first general poll was
/// ignored. These tests pin the host's three answers to that: a chirp is named as a chirp, the
/// line is kept alive while a person acts, and a restarted machine is brought back into step
/// before the case polls it.
/// </summary>
public class LoopBreakTests
{
    private static readonly SasAddress Address = SasAddress.Of(1);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static PollEngineOptions Options() => new()
    {
        Address = Address,
        PollInterval = TimeSpan.FromMilliseconds(20),
        ResponseTimeout = TimeSpan.FromMilliseconds(150),
        QuietGap = TimeSpan.FromMilliseconds(15),
        LinkDownAfter = TimeSpan.FromSeconds(30),
    };

    [Fact]
    public void A_chirp_after_a_broadcast_is_named_as_a_chirp_and_not_as_an_answer()
    {
        using var bus = new LoopbackBus();
        using var chirper = new Chirper(bus.Egm, Address.Value, stopsWhenSynchronised: false);
        using var engine = new PollEngine(bus.Host, Options());
        engine.Start();
        engine.BeginSequence();

        GeneralPollResult broadcast = engine.Synchronise();

        Assert.Equal(GeneralPollOutcome.Broadcast, broadcast.Outcome);
        Assert.True(broadcast.IsLoopBreakChirp);
        Assert.Contains("loop break", broadcast.Detail, StringComparison.Ordinal);
    }

    /// <summary>Anything but the machine's own address after a broadcast is still a finding.</summary>
    [Fact]
    public void A_broadcast_answered_with_anything_else_is_still_a_finding()
    {
        using var bus = new LoopbackBus();
        using var engine = new PollEngine(bus.Host, Options());
        engine.Start();
        engine.BeginSequence();

        using var answerer = new Chirper(bus.Egm, 0x42, stopsWhenSynchronised: false);
        GeneralPollResult broadcast = engine.Synchronise();

        Assert.Equal(GeneralPollOutcome.Unreadable, broadcast.Outcome);
        Assert.False(broadcast.IsLoopBreakChirp);
    }

    [Fact]
    public void A_restarted_machine_is_synchronised_until_it_stops_chirping()
    {
        using var bus = new LoopbackBus();
        using var chirper = new Chirper(bus.Egm, Address.Value, stopsWhenSynchronised: true);
        using var engine = new PollEngine(bus.Host, Options());
        engine.Start();
        engine.BeginSequence();

        MachineResynchronisation? resync = MachineResynchronisation.Run(
            engine, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50));

        Assert.NotNull(resync);
        Assert.False(resync.Failed, resync.Summary);
        Assert.Contains("resynchronised", resync.Summary, StringComparison.Ordinal);
        Assert.True(chirper.Stopped);
    }

    [Fact]
    public void A_machine_that_never_stops_chirping_fails_the_step_rather_than_hanging()
    {
        using var bus = new LoopbackBus();
        using var chirper = new Chirper(bus.Egm, Address.Value, stopsWhenSynchronised: false);
        using var engine = new PollEngine(bus.Host, Options());
        engine.Start();
        engine.BeginSequence();

        MachineResynchronisation? resync = MachineResynchronisation.Run(
            engine, TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(50));

        Assert.NotNull(resync);
        Assert.True(resync.Failed);
        Assert.Contains("still chirping", resync.Summary, StringComparison.Ordinal);
    }

    /// <summary>Outside a sequence the round already keeps the machine in step, so nothing is added.</summary>
    [Fact]
    public void Neither_helper_touches_a_host_polling_on_its_own_timer()
    {
        using var bus = new LoopbackBus();
        using var engine = new PollEngine(bus.Host, Options());
        engine.Start();

        Assert.Null(MachineResynchronisation.Run(engine));
        Assert.Null(LinkKeepAlive.Start(engine));
    }

    /// <summary>
    /// A sync poll is a poll to another address, which the machine library takes as an implied ACK.
    /// On the board (GSA 3.22, R39) keep-alive syncs sent while the person was asked to cut the power
    /// acknowledged the exception the case was checking would survive the restart. A machine about
    /// to lose power needs no keep-alive, so those prompts get none.
    /// </summary>
    [Fact]
    public void A_restart_prompt_is_not_kept_alive_because_a_sync_would_acknowledge_the_queue()
    {
        Assert.False(LinkKeepAlive.Suits(new PowerCycleStep(SourceLocation.None)));
        Assert.False(LinkKeepAlive.Suits(new MemoryFaultStep(SourceLocation.None, MemoryFaultKind.CmosLost)));
        Assert.True(LinkKeepAlive.Suits(new DoorStep(SourceLocation.None, "main", true)));
    }

    [Fact]
    public void The_line_is_kept_alive_while_a_person_acts_and_left_alone_after()
    {
        using var bus = new LoopbackBus();
        using var engine = new PollEngine(bus.Host, Options());
        engine.Start();
        engine.BeginSequence();
        long before = engine.Statistics.SyncPolls;

        // Each sync waits about 200 ms for a reply nobody sends, so 1.5 s holds about five of them.
        using (LinkKeepAlive.Start(engine, TimeSpan.FromMilliseconds(100)))
            Thread.Sleep(1500);

        long during = engine.Statistics.SyncPolls - before;
        Thread.Sleep(300);
        long after = engine.Statistics.SyncPolls - before;

        Assert.InRange(during, 2, 15);
        Assert.Equal(during, after);
    }

    /// <summary>A machine end that sends one address byte every 30 ms, like a machine in a loop break.</summary>
    private sealed class Chirper : IDisposable
    {
        private readonly ISasTransport _end;
        private readonly byte _value;
        private readonly bool _stopsWhenSynchronised;
        private readonly CancellationTokenSource _done = new();
        private readonly Thread _thread;
        private volatile bool _stopped;

        public Chirper(ISasTransport end, byte value, bool stopsWhenSynchronised)
        {
            _end = end;
            _value = value;
            _stopsWhenSynchronised = stopsWhenSynchronised;
            _thread = new Thread(Run) { IsBackground = true };
            _thread.Start();
        }

        public bool Stopped => _stopped;

        public void Dispose()
        {
            _done.Cancel();
            _thread.Join();
            _done.Dispose();
        }

        private void Run()
        {
            var heard = new SasSymbol[64];
            long stopAt = long.MaxValue;
            while (!_done.IsCancellationRequested && !Cancel.IsCancellationRequested)
            {
                int read = _end.Read(heard, TimeSpan.FromMilliseconds(30));
                for (int i = 0; i < read; i++)
                {
                    if (_stopsWhenSynchronised && heard[i].Wakeup && heard[i].Data == 0x80
                        && stopAt == long.MaxValue)
                    {
                        // The machine stops on its next timer tick, not at once.
                        stopAt = Environment.TickCount64 + 100;
                    }
                }

                if (Environment.TickCount64 >= stopAt)
                {
                    _stopped = true;
                    continue;
                }

                _end.Write([SasSymbol.Data8(_value)]);
            }
        }
    }
}
