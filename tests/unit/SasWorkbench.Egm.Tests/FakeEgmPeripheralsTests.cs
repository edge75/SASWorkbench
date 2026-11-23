using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The software machine's service button and its note acceptor and printer faults, against the
/// platform's condition table (QSP SasNetwork_Conditions.hpp:70 to :97): the library only queues
/// what the platform writes, so each code, each silence and each tilt here is the platform's.
/// Every case runs with the host in a sequence, so nothing reaches the line the test did not send.
/// </summary>
public sealed class FakeEgmPeripheralsTests : IDisposable
{
    private static readonly SasMessageLayout LockLayout = SasLayouts.For(0x74)!;

    private readonly LoopbackBus _bus = new();
    private FakeEgm? _machine;
    private PollEngine? _host;

    private FakeEgm Machine => _machine!;

    private PollEngine Host => _host!;

    private void Start()
    {
        _machine = new FakeEgm(_bus.Egm, new FakeEgmOptions());
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

    private byte Poll()
    {
        GeneralPollResult result = Host.PollGenerally();
        Assert.True(result.Answered, $"the general poll went unanswered: {result.Outcome}");
        return result.Code;
    }

    /// <summary>Drains everything queued, acknowledging each with a synchronisation in between, and returns the codes in order.</summary>
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

    /// <summary>Asks for a lock on every transfer condition and answers the game lock status.</summary>
    private long RequestLock()
    {
        LongPollResult result = Host.Execute(0x74, [0x00, 0x0F, 0x99, 0x99]);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(LockLayout, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return decoded.Number("game_lock_status")!.Value;
    }

    private void CancelLock() => Host.Execute(0x74, [0x80, 0x00, 0x00, 0x00]);

    [Fact]
    public void The_service_button_lights_the_change_lamp_and_the_next_press_puts_it_out()
    {
        // The demo toggles the service request on each press (GameManager.cpp:788), and the
        // platform raises $71 and $72 for it (SasNetwork_Conditions.hpp:87).
        Start();

        Machine.PressService();
        Assert.True(Machine.ServiceRequested);
        Machine.PressService();
        Assert.False(Machine.ServiceRequested);

        Assert.Equal(new byte[] { 0x71, 0x72 }, Drain());
    }

    [Fact]
    public void A_disconnected_printer_reports_power_off_once_and_power_on_when_it_is_back()
    {
        // ePrinterDisconnected maps to $75 and $76; a second report of an active condition is a
        // duplicate the platform drops (ConditionHandler.cpp:263).
        Start();

        Assert.Null(Machine.PrinterFault("disconnected"));
        Assert.Null(Machine.PrinterFault("disconnected"));
        Assert.Null(Machine.PrinterFault("cleared"));

        Assert.Equal(new byte[] { 0x75, 0x76 }, Drain());
        Assert.Empty(Machine.PeripheralConditions);
    }

    [Fact]
    public void Each_printer_fault_raises_the_code_the_platform_gives_it_and_clears_silently()
    {
        Start();

        Assert.Null(Machine.PrinterFault("jam"));
        Assert.Null(Machine.PrinterFault("paper_out"));
        Assert.Null(Machine.PrinterFault("paper_low"));
        Assert.Null(Machine.PrinterFault("cleared"));

        // Jam, paper out and paper low have no cleared code in the platform's table.
        Assert.Equal(new byte[] { 0x78, 0x61, 0x74 }, Drain());
    }

    [Fact]
    public void A_printer_tilt_takes_the_machine_out_of_play_and_low_paper_does_not()
    {
        // ConditionDefaults.h:77 and :78: paper out interrupts the game, paper low does not.
        Start();

        Assert.Null(Machine.PrinterFault("paper_low"));
        Assert.Equal(0x00, RequestLock());
        CancelLock();

        Assert.Null(Machine.PrinterFault("paper_out"));
        Assert.Equal(0xFF, RequestLock());

        Assert.Null(Machine.PrinterFault("cleared"));
        Assert.Equal(0x00, RequestLock());
        CancelLock();
    }

    [Fact]
    public void A_tilt_withdraws_a_lock_already_granted()
    {
        Start();

        Assert.Equal(0x00, RequestLock());
        Assert.Null(Machine.NoteFault("jam"));

        LongPollResult read = Host.Execute(0x74, [0xFF, 0x00, 0x00, 0x00]);
        Assert.Equal(0xFF, SasMessageDecoder.DecodeResponse(LockLayout, read.Frame).Number("game_lock_status"));
    }

    [Fact]
    public void Note_acceptor_faults_raise_their_codes_and_a_rejected_note_raises_only_2B()
    {
        // A returned note is reported by the platform directly and is not a condition
        // (SasNetwork_Meters.hpp:113); a counterfeit is a lockup raising $2C and never $2B too.
        Start();

        Assert.Null(Machine.NoteFault("rejected"));
        Assert.Empty(Machine.PeripheralConditions);
        Assert.Equal(0x00, RequestLock());
        CancelLock();

        Assert.Null(Machine.NoteFault("cashbox_full"));
        Assert.Null(Machine.NoteFault("jam"));
        Assert.Null(Machine.NoteFault("failure"));
        Assert.Null(Machine.NoteFault("counterfeit"));
        Assert.Equal(4, Machine.PeripheralConditions.Count);

        Assert.Equal(new byte[] { 0x2B, 0x27, 0x28, 0x29, 0x2C }, Drain());
    }

    [Fact]
    public void Settling_the_machine_clears_every_fault_with_the_codes_the_platform_has_for_clearing()
    {
        Start();

        Assert.Null(Machine.NoteFault("jam"));
        Assert.Null(Machine.PrinterFault("failure"));
        Machine.Settle();

        Assert.Empty(Machine.PeripheralConditions);
        Assert.Equal(new byte[] { 0x28, 0x75, 0x76 }, Drain());
    }

    [Fact]
    public void Faults_the_platform_has_no_report_for_are_refused_with_a_reason()
    {
        Start();

        Assert.Contains("pulled back", Machine.NoteFault("reversed"), StringComparison.Ordinal);
        Assert.Contains("ribbon", Machine.PrinterFault("ribbon"), StringComparison.Ordinal);
        Assert.Contains("no fault called", Machine.PrinterFault("smoke"), StringComparison.Ordinal);

        Assert.Empty(Drain());
    }
}
