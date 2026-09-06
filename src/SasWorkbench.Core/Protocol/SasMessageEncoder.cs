using System.Globalization;
using System.Text;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Protocol;

/// <summary>
/// Builds a reply from named values, the inverse of <see cref="SasMessageDecoder"/>.
/// </summary>
/// <remarks>
/// Sharing the layouts between encoding and decoding is the point. A layout that is wrong is
/// wrong in both directions at once, so a round trip through both cannot paper over it — and the
/// fake machine and the host under test cannot quietly agree on a format the real machine does
/// not use.
/// </remarks>
public static class SasMessageEncoder
{
    /// <summary>
    /// Writes a complete reply — address, echoed command, payload, check sequence — into
    /// <paramref name="destination"/>.
    /// </summary>
    /// <returns>How many bytes were written.</returns>
    /// <exception cref="InvalidOperationException">A field has no value, or will not fit.</exception>
    public static int WriteResponse(
        SasMessageLayout layout,
        SasAddress address,
        IReadOnlyDictionary<string, object> values,
        Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(values);

        if (layout.Shape != SasResponseShape.Data)
            throw new InvalidOperationException(
                $"Long poll ${layout.Command:X2} does not answer with data.");

        destination[0] = address.Value;
        destination[1] = layout.Command;
        int cursor = 2;
        var writtenAt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (SasField field in layout.Response)
        {
            if (field.IsGroup)
            {
                cursor += WriteGroup(layout, field, field.Name, values, destination[cursor..]);
                continue;
            }

            if (field.BitsOfField is not null)
            {
                FoldBits(layout, field, values, destination, writtenAt);
                continue;
            }

            if (field.PresentWhen is not null && !values.ContainsKey(field.Name))
                continue;

            if (!values.TryGetValue(field.Name, out object? value))
                throw new InvalidOperationException(
                    $"Long poll ${layout.Command:X2} needs a value for '{field.Name}'.");

            writtenAt[field.Name] = cursor;
            cursor += WriteField(field, value, destination[cursor..]);
        }

        return SasCrc.Append(destination, cursor);
    }

    /// <summary>
    /// Writes the data a host sends after the address and command bytes, from named values.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="SasMessageDecoder.DecodeRequest"/>, and the reason a script can
    /// name the fields of a command instead of spelling out its bytes. A field the layout marks
    /// as conditional is written only when a value is supplied for it, which is what lets one
    /// layout describe both a redemption carrying an amount and a bare request for a status.
    ///
    /// A leading field called <c>length</c> is filled in from what follows unless the caller
    /// gives one, because a length that disagrees with the message is a defect in the host rather
    /// than a case worth writing by hand — and a caller that wants to send a wrong one still can.
    /// </remarks>
    /// <returns>How many bytes were written.</returns>
    /// <exception cref="InvalidOperationException">A field has no value, or will not fit.</exception>
    public static int WriteRequestData(
        SasMessageLayout layout,
        IReadOnlyDictionary<string, object> values,
        Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(values);

        int cursor = 0;
        int lengthAt = -1;
        var writtenAt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (SasField field in layout.Request)
        {
            if (field.IsGroup)
            {
                cursor += WriteGroup(layout, field, field.Name, values, destination[cursor..]);
                continue;
            }

            if (field.BitsOfField is not null)
            {
                FoldBits(layout, field, values, destination, writtenAt);
                continue;
            }

            if (field.PresentWhen is not null && !values.ContainsKey(field.Name))
                continue;

            if (!values.TryGetValue(field.Name, out object? value))
            {
                if (cursor == 0 && field.Length == 1
                    && field.Name.Equals("length", StringComparison.OrdinalIgnoreCase))
                {
                    lengthAt = cursor;
                    cursor += 1;
                    continue;
                }

                throw new InvalidOperationException(
                    $"Long poll ${layout.Command:X2} needs a value for '{field.Name}'.");
            }

            writtenAt[field.Name] = cursor;
            cursor += WriteField(field, value, destination[cursor..]);
        }

        if (lengthAt >= 0)
        {
            int payload = cursor - lengthAt - 1;
            if (payload > byte.MaxValue)
                throw new InvalidOperationException(
                    $"Long poll ${layout.Command:X2} carries {payload} bytes, which its length "
                    + "byte cannot express.");

            destination[lengthAt] = (byte)payload;
        }

        return cursor;
    }

    /// <summary>
    /// Writes a repeating group. Elements are named the way the decoder names them when it reads
    /// one back — <c>meters[0].value</c> — so a message can be built and read with one vocabulary
    /// rather than two that have to be kept in step.
    /// </summary>
    private static int WriteGroup(
        SasMessageLayout layout,
        SasField group,
        string path,
        IReadOnlyDictionary<string, object> values,
        Span<byte> destination)
    {
        if (!values.TryGetValue($"{path}.count", out object? countValue))
            throw new InvalidOperationException(
                $"Long poll ${layout.Command:X2} needs '{path}.count' to say how many "
                + $"'{group.Name}' entries to write.");

        int count = Convert.ToInt32(countValue, CultureInfo.InvariantCulture);
        int cursor = 0;

        for (int index = 0; index < count; index++)
        {
            string elementPath = $"{path}[{index}]";
            foreach (SasField member in group.Fields)
            {
                string memberPath = $"{elementPath}.{member.Name}";
                if (member.BitsOfField is not null)
                    throw new InvalidOperationException(
                        $"'{memberPath}' is a run of bits, and writing one inside a repeating "
                        + "group is not something this build does.");

                if (!values.TryGetValue(memberPath, out object? value))
                    throw new InvalidOperationException(
                        $"Long poll ${layout.Command:X2} needs a value for '{memberPath}'.");

                SasField toWrite = member;
                if (member.LengthFromField is { } lengthField)
                {
                    string lengthPath = $"{elementPath}.{lengthField}";
                    if (!values.TryGetValue(lengthPath, out object? lengthValue))
                        throw new InvalidOperationException(
                            $"'{memberPath}' takes its length from '{lengthPath}', which has no value.");

                    toWrite = member with
                    {
                        Length = Convert.ToInt32(lengthValue, CultureInfo.InvariantCulture),
                    };
                }

                cursor += WriteField(toWrite, value, destination[cursor..]);
            }
        }

        return cursor;
    }

    /// <summary>
    /// Puts a named run of bits into the byte it belongs to, which has already been written.
    /// </summary>
    /// <remarks>
    /// A run of bits carries no bytes of its own, so a caller may leave it out entirely and get
    /// whatever the host byte already says. Supplying one and having it ignored is the outcome
    /// worth refusing, so a value for a run whose host byte was never written is an error rather
    /// than a silent no-op.
    /// </remarks>
    private static void FoldBits(
        SasMessageLayout layout,
        SasField field,
        IReadOnlyDictionary<string, object> values,
        Span<byte> destination,
        Dictionary<string, int> writtenAt)
    {
        if (!values.TryGetValue(field.Name, out object? value))
            return;

        if (field.Bits is not { } range)
            throw new InvalidOperationException(
                $"'{field.Name}' is a run of bits but does not say which bits.");

        if (!writtenAt.TryGetValue(field.BitsOfField!, out int offset))
            throw new InvalidOperationException(
                $"'{field.Name}' is a run of bits of '{field.BitsOfField}', which long poll "
                + $"${layout.Command:X2} has not written.");

        long number = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        int mask = (1 << range.Width) - 1;
        if (number < 0 || number > mask)
            throw new InvalidOperationException(
                $"'{field.Name}' is {range}, which holds 0 to {mask}, and {number} is outside it.");

        destination[offset] = (byte)((destination[offset] & ~(mask << range.LowBit))
                                     | ((int)number << range.LowBit));
    }

    private static int WriteField(SasField field, object value, Span<byte> destination)
    {
        int length = field.Length;
        if (length == SasField.Remaining)
        {
            byte[] bytes = value switch
            {
                string text => Encoding.ASCII.GetBytes(text),
                byte[] raw => raw,
                _ => throw new InvalidOperationException(
                    $"Field '{field.Name}' fills the rest of the message, so it needs text or bytes."),
            };

            bytes.CopyTo(destination);
            return bytes.Length;
        }

        Span<byte> slot = destination[..length];
        slot.Clear();

        switch (field.Encoding)
        {
            case SasFieldEncoding.Bcd:
                WriteBcd(field, ToNumber(field, value), slot);
                break;

            case SasFieldEncoding.Binary:
            case SasFieldEncoding.Bits:
                WriteInteger(field, ToNumber(field, value), slot);
                break;

            case SasFieldEncoding.Ascii:
                WriteAscii(field, value, slot, padWithZeroes: false);
                break;

            case SasFieldEncoding.AsciiNumber:
                WriteAscii(field, value, slot, padWithZeroes: true);
                break;

            case SasFieldEncoding.Raw:
                if (value is not byte[] raw)
                    throw new InvalidOperationException($"Field '{field.Name}' needs raw bytes.");
                if (raw.Length != length)
                    throw new InvalidOperationException(
                        $"Field '{field.Name}' holds {length} byte(s), not {raw.Length}.");
                raw.CopyTo(slot);
                break;

            default:
                throw new InvalidOperationException(
                    $"Field '{field.Name}' has an encoding this encoder does not implement.");
        }

        return length;
    }

    private static long ToNumber(SasField field, object value) => value switch
    {
        long l => l,
        int i => i,
        short s => s,
        byte b => b,
        ushort u => u,
        uint u => u,
        bool flag => flag ? 1 : 0,
        Enum e => Convert.ToInt64(e),
        string text when long.TryParse(text, out long parsed) => parsed,
        _ => throw new InvalidOperationException(
            $"Field '{field.Name}' needs a number, not {value.GetType().Name}."),
    };

    private static void WriteBcd(SasField field, long value, Span<byte> slot)
    {
        if (value < 0)
            throw new InvalidOperationException($"Field '{field.Name}' cannot hold a negative value.");

        long remaining = value;
        for (int i = slot.Length - 1; i >= 0; i--)
        {
            int low = (int)(remaining % 10);
            remaining /= 10;
            int high = (int)(remaining % 10);
            remaining /= 10;
            slot[i] = (byte)((high << 4) | low);
        }

        if (remaining != 0)
            throw new InvalidOperationException(
                $"Field '{field.Name}' holds {slot.Length * 2} decimal digits, and {value} needs more.");
    }

    private static void WriteInteger(SasField field, long value, Span<byte> slot)
    {
        long remaining = value;
        if (field.ByteOrder == SasByteOrder.LittleEndian)
        {
            for (int i = 0; i < slot.Length; i++)
            {
                slot[i] = (byte)(remaining & 0xFF);
                remaining >>= 8;
            }
        }
        else
        {
            for (int i = slot.Length - 1; i >= 0; i--)
            {
                slot[i] = (byte)(remaining & 0xFF);
                remaining >>= 8;
            }
        }

        if (remaining is not (0 or -1))
            throw new InvalidOperationException(
                $"Field '{field.Name}' holds {slot.Length} byte(s), and {value} needs more.");
    }

    private static void WriteAscii(SasField field, object value, Span<byte> slot, bool padWithZeroes)
    {
        string text = value as string ?? value.ToString() ?? string.Empty;
        if (padWithZeroes)
            text = text.PadLeft(slot.Length, '0');

        if (text.Length > slot.Length)
            throw new InvalidOperationException(
                $"Field '{field.Name}' holds {slot.Length} character(s), and '{text}' is longer.");

        Encoding.ASCII.GetBytes(text).CopyTo(slot);
    }
}
