using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The software machine across a power cycle, a memory fault and a memory clear. What survives is what
/// the library keeps in its image (libsas_db.h:855) and what the platform keeps in its own memory; the
/// power and memory exceptions are all the platform's (QSP SasNetwork.cpp:159, :1219). Every case runs
/// with the host in a sequence, so nothing reaches the line the test did not send.
/// </summary>
public sealed class FakeEgmLifecycleTests : IDisposable
{
    private static readonly SasMessageLayout LockLayout = SasLayouts.For(0x74)!;

    private readonly LoopbackBus _bus = new();
    private FakeEgm? _machine;
    private PollEngine? _host;

    private FakeEgm Machine => _machine!;

    private PollEngine Host => _host!;

    private void Start(FakeEgmOptions? options = null)
    {
        _machine = new FakeEgm(_bus.Egm, (options ?? new FakeEgmOptions()) with
        {
            PowerOffTime = TimeSpan.FromMilliseconds(50),
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

    private long LockStatus(byte[] request)
    {
        LongPollResult result = Host.Execute(0x74, request);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(LockLayout, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return decoded.Number("game_lock_status")!.Value;
    }

    [Fact]
    public void The_queue_survives_a_power_cycle_and_the_power_exceptions_follow_it()
    {
        // The queue is in the library's image (libsas_db.h:869); the platform raises $18 on every start and
        // $17 once started (QSP SasNetwork.cpp:159, SasNetwork_Conditions.hpp:477).
        Start();
        Machine.SetDoor("main", open: true);
        Machine.SetDoor("main", open: false);

        Machine.PowerCycle();

        Assert.Equal(new byte[] { 0x11, 0x12, 0x18, 0x17 }, Drain());
    }

    [Fact]
    public void An_exception_sent_and_not_acknowledged_goes_again_after_a_power_cycle()
    {
        // Every queued entry is marked unsent at power-up (libsas_internal.cpp:622).
        Start();
        Machine.SetDoor("main", open: true);
        Machine.SetDoor("main", open: false);
        Host.Synchronise();
        Assert.Equal(0x11, Poll());

        Machine.PowerCycle();

        Assert.Equal(new byte[] { 0x11, 0x12, 0x18, 0x17 }, Drain());
    }

    [Fact]
    public void A_machine_just_started_ignores_its_own_polls_until_it_is_synchronised()
    {
        // Every channel starts chirping and waits for a poll to another address (libsas_internal.cpp:313).
        Start();
        Machine.PowerCycle();

        Assert.False(Host.PollGenerally().Answered);
        Host.Synchronise();
        Assert.True(Host.PollGenerally().Answered);
    }

    [Fact]
    public void A_power_cycle_turns_real_time_reporting_off()
    {
        // The flag lives in the channel context, which a new process starts zeroed (libsas_internal.cpp:305).
        Start();
        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x0E, [0x01]).Outcome);
        Assert.True(Machine.RealTimeEventsEnabled);

        Machine.PowerCycle();

        Assert.False(Machine.RealTimeEventsEnabled);
    }

    [Fact]
    public void A_power_cycle_ends_a_transfer_lock_and_the_machine_can_be_locked_again()
    {
        // QSP removes the lock at power-up (SasNetwork_Conditions.hpp:547).
        Start();
        Assert.Equal(0x00, LockStatus([0x00, 0x0F, 0x99, 0x99]));

        Machine.PowerCycle();
        Host.Synchronise();

        Assert.Equal(0xFF, Machine.AftLockStatus);
        Assert.Equal(0xFF, LockStatus([0xFF, 0x00, 0x00, 0x00]));
        Assert.Equal(0x00, LockStatus([0x00, 0x0F, 0x99, 0x99]));
    }

    [Fact]
    public void A_hand_pay_still_unread_is_announced_again_at_once_after_a_power_cycle()
    {
        // libsas_internal.cpp:887 raises $51 again for a hand pay awaiting its reading.
        Start();
        Machine.RaiseHandpay(HandpayKind.Jackpot, 150_000);
        Assert.Equal(new byte[] { 0x51 }, Drain());

        Machine.PowerCycle();

        Assert.True(Machine.HandpayPending);
        Assert.Equal(new byte[] { 0x51, 0x18, 0x17 }, Drain());
    }

    [Fact]
    public void Games_since_power_up_start_again_and_every_other_meter_is_kept()
    {
        // The platform counts games since power-up in memory only (QSP ConditionHandler.cpp:81).
        Start();
        Machine.InsertBill(2000);
        Machine.PlayGames(3, 1);
        long played = Machine.Meters.Read(SasMeterCode.GamesPlayed);

        Machine.PowerCycle();

        Assert.Equal(0, Machine.Meters.Read(SasMeterCode.GamesSinceLastReset));
        Assert.Equal(played, Machine.Meters.Read(SasMeterCode.GamesPlayed));
        Assert.True(Machine.Meters.Read(SasMeterCode.CurrentCredits) > 0);
    }

    [Fact]
    public void An_active_fault_is_reported_again_between_the_two_power_exceptions()
    {
        // Restored conditions run through the table while the machine starts (QSP ConditionHandler.cpp:741).
        Start();
        Assert.Null(Machine.PrinterFault("jam"));
        Assert.Equal(new byte[] { 0x78 }, Drain());

        Machine.PowerCycle();

        Assert.Equal(new byte[] { 0x18, 0x78, 0x17 }, Drain());
    }

    [Fact]
    public void A_memory_error_reports_32_once_and_takes_the_machine_out_of_play()
    {
        // The memory error condition raises $32 and interrupts the game (QSP SasNetwork_Conditions.hpp:59, ConditionDefaults.h:56).
        Start();

        Assert.Null(Machine.MemoryFault(MemoryFaultKind.CmosLost));
        Assert.Null(Machine.MemoryFault(MemoryFaultKind.CmosLost));

        Assert.True(Machine.MemoryTiltActive);
        Assert.Equal(new byte[] { 0x32 }, Drain());
        Assert.Equal(0xFF, LockStatus([0x00, 0x0F, 0x99, 0x99]));
    }

    [Fact]
    public void A_changed_program_reports_36_at_every_start_until_the_memory_is_cleared()
    {
        // The condition is kept in memory, so each power-up reports it again (QSP AttachableDeviceHandler.cpp:436).
        Start();

        Assert.Null(Machine.MemoryFault(MemoryFaultKind.EpromChanged));
        Assert.Equal(new byte[] { 0x18, 0x36, 0x17 }, Drain());

        Machine.PowerCycle();
        Assert.Equal(new byte[] { 0x18, 0x36, 0x17 }, Drain());

        Assert.Null(Machine.MemoryFault(MemoryFaultKind.Clear));
        Assert.False(Machine.MemoryTiltActive);
        Assert.Equal(new byte[] { 0x3A, 0x7A, 0x3C }, Drain());
    }

    [Theory]
    [InlineData(MemoryFaultKind.CmosRecovered, "$31")]
    [InlineData(MemoryFaultKind.CmosBadDevice, "$33")]
    [InlineData(MemoryFaultKind.EepromData, "$34")]
    [InlineData(MemoryFaultKind.EpromBadChecksum, "$37")]
    [InlineData(MemoryFaultKind.PartitionedEpromChanged, "$38")]
    public void Memory_faults_the_platform_never_reports_are_refused_with_a_reason(MemoryFaultKind kind, string code)
    {
        Start();

        Assert.Contains(code, Machine.MemoryFault(kind), StringComparison.Ordinal);
        Assert.Empty(Drain());
    }

    [Fact]
    public void A_memory_clear_empties_the_meters_and_the_records_and_reports_the_reset()
    {
        // PostNvramReset drops what was owed and holds $3A and $7A; $3C follows as the configuration completes
        // (QSP SasNetwork.cpp:1218, SasNetwork_Conditions.hpp:63). The asset number goes back to 1 (:1201).
        Start();
        Machine.SetAssetNumber(12345);
        Machine.InsertBill(2000);
        Machine.PlayGames(1, 1);
        Machine.SetDoor("main", open: true);
        Assert.Null(Machine.MemoryFault(MemoryFaultKind.CmosLost));

        Machine.ClearMemory();

        Assert.Equal(0, Machine.Meters.Read(SasMeterCode.GamesPlayed));
        Assert.Equal(0, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.Equal(1u, Machine.AssetNumber);
        Assert.Equal(1, Machine.AftHistoryIndex);
        Assert.Equal(0x80, Machine.AftRegistrationStatus);
        Assert.False(Machine.MemoryTiltActive);
        Assert.Equal(new byte[] { 0x3A, 0x7A, 0x3C }, Drain());
    }

    [Fact]
    public void A_secure_enhanced_machine_reports_its_missing_identifier_again_after_a_power_cycle()
    {
        // sasInit raises $3F when the identifier is unset (libsas_wrapper.cpp:337).
        Start(new FakeEgmOptions { CashoutValidation = SasCashoutValidation.SecureEnhanced });

        // The start's own $3F may already have gone to the round before the sequence began, so it is drained unasserted.
        Drain();

        Machine.PowerCycle();

        Assert.Equal(new byte[] { 0x3F, 0x18, 0x17 }, Drain());
    }
}
