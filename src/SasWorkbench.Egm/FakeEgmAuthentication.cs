using System.Diagnostics;
using System.Numerics;
using System.Text;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>
/// Component authentication: long poll $6E, the component list and its check sequence, and exceptions $8E and $8F.
/// </summary>
/// <remarks>
/// <para>
/// The protocol half is the machine library's, check for check (<c>lp6E_SendAuthenticationInfo</c>,
/// <c>internalRegisterComponent</c>, <c>internalReplyAuthentication</c> and the fifteen-second $8F timer).
/// </para>
/// <para>
/// The application half has no platform to copy: QSP starts the library with component authentication off
/// (SasNetwork.cpp:302) and registers no component, so on its default this machine does the same and the
/// library answers $6E with silence. A machine built with it on keeps two components and authenticates them
/// with CRC-16 alone, seeded and offset as the host asks, after <see cref="FakeEgmOptions.AuthenticationTime"/>.
/// </para>
/// </remarks>
public sealed partial class FakeEgm
{
    // COMPONENT_AUTHENTICATION_BIT of the second feature byte (libsas.h:203).
    private const byte ComponentAuthenticationBit = 0x10;

    // The actions, addressing modes, statuses and methods of $6E (libsas.h:1817 to :1853).
    private const byte ActionList = 0x00, ActionReadStatus = 0x01, ActionAuthenticate = 0x02, ActionLastResult = 0x03;
    private const byte ByIndex = 0x00, ByName = 0x01;
    private const byte StatusIdle = 0x00, StatusList = 0x01, StatusRunning = 0x40, StatusComplete = 0x41;
    private const byte StatusNoComponent = 0x80, StatusInvalidCommand = 0x82;
    private const byte StatusFailed = 0xC0, StatusAborted = 0xC1, StatusNoAuthentication = 0xC2;
    private const byte StatusMethodNotSupported = 0xC3, StatusInvalidData = 0xC4, StatusNothingToReport = 0xFF;
    private const uint MethodCrc16 = 0x01, MethodCrc32 = 0x02, MethodMd5 = 0x04, MethodKobetron1 = 0x08;
    private const uint MethodKobetron2 = 0x10, MethodSha1 = 0x20, MethodSha256 = 0x40;

    // The statuses a read reports once and then forgets (lp.cpp:3867, :4075).
    private static readonly byte[] ReportedOnce =
    [
        StatusComplete, StatusFailed, StatusAborted, StatusNoAuthentication, StatusMethodNotSupported,
        StatusInvalidData, 0xC5,
    ];

    // $8F goes again every fifteen seconds until the result is read: 75 ticks of the library's 200 ms timer (libsas_internal.cpp:6333).
    private static readonly TimeSpan AuthenticationReissue = TimeSpan.FromSeconds(15);

    /// <summary>One registered component, as the library keeps it (libsas_db.h:737).</summary>
    private sealed class AuthenticatedComponent(byte index, string name, ulong size)
    {
        public byte Index { get; } = index;

        public byte[] Name { get; } = Encoding.ASCII.GetBytes(name);

        public ulong Size { get; } = size;

        // This machine offers CRC-16 alone, the method every chapter 19 request names.
        public uint Methods { get; } = MethodCrc16;

        public byte Status { get; set; }

        public uint Method { get; set; }

        public byte[] Result { get; set; } = [];
    }

    /// <summary>What the library hands the application when it accepts an authentication (sComponentValidation).</summary>
    private readonly record struct AuthenticationRequest(byte Index, uint Method, byte[] Seed, ulong Offset, ulong OffsetHigh);

    private readonly List<AuthenticatedComponent> _components = [];
    private ushort _componentListCrc;
    private long _authenticationReissueDue;
    private long _authenticationDue;
    private AuthenticationRequest _authenticationRequest;

    /// <summary>The components the application registers at start, when the machine takes part at all.</summary>
    private void RegisterComponentsLocked()
    {
        _components.Clear();
        if (!_options.ComponentAuthentication)
            return;

        // Registered without $8E, as at every start (libsas.cpp:2662).
        RegisterComponentLocked(new AuthenticatedComponent(1, "SWB-GAME", 0x10000));
        RegisterComponentLocked(new AuthenticatedComponent(2, "SWB-PLATFORM", 0x20000));
    }

    /// <summary>Appends a component and recomputes the list's check sequence over every name in order (libsas_internal.cpp:6187, :6261).</summary>
    private void RegisterComponentLocked(AuthenticatedComponent component)
    {
        _components.Add(component);
        _componentListCrc = SasCrc.Compute(_components.SelectMany(c => c.Name).ToArray());
    }

    /// <summary>
    /// A component installed or updated at the machine: the library can only add one, and raises $8E for it
    /// (internalRegisterComponent with the exception asked for). Null when done, otherwise why not.
    /// </summary>
    public string? ChangeComponent()
    {
        lock (_gate)
        {
            if (!_options.ComponentAuthentication)
                return "the machine takes no part in component authentication, so the library refuses a component";

            byte index = (byte)(_components.Max(c => c.Index) + 1);
            RegisterComponentLocked(new AuthenticatedComponent(index, $"SWB-UPDATE-{index}", 0x1000));
        }

        QueueOrdinary((byte)SasExceptionCode.ComponentListChanged);
        return null;
    }

    /// <summary>The component authentication bit of the second feature byte, set on a machine that takes part.</summary>
    private byte ComponentAuthenticationFeatureBits() =>
        _options.ComponentAuthentication ? ComponentAuthenticationBit : (byte)0;

    /// <summary>
    /// Long poll $6E, reached after the check sequence and the channel category gate; the address and the
    /// feature bit follow in the library's order, each silent (lp.cpp:3721, :3739).
    /// </summary>
    private void AnswerAuthentication(MachineLink link, ReadOnlySpan<byte> frame)
    {
        if (frame[0] != link.Address.Value || !_options.ComponentAuthentication)
            return;

        // The request is read whole first: the reply is built where the request arrived.
        byte[] request = frame.ToArray();
        byte At(int i) => i < request.Length - 2 ? request[i] : (byte)0;

        byte action = At(3);
        if (action > ActionLastResult)
        {
            Reject(link);
            return;
        }

        byte mode = At(4);
        if (action is ActionReadStatus or ActionAuthenticate && mode is not (ByIndex or ByName))
        {
            Reject(link);
            return;
        }

        if (action == ActionAuthenticate && At(5) > 0x7F)
        {
            Reject(link);
            return;
        }

        List<byte> reply;
        AuthenticationRequest? accepted = null;
        lock (_gate)
        {
            reply = [link.Address.Value, (byte)SasLongPollCode.LP_SENDAUTHENTICATION, 0,
                SasCrc.LowByte(_componentListCrc), SasCrc.HighByte(_componentListCrc)];

            switch (action)
            {
                case ActionList:
                    // The list name length is never set by the library, so it is always empty (libsas_internal.cpp:6130).
                    reply.AddRange([StatusList, 0, 1, (byte)_components.Count, 0, 0, 0, 0]);
                    break;

                case ActionReadStatus:
                    ReadComponentStatus(reply, Find(mode, request, At(5)));
                    break;

                case ActionAuthenticate:
                    accepted = Authenticate(reply, Find(mode, request, At(5)), At);
                    break;

                default:
                    ReadLastResult(reply);
                    break;
            }
        }

        reply[2] = (byte)(reply.Count - 3);
        ushort crc = SasCrc.Compute(reply.ToArray());
        reply.Add(SasCrc.LowByte(crc));
        reply.Add(SasCrc.HighByte(crc));
        Send(link, reply.Select(SasSymbol.Data8).ToArray());

        // The application is told after the reply has gone (lp.cpp:4114).
        if (accepted is { } handed)
        {
            lock (_gate)
            {
                _authenticationRequest = handed;
                _authenticationDue = Stopwatch.GetTimestamp() + Ticks(_options.AuthenticationTime);
            }
        }
    }

    /// <summary>
    /// The first component whose index is the request's seventh byte, or whose name starts with the request's
    /// name over the length it gives, compared against the library's zero-padded buffer (lp.cpp:3818, :3827).
    /// </summary>
    private AuthenticatedComponent? Find(byte mode, byte[] request, byte length)
    {
        foreach (AuthenticatedComponent component in _components)
        {
            if (mode == ByIndex && request.Length - 2 > 6 && component.Index == request[6])
                return component;

            if (mode == ByName && NameMatches(component, request, length))
                return component;
        }

        return null;
    }

    private static bool NameMatches(AuthenticatedComponent component, byte[] request, byte length)
    {
        for (int i = 0; i < length; i++)
        {
            byte asked = 6 + i < request.Length - 2 ? request[6 + i] : (byte)0;
            byte held = i < component.Name.Length ? component.Name[i] : (byte)0;
            if (asked != held)
                return false;
        }

        return true;
    }

    /// <summary>Action $01: the component's status, name, size and methods, then the method and result where it has them (lp.cpp:3834).</summary>
    private void ReadComponentStatus(List<byte> reply, AuthenticatedComponent? component)
    {
        if (component is null)
        {
            reply.Add(StatusNoComponent);
            return;
        }

        DescribeComponent(reply, component);
        if (component.Status == StatusRunning)
        {
            // The authentication length byte is written past the end and left out of the reply (lp.cpp:3851).
            AddMethod(reply, component.Method);
        }
        else if (component.Status == StatusComplete)
        {
            // Read once, the result stops $8F at once, acknowledged or not (lp.cpp:3856).
            _authenticationReissueDue = 0;
            AddMethod(reply, component.Method);
            reply.Add((byte)component.Result.Length);
            reply.AddRange(component.Result);
        }

        // Every reported outcome is forgotten as soon as it is sent (lp.cpp:3867).
        if (ReportedOnce.Contains(component.Status))
            component.Status = StatusIdle;
    }

    /// <summary>Action $03: the first component running or holding an outcome, in list order (lp.cpp:4021).</summary>
    private void ReadLastResult(List<byte> reply)
    {
        AuthenticatedComponent? component =
            _components.FirstOrDefault(c => c.Status == StatusRunning || ReportedOnce.Contains(c.Status));
        if (component is null)
        {
            reply.Add(StatusNothingToReport);
            return;
        }

        DescribeComponent(reply, component);
        if (component.Status == StatusRunning)
        {
            AddMethod(reply, component.Method);
        }
        else if (component.Status == StatusComplete)
        {
            _authenticationReissueDue = 0;
            AddMethod(reply, component.Method);
            reply.Add((byte)component.Result.Length);
            reply.AddRange(component.Result);
        }
        else if (component.Status == StatusFailed)
        {
            // Only action $03 stops $8F for a failure (lp.cpp:4068).
            _authenticationReissueDue = 0;
        }

        if (ReportedOnce.Contains(component.Status))
            component.Status = StatusIdle;
    }

    /// <summary>
    /// Action $02: the seed, method and offset checks in the library's order, and on acceptance every running
    /// authentication aborted and this one started (lp.cpp:3907 to :4001).
    /// </summary>
    private AuthenticationRequest? Authenticate(
        List<byte> reply, AuthenticatedComponent? component, Func<int, byte> at)
    {
        if (component is null)
        {
            reply.Add(StatusNoComponent);
            return null;
        }

        int cursor = at(5) + 6;
        uint method = (uint)(at(cursor) | (at(cursor + 1) << 8) | (at(cursor + 2) << 16) | (at(cursor + 3) << 24));
        cursor += 4;
        byte seedLength = at(cursor++);
        if (seedLength > 32)
        {
            reply.Add(StatusInvalidCommand);
            return null;
        }

        byte[] seed = Enumerable.Range(cursor, seedLength).Select(at).ToArray();
        cursor += seedLength;
        byte offsetLength = at(cursor++);
        byte[] offset = new byte[16];
        for (int i = 0; i < Math.Min((int)offsetLength, 16); i++)
            offset[i] = at(cursor + i);
        ulong offsetLow = BitConverter.ToUInt64(offset, 0);
        ulong offsetHigh = BitConverter.ToUInt64(offset, 8);

        byte asked = (byte)method;
        if ((byte)component.Methods == 0)
        {
            reply.Add(StatusNoAuthentication);
            return null;
        }

        if ((asked & (byte)component.Methods) == 0)
        {
            reply.Add(StatusMethodNotSupported);
            return null;
        }

        // Only the first method byte is compared, and the size test catches an offset only when both of its
        // halves exceed the size's; the test against an empty size compares a pointer and never holds (lp.cpp:3934 to :3943).
        if ((asked == MethodCrc16 && seedLength > 2) || (asked == MethodCrc32 && seedLength > 4)
            || (asked == MethodMd5 && seedLength > 16) || (asked == MethodKobetron1 && seedLength > 4)
            || (asked == MethodKobetron2 && seedLength > 4) || (asked == MethodSha1 && seedLength > 20)
            || (asked == MethodSha256 && seedLength > 32)
            || (offsetLow > component.Size && offsetHigh > 0))
        {
            reply.Add(StatusInvalidData);
            return null;
        }

        // One authentication at a time: any running one is aborted, and no $8F is owed any longer (lp.cpp:3952, :3965).
        foreach (AuthenticatedComponent other in _components)
        {
            if (other.Status == StatusRunning)
                other.Status = StatusAborted;
        }

        component.Status = StatusRunning;
        component.Method = method;
        _authenticationReissueDue = 0;

        DescribeComponent(reply, component);
        AddMethod(reply, method);
        return new AuthenticationRequest(component.Index, method, seed, offsetLow, offsetHigh);
    }

    /// <summary>Status, name, size and the methods offered, as every reply about a found component starts (lp.cpp:3836).</summary>
    private static void DescribeComponent(List<byte> reply, AuthenticatedComponent component)
    {
        reply.Add(component.Status);
        reply.Add((byte)component.Name.Length);
        reply.AddRange(component.Name);

        // As few bytes as the size needs, least significant first (libsas_internal.cpp:6150).
        byte[] size = BitConverter.GetBytes(component.Size);
        int sizeLength = Math.Max(1, 8 - (BitOperations.LeadingZeroCount(component.Size) / 8));
        reply.Add((byte)sizeLength);
        reply.AddRange(size.Take(sizeLength));
        AddMethod(reply, component.Methods);
    }

    private static void AddMethod(List<byte> reply, uint method) =>
        reply.AddRange([(byte)method, (byte)(method >> 8), (byte)(method >> 16), (byte)(method >> 24)]);

    /// <summary>The application finishing an authentication, and $8F raised again while its result is unread.</summary>
    private void TickAuthentication()
    {
        long now = Stopwatch.GetTimestamp();
        bool raise = false;
        lock (_gate)
        {
            if (_authenticationDue != 0 && now >= _authenticationDue)
            {
                _authenticationDue = 0;
                raise = ReplyAuthenticationLocked(now);
            }

            if (!raise && _authenticationReissueDue != 0 && now >= _authenticationReissueDue)
            {
                _authenticationReissueDue = now + Ticks(AuthenticationReissue);
                raise = true;
            }
        }

        if (raise)
            QueueOrdinary((byte)SasExceptionCode.AuthenticationComplete);
    }

    /// <summary>
    /// The application's answer, which the library refuses for a component no longer running, and otherwise
    /// stores with $8F raised now and every fifteen seconds after (libsas_internal.cpp:6311 to :6339).
    /// </summary>
    private bool ReplyAuthenticationLocked(long now)
    {
        AuthenticationRequest asked = _authenticationRequest;
        AuthenticatedComponent? component = _components.FirstOrDefault(c => c.Index == asked.Index);
        if (component is not { Status: StatusRunning })
            return false;

        if (ComputeAuthentication(component, asked) is { } result)
        {
            component.Status = StatusComplete;
            component.Result = result;
        }
        else
        {
            component.Status = StatusFailed;
        }

        _authenticationReissueDue = now + Ticks(AuthenticationReissue);
        return true;
    }

    /// <summary>
    /// CRC-16 of the component from the offset to its end, started from the seed, least significant byte first;
    /// null, a failure, for any other method or an offset past the end. QSP has none to copy (SasNetwork.cpp:302).
    /// </summary>
    private static byte[]? ComputeAuthentication(AuthenticatedComponent component, AuthenticationRequest asked)
    {
        if (asked.Method != MethodCrc16 || asked.OffsetHigh != 0 || asked.Offset >= component.Size)
            return null;

        uint crc = asked.Seed.Length switch
        {
            0 => 0,
            1 => asked.Seed[0],
            _ => (uint)(asked.Seed[0] | (asked.Seed[1] << 8)),
        };

        for (ulong position = asked.Offset; position < component.Size; position++)
        {
            uint c = ComponentByte(component, position);
            uint q = (crc ^ c) & 0x0F;
            crc = (crc >> 4) ^ (q * 0x1081);
            q = (crc ^ (c >> 4)) & 0x0F;
            crc = (crc >> 4) ^ (q * 0x1081);
        }

        return [(byte)crc, (byte)(crc >> 8)];
    }

    /// <summary>What the component holds at a position: a fixed pattern, so a result can be checked by hand.</summary>
    private static byte ComponentByte(AuthenticatedComponent component, ulong position) =>
        (byte)((position * 7) + component.Index);

    /// <summary>
    /// The library's component state lives in working memory, so a restart forgets every status and the $8F
    /// timer, and the application registers its components again (libsas_db.h:765).
    /// </summary>
    private void RestartAuthenticationLocked()
    {
        foreach (AuthenticatedComponent component in _components)
        {
            component.Status = StatusIdle;
            component.Method = 0;
            component.Result = [];
        }

        _authenticationDue = 0;
        _authenticationReissueDue = 0;
    }
}
