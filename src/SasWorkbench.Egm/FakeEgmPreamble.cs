using System.Collections.Frozen;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>
/// Long poll $09, which switches one game, and the multi-denomination preamble $B0, as the library handles both.
/// </summary>
public sealed partial class FakeEgm
{
    // The preamble's error codes (libsas.h, MDPEC_*): not supported, badly formed, not per denomination, not in that form, not a player denomination.
    private const byte MdpeNotSupported = 0x01;
    private const byte MdpeImproperlyFormatted = 0x02;
    private const byte MdpeNotMultiDenomAware = 0x03;
    private const byte MdpeNotInThatFormat = 0x04;
    private const byte MdpeNotAPlayerDenomination = 0x05;

    // The base commands the preamble names as not answered per denomination (lp.cpp:9897 to :10010).
    private static readonly FrozenSet<byte> NotMultiDenomAware = new byte[]
    {
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x13,
        0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1E, 0x1F, 0x20, 0x21, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x31, 0x32, 0x33,
        0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44,
        0x45, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x4C, 0x4D, 0x4F, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x57, 0x58,
        0x6E, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x7B, 0x7C, 0x7D, 0x7E, 0x7F, 0x80, 0x83, 0x84, 0x85,
        0x86, 0x87, 0x8A, 0x8B, 0x8C, 0x8E, 0x8F, 0x90, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA0, 0xA4,
        0xA8, 0xAA, 0xB0, 0xB1, 0xB2, 0xB3, 0xB4, 0xB6, 0xB7,
    }.ToFrozenSet();

    // The single meters the preamble answers at a denomination, each by the library's shared handler (lp.cpp:10043 to :10064).
    private static readonly FrozenDictionary<byte, SasMeterCode> PreambleMeters = new Dictionary<byte, SasMeterCode>
    {
        [0x11] = SasMeterCode.TotalCoinInCredits,
        [0x12] = SasMeterCode.TotalCoinOutCredits,
        [0x14] = SasMeterCode.TotalJackpotCredits,
        [0x15] = SasMeterCode.GamesPlayed,
        [0x16] = SasMeterCode.GamesWon,
        [0x17] = SasMeterCode.GamesLost,
    }.ToFrozenDictionary();

    // The game status of a single-game machine, which long poll $09 sets as it sets any game's (lp.cpp:563).
    private bool _singleGameStatus = true;

    /// <summary>
    /// Long poll $09: game zero and a game the machine does not have are refused (lp.cpp:523); otherwise the status
    /// is set and the platform switches every denomination of the game for the host (QSP SasNetwork_Callbacks.hpp:559).
    /// </summary>
    /// <returns>False when the library refuses it.</returns>
    private bool ApplyEnableDisableGame(ReadOnlySpan<byte> frame, byte denomination = 0)
    {
        if (frame.Length < 5)
            return false;

        long number = BcdNumber(frame[2], frame[3]);
        bool enable = frame[4] != 0;
        lock (_gate)
        {
            if (!HasGameTable)
            {
                if (number != 1)
                    return false;
                _singleGameStatus = enable;
                return true;
            }

            if (number == 0 || GameLocked(number) is not { } game)
                return false;

            // A single-denomination game behind the preamble sets its status as a bare poll does (lp.cpp:551, :563).
            game.Status = enable;
            if (denomination == 0 || denomination == game.Code)
                game.HostEnabled = enable;
        }

        return true;
    }

    /// <summary>
    /// Long poll $B0, answered as <c>lpB0_MultiDenomPreamble</c> does: silent without the extensions bit, then the base
    /// command checked in the library's order, and the reply wrapped with the denomination and the base command.
    /// </summary>
    private void AnswerPreamble(MachineLink link, ReadOnlySpan<byte> frame)
    {
        if (!HasGameTable)
        {
            Ignore(link, SasLayouts.PreambleCommand);
            return;
        }

        if (frame.Length < 7)
        {
            Reject(link);
            return;
        }

        byte denomination = frame[3];
        byte baseCommand = frame[4];

        // The base command's own message: address, command and its data, with room for the check sequence it would carry.
        byte[] inner = [frame[0], baseCommand, .. frame[5..^2], 0x00, 0x00];

        (byte Command, byte[] Payload) answer = PreambleAnswer(link, baseCommand, denomination, inner);
        byte[] reply = [link.Address.Value, SasLayouts.PreambleCommand, (byte)(2 + answer.Payload.Length), denomination,
            answer.Command, .. answer.Payload];
        SendFrame(link, reply);
    }

    /// <summary>What the preamble wraps: the base command and its reply from the length byte on, or zero and a reason.</summary>
    private (byte Command, byte[] Payload) PreambleAnswer(MachineLink link, byte baseCommand, byte denomination, byte[] inner)
    {
        if (NotMultiDenomAware.Contains(baseCommand))
            return (0, [MdpeNotMultiDenomAware]);

        if (baseCommand == 0x09)
            return PreambleEnableDisable(link, denomination, inner);

        (Dictionary<string, object>? values, byte error) = baseCommand switch
        {
            0x2F => TableMetersForGame(inner, preamble: true, denomination),
            0x6F or 0xAF => TableSelectedMeters(inner, preamble: true, denomination),
            0x56 => (TableEnabledGames(preamble: true, denomination), (byte)0),
            0xB5 => PreambleExtendedGameInformation(inner, denomination),
            _ when PreambleMeters.TryGetValue(baseCommand, out SasMeterCode meter) =>
                (new Dictionary<string, object> { [SasLayouts.For(baseCommand)!.Response[0].Name] = ReadGameMeter(0, denomination, (ushort)meter) }, (byte)0),
            _ => (null, MdpeNotSupported),
        };

        if (values is null || ReferenceEquals(values, NoAnswer))
            return (0, [error == 0 ? MdpeImproperlyFormatted : error]);

        // A denomination game zero does not offer is not a player denomination (lp.cpp:10144); $56 names no game.
        bool namesMachine = baseCommand is 0xB5 or (>= 0x11 and <= 0x17);
        if (namesMachine && !PreambleDenominationIsValid(inner, baseCommand, denomination))
            return (0, [MdpeNotAPlayerDenomination]);

        return (baseCommand, EncodeBaseReply(link, baseCommand, values));
    }

    /// <summary>
    /// $09 behind the preamble: a game the machine lacks, game zero or a denomination the game is not played at is
    /// error 05 and not executed (lp.cpp:10015); otherwise the base command's acknowledgement, the address.
    /// </summary>
    private (byte Command, byte[] Payload) PreambleEnableDisable(MachineLink link, byte denomination, byte[] inner)
    {
        long number = inner.Length >= 4 ? BcdNumber(inner[2], inner[3]) : 0;
        bool valid;
        lock (_gate)
            valid = number != 0 && GameLocked(number) is { } game && (denomination == 0 || game.Code == denomination);

        if (!valid || !ApplyEnableDisableGame(inner, denomination))
            return (0, [MdpeNotAPlayerDenomination]);

        return (0x09, [link.Address.Value]);
    }

    /// <summary>$B5 behind the preamble: a game the machine lacks is a badly formed poll (lp.cpp:10114, :10132).</summary>
    private (Dictionary<string, object>? Values, byte Error) PreambleExtendedGameInformation(byte[] inner, byte denomination)
    {
        SasMessageLayout layout = SasLayouts.For(0xB5)!;
        Dictionary<string, object>? values = TableExtendedGameInformation(layout, inner, preamble: true, denomination);
        return ReferenceEquals(values, NoAnswer) ? (null, MdpeImproperlyFormatted) : (values, (byte)0);
    }

    /// <summary>Whether the game a base command names offers this denomination; zero always does (lp.cpp:9799).</summary>
    private bool PreambleDenominationIsValid(byte[] inner, byte baseCommand, byte denomination)
    {
        if (denomination == 0)
            return true;

        long number = baseCommand == 0xB5 && inner.Length >= 4 ? BcdNumber(inner[2], inner[3]) : 0;
        lock (_gate)
            return number == 0 ? MachineOffersLocked(denomination) : GameLocked(number)?.Code == denomination;
    }

    /// <summary>A base reply's bytes from its length byte on, as its layout writes them.</summary>
    private static byte[] EncodeBaseReply(MachineLink link, byte baseCommand, Dictionary<string, object> values)
    {
        byte[] buffer = new byte[HostMessage.MaxSymbols];
        int length = SasMessageEncoder.WriteResponse(SasLayouts.For(baseCommand)!, link.Address, values, buffer);
        return buffer[2..(length - 2)];
    }

    /// <summary>Sends a whole reply, adding its check sequence.</summary>
    private static void SendFrame(MachineLink link, byte[] reply)
    {
        int length = reply.Length;
        reply.CopyTo(link.Frame, 0);
        ushort crc = SasCrc.Compute(link.Frame.AsSpan(0, length));
        link.Frame[length++] = SasCrc.LowByte(crc);
        link.Frame[length++] = SasCrc.HighByte(crc);

        for (int i = 0; i < length; i++)
            link.Outbound[i] = SasSymbol.Data8(link.Frame[i]);

        Send(link, link.Outbound.AsSpan(0, length));
    }
}
