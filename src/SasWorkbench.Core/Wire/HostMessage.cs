namespace SasWorkbench.Core.Wire;

/// <summary>What kind of message the host put on the wire.</summary>
public enum HostMessageKind
{
    /// <summary>A general poll of address zero, which resynchronises every machine on the link.</summary>
    SyncPoll,

    /// <summary>A general poll of one machine, which asks for the next queued exception.</summary>
    GeneralPoll,

    /// <summary>An addressed command.</summary>
    LongPoll,
}

/// <summary>
/// Builds host messages directly into a caller-supplied buffer, so the poll loop can run
/// without allocating.
/// </summary>
/// <remarks>
/// The wakeup bit rides on the address byte and on nothing else. Every writer here enforces
/// that, because it is the one property of the link that a test cannot observe going wrong:
/// a machine that never sees an address byte simply stays silent.
/// </remarks>
public static class HostMessage
{
    /// <summary>Bit 7 of an address byte turns an addressed poll into a general poll.</summary>
    public const byte GeneralPollFlag = 0x80;

    /// <summary>
    /// Byte 1 of a general poll's answer when the machine is reporting real-time events, which is
    /// how a reply in that format is told from an ordinary long poll's answer.
    /// </summary>
    public const byte RealTimeEventIdentifier = 0xFF;

    /// <summary>
    /// Byte 1 of a busy response: the machine's address and a zero, with no echoed command and no
    /// check sequence. The one reply to a long poll that does not echo the command it answers.
    /// </summary>
    public const byte BusyResponseByte = 0x00;

    /// <summary>The longest host message: address, command, length, 255 data bytes and two check bytes.</summary>
    public const int MaxSymbols = 3 + 255 + 2;

    /// <summary>Writes a general poll of address zero. One symbol.</summary>
    public static int WriteSyncPoll(Span<SasSymbol> destination)
    {
        if (destination.Length < 1)
            throw new ArgumentException("A sync poll needs one symbol.", nameof(destination));

        destination[0] = SasSymbol.Address(GeneralPollFlag);
        return 1;
    }

    /// <summary>Writes a general poll of one machine. One symbol.</summary>
    public static int WriteGeneralPoll(Span<SasSymbol> destination, SasAddress address)
    {
        if (destination.Length < 1)
            throw new ArgumentException("A general poll needs one symbol.", nameof(destination));

        destination[0] = SasSymbol.Address((byte)(address.Value | GeneralPollFlag));
        return 1;
    }

    /// <summary>
    /// Writes a long poll. A message carrying data always ends in a check sequence, because any
    /// message longer than two bytes is checked on arrival; a command with no data ends in one
    /// only when that command requires it, which is a property of the command and not of the
    /// message's length.
    /// </summary>
    /// <param name="checkSequence">
    /// True when this command's own handler in the machine refuses a message with no check
    /// sequence, so a two-byte poll of it would be rejected. Ask
    /// <see cref="Protocol.SasCommandFraming"/> rather than deciding here: the rule is read out
    /// of the machine library, command by command, and the default of false is the protocol's
    /// own — never a stand-in for not having looked.
    /// </param>
    public static int WriteLongPoll(
        Span<SasSymbol> destination,
        SasAddress address,
        byte command,
        ReadOnlySpan<byte> data = default,
        bool checkSequence = false)
    {
        int bodyLength = 2 + data.Length;
        bool needsCrc = data.Length > 0 || checkSequence;
        int total = needsCrc ? bodyLength + 2 : bodyLength;

        if (destination.Length < total)
            throw new ArgumentException(
                $"Long poll ${command:X2} needs {total} symbols, not {destination.Length}.",
                nameof(destination));
        if (data.Length > 255)
            throw new ArgumentOutOfRangeException(nameof(data), data.Length,
                "A long poll carries at most 255 data bytes.");

        Span<byte> body = stackalloc byte[MaxSymbols];
        body[0] = address.Value;
        body[1] = command;
        data.CopyTo(body[2..]);

        int length = bodyLength;
        if (needsCrc)
            length = SasCrc.Append(body, bodyLength);

        destination[0] = SasSymbol.Address(body[0]);
        for (int i = 1; i < length; i++)
            destination[i] = SasSymbol.Data8(body[i]);

        return length;
    }

    /// <summary>
    /// True when a message this long ends in two check bytes, so a reader has a check sequence to
    /// verify. This is a fact about a run of bytes, not a rule about a command: whether a command
    /// <em>requires</em> one is <see cref="Protocol.SasCommandFraming"/>'s question, and a
    /// two-byte message that should have carried one fails here by having nothing to check.
    /// </summary>
    public static bool CarriesCrc(int symbolCount) => symbolCount > 2;
}
