using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Scripting;

/// <summary>Component authentication, read off the enabled-features reply and the component list.</summary>
public static partial class CapabilityProbe
{
    /// <summary>The capability a machine with more than one component to authenticate answers with.</summary>
    public const string MultipleComponentsCapability = "machine.multiple_components";

    // The library's COMPONENT_AUTHENTICATION_BIT of the second feature byte (libsas.h:203).
    private const byte ComponentAuthenticationBit = 0x10;

    /// <summary>
    /// Reads whether the machine takes part in component authentication off bit 4 of the second feature byte,
    /// which the library clears on a channel that does not own the category (libsas_utils.cpp:1249).
    /// </summary>
    private static string? AskComponentAuthentication(PollEngine engine, Dictionary<string, string?> values)
    {
        SasMessageLayout layout = SasLayouts.For((byte)SasLongPollCode.LP_SENDENABLEFEATURES)!;

        if (Ask(engine, layout, [0x00, 0x00]) is not { } result)
        {
            return "the enabled-features poll could not be put on the link, so whether the machine "
                   + "authenticates components was never asked" + Because(engine);
        }

        if (!result.Succeeded)
        {
            return $"the machine did not answer the enabled-features poll ({result.Outcome}), so "
                   + "whether it authenticates components cannot be read off this link";
        }

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, result.Frame);
        if (decoded.Number("feature_codes_2") is not { } features)
        {
            return "the machine's enabled-features reply could not be decoded: "
                   + (decoded.Failure ?? "it carries no second feature byte");
        }

        if ((features & ComponentAuthenticationBit) == 0)
            return "the machine reports component authentication off in its enabled-features reply on this channel";

        values[MachineAuthenticationDeclaration.Capability] = "yes";
        return null;
    }

    /// <summary>Reads how many components the machine lists off long poll $6E, action $00.</summary>
    private static string? AskMultipleComponents(PollEngine engine, Dictionary<string, string?> values)
    {
        SasMessageLayout layout = SasLayouts.For((byte)SasLongPollCode.LP_SENDAUTHENTICATION)!;

        if (Ask(engine, layout, [0x01, 0x00]) is not { } result)
        {
            return "the component list poll could not be put on the link, so how many components the "
                   + "machine has was never asked" + Because(engine);
        }

        if (!result.Succeeded)
        {
            return $"the machine did not answer the component list poll ({result.Outcome}), so how many "
                   + "components it has cannot be read off this link";
        }

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, result.Frame);
        if (decoded.Number("size") is not { } count)
        {
            return "the machine's component list reply could not be decoded: "
                   + (decoded.Failure ?? "it carries no component count");
        }

        if (count < 2)
            return $"the machine lists {count} component(s) for authentication, so there is no second one to start";

        values[MultipleComponentsCapability] = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return null;
    }
}
