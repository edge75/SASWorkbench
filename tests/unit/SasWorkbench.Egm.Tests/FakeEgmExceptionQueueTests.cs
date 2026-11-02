using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The software machine's exception queue against the three rules chapters 3 and 4 test on a real
/// machine: the queue is sixty-four deep and loses its oldest with one $70, a repeated general poll
/// is a refusal and the third is final, and five seconds of silence is a loop break. Every case
/// runs in a sequence, so the host puts nothing on the line the test did not ask for.
/// </summary>
public sealed class FakeEgmExceptionQueueTests : IDisposable
{
    private static readonly SasAddress Address = SasAddress.Of(1);

    // The gaming machine library's depth, and exception buffer overflow.
    private const int QueueDepth = 64;
    private const byte Overflow = 0x70;

    private readonly LoopbackBus _bus = new();
    private FakeEgm? _machine;
    private PollEngine? _host;

    private FakeEgm Machine => _machine!;

    private PollEngine Host => _host!;

    private void Start(FakeEgmOptions? options = null)
    {
        _machine = new FakeEgm(_bus.Egm, options);
        _machine.Start();
        _host = new PollEngine(_bus.Host, new PollEngineOptions
        {
            Address = Address,
            PollInterval = TimeSpan.FromMilliseconds(20),
            // Long enough that a busy test machine is never mistaken for a silent one, short
            // enough that the one poll expected to go unanswered does not dominate the run.
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

    private byte Poll()
    {
        GeneralPollResult result = Host.PollGenerally();
        Assert.True(result.Answered, $"the general poll went unanswered: {result.Outcome}");
        return result.Code;
    }

    /// <summary>The identification poll: answered at once, and an acknowledgement like any other message.</summary>
    private void Acknowledge() => Assert.True(Host.Execute(0x1F).Frame.Length > 0, "the identification poll went unanswered");

    /// <summary>
    /// Sixty-six into a queue of sixty-four: the two oldest go, the overflow comes out first and
    /// only once, and then the sixty-four that fit, then no activity. The two lost ones carry
    /// codes nothing else uses, so seeing either would mean the wrong end of the queue was cut.
    /// </summary>
    [Fact]
    public void A_full_queue_loses_its_oldest_and_reports_the_overflow_once()
    {
        Start();
        Machine.RaiseException(0x20);
        Machine.RaiseException(0x21);
        for (int i = 0; i < QueueDepth; i++)
            Machine.RaiseException(0x11);

        Host.Synchronise();
        Assert.Equal(Overflow, Poll());
        for (int i = 0; i < QueueDepth; i++)
        {
            Acknowledge();
            Assert.Equal(0x11, Poll());
        }

        Acknowledge();
        Assert.Equal(0x00, Poll());
    }

    /// <summary>
    /// The same general poll twice is a refusal and draws the same exception; the third is final
    /// and acknowledges nothing, so the exception it refused is the next one sent after any other
    /// message. Only then does an acknowledgement move the queue on.
    /// </summary>
    [Fact]
    public void A_repeated_general_poll_is_a_refusal_and_the_third_is_final()
    {
        Start();
        Machine.RaiseException(0x11);
        Machine.RaiseException(0x12);

        Host.Synchronise();
        Assert.Equal(0x11, Poll());
        Assert.Equal(0x11, Poll());
        Assert.Equal(0x11, Poll());

        Host.Synchronise();
        Assert.Equal(0x11, Poll());
        Host.Synchronise();
        Assert.Equal(0x12, Poll());
        Host.Synchronise();
        Assert.Equal(0x00, Poll());
    }

    /// <summary>
    /// After a silence longer than the loop-break limit the machine ignores its own polls until a
    /// message to another address arrives, and the exception that was awaiting acknowledgement
    /// comes back first. The limit is shortened, but kept above the second a broadcast is listened to.
    /// </summary>
    [Fact]
    public void A_loop_break_refuses_the_last_exception_and_waits_for_synchronisation()
    {
        Start(new FakeEgmOptions { LinkLossAfter = TimeSpan.FromMilliseconds(1500) });
        Machine.RaiseException(0x11);
        Machine.RaiseException(0x12);

        Host.Synchronise();
        Assert.Equal(0x11, Poll());
        Thread.Sleep(2000);

        Assert.Equal(GeneralPollOutcome.Silent, Host.PollGenerally().Outcome);

        Host.Synchronise();
        Assert.Equal(0x11, Poll());
        Host.Synchronise();
        Assert.Equal(0x12, Poll());
    }
}
