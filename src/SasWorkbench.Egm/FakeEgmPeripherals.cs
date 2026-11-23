using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Egm;

/// <summary>
/// The service button, and the note acceptor and printer faults, as the platform reports them.
/// </summary>
/// <remarks>
/// The library only queues what the application writes, so every code here is the platform's
/// choice: its condition table maps each fault to the exception it raises on and the one it raises
/// on clearing (QSP SasNetwork_Conditions.hpp:70 to :97). The platform has no coin acceptor, hopper
/// or reels, so this machine has none either (D46).
/// </remarks>
public sealed partial class FakeEgm
{
    /// <summary>One platform condition: the codes it raises, and whether it takes the machine out of play.</summary>
    private sealed record PeripheralCondition(
        string Name, SasExceptionCode Raised, SasExceptionCode Cleared, bool Tilts);

    // QSP SasNetwork_Conditions.hpp:70-97 for the codes, ConditionDefaults.h:67-93 for which interrupt the game.
    private static readonly Dictionary<string, PeripheralCondition> NoteConditions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cashbox_full"] = new("note acceptor stacker full", SasExceptionCode.CashboxFull, SasExceptionCode.NoActivity, true),
        ["jam"] = new("note acceptor jam", SasExceptionCode.BillJam, SasExceptionCode.NoActivity, true),
        ["failure"] = new("note acceptor malfunction", SasExceptionCode.BillAcceptorHwFailure, SasExceptionCode.NoActivity, true),
        ["counterfeit"] = new("note acceptor counterfeit", SasExceptionCode.CounterfeitBill, SasExceptionCode.NoActivity, true),
    };

    private static readonly Dictionary<string, PeripheralCondition> PrinterConditions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disconnected"] = new("printer disconnected", SasExceptionCode.PrinterPowerOff, SasExceptionCode.PrinterPowerOn, true),
        ["jam"] = new("printer jam", SasExceptionCode.PrinterCarriageJammed, SasExceptionCode.NoActivity, true),
        ["failure"] = new("printer malfunction", SasExceptionCode.PrinterPowerOff, SasExceptionCode.PrinterPowerOn, true),
        ["paper_out"] = new("printer paper out", SasExceptionCode.PrinterPaperOutError, SasExceptionCode.NoActivity, true),
        ["paper_low"] = new("printer paper low", SasExceptionCode.PrinterPaperLow, SasExceptionCode.NoActivity, false),
    };

    // The conditions active now, by name; locked on itself, never around the gate.
    private readonly Dictionary<string, PeripheralCondition> _conditions = new(StringComparer.Ordinal);
    private bool _serviceRequest;

    /// <summary>True while the change lamp is lit.</summary>
    public bool ServiceRequested
    {
        get
        {
            lock (_conditions)
                return _serviceRequest;
        }
    }

    /// <summary>The active peripheral conditions, by the platform's name for each.</summary>
    public IReadOnlyList<string> PeripheralConditions
    {
        get
        {
            lock (_conditions)
                return [.. _conditions.Keys.Order(StringComparer.Ordinal)];
        }
    }

    /// <summary>Presses the service button: the first press raises the service request, the next clears it.</summary>
    public void PressService()
    {
        bool on;
        lock (_conditions)
        {
            // QSP_DemoOpenGL GameManager.cpp:788 toggles it, and the condition table raises $71 and $72 (QSP SasNetwork_Conditions.hpp:87).
            _serviceRequest = !_serviceRequest;
            on = _serviceRequest;
        }

        RaiseException(on ? SasExceptionCode.ChangeLampOn : SasExceptionCode.ChangeLampOff);
    }

    /// <summary>
    /// Puts the note acceptor into a fault, or clears all of them; null when done, otherwise why the platform cannot.
    /// </summary>
    public string? NoteFault(string fault)
    {
        ArgumentNullException.ThrowIfNull(fault);

        switch (fault.ToLowerInvariant())
        {
            case "cleared":
                ClearConditions(NoteConditions.Values);
                return null;

            // A returned note is not a condition: the platform writes $2B and nothing else (QSP SasNetwork_Meters.hpp:113).
            case "rejected":
                RaiseException(SasExceptionCode.BillRejected);
                return null;

            case "reversed":
                return "the platform has no report for a note pulled back out: its acceptor events are a return, "
                       + "which it reports as a rejection, or a jam, and which one a pulled note is depends on the acceptor";

            default:
                return NoteConditions.TryGetValue(fault, out PeripheralCondition? condition)
                    ? Activate(condition)
                    : $"the note acceptor has no fault called '{fault}'";
        }
    }

    /// <summary>
    /// Puts the printer into a fault, or clears all of them; null when done, otherwise why the platform cannot.
    /// </summary>
    public string? PrinterFault(string fault)
    {
        ArgumentNullException.ThrowIfNull(fault);

        if (fault.Equals("cleared", StringComparison.OrdinalIgnoreCase))
        {
            ClearConditions(PrinterConditions.Values);
            return null;
        }

        if (fault.Equals("ribbon", StringComparison.OrdinalIgnoreCase))
            return "the platform's printers are thermal and it has no ribbon condition, so nothing raises $77";

        return PrinterConditions.TryGetValue(fault, out PeripheralCondition? condition)
            ? Activate(condition)
            : $"the printer has no fault called '{fault}'";
    }

    /// <summary>Clears every peripheral fault, as an attendant reconnecting and resetting does.</summary>
    private void ClearPeripheralFaults()
    {
        ClearConditions(NoteConditions.Values);
        ClearConditions(PrinterConditions.Values);
    }

    /// <summary>True while a condition that takes the machine out of play is active.</summary>
    private bool PeripheralTilt()
    {
        lock (_conditions)
            return _conditions.Values.Any(condition => condition.Tilts);
    }

    private string? Activate(PeripheralCondition condition)
    {
        lock (_conditions)
        {
            // A condition already active is a duplicate and raises nothing (QSP ConditionHandler.cpp:263).
            if (!_conditions.TryAdd(condition.Name, condition))
                return null;
        }

        RaiseException(condition.Raised);
        if (condition.Tilts)
            WithdrawLockIfUnplayable();
        return null;
    }

    private void ClearConditions(IEnumerable<PeripheralCondition> device)
    {
        List<PeripheralCondition> cleared = [];
        lock (_conditions)
        {
            foreach (PeripheralCondition condition in device)
            {
                if (_conditions.Remove(condition.Name))
                    cleared.Add(condition);
            }
        }

        // QSP SasNetwork_Conditions.hpp:394 raises the cleared code only where the table has one.
        foreach (PeripheralCondition condition in cleared)
        {
            if (condition.Cleared != SasExceptionCode.NoActivity)
                RaiseException(condition.Cleared);
        }
    }
}
