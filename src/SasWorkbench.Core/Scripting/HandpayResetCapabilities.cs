namespace SasWorkbench.Core.Scripting;

/// <summary>The capabilities of a machine whose hand pays the host may clear, onto the credit meter or not.</summary>
public static class HandpayResetCapabilities
{
    /// <summary>The host may clear a hand pay with long poll $94.</summary>
    public const string Remote = "machine.remote_handpay_reset";

    /// <summary>A jackpot hand pay may be paid onto the credit meter when the host asks with long poll $A8.</summary>
    public const string ToCredits = "machine.handpay_reset_to_credits";
}
