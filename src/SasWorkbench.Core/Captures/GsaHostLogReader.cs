using System.Buffers.Binary;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Captures;

/// <summary>
/// Reads the conformance toolkit's binary session log.
/// </summary>
/// <remarks>
/// <para>
/// The layout was recovered from the file itself and is little-endian throughout. Each record is a
/// version word, a whole-second time, a payload length and the payload; the payload's first byte
/// says what kind it is. Two kinds occur: a line of the toolkit's own text, and one message.
/// </para>
/// <code>
/// record  := u16 version | u32 unixTime | u16 payloadLength | payload
/// payload := u8 kind, then
///   kind 0x00  the toolkit's own text, to the end of the payload
///   kind 0x44  u16 index | u32 startTicks | u16 byteCount | u8 direction
///              then byteCount x { u8 value | u16 ticks }
/// </code>
/// <para>
/// <b>The ninth bit is not in this format.</b> Only the eight data bits were stored, so a message
/// read back from it cannot say where the wakeup bit was, and the capture says so rather than
/// putting the bit where the protocol would have wanted it.
/// </para>
/// <para>
/// <b>Direction one is the host and direction two is the machine</b>, corrected here against the
/// bytes rather than taken from the earlier note on this format, which had zero for the machine.
/// Measured over the log the toolkit ships: 723 records carry direction one, 645 carry direction
/// two, and none carries zero. Every direction-two record is exactly one byte, never begins with a
/// poll byte, and sits immediately after a host general poll — which is a machine's answer and
/// nothing else. A direction this build has not seen, a record of another kind, or a version it
/// has not seen is named as unreadable rather than guessed at.
/// </para>
/// <para>
/// The message index is not the order. The two directions number their messages separately, so the
/// file's own order is the only ordering there is, and that is what is used.
/// </para>
/// </remarks>
public static class GsaHostLogReader
{
    private const byte KindText = 0x00;
    private const byte KindMessage = 0x44;
    private const ushort KnownVersion = 0x0001;
    private const byte HostDirection = 1;
    private const byte MachineDirection = 2;

    public static Capture Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Parse(File.ReadAllBytes(path), path);
    }

    /// <summary>
    /// Whether these bytes begin a session log.
    /// </summary>
    /// <remarks>
    /// A version word of one and a length is not a signature — the toolkit's own configuration
    /// files begin that way too, and reading one as a log produced pages of warnings about records
    /// that were never records. So the chain is walked: several records in a row must each carry
    /// the one version this format uses, a time somebody could have recorded, and a kind this
    /// format defines. A file has to be a log on purpose to pass.
    /// </remarks>
    public static bool Looks(ReadOnlySpan<byte> bytes)
    {
        // Comfortably around any real recording, and nowhere near a length or a count read as a
        // time by accident.
        long earliest = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        long latest = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        int offset = 0;
        int records = 0;
        while (records < 3)
        {
            if (offset + 8 > bytes.Length)
                break;

            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
            uint seconds = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 2)..]);
            ushort length = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offset + 6)..]);

            if (version != KnownVersion || seconds < earliest || seconds > latest || length == 0)
                return false;

            if (offset + 8 + length > bytes.Length)
                return false;

            byte kind = bytes[offset + 8];
            if (kind != KindText && kind != KindMessage)
                return false;

            offset += 8 + length;
            records++;
        }

        return records > 0;
    }

    public static Capture Parse(ReadOnlySpan<byte> bytes, string source)
    {
        var messages = new List<CapturedMessage>();
        var unreadable = new List<string>();
        DateTimeOffset? startedAt = null;
        uint? firstTicks = null;
        int offset = 0;
        int record = 0;

        while (offset + 8 <= bytes.Length)
        {
            int start = offset;
            record++;
            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
            uint seconds = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 2)..]);
            ushort length = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offset + 6)..]);
            offset += 8;

            if (offset + length > bytes.Length)
            {
                unreadable.Add($"{source}: record {record} at byte {start} says it is {length} "
                               + "bytes long and the file ends before that, so the log is truncated");
                break;
            }

            ReadOnlySpan<byte> payload = bytes.Slice(offset, length);
            offset += length;

            if (version != KnownVersion)
            {
                unreadable.Add($"{source}: record {record} is version {version}, which this build "
                               + $"has not seen, so its {length} bytes are not read");
                continue;
            }

            if (payload.Length == 0)
            {
                unreadable.Add($"{source}: record {record} carries no payload at all");
                continue;
            }

            switch (payload[0])
            {
                case KindText:
                    // The toolkit's own words. Deliberately not carried into the record.
                    startedAt ??= DateTimeOffset.FromUnixTimeSeconds(seconds);
                    continue;

                case KindMessage:
                    if (!TryReadMessage(payload, out CapturedMessage? message, out uint ticks,
                            out string? why))
                    {
                        unreadable.Add($"{source}: record {record}: {why}");
                        continue;
                    }

                    startedAt ??= DateTimeOffset.FromUnixTimeSeconds(seconds);
                    firstTicks ??= ticks;

                    // The counter is thirty-two bits and wraps, so the difference is taken unsigned.
                    messages.Add(new CapturedMessage(
                        message!.Direction,
                        message.Symbols,
                        TimeSpan.FromMilliseconds(unchecked(ticks - firstTicks.Value))));
                    continue;

                default:
                    unreadable.Add($"{source}: record {record} is of kind 0x{payload[0]:X2}, which "
                                   + $"is not a kind this build reads, so its {length} bytes "
                                   + "are not read");
                    continue;
            }
        }

        if (offset != bytes.Length && offset + 8 > bytes.Length && offset < bytes.Length)
            unreadable.Add($"{source}: {bytes.Length - offset} trailing byte(s) are too few to be "
                           + "a record, so the log does not end where a record does");

        return new Capture
        {
            Source = source,
            Format = CaptureFormat.GsaHostLog,
            WakeupBitCaptured = false,
            StartedAt = startedAt,
            Messages = messages,
            Unreadable = unreadable,
            TimingCaveat = "Times come from the recorder's own millisecond counter, taken from the "
                           + "first message in the file. The whole-second time on each record is a "
                           + "separate clock and is used only for when the log began.",
        };
    }

    /// <summary>Reads one message payload, and the recorder's own counter for it.</summary>
    private static bool TryReadMessage(
        ReadOnlySpan<byte> payload, out CapturedMessage? message, out uint ticks, out string? why)
    {
        message = null;
        ticks = 0;
        why = null;

        if (payload.Length < 10)
        {
            why = $"a message header needs ten bytes and this payload is {payload.Length}";
            return false;
        }

        ticks = BinaryPrimitives.ReadUInt32LittleEndian(payload[3..]);
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(payload[7..]);
        byte direction = payload[9];

        if (direction is not (HostDirection or MachineDirection))
        {
            why = $"direction {direction} is neither the host nor the machine, and this build will "
                  + "not guess which a message with it came from";
            return false;
        }

        if (payload.Length < 10 + (count * 3))
        {
            why = $"it says it holds {count} byte(s) and the payload has room for "
                  + $"{(payload.Length - 10) / 3}";
            return false;
        }

        var symbols = new SasSymbol[count];
        for (int i = 0; i < count; i++)
        {
            // Each byte is stored with its own arrival time, which this build does not carry: a
            // record holds one time per message, and inventing a per-symbol one would be a shape
            // nothing else in this product uses.
            symbols[i] = SasSymbol.Data8(payload[10 + (i * 3)]);
        }

        message = new CapturedMessage(
            direction == HostDirection ? WireDirection.Transmit : WireDirection.Receive,
            symbols,
            TimeSpan.Zero);
        return true;
    }
}
