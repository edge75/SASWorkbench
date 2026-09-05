using System.Runtime.InteropServices;

namespace SasWorkbench.Qpm;

/// <summary>
/// Where each field of the peripheral simulator's shared region lives, and who owns it.
/// </summary>
/// <remarks>
/// The region is a plain struct with no header, no framing and no lock. What keeps two writers
/// from corrupting each other is that no field has two owners: the machine writes the heartbeat
/// and the lamps, whoever is providing stimulus writes the four masks, and nobody reads a field
/// in order to write it back. Every write here is one aligned 32-bit store, which cannot tear.
/// </remarks>
public static class QpmLayout
{
    /// <summary>Offset of the heartbeat counter. Written by the machine, once per 125ms.</summary>
    public const int Heartbeat = 0x000;

    /// <summary>Offset of the door mask. One bit per intrusion input.</summary>
    public const int Intrusion = 0x004;

    /// <summary>Offset of the general-purpose input mask, which is where buttons live.</summary>
    public const int Dio = 0x008;

    /// <summary>Offset of the first lamp. Written by the machine; a read-back of its intent.</summary>
    public const int Lamps = 0x00C;

    /// <summary>How many lamps there are.</summary>
    public const int LampCount = 32;

    /// <summary>Offset of the note acceptor mask.</summary>
    public const int Bna = 0x08C;

    /// <summary>Offset of the printer mask.</summary>
    public const int Printer = 0x090;

    /// <summary>How much of the region is defined. The rest is zero.</summary>
    public const int DefinedBytes = 0x094;

    /// <summary>How large the region is, in bytes.</summary>
    public const int RegionBytes = 1024;

    /// <summary>Bit of the note mask that reports the acceptor as disconnected.</summary>
    public const int BnaDisconnectedBit = 8;

    /// <summary>Bit of the note mask that reports a jam.</summary>
    public const int BnaJamBit = 9;

    /// <summary>How often the machine advances the heartbeat.</summary>
    public static TimeSpan HeartbeatInterval { get; } = TimeSpan.FromMilliseconds(125);

    /// <summary>
    /// The note values the machine will recognise, in mask-bit order, in cents.
    /// </summary>
    /// <remarks>
    /// This is the machine's own table, not the simulator's. The simulator labels these bits from
    /// its configuration file and the two disagree from bit seven upwards, so a note asked for by
    /// value is resolved here and a value that is not in this list is refused rather than
    /// approximated. The machine only examines the first eight bits, so only eight notes exist
    /// however many its table holds.
    /// </remarks>
    public static IReadOnlyList<int> NoteValues => Notes;

    private static readonly int[] Notes = [100, 200, 500, 1000, 2000, 5000, 10000, 20000];

    /// <summary>Where the shared region is on this operating system.</summary>
    /// <remarks>
    /// It is a file-backed section with no kernel object name, so it is opened as a file. Asking
    /// the operating system for a named mapping finds nothing at all.
    /// </remarks>
    public static string DefaultPath => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? @"C:\ProgramData\shared_memory\lsm_QpmSharedMemory.shm"
        : "/dev/shm/QpmSharedMemory";

    /// <summary>The mask bit for a note of this many cents, or null when there is none.</summary>
    public static int? NoteBit(int cents)
    {
        int index = Array.IndexOf(Notes, cents);
        return index < 0 ? null : index;
    }

    /// <summary>The note values a script may ask for, as a readable list.</summary>
    public static string NoteValuesAsText() => string.Join(", ", NoteValues);
}

/// <summary>What a lamp is doing, as the machine reports it.</summary>
public enum QpmLampState
{
    Off = 0,
    On = 1,
    Blinking = 2,
    BlinkingAlternate = 4,
}
