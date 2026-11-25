using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Scripting;

/// <summary>The cash-out limit, which a machine reports only when it has one.</summary>
public static partial class CapabilityProbe
{
    /// <summary>The capability a machine that reports a cash-out limit answers with, its value the limit in accounting units.</summary>
    public const string CashoutLimitCapability = "machine.cashout_limit";

    /// <summary>
    /// Asks long poll $A4 for the machine as a whole; the library answers it only once the application has set a limit
    /// (lpA4_SendCashoutLimit), so silence means the machine has none to report.
    /// </summary>
    private static string? AskCashoutLimit(PollEngine engine, Dictionary<string, string?> values)
    {
        SasMessageLayout layout = SasLayouts.For((byte)SasLongPollCode.LP_SENDCASHOUTLIMIT)!;

        if (Ask(engine, layout, [0x00, 0x00]) is not { } result)
        {
            return "the cash-out limit poll could not be put on the link, so whether the machine has a limit "
                   + "was never asked" + Because(engine);
        }

        if (!result.Succeeded)
        {
            return $"the machine did not answer the cash-out limit poll ({result.Outcome}); a machine with no "
                   + "limit configured, one with no hopper, leaves it unanswered";
        }

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, result.Frame);
        if (decoded.Number("cashout_limit") is not { } limit)
            return "the machine's cash-out limit reply could not be decoded: " + (decoded.Failure ?? "it carries no limit");

        values[CashoutLimitCapability] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return null;
    }
}
