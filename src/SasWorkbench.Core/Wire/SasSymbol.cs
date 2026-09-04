namespace SasWorkbench.Core.Wire;

/// <summary>
/// One symbol on a SAS link: eight data bits plus the wakeup bit that marks an address byte.
/// </summary>
/// <remarks>
/// The wakeup bit is the whole reason this type exists rather than a plain <see cref="byte"/>.
/// A SAS message is framed only by which symbol carries it, so a transport that cannot
/// represent nine bits cannot represent SAS at all.
/// </remarks>
public readonly struct SasSymbol : IEquatable<SasSymbol>
{
    /// <summary>Bit 8 — set on an address byte, clear on every other byte of a message.</summary>
    public const ushort WakeupMask = 0x0100;

    /// <summary>Bits 0-7 — the data byte.</summary>
    public const ushort DataMask = 0x00FF;

    /// <summary>Every bit a symbol may legally use.</summary>
    public const ushort SymbolMask = WakeupMask | DataMask;

    private readonly ushort _raw;

    private SasSymbol(ushort raw) => _raw = raw;

    /// <summary>The data byte, with the wakeup bit stripped.</summary>
    public byte Data => (byte)(_raw & DataMask);

    /// <summary>True when this symbol is an address byte.</summary>
    public bool Wakeup => (_raw & WakeupMask) != 0;

    /// <summary>The nine-bit value, as it travels over a transport.</summary>
    public ushort Raw => _raw;

    /// <summary>A data byte — no wakeup bit.</summary>
    public static SasSymbol Data8(byte value) => new(value);

    /// <summary>An address byte — wakeup bit set.</summary>
    public static SasSymbol Address(byte value) => new((ushort)(value | WakeupMask));

    /// <summary>A symbol built from a raw nine-bit value.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value sets a bit above bit 8.</exception>
    public static SasSymbol FromRaw(ushort raw)
    {
        if ((raw & ~SymbolMask) != 0)
            throw new ArgumentOutOfRangeException(nameof(raw), raw,
                "A SAS symbol uses nine bits; bits 9 and above must be zero.");
        return new SasSymbol(raw);
    }

    /// <summary>A symbol built from a raw value, with bits above bit 8 discarded.</summary>
    public static SasSymbol FromRawTruncating(ushort raw) => new((ushort)(raw & SymbolMask));

    public bool Equals(SasSymbol other) => _raw == other._raw;

    public override bool Equals(object? obj) => obj is SasSymbol other && Equals(other);

    public override int GetHashCode() => _raw;

    /// <summary>Renders as <c>1.81</c> — the wakeup flag, a dot, then the data byte in hex.</summary>
    public override string ToString() => $"{(Wakeup ? '1' : '0')}.{Data:X2}";

    public static bool operator ==(SasSymbol left, SasSymbol right) => left.Equals(right);

    public static bool operator !=(SasSymbol left, SasSymbol right) => !left.Equals(right);
}
