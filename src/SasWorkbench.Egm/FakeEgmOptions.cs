using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>How the fake gaming machine identifies and configures itself.</summary>
/// <remarks>
/// The defaults are the values a real machine reported over a real link, so a test written
/// against the fake machine sees the same identification reply it would see against the real
/// one. That makes the two comparable, which is the whole reason the fake exists.
/// </remarks>
public sealed record FakeEgmOptions
{
    /// <summary>The poll address this machine answers to.</summary>
    public SasAddress Address { get; init; } = SasAddress.Of(1);

    /// <summary>Which channel this machine is answering on.</summary>
    public SasChannel Channel { get; init; } = SasChannel.One;

    /// <summary>Two-character manufacturer identifier.</summary>
    public string GameId { get; init; } = "QX";

    /// <summary>Three-character machine identifier.</summary>
    public string AdditionalId { get; init; } = "001";

    /// <summary>Accounting denomination code.</summary>
    public byte Denomination { get; init; } = 0x02;

    /// <summary>Largest bet the machine accepts, in credits.</summary>
    public byte MaxBet { get; init; } = 0xFF;

    /// <summary>Progressive group the machine belongs to.</summary>
    public byte ProgressiveGroup { get; init; }

    /// <summary>Machine option bits.</summary>
    public ushort GameOptions { get; init; }

    /// <summary>Paytable identifier, up to six characters.</summary>
    public string PaytableId { get; init; } = "001";

    /// <summary>Theoretical payback percentage, in hundredths of a percent.</summary>
    public int BasePercentage { get; init; } = 9210;

    /// <summary>Protocol revision this machine claims, as three digits.</summary>
    public string SasVersion { get; init; } = "602";

    /// <summary>Machine serial number.</summary>
    public string SerialNumber { get; init; } = "QXFAKE0001";

    /// <summary>Cents per credit, used to turn an inserted note into credits.</summary>
    public int CentsPerCredit { get; init; } = 1;

    /// <summary>
    /// How many coins are in the hopper. Machine state rather than a meter, because the library
    /// keeps no meter code for it.
    /// </summary>
    public int HopperLevel { get; init; }

    /// <summary>
    /// How long the machine takes to answer, so a test can exercise a slow machine on purpose.
    /// </summary>
    public TimeSpan ResponseDelay { get; init; } = TimeSpan.Zero;
}
