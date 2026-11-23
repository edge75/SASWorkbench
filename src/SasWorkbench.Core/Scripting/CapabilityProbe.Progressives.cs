using System.Globalization;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Scripting;

/// <summary>The SAS progressive group, read off the machine's identification reply.</summary>
public static partial class CapabilityProbe
{
    /// <summary>
    /// Reads the group long poll $1F reports; the library reports zero on a channel that does not own
    /// the progressive category, and zero means no SAS progressive at all (lp.cpp:1341).
    /// </summary>
    private static string? AskSasProgressiveGroup(PollEngine engine, Dictionary<string, string?> values)
    {
        SasMessageLayout layout = SasLayouts.For((byte)SasLongPollCode.LP_SENDMACHINEID_INFO)!;

        if (Ask(engine, layout, []) is not { } result)
        {
            return "the identification poll could not be put on the link, so the machine's progressive "
                   + "group was never asked" + Because(engine);
        }

        if (!result.Succeeded)
        {
            return $"the machine did not answer the identification poll ({result.Outcome}), so its "
                   + "progressive group cannot be read off this link";
        }

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, result.Frame);
        if (decoded.Number("progressive_group") is not { } group)
        {
            return "the machine's identification reply could not be decoded: "
                   + (decoded.Failure ?? "it carries no progressive group");
        }

        if (group == 0)
            return "the machine reports progressive group 00 on this channel, which is no SAS progressive";

        values[MachineProgressiveDeclaration.SasCapability] = group.ToString(CultureInfo.InvariantCulture);
        return null;
    }
}
