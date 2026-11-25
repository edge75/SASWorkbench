using System.Text;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>
/// The note acceptor switch ($06, $07), the ROM signature ($21), the ticket text ($7C, $7D), the custom transfer ticket
/// data ($76), the cash-out limit ($A4) and the two menus, each as the library and the platform do them.
/// </summary>
public sealed partial class FakeEgm
{
    // What the machine signs: a stand-in for the program memory the platform hashes, fixed so a seed always gives one signature.
    private static readonly byte[] ProgramImage = Encoding.ASCII.GetBytes("SasWorkbench software machine program image 1.0");

    // The lengths sExtendedTicketData and sCustomAftTicketData give each line (libsas_db.h), the terminator included for $76.
    private const int TicketLineMax = 40;
    private const int TicketTitleMax = 16;

    private bool _acceptorDisabledByHost;
    private readonly Dictionary<byte, string> _extendedTicketText = [];
    private readonly string[] _ticketDataLines = [string.Empty, string.Empty, string.Empty];
    private bool _extendedTicketDataSet;
    private bool _ticketDataSet;
    private readonly SortedDictionary<byte, string> _customTicketData = [];
    private bool _attendantMenuOpen;
    private bool _operatorMenuOpen;

    /// <summary>The signature this machine computes for a seed: the protocol's check sequence over its program, started from the seed.</summary>
    public static ushort RomSignatureFor(ushort seed)
    {
        // The protocol's own CRC-16 step, started from the seed rather than from zero, as QSP chains its CRC16 targets.
        uint crc = seed;
        foreach (byte c in ProgramImage)
        {
            uint q = (crc ^ c) & 0x0F;
            crc = (crc >> 4) ^ (q * 0x1081);
            q = (crc ^ (uint)(c >> 4)) & 0x0F;
            crc = (crc >> 4) ^ (q * 0x1081);
        }

        return (ushort)crc;
    }

    /// <summary>Whether the host has switched the note acceptor off with $07.</summary>
    public bool AcceptorDisabledByHost
    {
        get
        {
            lock (_gate)
                return _acceptorDisabledByHost;
        }
    }

    /// <summary>$06 and $07 set or clear the platform's network disable bit, which it keeps across a restart (QSP SasNetwork_Callbacks.hpp:486).</summary>
    private bool ApplyAcceptorSwitch(byte command)
    {
        lock (_gate)
            _acceptorDisabledByHost = command == (byte)SasLongPollCode.LP_DISABLEBILLACCEPTOR;
        return true;
    }

    /// <summary>lp21_RomSignature after the address and check sequence: acknowledged, then the application registers the signature for this channel.</summary>
    private void AnswerRomSignature(MachineLink link, ReadOnlySpan<byte> frame)
    {
        if (frame[0] != link.Address.Value || frame.Length < 6)
            return;

        ushort seed = (ushort)((frame[2] << 8) | frame[3]);
        Send(link, [SasSymbol.Data8(link.Address.Value)]);

        // QSP hands the seed to its hash chain or the game and registers what comes back (SasNetwork_Callbacks.hpp:608, :758).
        lock (_gate)
        {
            link.RomSignature = RomSignatureFor(seed);
            link.RomSignatureSent = false;
        }
    }

    /// <summary>Sends the pending signature in place of an exception, as the library's poll answer does while one is pending (libsas_threads.cpp:3369).</summary>
    private bool SendPendingRomSignature(MachineLink link)
    {
        ushort signature;
        lock (_gate)
        {
            if (link.RomSignature is not { } pending)
                return false;
            signature = pending;
            link.RomSignatureSent = true;
        }

        link.Frame[0] = link.Address.Value;
        link.Frame[1] = (byte)SasLongPollCode.LP_ROMVERIFICATION;
        link.Frame[2] = (byte)(signature >> 8);
        link.Frame[3] = (byte)signature;
        ushort crc = SasCrc.Compute(link.Frame.AsSpan(0, 4));
        link.Frame[4] = SasCrc.LowByte(crc);
        link.Frame[5] = SasCrc.HighByte(crc);
        for (int i = 0; i < 6; i++)
            link.Outbound[i] = SasSymbol.Data8(link.Frame[i]);

        Send(link, link.Outbound.AsSpan(0, 6));
        return true;
    }

    /// <summary>Any message but a repeated general poll acknowledges a signature already sent, and it is no longer pending (libsas_threads.cpp:332, :423, :805).</summary>
    private static void NoteRomSignatureAcknowledged(MachineLink link)
    {
        if (link.RomSignatureSent)
        {
            link.RomSignature = null;
            link.RomSignatureSent = false;
        }
    }

    /// <summary>A loop break means the signature was never acknowledged, so it goes again (libsas_threads.cpp:129).</summary>
    private static void NoteRomSignatureUnacknowledged(MachineLink link) => link.RomSignatureSent = false;

    /// <summary>
    /// lp7C_SetExtendedTicketData after the category gate: an addressed or broadcast poll, refused on a bad check sequence
    /// only when addressed, each element stored, and answered with the status flag only when addressed.
    /// </summary>
    private void AnswerExtendedTicketData(MachineLink link, ReadOnlySpan<byte> frame)
    {
        if (!FramedByLength(frame))
            return;

        bool addressed = frame[0] == link.Address.Value;
        if (!addressed && frame[0] != SasAddress.Sync.Value)
            return;

        int length = frame[2];
        var lines = new Dictionary<byte, string>();
        bool valid = true;
        for (int i = 3; i < length + 3 && valid;)
        {
            byte code = frame[i++];
            int size = frame[i++];
            int max = code switch
            {
                0x00 or 0x01 or 0x02 => TicketLineMax,
                0x10 or 0x20 => TicketTitleMax,
                _ => -1,
            };

            // An unknown code or a line too long ends the poll unanswered, with the flag cleared (lp.cpp:7442, :7452).
            valid = max >= 0 && size <= max && i + size <= length + 3;
            if (valid)
                lines[code] = Encoding.ASCII.GetString(frame.Slice(i, size));
            i += size;
        }

        bool status;
        lock (_gate)
        {
            if (!valid)
            {
                _extendedTicketDataSet = false;
                return;
            }

            if (length > 0)
            {
                foreach ((byte code, string text) in lines)
                    _extendedTicketText[code] = text;
                _extendedTicketDataSet = true;
            }

            status = _extendedTicketDataSet;
            if (addressed)
                _extendedTicketDataSet = false;
        }

        if (addressed)
            SendStatusFlag(link, (byte)SasLongPollCode.LP_SETEXTENDEDTICKET, status);
    }

    /// <summary>lp7D_SetTicketData: the host identifier, the expiration and three lines in order, the status flag answered when addressed.</summary>
    private void AnswerTicketData(MachineLink link, ReadOnlySpan<byte> frame)
    {
        if (!FramedByLength(frame))
            return;

        bool addressed = frame[0] == link.Address.Value;
        if (!addressed && frame[0] != SasAddress.Sync.Value)
            return;

        int length = frame[2];
        string[] lines = [string.Empty, string.Empty, string.Empty];
        if (length == 1)
        {
            // A length of one is refused and clears the flag (lp.cpp:7579).
            lock (_gate)
                _ticketDataSet = false;
            return;
        }

        if (length > 2)
        {
            int line = 0;
            for (int i = 6; i < length + 3; line++)
            {
                int size = frame[i++];

                // A fourth line ends the poll with nothing stored and nothing answered (lp.cpp:7614).
                if (line > 2)
                    return;
                if (size > TicketLineMax || i + size > length + 3)
                {
                    lock (_gate)
                        _ticketDataSet = false;
                    return;
                }

                lines[line] = Encoding.ASCII.GetString(frame.Slice(i, size));
                i += size;
            }
        }

        bool status;
        lock (_gate)
        {
            if (length > 0)
            {
                // A length of two keeps the identifier and clears the rest (lp.cpp:7639).
                Array.Copy(lines, _ticketDataLines, 3);
                _ticketDataSet = true;
            }

            status = _ticketDataSet;
            if (addressed)
                _ticketDataSet = false;
        }

        if (addressed)
            SendStatusFlag(link, (byte)SasLongPollCode.LP_SETTICKETDATA, status);
    }

    /// <summary>Whether a message is as long as its length byte says; the library's receiver waits for the rest and never hands a short one on.</summary>
    private static bool FramedByLength(ReadOnlySpan<byte> frame) => frame.Length >= 5 && frame[2] + 5 == frame.Length;

    private void SendStatusFlag(MachineLink link, byte command, bool status)
    {
        link.Frame[0] = link.Address.Value;
        link.Frame[1] = command;
        link.Frame[2] = status ? (byte)1 : (byte)0;
        SendFrame(link, 3);
    }

    /// <summary>
    /// lp76_SetCustomAftTicketData after the category gate: silent without transfers, refused on a line too long, a set or a
    /// clear stored, an unknown function ignored, and answered with the codes held only when addressed.
    /// </summary>
    private void AnswerCustomTicketData(MachineLink link, ReadOnlySpan<byte> frame)
    {
        if (!FramedByLength(frame))
            return;

        bool addressed = frame[0] == link.Address.Value;
        lock (_gate)
        {
            if (!_aftOn)
                return;
        }

        if (!addressed && frame[0] != SasAddress.Sync.Value)
            return;

        int length = frame[2];
        byte function = frame[3];
        var held = new SortedDictionary<byte, string>();
        lock (_gate)
        {
            foreach ((byte code, string text) in _customTicketData)
                held[code] = text;
        }

        if (function == 0x00)
        {
            for (int i = 4; i < length + 3;)
            {
                byte code = frame[i];
                int size = frame[i + 1];
                int max = code switch
                {
                    0x00 or 0x01 or 0x02 => TicketLineMax + 1,
                    0x03 => 4,
                    0x10 => TicketTitleMax + 1,
                    _ => 0,
                };

                // A line as long as its buffer, or a code the library has no buffer for, is refused (lp.cpp:6913).
                if (size >= max)
                {
                    Reject(link);
                    return;
                }

                held[code] = Encoding.ASCII.GetString(frame.Slice(Math.Min(i + 2, frame.Length), Math.Max(0, Math.Min(size, frame.Length - i - 2))));
                i += size + 2;
            }
        }
        else if (function == 0x80)
        {
            held.Clear();
        }
        else
        {
            return;
        }

        lock (_gate)
        {
            _customTicketData.Clear();
            foreach ((byte code, string text) in held)
                _customTicketData[code] = text;
        }

        // QSP keeps nothing of it, as it prints no transfer tickets (SasNetwork_Callbacks.hpp:159).
        if (!addressed)
            return;

        int at = 3;
        link.Frame[0] = link.Address.Value;
        link.Frame[1] = (byte)SasLongPollCode.LP_AFT_SETCUSTOMTICKET;
        foreach ((byte code, string text) in held)
        {
            if (text.Length > 0)
                link.Frame[at++] = code;
        }

        link.Frame[2] = (byte)(at - 3);
        SendFrame(link, at);
    }

    private void SendFrame(MachineLink link, int length)
    {
        ushort crc = SasCrc.Compute(link.Frame.AsSpan(0, length));
        link.Frame[length] = SasCrc.LowByte(crc);
        link.Frame[length + 1] = SasCrc.HighByte(crc);
        for (int i = 0; i < length + 2; i++)
            link.Outbound[i] = SasSymbol.Data8(link.Frame[i]);
        Send(link, link.Outbound.AsSpan(0, length + 2));
    }

    /// <summary>
    /// The location and address lines a ticket prints: $7C's where it set one, else $7D's, else the operator's, which the
    /// platform leaves empty (QSP SasNetwork_Ticket.hpp:1229, TicketDataProvenance.h:116).
    /// </summary>
    private (string Location, string Address1, string Address2) TicketText()
    {
        lock (_gate)
        {
            string Resolve(byte code, int line) =>
                _extendedTicketText.TryGetValue(code, out string? text) && text.Length > 0 ? text : _ticketDataLines[line];

            return (Resolve(0x00, 0), Resolve(0x01, 1), Resolve(0x02, 2));
        }
    }

    /// <summary>A person enters or leaves a menu; the platform reports $82 and $83, or $84 and $85, once each way.</summary>
    /// <returns>Null when done, or why the menu was already where it was asked to go.</returns>
    public string? SetMenu(MachineMenu menu, bool enter)
    {
        lock (_gate)
        {
            ref bool open = ref menu == MachineMenu.Attendant ? ref _attendantMenuOpen : ref _operatorMenuOpen;
            if (open == enter)
                return $"the {menu.ToString().ToLowerInvariant()} menu is already {(enter ? "open" : "closed")}";
            open = enter;
        }

        // The attendant key's condition and the operator mode condition (QSP SasNetwork_Conditions.hpp:943, :95).
        RaiseException(menu == MachineMenu.Attendant
            ? enter ? SasExceptionCode.DisplayMetersOrAttendantMenuEntered : SasExceptionCode.DisplayMetersOrAttendantMenuExited
            : enter ? SasExceptionCode.SelfTestOrOperatorMenuEntered : SasExceptionCode.SelfTestOrOperatorMenuExited);
        return null;
    }

    /// <summary>What a memory clear does to all of this: the acceptor on, the host's ticket text and custom data gone, the menus shut.</summary>
    private void ClearMiscLocked()
    {
        _acceptorDisabledByHost = false;
        _extendedTicketText.Clear();
        Array.Fill(_ticketDataLines, string.Empty);
        _extendedTicketDataSet = false;
        _ticketDataSet = false;
        _customTicketData.Clear();
        _attendantMenuOpen = false;
        _operatorMenuOpen = false;
    }
}
