using System.Text;

namespace SasWorkbench.Core.Protocol;

/// <summary>
/// Reads a reply against a layout and produces named values a test can assert on.
/// </summary>
/// <remarks>
/// Two rules run through this class. A field whose bytes cannot be read the way the layout says
/// stops the decode and names the byte — guessing produces a plausible wrong number, which is
/// the worst outcome available. A field that reads fine but breaks its constraint does not stop
/// the decode: the value is reported with the reason it is wrong, so a test asserting on a
/// different field still gets an answer.
/// </remarks>
public static class SasMessageDecoder
{
    /// <summary>
    /// Decodes the response part of <paramref name="frame"/> — address, command, payload, check
    /// sequence — against <paramref name="layout"/>.
    /// </summary>
    public static SasDecoded DecodeResponse(SasMessageLayout layout, ReadOnlySpan<byte> frame)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (layout.Shape == SasResponseShape.None)
            return new SasDecoded(layout.Command, layout, [],
                $"Long poll ${layout.Command:X2} produces no reply to decode.");

        if (frame.Length < 4)
            return new SasDecoded(layout.Command, layout, [],
                $"A reply of {frame.Length} byte(s) is too short to hold an address, a command "
                + "and a check sequence.");

        if (frame[1] != layout.Command)
            return new SasDecoded(layout.Command, layout, [],
                $"The reply echoes command ${frame[1]:X2}, not ${layout.Command:X2}.");

        // The payload is everything between the echoed command and the check sequence.
        return Decode(layout, layout.Command, layout.Response, frame[2..^2], startOffset: 2);
    }

    /// <summary>
    /// Decodes a real-time event message — address, the event marker, the exception code, the
    /// payload and a check sequence — against the payload layout for that exception.
    /// </summary>
    /// <remarks>
    /// Offsets are reported from the start of the frame, as they are for a reply, so a field's
    /// position in the log is the position in the bytes somebody is reading.
    /// </remarks>
    public static SasDecoded DecodeEvent(SasEventLayout layout, ReadOnlySpan<byte> frame)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (frame.Length < SasEventLayouts.HeaderLength + 2)
            return new SasDecoded(layout.Code, null, [],
                $"A real-time event of {frame.Length} byte(s) is too short to hold an address, "
                + "the event marker, an exception code and a check sequence.");

        if (frame[1] != SasEventLayouts.Marker)
            return new SasDecoded(layout.Code, null, [],
                $"The message carries ${frame[1]:X2} where a real-time event carries "
                + $"${SasEventLayouts.Marker:X2}.");

        if (frame[2] != layout.Code)
            return new SasDecoded(layout.Code, null, [],
                $"The event reports exception ${frame[2]:X2}, not ${layout.Code:X2}.");

        return Decode(null, layout.Code, layout.Payload,
            frame[SasEventLayouts.HeaderLength..^2], SasEventLayouts.HeaderLength);
    }

    /// <summary>
    /// Decodes the data a host sent after the address and command bytes, so a recorded session
    /// can be read back and so the fake machine can act on requests.
    /// </summary>
    public static SasDecoded DecodeRequest(SasMessageLayout layout, ReadOnlySpan<byte> frame)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (frame.Length < 2)
            return new SasDecoded(layout.Command, layout, [],
                "A long poll is at least an address byte and a command byte.");

        // A request with data carries a check sequence; one without carries none.
        int end = frame.Length > 2 ? frame.Length - 2 : frame.Length;
        return Decode(layout, layout.Command, layout.Request, frame[2..end], startOffset: 2);
    }

    private static SasDecoded Decode(
        SasMessageLayout? layout,
        byte code,
        IReadOnlyList<SasField> fields,
        ReadOnlySpan<byte> payload,
        int startOffset)
    {
        var values = new List<SasValue>();
        int cursor = 0;
        string? failure = ReadFields(fields, payload, ref cursor, startOffset, prefix: null, values);

        if (failure is null && cursor < payload.Length)
        {
            // Trailing bytes are not an error. A machine may send more than the protocol defines
            // and the extra bytes must be ignored, so they are reported rather than rejected.
            values.Add(new SasValue(
                "unmodelled_trailer",
                new SasField
                {
                    Name = "unmodelled_trailer",
                    Length = payload.Length - cursor,
                    Encoding = SasFieldEncoding.Raw,
                    Description = "Bytes beyond what the layout describes.",
                },
                startOffset + cursor,
                payload[cursor..].ToArray()));
        }

        return new SasDecoded(code, layout, values, failure);
    }

    private static string? ReadFields(
        IReadOnlyList<SasField> fields,
        ReadOnlySpan<byte> payload,
        ref int cursor,
        int startOffset,
        string? prefix,
        List<SasValue> values)
    {
        foreach (SasField field in fields)
        {
            string path = prefix is null ? field.Name : $"{prefix}.{field.Name}";

            if (field.PresentWhen is not null && !Holds(field.PresentWhen, values, prefix, payload.Length - cursor))
                continue;

            if (field.IsGroup)
            {
                string? groupFailure = ReadGroup(field, payload, ref cursor, startOffset, path, values);
                if (groupFailure is not null)
                    return groupFailure;
                continue;
            }

            if (field.BitsOfField is not null)
            {
                string? bitFailure = ReadBits(field, path, values, prefix, out SasValue bits);
                if (bitFailure is not null)
                    return bitFailure;

                values.Add(bits);
                continue;
            }

            int length;
            if (field.LengthFromField is { } lengthField)
            {
                // The most recent match, not the first: inside a repeating group every element has
                // its own size byte, and the one that governs this field is the one just decoded.
                SasValue? source = values.LastOrDefault(v =>
                    v.Path == lengthField || v.Path.EndsWith('.' + lengthField, StringComparison.Ordinal));
                if (source?.Number is null)
                    return $"Field '{path}' takes its length from '{lengthField}', but that field "
                           + "was not decoded as a number.";
                length = (int)source.Number.Value;
            }
            else if (field.MeterWidthFromField is { } meterField)
            {
                // The most recent match, for the same reason as above: inside a repeating group
                // the code that governs this meter is the one just decoded.
                SasValue? source = values.LastOrDefault(v =>
                    v.Path == meterField || v.Path.EndsWith('.' + meterField, StringComparison.Ordinal));
                if (source?.Number is null)
                    return $"Field '{path}' takes its width from the meter named by '{meterField}', "
                           + "but that field was not decoded as a number.";
                length = SasMeterWidths.Of((ushort)source.Number.Value);
            }
            else
            {
                length = field.Length == SasField.Remaining ? payload.Length - cursor : field.Length;
            }

            if (length < 0 || cursor + length > payload.Length)
                return $"Field '{path}' needs {length} byte(s) at offset "
                       + $"{startOffset + cursor} but the reply holds only "
                       + $"{payload.Length - cursor}.";

            ReadOnlySpan<byte> bytes = payload.Slice(cursor, length);
            string? readFailure = ReadScalar(field, path, bytes, startOffset + cursor, out SasValue value);
            if (readFailure is not null)
                return readFailure;

            values.Add(value);
            cursor += length;
        }

        return null;
    }

    /// <summary>
    /// Reads a run of bits out of a field that has already been decoded.
    /// </summary>
    /// <remarks>
    /// The offset reported is the byte the bits came from, so a failure still points at a place in
    /// the frame. The bytes kept are that byte, not the extracted value: what came off the wire is
    /// the byte, and a test asserting on the wire should see it.
    /// </remarks>
    private static string? ReadBits(
        SasField field, string path, List<SasValue> values, string? prefix, out SasValue value)
    {
        value = default!;
        string source = field.BitsOfField!;

        if (field.Bits is not { } range)
            return $"Field '{path}' is a run of bits of '{source}' but does not say which bits.";

        SasValue? host = values.LastOrDefault(v =>
            v.Path == source || v.Path.EndsWith('.' + source, StringComparison.Ordinal));
        if (host is null)
        {
            return $"Field '{path}' reads bits of '{source}', which is not a field decoded before "
                   + "it in this message.";
        }

        if (host.Bytes.Length != 1)
        {
            return $"Field '{path}' reads bits of '{source}', which is {host.Bytes.Length} bytes "
                   + "wide; a run of bits is named inside a single byte.";
        }

        if (range.LowBit < 0 || range.HighBit > 7 || range.Width < 1)
            return $"Field '{path}' names {range}, which is not inside a byte.";

        int mask = (1 << range.Width) - 1;
        long number = (host.Bytes[0] >> range.LowBit) & mask;

        value = new SasValue(path, field, host.Offset, [host.Bytes[0]], number,
            Violation: Violated(field, number, host.Bytes.Length));
        _ = prefix;
        return null;
    }

    private static string? ReadGroup(
        SasField group,
        ReadOnlySpan<byte> payload,
        ref int cursor,
        int startOffset,
        string path,
        List<SasValue> values)
    {
        SasRepeat repeat = group.Repeat ?? SasRepeat.Times(1);
        int count;

        if (repeat.FixedCount is { } fixedCount)
        {
            count = fixedCount;
        }
        else if (repeat.CountFromField is { } countField)
        {
            SasValue? source = values.FirstOrDefault(v =>
                v.Path == countField || v.Path.EndsWith('.' + countField, StringComparison.Ordinal));
            if (source?.Number is null)
                return $"Group '{path}' repeats as many times as '{countField}' says, but that "
                       + "field was not decoded as a number.";
            count = (int)source.Number.Value;
        }
        else
        {
            int elementLength = FixedLengthOf(group.Fields);
            if (elementLength > 0)
            {
                count = (payload.Length - cursor) / elementLength;
            }
            else
            {
                // Elements of differing sizes, so how many there are is only known by walking
                // them. A selected-meter reply is exactly this: each meter states its own width.
                return ReadUntilExhausted(group, payload, ref cursor, startOffset, path, values);
            }
        }

        for (int index = 0; index < count; index++)
        {
            string elementPath = $"{path}[{index}]";
            string? failure = ReadFields(
                group.Fields, payload, ref cursor, startOffset, elementPath, values);
            if (failure is not null)
                return failure;
        }

        values.Add(new SasValue($"{path}.count", group, startOffset + cursor, [], count));
        return null;
    }

    /// <summary>
    /// Reads elements of a repeating group one at a time until the reply runs out. Used where the
    /// elements are not all the same size, so their number cannot be worked out by division.
    /// </summary>
    private static string? ReadUntilExhausted(
        SasField group,
        ReadOnlySpan<byte> payload,
        ref int cursor,
        int startOffset,
        string path,
        List<SasValue> values)
    {
        int count = 0;
        while (cursor < payload.Length)
        {
            int before = cursor;
            string? failure = ReadFields(
                group.Fields, payload, ref cursor, startOffset, $"{path}[{count}]", values);
            if (failure is not null)
                return failure;

            // A element that consumed nothing would repeat for ever. Stopping with a reason beats
            // hanging, and beats reporting a count that says nothing about the bytes.
            if (cursor == before)
                return $"Group '{path}' made no progress at offset {startOffset + cursor}, so the "
                       + "reply cannot be walked to its end.";

            count++;
        }

        values.Add(new SasValue($"{path}.count", group, startOffset + cursor, [], count));
        return null;
    }

    private static int FixedLengthOf(IReadOnlyList<SasField> fields)
    {
        int total = 0;
        foreach (SasField field in fields)
        {
            if (field.IsGroup || field.PresentWhen is not null
                || field.Length == SasField.Remaining || field.LengthFromField is not null
                || field.MeterWidthFromField is not null)
            {
                return -1;
            }

            total += field.Length;
        }

        return total;
    }

    private static string? ReadScalar(
        SasField field,
        string path,
        ReadOnlySpan<byte> bytes,
        int offset,
        out SasValue value)
    {
        value = default!;
        long? number = null;
        string? text = null;

        switch (field.Encoding)
        {
            case SasFieldEncoding.Binary:
            case SasFieldEncoding.Bits:
                number = ReadInteger(bytes, field.ByteOrder);
                break;

            case SasFieldEncoding.Bcd:
                if (!TryReadBcd(bytes, out long bcd, out int badByte))
                    return $"Field '{path}' is declared as binary coded decimal but byte "
                           + $"{offset + badByte} is ${bytes[badByte]:X2}, which is not two "
                           + "decimal digits.";
                number = bcd;
                text = bcd.ToString();
                break;

            case SasFieldEncoding.Ascii:
                text = Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ');
                break;

            case SasFieldEncoding.AsciiNumber:
                text = Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ').Trim();
                if (text.Length > 0 && long.TryParse(text, out long parsed))
                    number = parsed;
                else if (text.Length > 0)
                    return $"Field '{path}' is declared as decimal digits held as text but reads "
                           + $"as '{text}'.";
                break;

            case SasFieldEncoding.Raw:
                break;

            default:
                return $"Field '{path}' has an encoding this decoder does not implement.";
        }

        value = new SasValue(path, field, offset, bytes.ToArray(), number, text,
            Violated(field, number, bytes.Length));
        return null;
    }

    private static long ReadInteger(ReadOnlySpan<byte> bytes, SasByteOrder order)
    {
        long result = 0;
        if (order == SasByteOrder.LittleEndian)
        {
            for (int i = bytes.Length - 1; i >= 0; i--)
                result = (result << 8) | bytes[i];
        }
        else
        {
            foreach (byte b in bytes)
                result = (result << 8) | b;
        }

        return result;
    }

    private static bool TryReadBcd(ReadOnlySpan<byte> bytes, out long value, out int badByte)
    {
        value = 0;
        badByte = -1;
        for (int i = 0; i < bytes.Length; i++)
        {
            int high = bytes[i] >> 4;
            int low = bytes[i] & 0x0F;
            if (high > 9 || low > 9)
            {
                badByte = i;
                return false;
            }

            value = (value * 100) + (high * 10) + low;
        }

        return true;
    }

    private static string? Violated(SasField field, long? number, int byteLength)
    {
        SasConstraint? constraint = field.Constraint;
        if (constraint is null)
            return null;

        if (constraint.Length is { } expected && byteLength != expected)
            return $"expected {expected} byte(s), found {byteLength}";

        if (number is null)
            return null;

        long value = number.Value;
        if (constraint.Minimum is { } minimum && value < minimum)
            return $"below the permitted minimum of {minimum}";
        if (constraint.Maximum is { } maximum && value > maximum)
            return $"above the permitted maximum of {maximum}";
        if (constraint.OneOf is { Count: > 0 } allowed && !allowed.Contains(value))
            return $"not one of {string.Join(", ", allowed)}";

        return null;
    }

    private static bool Holds(SasCondition condition, List<SasValue> values, string? prefix, int remaining)
    {
        long actual;
        if (condition.Field == SasCondition.RemainingBytes)
        {
            actual = remaining;
        }
        else
        {
            SasValue? source = null;
            if (prefix is not null)
                source = values.FirstOrDefault(v => v.Path == $"{prefix}.{condition.Field}");
            source ??= values.FirstOrDefault(v => v.Path == condition.Field);

            if (source?.Number is not { } number)
                return false;
            actual = number;
        }

        return condition.Comparison switch
        {
            SasComparison.Equal => actual == condition.Value,
            SasComparison.NotEqual => actual != condition.Value,
            SasComparison.LessThan => actual < condition.Value,
            SasComparison.AtMost => actual <= condition.Value,
            SasComparison.GreaterThan => actual > condition.Value,
            SasComparison.AtLeast => actual >= condition.Value,
            SasComparison.BitsSet => (actual & condition.Value) == condition.Value,
            SasComparison.BitsClear => (actual & condition.Value) == 0,
            _ => false,
        };
    }
}
