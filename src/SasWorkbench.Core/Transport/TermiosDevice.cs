using System.Runtime.InteropServices;

namespace SasWorkbench.Core.Transport;

/// <summary>The system calls a Linux serial port needs, so the wakeup-bit logic can be tested without one.</summary>
public interface ITermiosDevice : IDisposable
{
    /// <summary>The device path, such as /dev/ttyS4.</summary>
    string Path { get; }

    /// <summary>The kernel driver behind the port, such as serial8250, or null when it cannot be told.</summary>
    string? Driver { get; }

    KernelTermios GetAttributes();

    void SetAttributes(in KernelTermios settings);

    /// <summary>Returns once the driver reports every queued byte transmitted.</summary>
    void Drain();

    int BytesAvailable { get; }

    int Read(byte[] buffer, int offset, int count);

    void Write(byte value);
}

/// <summary>A Linux serial device driven through the kernel's own terminal requests.</summary>
public sealed class LinuxTermiosDevice : ITermiosDevice
{
    private const int O_RDWR = 0x0002;
    private const int O_NOCTTY = 0x0100;
    private const int O_NONBLOCK = 0x0800;
    private const int O_CLOEXEC = 0x80000;
    private const int EINTR = 4;
    private const int EAGAIN = 11;

    private const nuint TCGETS = 0x5401;
    private const nuint TCSETS = 0x5402;
    private const nuint TCSBRK = 0x5409;
    private const nuint TCFLSH = 0x540B;
    private const nuint FIONREAD = 0x541B;
    private const nint TCIOFLUSH = 2;

    private int _fd;

    private LinuxTermiosDevice(string path, int fd)
    {
        Path = path;
        _fd = fd;
        Driver = DriverOf(path);
    }

    public string Path { get; }

    public string? Driver { get; }

    /// <summary>True on the Linux builds whose terminal request numbers and layout this class knows.</summary>
    public static bool IsSupported =>
        OperatingSystem.IsLinux()
        && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;

    /// <summary>Opens a device without waiting for carrier and without making it the controlling terminal.</summary>
    public static LinuxTermiosDevice Open(string path)
    {
        int fd = open(path, O_RDWR | O_NOCTTY | O_NONBLOCK | O_CLOEXEC);
        if (fd < 0)
            throw new TransportRefusedException($"Cannot open {path}: {LastError()}");

        var device = new LinuxTermiosDevice(path, fd);
        try
        {
            device.Check(ioctl(fd, TCFLSH, TCIOFLUSH), "discard what was queued on");
        }
        catch (IOException ex)
        {
            device.Dispose();
            throw new TransportRefusedException(ex.Message, ex);
        }

        return device;
    }

    public KernelTermios GetAttributes()
    {
        KernelTermios settings = default;
        Check(ioctl(_fd, TCGETS, ref settings), "read the settings of");
        return settings;
    }

    public void SetAttributes(in KernelTermios settings)
    {
        KernelTermios copy = settings;
        Check(ioctl(_fd, TCSETS, ref copy), "apply settings to");
    }

    // TCSBRK with a non-zero argument is tcdrain: it waits for the driver's transmitter to empty.
    public void Drain() => Check(ioctl(_fd, TCSBRK, 1), "drain");

    public int BytesAvailable
    {
        get
        {
            Check(ioctl(_fd, FIONREAD, out int count), "count the bytes waiting on");
            return count;
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        while (true)
        {
            nint got = read(_fd, ref buffer[offset], count);
            if (got >= 0)
                return (int)got;

            int errno = Marshal.GetLastPInvokeError();
            if (errno == EAGAIN)
                return 0;
            if (errno != EINTR)
                throw new IOException($"Cannot read {Path}: {Marshal.GetPInvokeErrorMessage(errno)}");
        }
    }

    public void Write(byte value)
    {
        while (true)
        {
            nint put = write(_fd, ref value, 1);
            if (put == 1)
                return;

            int errno = Marshal.GetLastPInvokeError();
            if (put < 0 && errno == EAGAIN)
            {
                Drain();
                continue;
            }

            if (put < 0 && errno == EINTR)
                continue;

            throw new IOException($"Cannot write {Path}: {Marshal.GetPInvokeErrorMessage(errno)}");
        }
    }

    public void Dispose()
    {
        int fd = _fd;
        _fd = -1;
        if (fd >= 0)
            _ = close(fd);
    }

    /// <summary>Reads the driver name from sysfs, which tells an on-board UART from a USB adapter.</summary>
    public static string? DriverOf(string path)
    {
        try
        {
            string name = System.IO.Path.GetFileName(path);
            var link = new DirectoryInfo($"/sys/class/tty/{name}/device/driver");
            return link.ResolveLinkTarget(returnFinalTarget: true)?.Name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Check(int result, string what)
    {
        if (result < 0)
            throw new IOException($"Cannot {what} {Path}: {LastError()}");
    }

    private static string LastError() => Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError());

    [DllImport("libc", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern nint read(int fd, ref byte buffer, nint count);

    [DllImport("libc", SetLastError = true)]
    private static extern nint write(int fd, ref byte buffer, nint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, nuint request, ref KernelTermios settings);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, nuint request, out int value);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, nuint request, nint value);
}
