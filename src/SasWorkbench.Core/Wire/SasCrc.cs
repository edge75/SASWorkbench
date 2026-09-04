namespace SasWorkbench.Core.Wire;

/// <summary>
/// The SAS 6.02 frame check sequence: a reflected CRC-16 over the whole message, seed zero,
/// transmitted low byte first.
/// </summary>
/// <remarks>
/// Ported from the gaming machine library's own implementation, nibble step for nibble step,
/// so host and machine cannot disagree. Verified against a captured 24-byte response to long
/// poll <c>$1F</c>, whose trailing bytes are <c>86 3F</c>.
/// </remarks>
public static class SasCrc
{
    // 0o10201 == 0x1081: the reflected 0x1021 polynomial, folded four bits at a time.
    private const uint Polynomial = 0x1081;

    /// <summary>
    /// Computes the check sequence over <paramref name="message"/>. The low byte of the result
    /// is transmitted first.
    /// </summary>
    public static ushort Compute(ReadOnlySpan<byte> message)
    {
        uint crc = 0;
        foreach (byte c in message)
        {
            uint q = (crc ^ c) & 0x0F;
            crc = (crc >> 4) ^ (q * Polynomial);
            q = (crc ^ (uint)(c >> 4)) & 0x0F;
            crc = (crc >> 4) ^ (q * Polynomial);
        }

        return (ushort)crc;
    }

    /// <summary>The first check byte on the wire.</summary>
    public static byte LowByte(ushort crc) => (byte)(crc & 0xFF);

    /// <summary>The second check byte on the wire.</summary>
    public static byte HighByte(ushort crc) => (byte)(crc >> 8);

    /// <summary>
    /// Appends the two check bytes for <paramref name="message"/> to <paramref name="destination"/>,
    /// which must be the same buffer with two bytes to spare.
    /// </summary>
    /// <returns>The total length, message plus check bytes.</returns>
    public static int Append(Span<byte> destination, int messageLength)
    {
        if (messageLength < 0 || messageLength + 2 > destination.Length)
            throw new ArgumentOutOfRangeException(nameof(messageLength), messageLength,
                "The destination needs two spare bytes for the check sequence.");

        ushort crc = Compute(destination[..messageLength]);
        destination[messageLength] = LowByte(crc);
        destination[messageLength + 1] = HighByte(crc);
        return messageLength + 2;
    }

    /// <summary>
    /// True when <paramref name="frame"/> — message followed by its two check bytes — checks out.
    /// A frame shorter than three bytes cannot carry a check sequence and is rejected.
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 3)
            return false;

        int messageLength = frame.Length - 2;
        ushort crc = Compute(frame[..messageLength]);
        return frame[messageLength] == LowByte(crc) && frame[messageLength + 1] == HighByte(crc);
    }
}
