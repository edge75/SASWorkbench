using System.IO.Ports;
using System.Runtime.InteropServices;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Transport;

/// <summary>How to open a serial port for SAS.</summary>
/// <param name="PortName">
/// The plain port name, such as <c>COM12</c>. Do not prefix it: the framework builds the
/// extended device path itself and rejects a hand-built one, which is why ports above nine
/// are unreachable for tools that prefix them by hand.
/// </param>
/// <param name="BaudRate">19200 in every SAS deployment.</param>
public sealed record SerialTransportOptions(string PortName, int BaudRate = 19200);

/// <summary>
/// Sends SAS over a real serial port, using mark parity for the address byte and space parity
/// for every other byte.
/// </summary>
/// <remarks>
/// <para>
/// Receiving needs no ninth bit: a gaming machine never sets the wakeup bit, so eight-bit
/// reads are complete. Transmitting does, and the drain between parity switches is the part
/// legacy tools omit — they sleep instead, which does not guarantee the previous byte has
/// left the shift register.
/// </para>
/// <para>
/// On anything other than Windows this refuses to open. The framework's serial layer discards
/// mark and space parity on those platforms with no exception and no warning, so the wakeup bit
/// would simply never be sent and every test would pass over a link that does not work.
/// </para>
/// <para>
/// Reported as unverified: no adapter has been measured. A pass here is not evidence about
/// hardware until someone attaches some.
/// </para>
/// </remarks>
public sealed class SerialTransport : ISasTransport
{
    private static readonly TransportCapabilities WindowsCaps = new(
        TransportKind.Serial,
        CarriesWakeupBit: true,
        TransportFidelity.Unverified,
        "Serial timing has never been measured against an adapter; treat a pass as untested.");

    private static readonly TransportCapabilities RefusedCaps = new(
        TransportKind.Serial,
        CarriesWakeupBit: false,
        TransportFidelity.Unverified,
        "This platform silently discards mark and space parity, so the wakeup bit cannot be sent.");

    private readonly SerialTransportOptions _options;
    private readonly Lock _gate = new();
    private readonly byte[] _readBuffer = new byte[512];
    private SerialPort? _port;

    public SerialTransport(SerialTransportOptions options)
    {
        _options = options;
        Description = $"serial {options.PortName} @ {options.BaudRate}";
    }

    public string Description { get; }

    public TransportCapabilities Capabilities =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WindowsCaps : RefusedCaps;

    public bool IsConnected => _port is { IsOpen: true };

    /// <summary>
    /// Names the ports this machine has, so a runner can skip with a reason instead of failing
    /// when the one a test asks for is absent.
    /// </summary>
    public static string[] AvailablePorts()
    {
        try
        {
            return SerialPort.GetPortNames();
        }
        catch (PlatformNotSupportedException)
        {
            return [];
        }
    }

    public void Open()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new TransportRefusedException(
                "Serial SAS is refused on this platform: mark and space parity are discarded "
                + "without error here, so the wakeup bit would never reach the gaming machine. "
                + "Use the TCP transport, or run the serial link on Windows.");

        lock (_gate)
        {
            if (_port is { IsOpen: true })
                return;

            var port = new SerialPort(_options.PortName, _options.BaudRate, Parity.Space, 8, StopBits.One)
            {
                ReadTimeout = 50,
                WriteTimeout = 500,
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false,
            };

            try
            {
                port.Open();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                                       or ArgumentException or InvalidOperationException)
            {
                port.Dispose();
                throw new TransportRefusedException(
                    $"Cannot open {_options.PortName}: {ex.Message}", ex);
            }

            _port = port;
        }
    }

    public void Write(ReadOnlySpan<SasSymbol> symbols)
    {
        lock (_gate)
        {
            SerialPort? port = _port;
            if (port is null || !port.IsOpen)
                return;

            foreach (SasSymbol symbol in symbols)
            {
                Parity wanted = symbol.Wakeup ? Parity.Mark : Parity.Space;
                if (port.Parity != wanted)
                {
                    // Let the previous byte leave the shift register before changing the parity
                    // that will be applied to it. Flush maps to a drain, not a buffer copy.
                    port.BaseStream.Flush();
                    port.Parity = wanted;
                }

                port.BaseStream.WriteByte(symbol.Data);
            }

            port.BaseStream.Flush();
        }
    }

    public int Read(Span<SasSymbol> destination, TimeSpan timeout)
    {
        SerialPort? port = _port;
        if (port is null || !port.IsOpen || destination.IsEmpty)
            return 0;

        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        int produced = 0;

        while (produced == 0 && Environment.TickCount64 < deadline)
        {
            int available;
            try
            {
                available = port.BytesToRead;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }

            if (available == 0)
            {
                Thread.Sleep(1);
                continue;
            }

            int wanted = Math.Min(Math.Min(available, _readBuffer.Length), destination.Length);
            int read;
            try
            {
                read = port.Read(_readBuffer, 0, wanted);
            }
            catch (TimeoutException)
            {
                continue;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                return 0;
            }

            // A gaming machine never sets the wakeup bit, so every received byte is data.
            for (int i = 0; i < read; i++)
                destination[produced++] = SasSymbol.Data8(_readBuffer[i]);
        }

        return produced;
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        lock (_gate)
        {
            SerialPort? port = _port;
            _port = null;
            if (port is null)
                return;

            try
            {
                if (port.IsOpen)
                    port.Close();
            }
            catch (IOException)
            {
                // The port has gone away underneath us; nothing to close.
            }

            port.Dispose();
        }
    }
}
