using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

public class TermiosFrameTests
{
    /// <summary>
    /// Stick parity is PARENB with CMSPAR: PARODD then fixes the ninth bit at one (mark, the
    /// address byte) and its absence fixes it at zero (space, every other byte).
    /// </summary>
    [Fact]
    public void Mark_and_space_are_stick_parity_differing_only_in_the_odd_bit()
    {
        KernelTermios mark = TermiosFrame.ForSas(default, 19200, mark: true);
        KernelTermios space = TermiosFrame.ForSas(default, 19200, mark: false);

        uint stick = TermiosFrame.PARENB | TermiosFrame.CMSPAR;
        Assert.Equal(stick, mark.ControlFlags & stick);
        Assert.Equal(stick, space.ControlFlags & stick);
        Assert.Equal(TermiosFrame.PARODD, mark.ControlFlags & TermiosFrame.PARODD);
        Assert.Equal(0u, space.ControlFlags & TermiosFrame.PARODD);
        Assert.Equal(TermiosFrame.PARODD, mark.ControlFlags ^ space.ControlFlags);
    }

    /// <summary>
    /// Eight data bits, one stop bit, 19200 baud, no hardware flow control, and the receiver on
    /// without waiting for carrier: the framing a gaming machine expects.
    /// </summary>
    [Fact]
    public void The_frame_is_eight_bits_one_stop_at_19200_with_no_flow_control()
    {
        KernelTermios start = default;
        start.ControlFlags = TermiosFrame.CRTSCTS | TermiosFrame.CSTOPB | TermiosFrame.HUPCL | 0x0000_000D;

        uint flags = TermiosFrame.ForSas(start, 19200, mark: false).ControlFlags;

        Assert.Equal(0x0000_000Eu, flags & TermiosFrame.CBAUD);
        Assert.Equal(TermiosFrame.CS8, flags & TermiosFrame.CSIZE);
        Assert.Equal(0u, flags & TermiosFrame.CSTOPB);
        Assert.Equal(0u, flags & TermiosFrame.CRTSCTS);
        Assert.Equal(0u, flags & TermiosFrame.CIBAUD);
        Assert.Equal(TermiosFrame.CREAD | TermiosFrame.CLOCAL,
            flags & (TermiosFrame.CREAD | TermiosFrame.CLOCAL));
    }

    /// <summary>
    /// In mark parity every byte a machine sends arrives with a parity error, because a machine
    /// never sets the ninth bit. With input checking on, the kernel would drop or mangle those
    /// bytes, so input checking, marking and stripping must all be off, and so must every
    /// translation and echo. Reads must never wait, because the reader times its own deadline.
    /// </summary>
    [Fact]
    public void Input_is_raw_with_no_parity_check_and_reads_never_wait()
    {
        KernelTermios start = default;
        start.InputFlags = 0xFFFF_FFFF;
        start.OutputFlags = 0xFFFF_FFFF;
        start.LocalFlags = 0xFFFF_FFFF;
        start.ControlChars[TermiosFrame.VMIN] = 1;
        start.ControlChars[TermiosFrame.VTIME] = 10;

        KernelTermios raw = TermiosFrame.ForSas(start, 19200, mark: true);

        Assert.Equal(0u, raw.InputFlags);
        Assert.Equal(0u, raw.OutputFlags);
        Assert.Equal(0u, raw.LocalFlags);
        Assert.Equal(0, raw.ControlChars[TermiosFrame.VMIN]);
        Assert.Equal(0, raw.ControlChars[TermiosFrame.VTIME]);
    }

    [Fact]
    public void A_baud_rate_with_no_kernel_code_is_refused_rather_than_approximated()
    {
        Assert.Throws<TransportRefusedException>(() => TermiosFrame.ForSas(default, 19201, mark: true));
    }

    [Fact]
    public void The_settings_struct_matches_the_kernel_layout()
    {
        Assert.Equal(36, Marshal.SizeOf<KernelTermios>());
    }

    [Theory]
    [InlineData("ttyS4", "/dev/ttyS4")]
    [InlineData("/dev/ttyS5", "/dev/ttyS5")]
    [InlineData("/dev/serial/by-id/x", "/dev/serial/by-id/x")]
    public void A_short_port_name_is_found_under_dev(string name, string path)
    {
        Assert.Equal(path, LinuxSerialPort.DevicePath(name));
    }
}

public class LinuxSerialPortTests
{
    /// <summary>
    /// The point of the backend: a driver that quietly forgets CMSPAR would turn mark and space
    /// into odd and even parity, and the address byte would carry a wakeup bit only when its
    /// data happened to need one. Opening must stop and say which bit the driver dropped.
    /// </summary>
    [Fact]
    public void A_driver_that_drops_stick_parity_is_refused_and_says_what_it_dropped()
    {
        var device = new FakeDevice { Clears = TermiosFrame.CMSPAR };

        TransportRefusedException refusal =
            Assert.Throws<TransportRefusedException>(() => LinuxSerialPort.Open(device, 19200));

        Assert.Contains("wakeup bit", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("cleared CMSPAR", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("driver fake_uart", refusal.Message, StringComparison.Ordinal);
        Assert.True(device.Disposed);
    }

    /// <summary>A pseudo-terminal clears PARENB on every request, which is a refusal of the same kind.</summary>
    [Fact]
    public void A_driver_that_clears_parity_altogether_is_refused()
    {
        var device = new FakeDevice { Clears = TermiosFrame.PARENB };

        TransportRefusedException refusal =
            Assert.Throws<TransportRefusedException>(() => LinuxSerialPort.Open(device, 19200));

        Assert.Contains("cleared PARENB", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_driver_that_keeps_stick_parity_opens_in_space()
    {
        var device = new FakeDevice();

        using LinuxSerialPort port = LinuxSerialPort.Open(device, 19200);

        Assert.Equal(Parity.Space, port.Parity);
        Assert.Equal(0u, device.Current.ControlFlags & TermiosFrame.PARODD);
        Assert.Equal(TermiosFrame.CMSPAR, device.Current.ControlFlags & TermiosFrame.CMSPAR);
    }

    /// <summary>
    /// The address byte goes out in mark and the rest in space, and the driver is drained before
    /// each switch, so the parity of a byte still in the transmitter is never changed under it.
    /// </summary>
    [Fact]
    public void A_poll_goes_out_with_the_wakeup_bit_on_its_address_byte_only()
    {
        var device = new FakeDevice();
        using LinuxSerialPort port = LinuxSerialPort.Open(device, 19200);
        device.Calls.Clear();

        SerialTransport.WriteSymbols(port, [SasSymbol.Address(0x01), SasSymbol.Data8(0x1F)]);

        Assert.Equal(["drain", "set mark", "write 01", "drain", "set space", "write 1F"], device.Calls);
    }

    [Fact]
    public void Only_mark_and_space_can_be_asked_for()
    {
        using LinuxSerialPort port = LinuxSerialPort.Open(new FakeDevice(), 19200);

        Assert.Throws<ArgumentOutOfRangeException>(() => port.Parity = Parity.Even);
    }

    /// <summary>
    /// A probe of a port someone else may be using must leave it as it found it, whether the
    /// port passes or not.
    /// </summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(TermiosFrame.CMSPAR)]
    public void A_probe_puts_the_settings_back(uint clears)
    {
        var device = new FakeDevice { Clears = clears };
        KernelTermios before = device.Current;
        before.ControlFlags = 0x0000_08BD;
        device.Current = before;

        WakeupBitCheck check = LinuxSerialPort.Probe(device, 19200);

        Assert.Equal(clears == 0, check.CarriesWakeupBit);
        Assert.Equal(0x0000_08BDu, device.Current.ControlFlags);
        Assert.False(device.Disposed);
    }

    [Fact]
    public void Received_bytes_are_kept_as_data()
    {
        var device = new FakeDevice();
        device.Incoming.Enqueue(0x01);
        device.Incoming.Enqueue(0xFF);
        using LinuxSerialPort port = LinuxSerialPort.Open(device, 19200);

        var destination = new SasSymbol[4];
        int read = SerialTransport.ReadSymbols(port, destination, TimeSpan.Zero, new byte[16]);

        Assert.Equal(2, read);
        Assert.Equal(SasSymbol.Data8(0x01), destination[0]);
        Assert.Equal(SasSymbol.Data8(0xFF), destination[1]);
    }

    private sealed class FakeDevice : ITermiosDevice
    {
        public uint Clears { get; init; }

        public KernelTermios Current { get; set; }

        public List<string> Calls { get; } = [];

        public Queue<byte> Incoming { get; } = new();

        public bool Disposed { get; private set; }

        public string Path => "/dev/ttyFAKE0";

        public string? Driver => "fake_uart";

        public KernelTermios GetAttributes() => Current;

        public void SetAttributes(in KernelTermios settings)
        {
            KernelTermios kept = settings;
            kept.ControlFlags &= ~Clears;
            Current = kept;
            Calls.Add((settings.ControlFlags & TermiosFrame.PARODD) != 0 ? "set mark" : "set space");
        }

        public void Drain() => Calls.Add("drain");

        public int BytesAvailable => Incoming.Count;

        public int Read(byte[] buffer, int offset, int count)
        {
            int n = 0;
            while (n < count && Incoming.Count > 0)
                buffer[offset + n++] = Incoming.Dequeue();
            return n;
        }

        public void Write(byte value) => Calls.Add($"write {value:X2}");

        public void Dispose() => Disposed = true;
    }
}

/// <summary>
/// Runs against the real Linux kernel, with a pseudo-terminal standing in for a port. A
/// pseudo-terminal clears PARENB on every settings request, exactly the kind of driver that would
/// silently send eight-bit bytes, so it proves the refusal works end to end through the kernel.
/// </summary>
public class LinuxKernelSerialTests
{
    [Fact]
    public void A_pseudo_terminal_cannot_carry_the_wakeup_bit_and_is_refused()
    {
        Assert.SkipUnless(LinuxTermiosDevice.IsSupported, "Needs the Linux kernel's terminal requests.");

        using PseudoTerminal pty = PseudoTerminal.Open();
        using var transport = new SerialTransport(new SerialTransportOptions(pty.SlavePath));

        TransportRefusedException refusal = Assert.Throws<TransportRefusedException>(transport.Open);

        Assert.Contains("cannot carry the wakeup bit", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("PARENB", refusal.Message, StringComparison.Ordinal);
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public void A_probe_of_a_pseudo_terminal_says_no_and_leaves_it_as_it_was()
    {
        Assert.SkipUnless(LinuxTermiosDevice.IsSupported, "Needs the Linux kernel's terminal requests.");

        using PseudoTerminal pty = PseudoTerminal.Open();
        KernelTermios before;
        using (LinuxTermiosDevice device = LinuxTermiosDevice.Open(pty.SlavePath))
            before = device.GetAttributes();

        WakeupBitCheck check = SerialTransport.CheckWakeupBit(new SerialTransportOptions(pty.SlavePath));

        using LinuxTermiosDevice again = LinuxTermiosDevice.Open(pty.SlavePath);
        Assert.False(check.CarriesWakeupBit);
        Assert.Equal(before.ControlFlags, again.GetAttributes().ControlFlags);
    }

    [Fact]
    public void A_missing_device_is_refused_with_its_path()
    {
        Assert.SkipUnless(LinuxTermiosDevice.IsSupported, "Needs the Linux kernel's terminal requests.");

        using var transport = new SerialTransport(new SerialTransportOptions("ttyNOSUCH9"));

        TransportRefusedException refusal = Assert.Throws<TransportRefusedException>(transport.Open);

        Assert.Contains("/dev/ttyNOSUCH9", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A port the kernel lists but cannot drive, such as an unwired legacy UART, fails its first
    /// request; that must come back as a refusal a runner can turn into a skip, not a crash.
    /// </summary>
    [Fact]
    public void A_port_that_fails_while_opening_is_refused_not_thrown_raw()
    {
        Assert.SkipUnless(LinuxTermiosDevice.IsSupported, "Needs the Linux kernel's terminal requests.");

        using var transport = new SerialTransport(new SerialTransportOptions("/dev/null"));

        Assert.Throws<TransportRefusedException>(transport.Open);
    }

    private sealed class PseudoTerminal : IDisposable
    {
        private readonly int _master;

        private PseudoTerminal(int master, string slavePath)
        {
            _master = master;
            SlavePath = slavePath;
        }

        public string SlavePath { get; }

        public static PseudoTerminal Open()
        {
            int master = posix_openpt(0x0002 | 0x0100);
            Assert.True(master >= 0, "posix_openpt failed");
            Assert.Equal(0, grantpt(master));
            Assert.Equal(0, unlockpt(master));

            byte[] name = new byte[128];
            Assert.Equal(0, ptsname_r(master, name, name.Length));
            return new PseudoTerminal(master, Encoding.UTF8.GetString(name, 0, Array.IndexOf(name, (byte)0)));
        }

        public void Dispose() => _ = close(_master);

        [DllImport("libc", SetLastError = true)]
        private static extern int posix_openpt(int flags);

        [DllImport("libc", SetLastError = true)]
        private static extern int grantpt(int fd);

        [DllImport("libc", SetLastError = true)]
        private static extern int unlockpt(int fd);

        [DllImport("libc", SetLastError = true)]
        private static extern int ptsname_r(int fd, byte[] buffer, nint length);

        [DllImport("libc", SetLastError = true)]
        private static extern int close(int fd);
    }
}
