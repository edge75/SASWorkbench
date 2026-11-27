using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The machine's own conditions, the memory faults a declared machine reports, memory found invalid at start-up,
/// period meters, the credit-meter key, restricted tickets and a poll with a broken check sequence (D62). Every case
/// runs with the host in a sequence, so each exception is read off a general poll in order.
/// </summary>
public sealed class FakeEgmConditionsTests : IDisposable
{
    private readonly LoopbackBus _bus = new();
    private FakeEgm? _machine;
    private PollEngine? _host;

    private FakeEgm Machine => _machine!;

    private PollEngine Host => _host!;

    private void Start(FakeEgmOptions? options = null)
    {
        _machine = new FakeEgm(_bus.Egm, (options ?? new FakeEgmOptions()) with
        {
            AccountingDenomination = 0x01,
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
        _ = Drain();
    }

    public void Dispose()
    {
        _host?.Dispose();
        _machine?.Dispose();
        _bus.Dispose();
    }

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

    private byte Poll()
    {
        GeneralPollResult result = Host.PollGenerally();
        Assert.True(result.Answered, $"the general poll went unanswered: {result.Outcome}");
        return result.Code;
    }

    [Fact]
    public void A_general_tilt_is_reported_once_and_takes_the_machine_out_of_play_until_cleared()
    {
        Start();

        Machine.SetCondition(MachineCondition.GeneralTilt, true);
        Machine.SetCondition(MachineCondition.GeneralTilt, true);
        Assert.Equal([(byte)SasExceptionCode.GeneralTilt], Drain());
        Assert.Contains("hardware error", Machine.PeripheralConditions);

        Machine.SetCondition(MachineCondition.GeneralTilt, false);
        Assert.Empty(Drain());
        Assert.Empty(Machine.PeripheralConditions);
    }

    [Fact]
    public void An_attendant_taking_the_machine_out_of_service_reports_86()
    {
        Start();

        Machine.SetCondition(MachineCondition.OutOfService, true);
        Assert.Equal([(byte)SasExceptionCode.GamingMachineOutOfService], Drain());
        Machine.SetCondition(MachineCondition.OutOfService, false);
        Assert.Empty(Drain());
    }

    [Fact]
    public void A_low_battery_is_reported_at_the_start_that_finds_it_and_not_after_it_is_replaced()
    {
        Start();

        Machine.SetCondition(MachineCondition.BatteryLow, true);
        List<byte> first = Drain();
        Assert.Single(first, (byte)SasExceptionCode.LowBackupBattery);
        Assert.True(first.IndexOf((byte)SasExceptionCode.ACPowerApplied) < first.IndexOf((byte)SasExceptionCode.LowBackupBattery));

        Machine.SetCondition(MachineCondition.BatteryLow, false);
        Assert.DoesNotContain((byte)SasExceptionCode.LowBackupBattery, Drain());
    }

    [Fact]
    public void Without_the_declaration_the_faults_the_platform_never_raises_are_refused()
    {
        Start();

        Assert.NotNull(Machine.MemoryFault(MemoryFaultKind.EepromData));
        Assert.NotNull(Machine.MemoryFault(MemoryFaultKind.CmosRecovered));
    }

    [Fact]
    public void Declared_memory_devices_report_each_fault_and_hold_the_machine_until_a_memory_clear()
    {
        Start(new FakeEgmOptions { MemoryFaults = true });

        Assert.Null(Machine.MemoryFault(MemoryFaultKind.EepromData));
        Assert.Equal([(byte)SasExceptionCode.EEPROMErrorData], Drain());
        Assert.True(Machine.MemoryTiltActive);

        Assert.Null(Machine.MemoryFault(MemoryFaultKind.EepromData));
        Assert.Empty(Drain());

        Assert.Null(Machine.MemoryFault(MemoryFaultKind.Clear));
        Assert.Contains((byte)SasExceptionCode.MemoryErrorReset, Drain());
        Assert.False(Machine.MemoryTiltActive);
    }

    [Fact]
    public void A_recovered_memory_error_is_reported_once_at_start_up_and_keeps_the_meters()
    {
        Start(new FakeEgmOptions { MemoryFaults = true });
        Machine.InsertBill(2000);
        long drop = Machine.Meters.Read(SasMeterCode.TotalDropIn_Crd);
        _ = Drain();

        Assert.Null(Machine.MemoryFault(MemoryFaultKind.CmosRecovered));
        Assert.Single(Drain(), (byte)SasExceptionCode.CMOSRAMErrorDataRecovered);
        Assert.Equal(drop, Machine.Meters.Read(SasMeterCode.TotalDropIn_Crd));
        Assert.False(Machine.MemoryTiltActive);

        Machine.PowerCycle();
        Assert.DoesNotContain((byte)SasExceptionCode.CMOSRAMErrorDataRecovered, Drain());
    }

    [Fact]
    public void A_bad_checksum_found_at_start_up_is_reported_then_and_again_at_every_start()
    {
        Start(new FakeEgmOptions { MemoryFaults = true });

        Assert.Null(Machine.MemoryFault(MemoryFaultKind.PartitionedEpromBadChecksum));
        Assert.Single(Drain(), (byte)SasExceptionCode.PartitionedEEPROMErrorBadChecksum);

        Machine.PowerCycle();
        Assert.Single(Drain(), (byte)SasExceptionCode.PartitionedEEPROMErrorBadChecksum);
    }

    [Fact]
    public void Memory_found_invalid_at_start_up_leaves_the_machine_silent_until_an_address_is_set()
    {
        Start();

        Assert.Null(Machine.MemoryFault(MemoryFaultKind.AtStartUp));
        Host.Synchronise();
        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x1F).Outcome);

        Assert.Null(Machine.SetPollAddress(SasAddress.Of(1)));
        Host.Synchronise();
        Assert.Equal(LongPollOutcome.Answered, Host.Execute(0x1F).Outcome);
    }

    [Fact]
    public void Period_meters_cleared_on_a_declared_machine_report_7B_and_so_does_a_memory_clear()
    {
        Start(new FakeEgmOptions { PeriodMeters = true });

        Machine.ClearPeriodMeters();
        Assert.Equal([(byte)SasExceptionCode.BillValidatorTotalsResetByAttendant], Drain());

        Machine.ClearMemory();
        Assert.Contains((byte)SasExceptionCode.BillValidatorTotalsResetByAttendant, Drain());
    }

    [Fact]
    public void Without_period_meters_nothing_is_reported()
    {
        Start();

        Machine.ClearPeriodMeters();
        Assert.Empty(Drain());
    }

    [Fact]
    public void The_credit_key_pays_a_jackpot_onto_the_credit_meter_only_when_the_host_allowed_it()
    {
        Start(new FakeEgmOptions { CreditKey = true });

        // A standard reset chosen: the key leaves the hand pay where it is.
        Machine.RaiseHandpay(HandpayKind.Jackpot, 150000);
        Assert.Equal(LongPollOutcome.Answered, Host.Execute(0xA8, [0x00]).Outcome);
        Machine.ResetHandpayToCredits();
        Assert.True(Machine.HandpayPending);
        Assert.True(Machine.ResetHandpay());

        // The credit meter chosen: the key pays it there.
        Machine.RaiseHandpay(HandpayKind.Jackpot, 150000);
        Assert.Equal(LongPollOutcome.Answered, Host.Execute(0xA8, [0x01]).Outcome);
        Machine.ResetHandpayToCredits();
        Assert.False(Machine.HandpayPending);
        Assert.Equal(150000, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.Equal(150000, Machine.Meters.Read(SasMeterCode.TotalJackpotCredits));
    }

    [Fact]
    public void A_poll_with_a_broken_check_sequence_is_refused()
    {
        Start();
        Host.Synchronise();

        LongPollResult result = Host.Execute(0x21, [0x12, 0x34], checkSequence: true, badCheckSequence: true);
        Assert.Equal(LongPollOutcome.Rejected, result.Outcome);
        Assert.Equal(LongPollOutcome.Answered, Host.Execute(0x1F).Outcome);
    }

    [Fact]
    public void Restricted_tickets_follow_the_operator_option_in_the_status_bits()
    {
        Start(new FakeEgmOptions { CashoutValidation = SasCashoutValidation.SecureEnhanced });

        static byte[] Status(ushort mask, ushort states) =>
            [0x08, (byte)mask, (byte)(mask >> 8), (byte)states, (byte)(states >> 8), 0, 0, 0, 0];

        long Reply()
        {
            LongPollResult result = Host.Execute(0x7B, Status(0, 0));
            SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x7B)!, result.Frame);
            return decoded.Number("current_validation_status")!.Value & 0x0018;
        }

        Assert.Equal(0, Reply());
        Machine.SetRestrictedTickets(true);
        Assert.Equal(0x0018, Reply());
    }
}
