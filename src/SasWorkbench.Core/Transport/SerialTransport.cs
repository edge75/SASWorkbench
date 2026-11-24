using System.Diagnostics;
using System.IO.Ports;
using System.Runtime.InteropServices;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Transport;

/// <summary>How to open a serial port for SAS.</summary>
/// <param name="PortName">
/// The plain port name, such as <c>COM12</c>. Do not prefix it: the framework builds the
/// extended device path itself and rejects a hand-built one, which is why ports above nine
/// are unreachable for tools that prefix them by hand. On Linux, a device path such as
/// <c>/dev/ttyS4</c>, or just <c>ttyS4</c>.
/// </param>
/// <param name="BaudRate">19200 in every SAS deployment.</param>
public sealed record SerialTransportOptions(string PortName, int BaudRate = 19200);

/// <summary>
/// Sends SAS over a real serial port, using mark parity for the address byte and space parity
/// for every other byte.
/// </summary>
/// <remarks>
/// <para>
/// Receiving needs no ninth bit, because a gaming machine never sets it, so a byte that fails the
/// parity check is kept as received rather than replaced. Transmitting does, so the line is
/// drained onto the wire before every parity change.
/// </para>
/// <para>
/// On Linux the framework's serial layer discards mark and space parity with no exception and no
/// warning, so the port is driven through the kernel's terminal requests instead, with stick
/// parity, and refused when its driver does not keep it. Anywhere else this refuses to open.
/// </para>
/// <para>
/// Reported as unverified, because only an on-board UART has been measured (a Quixant IQ-2 on
/// serial.sys at 19200 baud) and this transport cannot tell that from a USB adapter, none of which has.
/// </para>
/// </remarks>
public sealed class SerialTransport : ISasTransport
{
    private static readonly TransportCapabilities WindowsCaps = new(
        TransportKind.Serial,
        CarriesWakeupBit: true,
        TransportFidelity.Unverified,
        "Measured only on a Quixant IQ-2's on-board UART (serial.sys, 19200 baud); USB adapters "
        + "are unmeasured, so treat a pass over one as untested.");

    private static readonly TransportCapabilities LinuxCaps = new(
        TransportKind.Serial,
        CarriesWakeupBit: true,
        TransportFidelity.Unverified,
        "Linux stick parity, checked on each port when it opens and refused if the driver drops it; "
        + "not yet measured on a board, and USB adapters are unmeasured.");

    private static readonly TransportCapabilities RefusedCaps = new(
        TransportKind.Serial,
        CarriesWakeupBit: false,
        TransportFidelity.Unverified,
        "This platform silently discards mark and space parity, so the wakeup bit cannot be sent.");

    private readonly SerialTransportOptions _options;
    private readonly Lock _gate = new();
    private readonly byte[] _readBuffer = new byte[512];
    private SerialPort? _port;
    private LinuxSerialPort? _linux;

    public SerialTransport(SerialTransportOptions options)
    {
        _options = options;
        Description = $"serial {options.PortName} @ {options.BaudRate}";
    }

    public string Description { get; }

    public TransportCapabilities Capabilities =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WindowsCaps
        : LinuxTermiosDevice.IsSupported ? LinuxCaps
        : RefusedCaps;

    public bool IsConnected => _port is { IsOpen: true } || _linux is not null;

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

    /// <summary>
    /// Says whether a named port can carry the wakeup bit, leaving its settings as they were.
    /// </summary>
    public static WakeupBitCheck CheckWakeupBit(SerialTransportOptions options)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using SerialPort port = CreatePort(options);
            try
            {
                port.Open();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                                       or ArgumentException or InvalidOperationException)
            {
                return new WakeupBitCheck(false, $"Cannot open {options.PortName}: {ex.Message}");
            }

            return new WakeupBitCheck(
                true, $"{options.PortName} opened; Windows carries mark and space parity.");
        }

        if (!LinuxTermiosDevice.IsSupported)
            return new WakeupBitCheck(false, RefusedCaps.Caveat!);

        try
        {
            using LinuxTermiosDevice device =
                LinuxTermiosDevice.Open(LinuxSerialPort.DevicePath(options.PortName));
            return LinuxSerialPort.Probe(device, options.BaudRate);
        }
        catch (Exception ex) when (ex is TransportRefusedException or IOException)
        {
            return new WakeupBitCheck(false, ex.Message);
        }
    }

    public void Open()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (!LinuxTermiosDevice.IsSupported)
                throw new TransportRefusedException(
                    "Serial SAS is refused on this platform: mark and space parity are discarded "
                    + "without error here, so the wakeup bit would never reach the gaming machine. "
                    + "Use the TCP transport, or run the serial link on Windows or Linux.");

            OpenLinux();
            return;
        }

        lock (_gate)
        {
            if (_port is { IsOpen: true })
                return;

            SerialPort port = CreatePort(_options);

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

    private void OpenLinux()
    {
        lock (_gate)
        {
            if (_linux is not null)
                return;

            LinuxTermiosDevice device =
                LinuxTermiosDevice.Open(LinuxSerialPort.DevicePath(_options.PortName));
            try
            {
                _linux = LinuxSerialPort.Open(device, _options.BaudRate);
            }
            catch (IOException ex)
            {
                device.Dispose();
                throw new TransportRefusedException(
                    $"Cannot configure {_options.PortName}: {ex.Message}", ex);
            }
            catch (TransportRefusedException)
            {
                device.Dispose();
                throw;
            }
        }
    }

    /// <summary>A closed port configured for SAS, with parity-error replacement turned off.</summary>
    public static SerialPort CreatePort(SerialTransportOptions options) =>
        new(options.PortName, options.BaudRate, Parity.Space, 8, StopBits.One)
        {
            ReadTimeout = 50,
            WriteTimeout = 500,
            Handshake = Handshake.None,
            DtrEnable = false,
            RtsEnable = false,
            ParityReplace = 0,
        };

    /// <summary>How long one character takes on the wire: start, eight data, parity and stop bits.</summary>
    public static TimeSpan CharacterTime(int baudRate) =>
        TimeSpan.FromTicks((long)Math.Ceiling(11.0 * TimeSpan.TicksPerSecond / baudRate));

    /// <summary>Writes symbols, draining the line before each parity change so no byte is re-framed in flight.</summary>
    public static void WriteSymbols(ISerialLine line, ReadOnlySpan<SasSymbol> symbols)
    {
        foreach (SasSymbol symbol in symbols)
        {
            Parity wanted = symbol.Wakeup ? Parity.Mark : Parity.Space;
            if (line.Parity != wanted)
            {
                line.Drain();
                line.Parity = wanted;
            }

            line.WriteByte(symbol.Data);
        }

        line.Flush();
    }

    public void Write(ReadOnlySpan<SasSymbol> symbols)
    {
        lock (_gate)
        {
            if (_linux is { } linux)
            {
                WriteSymbols(linux, symbols);
                return;
            }

            SerialPort? port = _port;
            if (port is null || !port.IsOpen)
                return;

            WriteSymbols(new SerialPortLine(port), symbols);
        }
    }

    public int Read(Span<SasSymbol> destination, TimeSpan timeout)
    {
        if (_linux is { } linux)
            return ReadSymbols(linux, destination, timeout, _readBuffer);

        SerialPort? port = _port;
        if (port is null || !port.IsOpen)
            return 0;

        return ReadSymbols(new SerialPortInput(port), destination, timeout, _readBuffer);
    }

    /// <summary>
    /// Reads what has arrived, waiting up to <paramref name="timeout"/> for the first byte; a zero
    /// timeout still takes what is already waiting, so a drain before a poll empties the line.
    /// </summary>
    public static int ReadSymbols(
        ISerialInput input, Span<SasSymbol> destination, TimeSpan timeout, byte[] buffer)
    {
        if (destination.IsEmpty)
            return 0;

        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        int produced = 0;

        while (true)
        {
            int available;
            try
            {
                available = input.BytesToRead;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }

            if (available > 0)
            {
                int wanted = Math.Min(Math.Min(available, buffer.Length), destination.Length);
                int read;
                try
                {
                    read = input.Read(buffer, 0, wanted);
                }
                catch (TimeoutException)
                {
                    read = 0;
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException)
                {
                    return 0;
                }

                // A gaming machine never sets the wakeup bit, so every received byte is data.
                for (int i = 0; i < read; i++)
                    destination[produced++] = SasSymbol.Data8(buffer[i]);

                if (produced > 0)
                    return produced;
            }

            if (Environment.TickCount64 >= deadline)
                return produced;

            Thread.Sleep(1);
        }
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        lock (_gate)
        {
            _linux?.Dispose();
            _linux = null;

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

    private sealed class SerialPortInput(SerialPort port) : ISerialInput
    {
        public int BytesToRead => port.BytesToRead;

        public int Read(byte[] buffer, int offset, int count) => port.Read(buffer, offset, count);
    }

    private sealed class SerialPortLine(SerialPort port) : ISerialLine
    {
        // How long to wait for the driver's queue to empty before giving up on it.
        private static readonly TimeSpan QueueLimit = TimeSpan.FromMilliseconds(100);

        public Parity Parity
        {
            get => port.Parity;
            set => port.Parity = value;
        }

        // The driver's queue empties when the UART has taken the byte, not when it is on the wire,
        // so two character times cover one byte in the holding register and one being shifted out.
        public void Drain()
        {
            port.BaseStream.Flush();

            long queueDeadline = Stopwatch.GetTimestamp() + Ticks(QueueLimit);
            while (port.BytesToWrite > 0 && Stopwatch.GetTimestamp() < queueDeadline)
                Thread.SpinWait(50);

            long wireDeadline = Stopwatch.GetTimestamp() + Ticks(2 * CharacterTime(port.BaudRate));
            while (Stopwatch.GetTimestamp() < wireDeadline)
                Thread.SpinWait(20);
        }

        public void WriteByte(byte value) => port.BaseStream.WriteByte(value);

        public void Flush() => port.BaseStream.Flush();

        private static long Ticks(TimeSpan span) =>
            (long)(span.TotalSeconds * Stopwatch.Frequency);
    }
}
