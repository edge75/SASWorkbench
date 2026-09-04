using System.Net.Sockets;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Transport;

/// <summary>
/// Carries SAS symbols over TCP, one little-endian sixteen-bit word per symbol with bit 8 as
/// the wakeup bit.
/// </summary>
/// <remarks>
/// This is the link a software gaming machine is reached on. A virtual serial-port pair was
/// rejected for the job: it carries eight-bit bytes, so the wakeup bit would be lost and
/// address-versus-data detection — the most protocol-critical behaviour in SAS — would be
/// untested while every test still passed.
/// </remarks>
public sealed class TcpTransport : ISasTransport
{
    private static readonly TransportCapabilities Caps = new(
        TransportKind.Tcp,
        CarriesWakeupBit: true,
        TransportFidelity.Proven,
        null);

    private readonly TcpTransportOptions _options;
    private readonly Lock _writeGate = new();
    private readonly byte[] _writeBuffer = new byte[2 * HostMessage.MaxSymbols];
    private readonly byte[] _readBuffer = new byte[4096];

    private Socket? _listener;
    private Socket? _peer;
    private Thread? _connector;
    private volatile bool _disposed;
    private volatile bool _opened;
    private int _carry = -1;

    public TcpTransport(TcpTransportOptions options)
    {
        _options = options;
        Description = $"tcp {options.Endpoint} ({options.Role.ToString().ToLowerInvariant()})";
    }

    public string Description { get; }

    public TransportCapabilities Capabilities => Caps;

    public bool IsConnected => _peer is { Connected: true };

    /// <summary>Raised when a peer attaches, with its endpoint as text.</summary>
    public event Action<string>? PeerAttached;

    /// <summary>Raised when the peer goes away.</summary>
    public event Action? PeerDetached;

    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_opened)
            return;

        if (_options.Role == TcpRole.Listen)
        {
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            try
            {
                listener.Bind(_options.Endpoint);
                listener.Listen(1);
            }
            catch (SocketException ex)
            {
                listener.Dispose();
                throw new TransportRefusedException(
                    $"Cannot listen on {_options.Endpoint}: {ex.SocketErrorCode}.", ex);
            }

            _listener = listener;
        }

        _opened = true;
        _connector = new Thread(AttachLoop)
        {
            IsBackground = true,
            Name = "sas-tcp-attach",
        };
        _connector.Start();
    }

    /// <summary>
    /// Blocks until a peer attaches. Returns false on timeout, which the runner reports as a
    /// skip rather than a failure — no machine on the wire is an environment problem.
    /// </summary>
    public bool WaitForPeer(TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!IsConnected && !_disposed && Environment.TickCount64 < deadline)
            Thread.Sleep(10);

        return IsConnected;
    }

    public void Write(ReadOnlySpan<SasSymbol> symbols)
    {
        Socket? peer = _peer;
        if (peer is null || !peer.Connected)
            return;

        lock (_writeGate)
        {
            int length = 0;
            foreach (SasSymbol symbol in symbols)
            {
                _writeBuffer[length++] = (byte)(symbol.Raw & 0xFF);
                _writeBuffer[length++] = (byte)(symbol.Raw >> 8);
            }

            try
            {
                int sent = 0;
                while (sent < length)
                    sent += peer.Send(_writeBuffer.AsSpan(sent, length - sent), SocketFlags.None);
            }
            catch (SocketException)
            {
                DetachPeer();
            }
            catch (ObjectDisposedException)
            {
                DetachPeer();
            }
        }
    }

    public int Read(Span<SasSymbol> destination, TimeSpan timeout)
    {
        Socket? peer = _peer;
        if (peer is null || destination.IsEmpty)
            return 0;

        int microseconds = (int)Math.Clamp(timeout.TotalMilliseconds * 1000, 0, int.MaxValue);
        try
        {
            if (!peer.Poll(microseconds, SelectMode.SelectRead))
                return 0;

            int wanted = Math.Min(_readBuffer.Length, destination.Length * 2);
            int read = peer.Receive(_readBuffer.AsSpan(0, wanted), SocketFlags.None);
            if (read == 0)
            {
                DetachPeer();
                return 0;
            }

            int produced = 0;
            for (int i = 0; i < read; i++)
            {
                if (_carry < 0)
                {
                    _carry = _readBuffer[i];
                    continue;
                }

                ushort raw = (ushort)(_carry | (_readBuffer[i] << 8));
                _carry = -1;
                if (produced < destination.Length)
                    destination[produced++] = SasSymbol.FromRawTruncating(raw);
            }

            return produced;
        }
        catch (SocketException)
        {
            DetachPeer();
            return 0;
        }
        catch (ObjectDisposedException)
        {
            return 0;
        }
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _opened = false;
        DetachPeer();

        Socket? listener = Interlocked.Exchange(ref _listener, null);
        listener?.Dispose();
        _connector?.Join(TimeSpan.FromMilliseconds(500));
    }

    private void AttachLoop()
    {
        while (!_disposed)
        {
            if (IsConnected)
            {
                Thread.Sleep(20);
                continue;
            }

            try
            {
                Socket? attached = _options.Role == TcpRole.Listen ? Accept() : Dial();
                if (attached is null)
                    continue;

                attached.NoDelay = true;
                _carry = -1;
                _peer = attached;
                PeerAttached?.Invoke(attached.RemoteEndPoint?.ToString() ?? "unknown");
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                Thread.Sleep(_options.Reconnect);
            }
        }
    }

    private Socket? Accept()
    {
        Socket? listener = _listener;
        if (listener is null)
            return null;

        // Poll rather than block, so Dispose can stop this thread promptly.
        return listener.Poll(200_000, SelectMode.SelectRead) ? listener.Accept() : null;
    }

    private Socket? Dial()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Connect(_options.Endpoint);
            return socket;
        }
        catch (SocketException)
        {
            socket.Dispose();
            Thread.Sleep(_options.Reconnect);
            return null;
        }
    }

    private void DetachPeer()
    {
        Socket? peer = Interlocked.Exchange(ref _peer, null);
        if (peer is null)
            return;

        try
        {
            peer.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
            // Already gone; nothing left to shut down.
        }
        catch (ObjectDisposedException)
        {
            // Already gone; nothing left to shut down.
        }

        peer.Dispose();
        PeerDetached?.Invoke();
    }
}
