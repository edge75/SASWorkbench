using System.IO.MemoryMappedFiles;

namespace SasWorkbench.Qpm;

/// <summary>Thrown when the peripheral simulator's shared region cannot be used, and why.</summary>
public sealed class QpmUnavailableException(string reason) : Exception(reason)
{
    /// <summary>The reason, phrased for the skip line of a report.</summary>
    public string Reason => Message;
}

/// <summary>
/// The peripheral simulator's shared region, mapped for reading and writing.
/// </summary>
/// <remarks>
/// Every access goes through a volatile load or store on the mapped page. The region is written
/// by another process while this one reads it, so an access the compiler is free to hoist out of
/// a loop or fold away would make a heartbeat that has stopped look like one that is still
/// running — the one mistake that turns "the machine is gone" into "the stimulus worked".
/// </remarks>
public sealed unsafe class QpmSharedRegion : IDisposable
{
    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private byte* _base;
    private bool _disposed;

    private QpmSharedRegion(string path, MemoryMappedFile file, MemoryMappedViewAccessor view)
    {
        Path = path;
        _file = file;
        _view = view;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _base);
    }

    /// <summary>The file the region was mapped from.</summary>
    public string Path { get; }

    /// <summary>The machine's heartbeat counter.</summary>
    public uint Heartbeat => Read(QpmLayout.Heartbeat);

    /// <summary>
    /// Maps the region, or explains why it could not be.
    /// </summary>
    /// <param name="path">Where the region is, or null for this platform's usual place.</param>
    /// <exception cref="QpmUnavailableException">It is missing, too small, or unreadable.</exception>
    public static QpmSharedRegion Open(string? path = null)
    {
        string target = string.IsNullOrWhiteSpace(path) ? QpmLayout.DefaultPath : path;

        if (!File.Exists(target))
            throw new QpmUnavailableException(
                $"there is no peripheral shared memory at '{target}': the file appears once a "
                + "gaming machine or the peripheral simulator has run at least once");

        var info = new FileInfo(target);
        if (info.Length < QpmLayout.DefinedBytes)
            throw new QpmUnavailableException(
                $"'{target}' is {info.Length} bytes, and the peripheral region is "
                + $"{QpmLayout.DefinedBytes}: this is not the file it claims to be");

        FileStream stream;
        try
        {
            stream = new FileStream(
                target, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new QpmUnavailableException(
                $"'{target}' could not be opened for writing: {ex.Message}");
        }

        try
        {
            var file = MemoryMappedFile.CreateFromFile(
                stream, mapName: null, capacity: 0,
                MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: false);

            MemoryMappedViewAccessor view = file.CreateViewAccessor(
                0, QpmLayout.RegionBytes, MemoryMappedFileAccess.ReadWrite);

            return new QpmSharedRegion(target, file, view);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stream.Dispose();
            throw new QpmUnavailableException($"'{target}' could not be mapped: {ex.Message}");
        }
    }

    /// <summary>Reads one field.</summary>
    public uint Read(int offset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Check(offset);
        return Volatile.Read(ref *(uint*)(_base + offset));
    }

    /// <summary>
    /// Writes one field, in a single aligned store.
    /// </summary>
    /// <remarks>
    /// A mask is always built in full and written once. Clearing a mask and then setting its bits
    /// back — which is what the simulator's own interface does every frame — leaves a window in
    /// which the machine can see every door shut and reopen, and invent a pair of door exceptions
    /// on the wire that nothing in the test asked for.
    /// </remarks>
    public void Write(int offset, uint value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Check(offset);
        Volatile.Write(ref *(uint*)(_base + offset), value);
    }

    /// <summary>Reads every lamp, so what the machine is trying to light can be observed.</summary>
    public IReadOnlyList<QpmLampState> Lamps()
    {
        var states = new QpmLampState[QpmLayout.LampCount];
        for (int i = 0; i < states.Length; i++)
            states[i] = (QpmLampState)Read(QpmLayout.Lamps + 4 * i);

        return states;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_base is not null)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _base = null;
        }

        _view.Dispose();
        _file.Dispose();
    }

    private static void Check(int offset)
    {
        if (offset < 0 || offset + sizeof(uint) > QpmLayout.RegionBytes || offset % 4 != 0)
            throw new ArgumentOutOfRangeException(nameof(offset), offset,
                "A field of the peripheral region is a 32-bit value at a 32-bit boundary.");
    }
}
