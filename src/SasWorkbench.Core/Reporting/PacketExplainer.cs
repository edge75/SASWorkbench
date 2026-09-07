using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Reporting;

/// <summary>
/// Explains recorded packets from their own bytes.
/// </summary>
/// <remarks>
/// <para>
/// Stateful over a sequence, because two things a packet means are only knowable from the packets
/// around it. A one-byte answer of <c>$4A</c> is exception <c>$4A</c> on a machine at any other
/// address and an acknowledgement on the machine at address <c>$4A</c>, and the poll address is
/// learnable from the host's own general polls; a reply's echoed address can then be checked
/// against it. Where a record never showed the address, both readings are stated and neither is
/// chosen.
/// </para>
/// <para>
/// The note the engine wrote is carried through rather than re-derived, and everything else comes
/// out of the symbols. Nothing is read from the machine, the script or the environment.
/// </para>
/// </remarks>
public sealed class PacketExplainer
{
    private SasAddress? _address;

    /// <param name="address">
    /// The machine's poll address, when a caller already knows it. Leaving it out is normal: it is
    /// learned from the first general poll or long poll in the record.
    /// </param>
    public PacketExplainer(SasAddress? address = null) => _address = address;

    /// <summary>The address in force, once the record has shown one.</summary>
    public SasAddress? Address => _address;

    /// <summary>Explains a whole sequence, in the order it was recorded.</summary>
    public static IReadOnlyList<PacketExplanation> ExplainAll(
        IEnumerable<WireRecord> records, SasAddress? address = null)
    {
        ArgumentNullException.ThrowIfNull(records);

        var explainer = new PacketExplainer(address);
        return records.Select(explainer.Explain).ToList();
    }

    /// <summary>Explains one packet.</summary>
    public PacketExplanation Explain(WireRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        byte[] bytes = record.Symbols.Select(s => s.Data).ToArray();
        var findings = new List<string>();
        WirePurpose.TryRead(record.Note, out string purpose);

        var built = record.Direction == WireDirection.Transmit
            ? FromHost(record, bytes, findings)
            : FromMachine(record, bytes, findings);

        return built with
        {
            Sequence = record.Sequence,
            Direction = record.Direction,
            Channel = record.Channel,
            At = record.Elapsed,
            Hex = record.ToHex(),
            Note = record.Note,
            Purpose = purpose.Length == 0 ? null : purpose,
            Findings = findings,
        };
    }

    private PacketExplanation FromHost(WireRecord record, byte[] bytes, List<string> findings)
    {
        CheckHostWakeupBits(record, findings);

        if (bytes.Length == 0)
        {
            return Bare("nothing was recorded for this packet", PacketMeaning.Unknown,
                limit: "a packet with no symbols cannot be read");
        }

        if (bytes.Length == 1)
            return OneByteFromHost(bytes[0]);

        byte command = bytes[1];
        _address ??= Poll(bytes[0]) is { } learned ? learned : _address;

        CheckCheckSequence(bytes, findings);

        SasMessageLayout? layout = SasLayouts.For(command);
        string name = CatalogueName(command);
        string headline = $"long poll ${command:X2} — {name}, to the machine at ${bytes[0]:X2}";

        if (layout is null)
        {
            return Bare(headline, Named(command), command: command,
                limit: NoLayoutReason(command));
        }

        SasDecoded decoded = SasMessageDecoder.DecodeRequest(layout, bytes);
        return Decoded($"long poll ${command:X2} — {layout.Description}", decoded, command, findings);
    }

    private PacketExplanation OneByteFromHost(byte value)
    {
        if ((value & HostMessage.GeneralPollFlag) == 0)
        {
            // Every one-symbol host message is a poll and every poll carries bit 7. This is not
            // one, and naming what it would have to be beats inventing a message for it.
            return Bare(
                $"one symbol ${value:X2} with the general-poll bit clear, which is no host message "
                + "this build writes",
                PacketMeaning.Unknown,
                limit: "a single host symbol is a general poll, and a general poll sets bit 7");
        }

        if (value == HostMessage.GeneralPollFlag)
        {
            return Bare(
                "synchronisation poll — a general poll of address zero, which resynchronises every "
                + "machine on the link and which none of them answers",
                PacketMeaning.Explained);
        }

        var address = SasAddress.Of((byte)(value & ~HostMessage.GeneralPollFlag));
        _address ??= address;
        return Bare(
            $"general poll of the machine at ${address.Value:X2} — asks it for the next exception "
            + "it has queued, or for silence if it has none",
            PacketMeaning.Explained);
    }

    private PacketExplanation FromMachine(WireRecord record, byte[] bytes, List<string> findings)
    {
        if (record.Symbols.Any(s => s.Wakeup))
        {
            findings.Add(
                "a received symbol carries the wakeup bit; only a host may set it, so this is a "
                + "misbehaving machine or a host hearing its own echo");
        }

        if (bytes.Length == 0)
        {
            return Bare("nothing was recorded for this packet", PacketMeaning.Unknown,
                limit: "a packet with no symbols cannot be read");
        }

        if (bytes.Length == 1)
            return OneByteFromMachine(bytes[0]);

        byte command = bytes[1];
        if (_address is { } known && bytes[0] != known.Value)
        {
            findings.Add(
                $"the reply is addressed ${bytes[0]:X2} and the machine being polled is "
                + $"${known.Value:X2}");
        }

        CheckCheckSequence(bytes, findings);

        SasMessageLayout? layout = SasLayouts.For(command);
        string headline = $"reply to long poll ${command:X2} — {CatalogueName(command)}";

        if (layout is null)
        {
            return Bare(headline, Named(command), command: command,
                limit: NoLayoutReason(command));
        }

        if (layout.Shape != SasResponseShape.Data)
        {
            findings.Add(
                $"long poll ${command:X2} is answered with an acknowledgement rather than data, "
                + $"and {bytes.Length} bytes came back");
        }

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, bytes);
        return Decoded($"reply to long poll ${command:X2} — {layout.Description}",
            decoded, command, findings);
    }

    private PacketExplanation OneByteFromMachine(byte value)
    {
        if (value == 0x00)
        {
            return Bare(
                "no exception — the machine has nothing queued to report",
                PacketMeaning.Explained);
        }

        if (_address is { } known)
        {
            if (value == known.Value)
            {
                return Bare(
                    $"acknowledgement from the machine at ${value:X2} — it accepted the command "
                    + "and has nothing to send back",
                    PacketMeaning.Explained);
            }

            if (value == (byte)(known.Value | HostMessage.GeneralPollFlag))
            {
                return Bare(
                    $"refusal from the machine at ${known.Value:X2} — bit 7 set on its own address "
                    + "means it will not do what was asked",
                    PacketMeaning.Explained);
            }

            return Exception(value);
        }

        // Nothing in the record has shown the machine's address, so this byte has two readings and
        // choosing one would be a guess. Both are stated instead.
        return Bare(
            $"one byte ${value:X2} — either exception ${value:X2} "
            + $"{SasNames.ExceptionName(value)}, or an acknowledgement from a machine at address "
            + $"${value:X2}",
            PacketMeaning.NamedOnly,
            exception: value,
            limit: "no general poll or long poll in this record shows the machine's address, so "
                   + "which of the two this is cannot be said");
    }

    private static PacketExplanation Exception(byte code)
    {
        if (!SasCatalogue.Exceptions.TryGetValue(code, out SasExceptionInfo? info))
        {
            return Bare(
                $"exception ${code:X2}, which is in no exception list this build holds",
                PacketMeaning.Unknown,
                exception: code,
                limit: "the machine reported a code the library does not define");
        }

        return Bare(
            $"exception ${code:X2} {SasNames.Underscore(info.Symbol)} — {info.Description}",
            PacketMeaning.Explained,
            exception: code);
    }

    /// <summary>Turns a decode into an explanation, or into the reason it is not one.</summary>
    private static PacketExplanation Decoded(
        string headline, SasDecoded decoded, byte command, List<string> findings)
    {
        if (!decoded.Succeeded)
        {
            return Bare($"{headline} (the bytes do not fit the layout)",
                PacketMeaning.LayoutDidNotFit,
                command: command,
                limit: decoded.Failure,
                derivedFrom: decoded.Layout?.DerivedFrom);
        }

        SasValue? trailer = decoded["unmodelled_trailer"];
        if (trailer is not null)
        {
            findings.Add(
                $"{trailer.Bytes.Length} byte(s) beyond what the layout describes: {trailer.ToHex()}");
        }

        return new PacketExplanation
        {
            Sequence = 0,
            Direction = WireDirection.Transmit,
            Channel = SasChannel.One,
            At = TimeSpan.Zero,
            Hex = string.Empty,
            Headline = headline,
            Meaning = PacketMeaning.Explained,
            Command = command,
            DerivedFrom = decoded.Layout?.DerivedFrom,
            Fields = decoded.Values.Select(Field).ToList(),
        };
    }

    private static PacketField Field(SasValue value) => new(
        value.Path,
        value.ToHex(),
        Render(value),
        value.Field.Description,
        value.Violation);

    private static string Render(SasValue value)
    {
        string rendered = value.Text ?? value.Number?.ToString() ?? value.ToHex();
        if (rendered.Length == 0)
            rendered = "(no bytes)";

        return Measures(value.Field.Unit)
            ? $"{rendered} {SasNames.Underscore(value.Field.Unit.ToString())}"
            : rendered;
    }

    /// <summary>
    /// Whether a unit says what a number is measured in, rather than what kind of thing it is.
    /// </summary>
    /// <remarks>
    /// An index and a code are not units: "0 index" reads as a quantity of indices, and the
    /// field's own description already says which table the number is into. A meter in credits is
    /// a different matter, because the same number means different money at another denomination.
    /// </remarks>
    private static bool Measures(SasUnit unit) => unit
        is SasUnit.Credits or SasUnit.Cents or SasUnit.Dollars or SasUnit.Count
        or SasUnit.HundredthsOfPercent or SasUnit.Milliseconds or SasUnit.Seconds;

    /// <summary>An explanation with a headline and no fields.</summary>
    private static PacketExplanation Bare(
        string headline,
        PacketMeaning meaning,
        byte? command = null,
        byte? exception = null,
        string? limit = null,
        string? derivedFrom = null) => new()
        {
            Sequence = 0,
            Direction = WireDirection.Transmit,
            Channel = SasChannel.One,
            At = TimeSpan.Zero,
            Hex = string.Empty,
            Headline = headline,
            Meaning = meaning,
            Command = command,
            Exception = exception,
            Limit = limit,
            DerivedFrom = derivedFrom,
        };

    /// <summary>Whether a command with no layout is at least named somewhere.</summary>
    private static PacketMeaning Named(byte command) =>
        SasCatalogue.LongPolls.ContainsKey(command)
            ? PacketMeaning.NamedOnly
            : PacketMeaning.Unknown;

    private static string CatalogueName(byte command) =>
        SasCatalogue.LongPolls.TryGetValue(command, out SasLongPollInfo? info)
            ? info.Description
            : "in no long poll list this build holds";

    private static string NoLayoutReason(byte command) =>
        SasCatalogue.LongPolls.ContainsKey(command)
            ? $"the gaming machine library names long poll ${command:X2} and this build has not "
              + "established the layout of its bytes, so what they mean is not claimed"
            : $"long poll ${command:X2} is in no list this build holds, so it cannot even be named";

    private static void CheckHostWakeupBits(WireRecord record, List<string> findings)
    {
        if (record.Symbols.Length == 0)
            return;

        if (!record.Symbols[0].Wakeup)
        {
            findings.Add(
                "the first symbol does not carry the wakeup bit, so no machine would read this as "
                + "an address and none would answer");
        }

        for (int i = 1; i < record.Symbols.Length; i++)
        {
            if (record.Symbols[i].Wakeup)
            {
                findings.Add(
                    $"symbol {i} carries the wakeup bit, which belongs on the address byte alone");
                break;
            }
        }
    }

    /// <summary>
    /// Checks the frame check sequence, for the messages that carry one.
    /// </summary>
    /// <remarks>
    /// A message of two symbols or fewer carries none — that is the rule the machine itself
    /// applies when it decides whether to answer — so checking one there would report every bare
    /// long poll as corrupt.
    /// </remarks>
    private static void CheckCheckSequence(byte[] bytes, List<string> findings)
    {
        if (!HostMessage.CarriesCrc(bytes.Length))
            return;

        if (!SasCrc.Verify(bytes))
        {
            findings.Add(
                $"the check sequence does not check out: the last two bytes are "
                + $"{Convert.ToHexString(bytes.AsSpan(bytes.Length - 2))} and this message computes "
                + $"{Expected(bytes)}");
        }
    }

    private static string Expected(byte[] bytes)
    {
        ushort crc = SasCrc.Compute(bytes.AsSpan(0, bytes.Length - 2));
        return $"{SasCrc.LowByte(crc):X2}{SasCrc.HighByte(crc):X2}";
    }

    /// <summary>The machine an address byte with the general-poll bit set is addressing.</summary>
    private static SasAddress? Poll(byte first)
    {
        byte value = (byte)(first & ~HostMessage.GeneralPollFlag);
        return value is 0 or > 127 ? null : SasAddress.Of(value);
    }
}
