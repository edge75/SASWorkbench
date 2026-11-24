using System.Diagnostics;
using System.IO.Ports;

namespace SasWorkbench.Core.Transport;

/// <summary>What checking a port for the wakeup bit found.</summary>
/// <param name="CarriesWakeupBit">True when the driver kept both mark and space stick parity.</param>
/// <param name="Detail">One sentence saying why, naming the driver when it is known.</param>
public sealed record WakeupBitCheck(bool CarriesWakeupBit, string Detail);

/// <summary>
/// A Linux serial port that carries the SAS wakeup bit as stick parity, and refuses to open when
/// its driver will not keep mark and space parity.
/// </summary>
public sealed class LinuxSerialPort : ISerialLine, ISerialInput, IDisposable
{
    private readonly ITermiosDevice _device;
    private readonly int _baudRate;
    private KernelTermios _settings;
    private bool _mark;

    private LinuxSerialPort(ITermiosDevice device, int baudRate, KernelTermios settings)
    {
        _device = device;
        _baudRate = baudRate;
        _settings = settings;
        _mark = false;
    }

    /// <summary>The driver behind the port, when sysfs names one.</summary>
    public string? Driver => _device.Driver;

    /// <summary>Turns a short name such as ttyS4 into its device path.</summary>
    public static string DevicePath(string portName) =>
        portName.StartsWith('/') ? portName : $"/dev/{portName}";

    /// <summary>Configures the device for SAS, refusing if either stick parity does not read back.</summary>
    public static LinuxSerialPort Open(ITermiosDevice device, int baudRate)
    {
        KernelTermios original = device.GetAttributes();
        WakeupBitCheck check = Verify(device, original, baudRate, out KernelTermios space);
        if (!check.CarriesWakeupBit)
        {
            device.Dispose();
            throw new TransportRefusedException(check.Detail);
        }

        return new LinuxSerialPort(device, baudRate, space);
    }

    /// <summary>Checks a device for the wakeup bit and puts its settings back as they were.</summary>
    public static WakeupBitCheck Probe(ITermiosDevice device, int baudRate)
    {
        KernelTermios original = device.GetAttributes();
        try
        {
            return Verify(device, original, baudRate, out _);
        }
        finally
        {
            device.SetAttributes(original);
        }
    }

    private static WakeupBitCheck Verify(
        ITermiosDevice device, KernelTermios current, int baudRate, out KernelTermios space)
    {
        string driver = device.Driver is { } name ? $"driver {name}" : "an unnamed driver";

        KernelTermios mark = TermiosFrame.ForSas(current, baudRate, mark: true);
        device.SetAttributes(mark);
        KernelTermios markBack = device.GetAttributes();

        space = TermiosFrame.ForSas(current, baudRate, mark: false);
        device.SetAttributes(space);
        KernelTermios spaceBack = device.GetAttributes();

        if (!TermiosFrame.Holds(markBack, mark))
            return Refusal(device.Path, driver, "mark", TermiosFrame.Dropped(markBack, mark));
        if (!TermiosFrame.Holds(spaceBack, space))
            return Refusal(device.Path, driver, "space", TermiosFrame.Dropped(spaceBack, space));

        return new WakeupBitCheck(
            true,
            $"{device.Path} ({driver}) kept mark and space stick parity at {baudRate} baud.");
    }

    private static WakeupBitCheck Refusal(string path, string driver, string which, string dropped) =>
        new(false,
            $"{path} ({driver}) cannot carry the wakeup bit: asked for {which} stick parity, "
            + $"the driver {dropped}. Refusing rather than sending eight-bit bytes no machine "
            + "would recognise as addresses.");

    public Parity Parity
    {
        get => _mark ? Parity.Mark : Parity.Space;
        set
        {
            bool mark = value switch
            {
                Parity.Mark => true,
                Parity.Space => false,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(value), value, "Only mark and space carry the wakeup bit."),
            };

            if (mark == _mark)
                return;

            KernelTermios next = TermiosFrame.WithStickParity(_settings, mark);
            _device.SetAttributes(next);
            _settings = next;
            _mark = mark;
        }
    }

    // The driver's drain is followed by one character time in case it reports empty while the last byte is still shifting out.
    public void Drain()
    {
        _device.Drain();
        long until = Stopwatch.GetTimestamp()
            + (long)(SerialTransport.CharacterTime(_baudRate).TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < until)
            Thread.SpinWait(20);
    }

    public void WriteByte(byte value) => _device.Write(value);

    // A write is already in the kernel when it returns, so there is nothing to push.
    public void Flush()
    {
    }

    // Reported as the framework reports a port that has gone away, so the reader treats both alike.
    public int BytesToRead
    {
        get
        {
            try
            {
                return _device.BytesAvailable;
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException(ex.Message, ex);
            }
        }
    }

    public int Read(byte[] buffer, int offset, int count) => _device.Read(buffer, offset, count);

    public void Dispose() => _device.Dispose();
}
