namespace SasWorkbench.Core.Wire;

/// <summary>A SAS poll address. Zero is the synchronisation address; 1-127 identify machines.</summary>
public readonly struct SasAddress : IEquatable<SasAddress>
{
    /// <summary>Address zero. A general poll of it resynchronises every machine on the link.</summary>
    public static readonly SasAddress Sync = new(0);

    /// <summary>The highest addressable machine.</summary>
    public const byte MaxValue = 127;

    private SasAddress(byte value) => Value = value;

    /// <summary>The address itself, 0-127.</summary>
    public byte Value { get; }

    /// <summary>True for address zero, which no machine answers to directly.</summary>
    public bool IsSync => Value == 0;

    /// <exception cref="ArgumentOutOfRangeException">Above 127 — bit 7 is the general-poll flag.</exception>
    public static SasAddress Of(byte value)
    {
        if (value > MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), value,
                $"A SAS poll address is 0-{MaxValue}; bit 7 marks a general poll.");
        return new SasAddress(value);
    }

    public static bool TryParse(string? text, out SasAddress address)
    {
        address = Sync;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string trimmed = text.Trim();
        bool hex = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (hex)
            trimmed = trimmed[2..];

        // A bare two-digit address is written in hex everywhere in the specification and in
        // every capture, so "01" means one and "10" means sixteen.
        System.Globalization.NumberStyles style = hex || trimmed.Length == 2
            ? System.Globalization.NumberStyles.HexNumber
            : System.Globalization.NumberStyles.Integer;

        if (!byte.TryParse(trimmed, style, System.Globalization.CultureInfo.InvariantCulture, out byte value))
            return false;
        if (value > MaxValue)
            return false;

        address = new SasAddress(value);
        return true;
    }

    public bool Equals(SasAddress other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is SasAddress other && Equals(other);

    public override int GetHashCode() => Value;

    public override string ToString() => $"{Value:X2}";

    public static bool operator ==(SasAddress left, SasAddress right) => left.Equals(right);

    public static bool operator !=(SasAddress left, SasAddress right) => !left.Equals(right);
}
