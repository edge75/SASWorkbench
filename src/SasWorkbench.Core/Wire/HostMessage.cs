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
    /// Writes a long poll. A command with no data is two symbols and carries no check sequence;
    /// a command with data always carries one, because that is the rule the machine applies when
    /// it decides whether to answer.
    /// </summary>
    public static int WriteLongPoll(
        Span<SasSymbol> destination,
        SasAddress address,
        byte command,
        ReadOnlySpan<byte> data = default)
    {
        int bodyLength = 2 + data.Length;
        bool needsCrc = data.Length > 0;
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
    /// True when a host message this long is one the machine will check a frame check sequence on.
    /// Messages of two symbols or fewer carry none.
    /// </summary>
    public static bool CarriesCrc(int symbolCount) => symbolCount > 2;
}
