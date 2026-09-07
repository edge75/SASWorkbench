using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// A gaming machine just real enough to test a host against: it obeys the addressing and
/// framing rules and nothing else.
/// </summary>
/// <remarks>
/// This is deliberately not a model of a gaming machine. It exists so that tests of the poll
/// engine can control exactly what comes back — a rejection, a silence, a corrupted frame — which
/// is precisely what a real machine will not do on demand. Anything that needs a machine's
/// actual behaviour must run against the real library over a real link.
///
/// Messages are split on the wakeup bit rather than on a quiet gap. A machine has that luxury
/// and a host does not: the host's own messages are the only ones on the link that carry an
/// address byte, so the bit itself marks where each one starts even when two arrive together.
/// </remarks>
internal sealed class ScriptedMachine : IDisposable
{
    private readonly ISasTransport _transport;
    private readonly SasAddress _address;
    private readonly SasSymbol[] _incoming = new SasSymbol[HostMessage.MaxSymbols];
    private readonly List<SasSymbol> _message = new(HostMessage.MaxSymbols);
    private readonly SasSymbol[] _outbound = new SasSymbol[HostMessage.MaxSymbols];
    private readonly Lock _gate = new();
    private Thread? _thread;
    private volatile bool _running;

    public ScriptedMachine(ISasTransport transport, SasAddress address)
    {
        _transport = transport;
        _address = address;
    }

    /// <summary>Exception codes handed out one per general poll, then zero for ever.</summary>
    public Queue<byte> Exceptions { get; } = new();

    /// <summary>Payload to answer each long poll with, after the echoed address and command.</summary>
    public Dictionary<byte, byte[]> Replies { get; } = new();

    /// <summary>Commands answered with an acknowledgement — the address alone.</summary>
    public HashSet<byte> Acknowledge { get; } = new();

    /// <summary>Commands answered with a rejection — the address with bit 7 set.</summary>
    public HashSet<byte> Reject { get; } = new();

    /// <summary>Commands answered with nothing at all.</summary>
    public HashSet<byte> Silent { get; } = new();

    /// <summary>Commands answered with a deliberately wrong check sequence.</summary>
    public HashSet<byte> Corrupt { get; } = new();

    /// <summary>Commands answered with the wakeup bit set, which no machine may ever do.</summary>
    public HashSet<byte> WithWakeupBit { get; } = new();

    /// <summary>
    /// How long to think before answering a command. A machine that answers after the host has
    /// given up waiting is slow, not broken, and the host has to cope with it.
    /// </summary>
    public Dictionary<byte, TimeSpan> SlowToAnswer { get; } = new();

    /// <summary>Every long poll command that arrived, in order.</summary>
    public List<byte> CommandsSeen { get; } = new();

    /// <summary>How many general polls of this machine's address arrived.</summary>
    public int GeneralPolls { get; private set; }

    /// <summary>How many general polls of address zero arrived.</summary>
    public int SyncPolls { get; private set; }

    /// <summary>How many received symbols carried the wakeup bit where they should not have.</summary>
    public int MisplacedWakeupBits { get; private set; }

    /// <summary>A snapshot of the counters, taken under the same lock that writes them.</summary>
    public (int Sync, int General, int Misplaced, byte[] Commands) Snapshot()
    {
        lock (_gate)
            return (SyncPolls, GeneralPolls, MisplacedWakeupBits, CommandsSeen.ToArray());
    }

    public void Start()
    {
        _running = true;
        _transport.Open();
        _thread = new Thread(Loop) { IsBackground = true, Name = "scripted-machine" };
        _thread.Start();
    }

    public void Dispose()
    {
        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(1));
        _transport.Dispose();
    }

    private void Loop()
    {
        while (_running)
        {
            int read = _transport.Read(_incoming, TimeSpan.FromMilliseconds(10));
            if (read == 0)
            {
                Dispatch();
                continue;
            }

            for (int i = 0; i < read; i++)
            {
                SasSymbol symbol = _incoming[i];
                if (symbol.Wakeup)
                    Dispatch();
                else if (_message.Count == 0)
                    continue; // A data byte with no address byte before it is not a message.

                _message.Add(symbol);
            }

            // A read takes everything the line was holding, so a read that did not fill the
            // buffer ends the message. Answering on that rather than on a timed silence keeps
            // this machine's turnaround off the clock: a timed wait is the one thing a loaded
            // machine stretches, and stretching it here makes a host look at fault.
            if (read < _incoming.Length)
                Dispatch();
        }

        Dispatch();
    }

    private void Dispatch()
    {
        if (_message.Count == 0)
            return;

        for (int i = 1; i < _message.Count; i++)
        {
            if (_message[i].Wakeup)
            {
                lock (_gate)
                    MisplacedWakeupBits++;
            }
        }

        byte first = _message[0].Data;
        int count = _message.Count;
        byte command = count > 1 ? _message[1].Data : (byte)0;
        _message.Clear();

        if (count == 1)
        {
            HandlePoll(first);
            return;
        }

        if (first != _address.Value)
            return;

        lock (_gate)
            CommandsSeen.Add(command);
        HandleLongPoll(command);
    }

    private void HandlePoll(byte first)
    {
        if ((first & HostMessage.GeneralPollFlag) == 0)
            return;

        byte polled = (byte)(first & ~HostMessage.GeneralPollFlag);
        if (polled == 0)
        {
            lock (_gate)
                SyncPolls++;
            return;
        }

        if (polled != _address.Value)
            return;

        byte code;
        lock (_gate)
        {
            GeneralPolls++;
            code = Exceptions.Count > 0 ? Exceptions.Dequeue() : (byte)0x00;
        }

        _outbound[0] = SasSymbol.Data8(code);
        _transport.Write(_outbound.AsSpan(0, 1));
    }

    private void HandleLongPoll(byte command)
    {
        if (SlowToAnswer.TryGetValue(command, out TimeSpan delay))
            Thread.Sleep(delay);

        if (Silent.Contains(command))
            return;

        if (Reject.Contains(command))
        {
            _outbound[0] = SasSymbol.Data8((byte)(_address.Value | HostMessage.GeneralPollFlag));
            _transport.Write(_outbound.AsSpan(0, 1));
            return;
        }

        if (Acknowledge.Contains(command))
        {
            _outbound[0] = SasSymbol.Data8(_address.Value);
            _transport.Write(_outbound.AsSpan(0, 1));
            return;
        }

        if (!Replies.TryGetValue(command, out byte[]? payload))
            return;

        Span<byte> frame = stackalloc byte[HostMessage.MaxSymbols];
        frame[0] = _address.Value;
        frame[1] = command;
        payload.CopyTo(frame[2..]);
        int length = SasCrc.Append(frame, 2 + payload.Length);

        if (Corrupt.Contains(command))
            frame[length - 1] ^= 0xFF;

        bool wakeup = WithWakeupBit.Contains(command);
        for (int i = 0; i < length; i++)
            _outbound[i] = wakeup ? SasSymbol.Address(frame[i]) : SasSymbol.Data8(frame[i]);

        _transport.Write(_outbound.AsSpan(0, length));
    }
}
