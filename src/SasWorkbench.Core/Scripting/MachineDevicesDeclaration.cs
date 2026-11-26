namespace SasWorkbench.Core.Scripting;

/// <summary>How the machine's coin acceptor reaches it, when it has one.</summary>
public enum CoinAcceptorKind
{
    /// <summary>No coin acceptor at all.</summary>
    None,

    /// <summary>The machine's own coin acceptor, its coins going to the hopper or the drop.</summary>
    Internal,

    /// <summary>An external coin acceptor, whose coins never reach a hopper.</summary>
    External,
}

/// <summary>
/// The coin-handling devices and reels a case assumes the machine has, written <c>machine coin acceptor on|external</c>,
/// <c>machine hopper on</c>, <c>machine reels on</c> and <c>machine partial pay on</c> (D58), and the bet-one button,
/// <c>machine bet button on</c> (D59).
/// </summary>
/// <remarks>
/// A declaration in the sense of <see cref="MachineDeclaration"/>: whether a cabinet has a hopper or reels is its
/// hardware, and no poll can tell it, since the library answers the hopper and reel polls whatever is fitted. The
/// software machine is built with them and claims each capability; the manual backend offers them for a person at a
/// cabinet that has them; any other machine skips the case.
/// </remarks>
/// <param name="Location">Where it was first declared.</param>
/// <param name="CoinAcceptor">The coin acceptor the machine has.</param>
/// <param name="Hopper">The machine pays coins from a hopper.</param>
/// <param name="Reels">The machine has physical reels.</param>
/// <param name="PartialPay">The hopper pays part of a hand pay before the attendant pays the rest.</param>
/// <param name="BetButton">
/// The player commits credits one at a time with a bet-one button, at a maximum bet of five, and the machine reports a
/// wager placed and not yet played as waiting for the player.
/// </param>
public sealed record MachineDevicesDeclaration(
    SourceLocation Location, CoinAcceptorKind CoinAcceptor, bool Hopper, bool Reels, bool PartialPay, bool BetButton = false)
{
    /// <summary>The capability a machine with a bet-one button carries.</summary>
    public const string BetButtonCapability = "machine.bet_button";

    /// <summary>The capability a machine with a coin acceptor carries.</summary>
    public const string CoinAcceptorCapability = "machine.coin_acceptor";

    /// <summary>The capability a machine whose coin acceptor is external carries.</summary>
    public const string ExternalCoinAcceptorCapability = "machine.external_coin_acceptor";

    /// <summary>The capability a machine with a hopper carries.</summary>
    public const string HopperCapability = "machine.hopper";

    /// <summary>The capability a machine with physical reels carries.</summary>
    public const string ReelsCapability = "machine.physical_reels";

    /// <summary>The capability a machine that pays part of a hand pay from its hopper carries.</summary>
    public const string PartialPayCapability = "machine.partial_pay";

    /// <summary>The declaration as the requirements only a machine built this way answers.</summary>
    public IEnumerable<Requirement> AsRequirements()
    {
        if (CoinAcceptor != CoinAcceptorKind.None)
            yield return new Requirement(Location, CoinAcceptorCapability);
        if (CoinAcceptor == CoinAcceptorKind.External)
            yield return new Requirement(Location, ExternalCoinAcceptorCapability);
        if (Hopper)
            yield return new Requirement(Location, HopperCapability);
        if (Reels)
            yield return new Requirement(Location, ReelsCapability);
        if (PartialPay)
            yield return new Requirement(Location, PartialPayCapability);
        if (BetButton)
            yield return new Requirement(Location, BetButtonCapability);
    }

    /// <summary>The capabilities a software machine built this way claims, its stimuli included.</summary>
    public IEnumerable<KeyValuePair<string, string?>> Claims()
    {
        foreach (Requirement requirement in AsRequirements())
            yield return new(requirement.Key, "yes");
        if (CoinAcceptor != CoinAcceptorKind.None)
            yield return new(DeviceStimuli.Coin, "yes");
        if (Hopper)
        {
            yield return new(DeviceStimuli.Hopper, "yes");
            yield return new(DeviceStimuli.HopperFault, "yes");
            yield return new(DeviceStimuli.HandpayCancel, "yes");
        }

        if (Reels)
            yield return new(DeviceStimuli.Reel, "yes");
        if (BetButton)
        {
            yield return new(PlayedGameCapabilities.BetButton, "yes");
            yield return new(PlayedGameCapabilities.PlayerInputState, "yes");
        }
    }

    public override string ToString() =>
        string.Join(", ", new[]
        {
            CoinAcceptor switch
            {
                CoinAcceptorKind.Internal => "coin acceptor on",
                CoinAcceptorKind.External => "coin acceptor external",
                _ => null,
            },
            Hopper ? "hopper on" : null,
            Reels ? "reels on" : null,
            PartialPay ? "partial pay on" : null,
            BetButton ? "bet button on" : null,
        }.OfType<string>());
}
