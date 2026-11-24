using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SasWorkbench.Core.Transport;

/// <summary>The nineteen control characters of the Linux kernel's own terminal settings.</summary>
[InlineArray(19)]
public struct KernelControlChars
{
    private byte _first;
}

/// <summary>
/// The Linux kernel's terminal settings exactly as the TCGETS and TCSETS requests carry them on
/// x86-64 and ARM, so no C library's own idea of a baud rate stands between us and the driver.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 36)]
public struct KernelTermios
{
    [FieldOffset(0)] public uint InputFlags;
    [FieldOffset(4)] public uint OutputFlags;
    [FieldOffset(8)] public uint ControlFlags;
    [FieldOffset(12)] public uint LocalFlags;
    [FieldOffset(16)] public byte LineDiscipline;
    [FieldOffset(17)] public KernelControlChars ControlChars;
}

/// <summary>
/// Builds and checks the terminal settings that carry the SAS wakeup bit as stick parity:
/// mark for the address byte, space for every other byte.
/// </summary>
public static class TermiosFrame
{
    public const uint CBAUD = 0x0000_100F;
    public const uint CSIZE = 0x0000_0030;
    public const uint CS8 = 0x0000_0030;
    public const uint CSTOPB = 0x0000_0040;
    public const uint CREAD = 0x0000_0080;
    public const uint PARENB = 0x0000_0100;
    public const uint PARODD = 0x0000_0200;
    public const uint HUPCL = 0x0000_0400;
    public const uint CLOCAL = 0x0000_0800;
    public const uint CIBAUD = 0x100F_0000;
    public const uint CMSPAR = 0x4000_0000;
    public const uint CRTSCTS = 0x8000_0000;

    public const int VTIME = 5;
    public const int VMIN = 6;

    /// <summary>The control bits that must read back unchanged for the wakeup bit to be trusted.</summary>
    public const uint FramingMask = CBAUD | CSIZE | CSTOPB | PARENB | PARODD | CMSPAR;

    /// <summary>The speed code for a SAS baud rate, or null when the kernel has no fixed code for it.</summary>
    public static uint? SpeedCode(int baudRate) => baudRate switch
    {
        9600 => 0x0000_000D,
        19200 => 0x0000_000E,
        38400 => 0x0000_000F,
        57600 => 0x0000_1001,
        115200 => 0x0000_1002,
        _ => null,
    };

    /// <summary>
    /// Raw eight-bit settings with stick parity: no echo, no translation, no flow control, no
    /// input parity check (a machine never sets the ninth bit, so its bytes arrive as parity
    /// errors in mark and must be kept as received), and reads that never wait.
    /// </summary>
    public static KernelTermios ForSas(KernelTermios current, int baudRate, bool mark)
    {
        uint speed = SpeedCode(baudRate)
            ?? throw new TransportRefusedException(
                $"{baudRate} baud has no fixed Linux speed code; SAS runs at 19200.");

        KernelTermios next = current;
        next.InputFlags = 0;
        next.OutputFlags = 0;
        next.LocalFlags = 0;
        next.ControlFlags = speed | CS8 | CREAD | CLOCAL | PARENB | CMSPAR | (mark ? PARODD : 0);
        next.ControlChars[VMIN] = 0;
        next.ControlChars[VTIME] = 0;
        return next;
    }

    /// <summary>The same settings with the parity bit stuck the other way.</summary>
    public static KernelTermios WithStickParity(KernelTermios current, bool mark)
    {
        KernelTermios next = current;
        next.ControlFlags = mark ? current.ControlFlags | PARODD : current.ControlFlags & ~PARODD;
        return next;
    }

    /// <summary>True when the driver kept every framing bit that was asked for.</summary>
    public static bool Holds(KernelTermios readBack, KernelTermios wanted) =>
        (readBack.ControlFlags & FramingMask) == (wanted.ControlFlags & FramingMask);

    /// <summary>Names the framing bits a driver changed, for the refusal message.</summary>
    public static string Dropped(KernelTermios readBack, KernelTermios wanted)
    {
        uint lost = (wanted.ControlFlags & ~readBack.ControlFlags) & FramingMask;
        uint added = (readBack.ControlFlags & ~wanted.ControlFlags) & FramingMask;
        var names = new List<string>();
        foreach ((uint bit, string name) in new[]
                 {
                     (CMSPAR, "CMSPAR"), (PARENB, "PARENB"), (PARODD, "PARODD"),
                     (CSIZE, "CSIZE"), (CSTOPB, "CSTOPB"), (CBAUD, "CBAUD"),
                 })
        {
            if ((lost & bit) != 0)
                names.Add($"cleared {name}");
            else if ((added & bit) != 0)
                names.Add($"set {name}");
        }

        return names.Count == 0 ? "nothing" : string.Join(", ", names);
    }
}
