using System.Collections.Frozen;

namespace SasWorkbench.Core.Protocol;

/// <summary>
/// How many bytes a meter occupies in a reply that carries no size byte of its own.
/// </summary>
/// <remarks>
/// <para>
/// Read from the gaming machine library's own <c>getMeterMinimumSize</c>. Most meters are four
/// BCD bytes; a named set counted in cents is five. The selected-meter reply of long poll
/// <c>$2F</c> is the reason this has to exist: unlike <c>$6F</c>, that reply states no width per
/// meter, so the only thing that says where one meter ends and the next begins is which meter it
/// is. A reader without this table cannot walk such a reply at all.
/// </para>
/// <para>
/// The five-byte set is the library's own list of codes, copied as a list rather than derived
/// from the unit of each meter. Deriving it would be reasoning about the library instead of
/// reading it, and the two are not the same set: a meter counted in cents that the library did
/// not name here is still four bytes wide on the wire.
/// </para>
/// </remarks>
public static class SasMeterWidths
{
    /// <summary>The width every meter has unless the library names it as wider.</summary>
    public const int Default = 4;

    /// <summary>The width of the meters the library counts in cents.</summary>
    public const int Wide = 5;

    /// <summary>
    /// The meter codes <c>getMeterMinimumSize</c> answers five for: the four ticket totals of
    /// <c>$0D</c>-<c>$10</c>, and the cents-denominated ticket, handpay and transfer meters.
    /// </summary>
    private static readonly FrozenSet<ushort> FiveBytes = new ushort[]
    {
        0x000D, 0x000E, 0x000F, 0x0010,
        0x0080, 0x0082, 0x0084, 0x0086, 0x0088, 0x008A, 0x008C, 0x008E, 0x0090, 0x0092,
        0x00A0, 0x00A2, 0x00A4, 0x00A6, 0x00A8, 0x00AA, 0x00AC, 0x00AE, 0x00B0,
        0x00B8, 0x00BA, 0x00BC,
    }.ToFrozenSet();

    /// <summary>How wide this meter is where the reply does not say.</summary>
    public static int Of(ushort code) => FiveBytes.Contains(code) ? Wide : Default;

    /// <summary>How wide this meter is where the reply does not say.</summary>
    public static int Of(SasMeterCode code) => Of((ushort)code);
}
