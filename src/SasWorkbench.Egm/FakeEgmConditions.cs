using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>
/// The machine's own conditions - the general tilt, out of service and a low battery - the memory faults a declared
/// machine reports, memory found invalid at start-up, period meters and the credit-meter key (D62).
/// </summary>
/// <remarks>
/// The library only queues what the application writes, so every code here is the platform's or, where the platform has
/// none, the GSA document's in its simplest form.
/// </remarks>
public sealed partial class FakeEgm
{
    // QSP SasNetwork_Conditions.hpp:86 and :93 for the codes, ConditionDefaults.h:83 and :90 for both taking the machine out of play.
    private static readonly PeripheralCondition HardwareError =
        new("hardware error", SasExceptionCode.GeneralTilt, SasExceptionCode.NoActivity, true);

    private static readonly PeripheralCondition MaintenanceMode =
        new("maintenance mode", SasExceptionCode.GamingMachineOutOfService, SasExceptionCode.NoActivity, true);

    // The faults a declared machine's memory devices hold until a memory clear, each raised again at every start.
    private readonly List<SasExceptionCode> _deviceFaults = [];

    // A fault found once at the next start and not again: a memory error the meters survived.
    private SasExceptionCode? _startUpFault;

    private bool _batteryLow;

    /// <summary>Raises or clears one of the machine's own conditions.</summary>
    public void SetCondition(MachineCondition condition, bool on)
    {
        switch (condition)
        {
            // QSP AttachableDeviceHandler.cpp:279 adds the hardware error; libqsp.cpp:2124 the attendant's out of service.
            case MachineCondition.GeneralTilt:
                SetPlatformCondition(HardwareError, on);
                return;

            case MachineCondition.OutOfService:
                SetPlatformCondition(MaintenanceMode, on);
                return;

            // The platform reads its batteries as it starts and reports a low one then (QSP SasNetwork_Meters.hpp:132).
            default:
                lock (_gate)
                    _batteryLow = on;
                PowerCycle();
                return;
        }
    }

    private void SetPlatformCondition(PeripheralCondition condition, bool on)
    {
        if (on)
            _ = Activate(condition);
        else
            ClearConditions([condition]);
    }

    /// <summary>
    /// A declared machine's memory fault; null when done, otherwise why the platform cannot (D62).
    /// </summary>
    private string? DeclaredMemoryFault(MemoryFaultKind kind)
    {
        switch (kind)
        {
            // Found and recovered while starting: $31 once, the meters kept and the machine playable.
            case MemoryFaultKind.CmosRecovered:
                lock (_gate)
                    _startUpFault = SasExceptionCode.CMOSRAMErrorDataRecovered;
                PowerCycle();
                return null;

            // Found while running, as the platform finds its own memory error, and out of play until a memory clear.
            case MemoryFaultKind.CmosBadDevice:
                RaiseDeviceFault(SasExceptionCode.CMOSRAMErrorBadDevice);
                return null;
            case MemoryFaultKind.EepromData:
                RaiseDeviceFault(SasExceptionCode.EEPROMErrorData);
                return null;
            case MemoryFaultKind.EepromBadDevice:
                RaiseDeviceFault(SasExceptionCode.EEPROMErrorBadDevice);
                return null;

            // Found while starting, as the platform finds a changed program.
            case MemoryFaultKind.EpromBadChecksum:
                return FaultAtStart(SasExceptionCode.EEPROMErrorBadChecksum);
            case MemoryFaultKind.PartitionedEpromChanged:
                return FaultAtStart(SasExceptionCode.PartitionedEEPROMErrorVersionChanged);
            case MemoryFaultKind.PartitionedEpromBadChecksum:
                return FaultAtStart(SasExceptionCode.PartitionedEEPROMErrorBadChecksum);

            default:
                return $"no memory fault called {kind}";
        }
    }

    private void RaiseDeviceFault(SasExceptionCode code)
    {
        lock (_gate)
        {
            // An active condition raised again is a duplicate and reports nothing (QSP ConditionHandler.cpp:263).
            if (_deviceFaults.Contains(code))
                return;
            _deviceFaults.Add(code);
        }

        QueueOrdinary((byte)code);
        WithdrawLockIfUnplayable();
    }

    private string? FaultAtStart(SasExceptionCode code)
    {
        lock (_gate)
        {
            if (!_deviceFaults.Contains(code))
                _deviceFaults.Add(code);
        }

        PowerCycle();
        return null;
    }

    /// <summary>
    /// Memory found invalid while the machine starts: the platform reads no poll address from it, so it comes up at
    /// address 0 with no library running, silent until the operator sets one (QSP SasNetwork.cpp:155, :309, :336).
    /// </summary>
    private void MemoryInvalidAtStartUp()
    {
        lock (_gate)
        {
            _memoryError = true;
            MachineLink link = _links[0];
            link.Exceptions.Clear();
            link.Address = SasAddress.Sync;
        }

        PowerCycle();
    }

    /// <summary>The codes a start raises after $17: a fault found once, the declared faults held, and a low battery.</summary>
    private List<SasExceptionCode> StartUpConditionCodes()
    {
        lock (_gate)
        {
            List<SasExceptionCode> codes = [.. _deviceFaults];
            if (_startUpFault is { } once)
                codes.Add(once);
            _startUpFault = null;
            if (_batteryLow)
                codes.Add(SasExceptionCode.LowBackupBattery);
            return codes;
        }
    }

    /// <summary>True while a declared memory fault holds the machine out of play. Called under the gate.</summary>
    private bool DeviceFaultTilt() => _deviceFaults.Count > 0;

    /// <summary>
    /// The attendant clears the period meters of a declared machine: $7B, the bill validator totals reset, as the GSA
    /// document asks (4.27); the platform keeps no period meters at all.
    /// </summary>
    public void ClearPeriodMeters()
    {
        if (_options.PeriodMeters)
            RaiseException(SasExceptionCode.BillValidatorTotalsResetByAttendant);
    }

    /// <summary>
    /// The attendant turns the credit-meter key of a declared machine: a jackpot hand pay the host allowed onto the credit
    /// meter is paid there, anything else is left as it was (GSA 20.2).
    /// </summary>
    public void ResetHandpayToCredits()
    {
        HandpayOwed? paid = null;
        lock (_gate)
        {
            if (!_options.CreditKey || _handpay is not { Kind: HandpayKind.Jackpot } owed
                || _hostResetMethod == ResetMethodStandard || !MayResetToCreditMeterLocked(owed))
            {
                return;
            }

            if (ResetWithLibraryLocked())
            {
                paid = owed;
                _handpay = null;
                _hostResetMethod = ResetMethodStandard;
            }
        }

        if (paid is not null)
            CreditHandpay(paid);
    }
}
