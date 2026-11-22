using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Scripting;

/// <summary>Legacy bonusing and multiplied jackpots, read off the machine's enabled-features reply.</summary>
public static partial class CapabilityProbe
{
    /// <summary>The capability a machine that takes legacy bonuses answers with.</summary>
    public const string LegacyBonusCapability = "machine.legacy_bonus";

    /// <summary>The capability a machine that multiplies jackpots answers with.</summary>
    public const string MultipliedJackpotCapability = "machine.multiplied_jackpot";

    // The library's LEGACY_BONUS_AWARDS_BIT and JACKPOT_MULTIPLIER_BIT of the first feature byte (libsas.h:186, :188).
    private const byte LegacyBonusAwardsBit = 0x04;
    private const byte JackpotMultiplierBit = 0x01;

    /// <summary>
    /// Reads whether the machine takes legacy bonuses off bit 2 of the first feature byte, which the
    /// library clears on a channel that does not own the legacy bonus category.
    /// </summary>
    private static string? AskLegacyBonus(PollEngine engine, Dictionary<string, string?> values) =>
        AskFirstFeatureBit(engine, values, LegacyBonusCapability, LegacyBonusAwardsBit, "legacy bonus");

    /// <summary>
    /// Reads whether the machine multiplies jackpots off bit 0 of the first feature byte, which the
    /// library clears with the legacy bonus bit on a channel without that category.
    /// </summary>
    private static string? AskMultipliedJackpot(PollEngine engine, Dictionary<string, string?> values) =>
        AskFirstFeatureBit(engine, values, MultipliedJackpotCapability, JackpotMultiplierBit, "multiplied jackpots");

    /// <summary>Sets a capability from one bit of the first enabled-features byte, or says why it cannot.</summary>
    private static string? AskFirstFeatureBit(
        PollEngine engine, Dictionary<string, string?> values, string capability, byte bit, string what)
    {
        SasMessageLayout layout = SasLayouts.For((byte)SasLongPollCode.LP_SENDENABLEFEATURES)!;

        if (Ask(engine, layout, [0x00, 0x00]) is not { } result)
        {
            return "the enabled-features poll could not be put on the link, so whether the machine "
                   + $"offers {what} was never asked" + Because(engine);
        }

        if (!result.Succeeded)
        {
            return $"the machine did not answer the enabled-features poll ({result.Outcome}), so "
                   + $"whether it offers {what} cannot be read off this link";
        }

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, result.Frame);
        if (decoded.Number("feature_codes_1") is not { } features)
        {
            return "the machine's enabled-features reply could not be decoded: "
                   + (decoded.Failure ?? "it carries no first feature byte");
        }

        if ((features & bit) == 0)
            return $"the machine reports {what} off in its enabled-features reply on this channel";

        values[capability] = "yes";
        return null;
    }
}
