namespace SasWorkbench.Core.Scripting;

/// <summary>The stimulus capabilities of the coin acceptor, the hopper, the reels and the cashbox (D58).</summary>
public static class DeviceStimuli
{
    /// <summary>Coins can be inserted and the coin acceptor faulted.</summary>
    public const string Coin = "stimulus.coin";

    /// <summary>The hopper can be filled, emptied, limited and set to pay wins.</summary>
    public const string Hopper = "stimulus.hopper";

    /// <summary>The hopper can be faulted.</summary>
    public const string HopperFault = "stimulus.hopper_fault";

    /// <summary>The reels can be faulted.</summary>
    public const string Reel = "stimulus.reel";

    /// <summary>The cashbox can be removed and installed.</summary>
    public const string Cashbox = "stimulus.cashbox";

    /// <summary>A player can cancel a cash out waiting for an attendant.</summary>
    public const string HandpayCancel = "stimulus.handpay.cancel";
}

/// <summary>Inserts coins at the coin acceptor.</summary>
public sealed record CoinInsertStep(SourceLocation Location, int Coins) : StimulusStep(Location)
{
    public override string RequiredCapability => DeviceStimuli.Coin;

    public override string Describe() => $"coin insert {Coins}";

    public override string? Guidance => $"Insert {Coins} coin(s) or token(s), one at a time.";
}

/// <summary>A device with faults a script can name: the coin acceptor, the hopper or the reels.</summary>
public enum CoinDevice
{
    /// <summary>The coin acceptor and its diverter.</summary>
    Coin,

    /// <summary>The hopper.</summary>
    Hopper,

    /// <summary>The reels.</summary>
    Reel,
}

/// <summary>Puts the coin acceptor, the hopper or a reel into one fault, or clears every fault the device holds.</summary>
/// <remarks>Named for what happens at the cabinet, as <see cref="PeripheralFaultStep"/> is.</remarks>
public sealed record DeviceFaultStep(SourceLocation Location, CoinDevice Device, string Fault) : StimulusStep(Location)
{
    /// <summary>The faults each device can be given, by the word a script uses.</summary>
    public static IReadOnlyDictionary<CoinDevice, IReadOnlyList<string>> Faults { get; } =
        new Dictionary<CoinDevice, IReadOnlyList<string>>
        {
            [CoinDevice.Coin] = ["tilt", "reversed", "lockout", "diverter", PeripheralFaultStep.Cleared],
            [CoinDevice.Hopper] =
            [
                "empty", "flooded_optics", "reverse_coin", "coin_too_short", "jam", "runaway", "disconnected",
                "full", "low", PeripheralFaultStep.Cleared,
            ],
            [CoinDevice.Reel] = ["tilt", "reel_1", "reel_2", "reel_3", "reel_4", "reel_5", "disconnected", PeripheralFaultStep.Cleared],
        };

    /// <summary>The script word for a device.</summary>
    public static string WordFor(CoinDevice device) => device switch
    {
        CoinDevice.Coin => "coin",
        CoinDevice.Hopper => "hopper",
        _ => "reel",
    };

    public override string RequiredCapability => Device switch
    {
        CoinDevice.Coin => DeviceStimuli.Coin,
        CoinDevice.Hopper => DeviceStimuli.HopperFault,
        _ => DeviceStimuli.Reel,
    };

    public override string Describe() => $"{WordFor(Device)} fault {Fault}";

    public override string? Guidance => (Device, Fault) switch
    {
        (_, PeripheralFaultStep.Cleared) => "Clear every fault on it: reconnect it, free whatever is stuck, and reset the machine if it asks.",
        (CoinDevice.Coin, "tilt") => "Insert tokens with the acceptor's wiring loosened, or several in fast succession.",
        (CoinDevice.Coin, "reversed") => "Insert a token on a string and pull it back out.",
        (CoinDevice.Coin, "lockout") => "Force the lockout into the accept position and insert a coin.",
        (CoinDevice.Coin, "diverter") => "Block the diverter shut, or its switch, and insert a token.",
        (CoinDevice.Hopper, "empty") => "Empty the hopper and make it pay.",
        (CoinDevice.Hopper, "full") => "Short the hopper-full detector.",
        (CoinDevice.Hopper, "low") => "Bring the hopper level below its low threshold.",
        (CoinDevice.Hopper, "runaway") => "Make the hopper pay an extra coin: tape a coin over the exit or trigger the release by hand.",
        (CoinDevice.Hopper, _) => $"Make the hopper report '{Fault.Replace('_', ' ')}'.",
        (CoinDevice.Reel, "disconnected") => "Disconnect a reel mechanism's connector.",
        (CoinDevice.Reel, "tilt") => "Interfere with a reel while it spins, so the machine cannot say which reel faulted.",
        (CoinDevice.Reel, _) => $"Interfere with reel {Fault[^1]} while it spins.",
        _ => null,
    };
}

/// <summary>Fills or empties the hopper to a number of coins, as an attendant does.</summary>
public sealed record HopperLevelStep(SourceLocation Location, int Coins) : StimulusStep(Location)
{
    public override string RequiredCapability => DeviceStimuli.Hopper;

    public override string Describe() => $"hopper level {Coins}";

    public override string? Guidance => $"Fill or empty the hopper until it holds {Coins} coin(s).";
}

/// <summary>Sets the most a cash out may pay from the hopper before it becomes a hand pay.</summary>
public sealed record SetHopperLimitStep(SourceLocation Location, long Cents) : StimulusStep(Location)
{
    public override string RequiredCapability => DeviceStimuli.Hopper;

    public override string Describe() => $"set hopper limit {Cents} cents";
}

/// <summary>Chooses whether game wins are paid from the hopper or to the credit meter.</summary>
public sealed record SetWinPayoutStep(SourceLocation Location, bool Hopper) : StimulusStep(Location)
{
    public override string RequiredCapability => DeviceStimuli.Hopper;

    public override string Describe() => $"set win payout {(Hopper ? "hopper" : "credits")}";
}

/// <summary>Removes or installs the note acceptor's cashbox.</summary>
public sealed record CashboxStep(SourceLocation Location, bool Removed) : StimulusStep(Location)
{
    public override string RequiredCapability => DeviceStimuli.Cashbox;

    public override string Describe() => $"cashbox {(Removed ? "removed" : "installed")}";
}

/// <summary>The player cancels a cash out the machine is waiting for an attendant to pay.</summary>
public sealed record HandpayCancelStep(SourceLocation Location) : StimulusStep(Location)
{
    public override string RequiredCapability => DeviceStimuli.HandpayCancel;

    public override string Describe() => "handpay cancel";

    public override string? Guidance => "Cancel the cash out at the player screen, if the machine offers it; say no if it does not.";
}
