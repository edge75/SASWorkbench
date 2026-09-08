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
    private readonly bool _wakeupBitCaptured;
    private SasAddress? _address;

    /// <param name="address">
    /// The machine's poll address, when a caller already knows it. Leaving it out is normal: it is
    /// learned from the first general poll or long poll in the record.
    /// </param>
    /// <param name="wakeupBitCaptured">
    /// False for imported traffic whose recorder kept only the eight data bits. Where the wakeup
    /// bit was is then not in the record at all, so it is reported as not recorded rather than as
    /// missing — the two look identical in the bytes and mean opposite things.
    /// </param>
    public PacketExplainer(SasAddress? address = null, bool wakeupBitCaptured = true)
    {
        _address = address;
        _wakeupBitCaptured = wakeupBitCaptured;
    }

    /// <summary>The address in force, once the record has shown one.</summary>
    public SasAddress? Address => _address;

    /// <summary>Explains a whole sequence, in the order it was recorded.</summary>
    public static IReadOnlyList<PacketExplanation> ExplainAll(
        IEnumerable<WireRecord> records,
        SasAddress? address = null,
        bool wakeupBitCaptured = true)
    {
        ArgumentNullException.ThrowIfNull(records);

        var explainer = new PacketExplainer(address, wakeupBitCaptured);
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
        CheckHostWakeupBits(record, findings, _wakeupBitCaptured);

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
        CheckCheckSequenceRequired(bytes, findings);

        if (!StandsAsAMessage(bytes, fromMachine: false))
            return NotAMessage(bytes, "host", fromMachine: false);

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
        if (_wakeupBitCaptured && record.Symbols.Any(s => s.Wakeup))
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

        // The busy response: the machine's address and a zero, two bytes and no check sequence.
        // It is the one answer to a long poll that echoes no command, so it has to be recognised
        // before anything tries to read a command out of the second byte.
        if (bytes.Length == 2 && bytes[1] == 0x00
            && (_address is not { } busyAt || bytes[0] == busyAt.Value))
        {
            return Bare(
                $"busy response from the machine at ${bytes[0]:X2} — it has the command and is "
                + "asking to be polled for the answer again, so this is not an answer",
                PacketMeaning.Explained);
        }

        byte command = bytes[1];
        if (_address is { } known && bytes[0] != known.Value)
        {
            findings.Add(
                $"the reply is addressed ${bytes[0]:X2} and the machine being polled is "
                + $"${known.Value:X2}");
        }

        CheckCheckSequence(bytes, findings);

        if (!StandsAsAMessage(bytes, fromMachine: true))
            return NotAMessage(bytes, "machine", fromMachine: true);

        // A machine reporting in real time answers a general poll with a whole message rather than
        // one byte, and the marker in place of the command byte is what says so. No long poll is
        // answered with it, so the two cannot be confused.
        if (command == SasEventLayouts.Marker)
            return RealTimeEvent(bytes, findings);

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

    /// <summary>
    /// Explains a real-time event message: the machine's address, the marker, an exception code,
    /// whatever that exception carries, and a check sequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three outcomes and they are kept apart. An exception the library builds with no data is
    /// explained in full by its three bytes, and saying so is not a concession — there is nothing
    /// else in the message. An exception that carries data and whose payload this build has
    /// established is decoded field by field. An exception that carries data and has no
    /// established payload is named, with the bytes quoted and nothing claimed about them.
    /// </para>
    /// <para>
    /// The no-activity event is the machine saying it has nothing queued, in the same format as
    /// everything else. It is not an exception and is not counted as one.
    /// </para>
    /// </remarks>
    private static PacketExplanation RealTimeEvent(byte[] bytes, List<string> findings) =>
        ReadEvent(bytes, findings) with { IsRealTimeEvent = true };

    private static PacketExplanation ReadEvent(byte[] bytes, List<string> findings)
    {
        if (bytes.Length < SasEventLayouts.HeaderLength + 2)
        {
            return Bare(
                $"a real-time event message of {bytes.Length} bytes, which is too short to carry "
                + "an exception code",
                PacketMeaning.Unknown,
                limit: "the marker says this is an event and there is no code byte after it");
        }

        byte code = bytes[2];
        if (code == 0x00)
        {
            return Bare(
                $"no exception, reported in the real-time event format by the machine at "
                + $"${bytes[0]:X2} — it has nothing queued",
                PacketMeaning.Explained);
        }

        string named = SasCatalogue.Exceptions.TryGetValue(code, out SasExceptionInfo? info)
            ? $"{SasNames.Underscore(info.Symbol)} — {info.Description}"
            : "which is in no exception list this build holds";
        string headline = $"real-time event ${code:X2} {named}, from the machine at ${bytes[0]:X2}";

        if (SasEventLayouts.For(code) is { } layout)
        {
            SasDecoded decoded = SasMessageDecoder.DecodeEvent(layout, bytes);
            if (!decoded.Succeeded)
            {
                return Bare($"{headline} (the bytes do not fit the payload)",
                    PacketMeaning.LayoutDidNotFit,
                    exception: code,
                    limit: decoded.Failure,
                    derivedFrom: layout.DerivedFrom);
            }

            SasValue? extra = decoded["unmodelled_trailer"];
            if (extra is not null)
            {
                findings.Add(
                    $"{extra.Bytes.Length} byte(s) beyond what the payload describes: "
                    + extra.ToHex());
            }

            return Bare(headline, PacketMeaning.Explained, exception: code,
                derivedFrom: layout.DerivedFrom) with
            {
                Fields = decoded.Values.Select(Field).ToList(),
            };
        }

        int carried = bytes.Length - SasEventLayouts.HeaderLength - 2;
        if (carried == 0)
        {
            // Most exceptions carry nothing at all, and for those the code is the whole event.
            // There is no payload to establish, so this is explained rather than merely named.
            return Bare(headline + ", carrying no data", Named(code, info), exception: code,
                limit: info is null
                    ? "the machine reported a code the library does not define"
                    : null);
        }

        return Bare($"{headline}, carrying {carried} byte(s) this build does not read",
            PacketMeaning.NamedOnly,
            exception: code,
            limit: SasEventLayouts.NeverQueued.TryGetValue(code, out string? why)
                ? $"exception ${code:X2} carries data and {why}"
                : $"exception ${code:X2} carries data and this build has established no payload "
                  + "for it, so what those bytes mean is not claimed");
    }

    /// <summary>Whether an exception with no payload is at least named somewhere.</summary>
    private static PacketMeaning Named(byte code, SasExceptionInfo? info) =>
        info is null ? PacketMeaning.Unknown : PacketMeaning.Explained;

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

    /// <summary>
    /// Whether a run of bytes is one whole SAS message, by the protocol's own test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A message of more than two symbols ends in a frame check sequence, and that sequence is the
    /// only statement anything makes about where a message begins and ends — SAS delimits with
    /// silence and nothing else. When it does not check out, no byte of the frame is established,
    /// the command byte included, so reading a long poll out of the second byte is a guess.
    /// </para>
    /// <para>
    /// Two things produce such bytes and neither is repaired here. A damaged message is one. The
    /// other is a recorder that printed one message as several lines: the conformance toolkit's
    /// tracer does that, and the pieces then arrive as separate records whose second byte is
    /// whatever payload happened to land there. Stitching them would put this build's guess about
    /// the message where the recording's own fault was, and in at least one capture the pieces do
    /// not add up to a whole message anyway.
    /// </para>
    /// </remarks>
    private static bool StandsAsAMessage(byte[] bytes, bool fromMachine)
    {
        // A long poll with no data is an address and a command and nothing else, and carries no
        // check sequence. A reply is never that short: it echoes both bytes and then checks them.
        if (bytes.Length < (fromMachine ? 4 : 2))
            return false;

        return !HostMessage.CarriesCrc(bytes.Length) || SasCrc.Verify(bytes);
    }

    private static PacketExplanation NotAMessage(byte[] bytes, string end, bool fromMachine) => Bare(
        $"{bytes.Length} byte(s) from the {end} that do not stand as a SAS message, so what they "
        + "say is not read: either a damaged message or a piece of one a recorder split",
        PacketMeaning.NotAMessage,
        limit: bytes.Length < (fromMachine ? 4 : 2)
            ? "too few bytes to hold an address, a command and a check sequence, and none of the "
              + "shorter forms either"
            : "the check sequence does not check out, so no byte of this frame is established - "
              + "naming the long poll from the second byte would be a guess");

    private static void CheckHostWakeupBits(
        WireRecord record, List<string> findings, bool captured)
    {
        if (record.Symbols.Length == 0)
            return;

        // Where the bit was is not in the record, so there is nothing here to find either way.
        // The case says so once, in the line that says what it was carried over; repeating it on
        // every packet of a million-message capture would bury the findings that are real.
        if (!captured)
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
    /// <summary>
    /// Reports a host message that ends where its command requires a check sequence.
    /// </summary>
    /// <remarks>
    /// A two-byte poll of a command whose handler checks the sequence is refused by the machine,
    /// however well formed it looks, so this is a finding about the traffic rather than about the
    /// decoding. It is reported over anybody's traffic, ours included: this build sent such
    /// messages until the overlay diff said so.
    /// </remarks>
    private static void CheckCheckSequenceRequired(byte[] bytes, List<string> findings)
    {
        if (HostMessage.CarriesCrc(bytes.Length) || bytes.Length < 2)
            return;

        byte command = bytes[1];
        if (!SasCommandFraming.RequiresCheckSequence(command))
            return;

        findings.Add(
            "this message ends after the command byte and carries no check sequence, and "
            + SasCommandFraming.Explain(command)
            + " - so a machine refuses it rather than acting on it");
    }

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
