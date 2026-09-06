namespace SasWorkbench.Core.Protocol;

/// <summary>
/// Which validation style a machine performs when it cashes out.
/// </summary>
/// <remarks>
/// <para>
/// A property of the machine rather than of the link: it is established when memory is cleared,
/// it is published in the enabled-features reply, and it decides which of the cashout commands
/// the machine answers at all. The codes are the gaming machine library's own
/// (<c>CashoutValidationType</c>), which is also the order the two feature bits carry them in.
/// </para>
/// <para>
/// The distinction that matters to a test: only the standard style answers long poll $3D, and
/// only the enhanced styles can redeem a ticket. A machine on fresh memory is in neither of the
/// enhanced styles, which is why a real one returns every ticket it is given.
/// </para>
/// </remarks>
public enum SasCashoutValidation : byte
{
    /// <summary>The machine numbers its own tickets and keeps no validation buffer.</summary>
    Standard = 0x00,

    /// <summary>The host issues the number, one ticket at a time.</summary>
    System = 0x01,

    /// <summary>The machine derives numbers from a seed the host sets.</summary>
    SecureEnhanced = 0x02,

    /// <summary>Not assigned. A machine reporting this is misconfigured.</summary>
    Reserved = 0x03,
}
