namespace SasWorkbench.Core.Scripting;

/// <summary>The capabilities of the machine's own conditions, its period meters and its credit-meter key (D62).</summary>
public static class MachineConditionCapabilities
{
    /// <summary>The machine raises the general tilt for a fault with no code of its own.</summary>
    public const string GeneralTilt = "machine.general_tilt";

    /// <summary>A script can put the machine into a fault with no code of its own.</summary>
    public const string Tilt = "stimulus.tilt";

    /// <summary>An attendant can take the machine out of service at the machine.</summary>
    public const string OutOfService = "machine.out_of_service";

    /// <summary>A low backup battery can be fitted.</summary>
    public const string Battery = "stimulus.battery";

    /// <summary>The machine keeps period meters an attendant can clear on their own.</summary>
    public const string PeriodMeters = "machine.period_meters";

    /// <summary>An attendant can clear the period meters.</summary>
    public const string PeriodMeterClear = "stimulus.period_meter_clear";

    /// <summary>The machine has a key that clears a jackpot hand pay onto the credit meter.</summary>
    public const string CreditKey = "machine.handpay_credit_key";

    /// <summary>An attendant can turn the credit-meter key.</summary>
    public const string CreditReset = "stimulus.handpay.credit_reset";

    /// <summary>The machine can print restricted tickets when its operator allows them.</summary>
    public const string RestrictedTickets = "machine.restricted_tickets";

    /// <summary>The operator can allow or forbid restricted tickets.</summary>
    public const string RestrictedTicketsOption = "stimulus.restricted_tickets";

    /// <summary>How many ordinary exceptions the machine's queue holds before it loses the oldest.</summary>
    public const string ExceptionQueueDepth = "machine.exception_queue_depth";
}

/// <summary>A condition of the machine itself a script can raise and clear.</summary>
public enum MachineCondition
{
    /// <summary>A fault with no code of its own, reported as the general tilt.</summary>
    GeneralTilt,

    /// <summary>The attendant's out of service setting.</summary>
    OutOfService,

    /// <summary>A low backup battery.</summary>
    BatteryLow,
}

/// <summary>
/// Raises or clears one of the machine's own conditions, written <c>egm tilt on|off</c>,
/// <c>egm out of service on|off</c> and <c>egm battery low|replaced</c>.
/// </summary>
public sealed record MachineConditionStep(SourceLocation Location, MachineCondition Condition, bool On) : StimulusStep(Location)
{
    public override string RequiredCapability => Condition switch
    {
        MachineCondition.GeneralTilt => MachineConditionCapabilities.Tilt,
        MachineCondition.OutOfService => MachineConditionCapabilities.OutOfService,
        _ => MachineConditionCapabilities.Battery,
    };

    public override string Describe() => Condition switch
    {
        MachineCondition.GeneralTilt => $"tilt {(On ? "on" : "off")}",
        MachineCondition.OutOfService => $"out of service {(On ? "on" : "off")}",
        _ => On ? "battery low" : "battery replaced",
    };

    public override string? Guidance => (Condition, On) switch
    {
        (MachineCondition.GeneralTilt, true) =>
            "Make a fault the machine has no other code for; on a QSP machine, disconnect a device it reports as a hardware error.",
        (MachineCondition.GeneralTilt, false) => "Clear that fault and reset the machine if it asks.",
        (MachineCondition.OutOfService, true) => "In the audit or self-test screens, take the machine out of service.",
        (MachineCondition.OutOfService, false) => "Put the machine back in service from the same screen.",
        (MachineCondition.BatteryLow, true) =>
            "Fit a backup battery low enough on charge for the machine to notice, and start the machine; confirm once it has booted.",
        _ => "Fit a good backup battery and start the machine; confirm once it has booted.",
    };
}

/// <summary>The attendant clears the period meters on their own, written <c>egm period meters clear</c>.</summary>
public sealed record ClearPeriodMetersStep(SourceLocation Location) : StimulusStep(Location)
{
    public override string RequiredCapability => MachineConditionCapabilities.PeriodMeterClear;

    public override string Describe() => "period meters clear";

    public override string? Guidance => "In the machine's set-up screens, clear its period meters and leave the lifetime meters alone.";
}

/// <summary>The attendant turns the credit-meter key on a jackpot hand pay, written <c>egm handpay reset credits</c>.</summary>
public sealed record HandpayCreditResetStep(SourceLocation Location) : StimulusStep(Location)
{
    public override string RequiredCapability => MachineConditionCapabilities.CreditReset;

    public override string Describe() => "handpay reset credits";

    public override string? Guidance => "Turn the credit-meter key on the hand pay showing.";
}

/// <summary>The operator allows or forbids restricted cash-out tickets, written <c>egm set restricted tickets on|off</c>.</summary>
public sealed record SetRestrictedTicketsStep(SourceLocation Location, bool On) : StimulusStep(Location)
{
    public override string RequiredCapability => MachineConditionCapabilities.RestrictedTicketsOption;

    public override string Describe() => $"set restricted tickets {(On ? "on" : "off")}";

    public override string? Guidance =>
        $"In the operator's ticket settings, {(On ? "allow" : "forbid")} restricted tickets and save.";
}
