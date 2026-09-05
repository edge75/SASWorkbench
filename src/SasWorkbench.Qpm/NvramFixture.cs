using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SasWorkbench.Qpm;

/// <summary>What a saved copy of a machine's memory is, and what it was taken from.</summary>
/// <param name="Path">Where the saved copy is.</param>
/// <param name="Bytes">How large the memory was.</param>
/// <param name="Digest">A hash of the memory, so two copies can be told apart.</param>
public sealed record NvramSnapshot(string Path, long Bytes, string Digest)
{
    public override string ToString() =>
        $"{Path} ({Bytes:N0} bytes, {Digest[..12]})";
}

/// <summary>
/// Saves and restores the gaming machine's non-volatile memory.
/// </summary>
/// <remarks>
/// <para>
/// A machine's configuration lives in this memory and nowhere else — which note acceptor it has,
/// which notes it will take, whether it can print a ticket at all. None of it can be set over the
/// wire, and all of it is set through a menu a person walks by hand. Saving that once and
/// restoring it is the difference between a suite anybody can run and a suite that begins with
/// twenty minutes of clicking.
/// </para>
/// <para>
/// It compresses to about a thousandth of its size, because almost all of it is zero, so a saved
/// copy is small enough to keep beside the tests that depend on it.
/// </para>
/// </remarks>
public static class NvramFixture
{
    /// <summary>Where the machine keeps its memory on this platform.</summary>
    public static string DefaultPath => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? @"C:\ProgramData\quixant\launchpad\nvram.bin"
        : "/var/lib/quixant/launchpad/nvram.bin";

    /// <summary>Saves the machine's memory to a compressed file.</summary>
    /// <exception cref="QpmUnavailableException">There is nothing to save, or it is in use.</exception>
    public static NvramSnapshot Save(
        string? memoryPath, string fixturePath, string? regionPath = null)
    {
        string source = Resolve(memoryPath);
        RequireStoppedMachine(regionPath,
            "saving a machine's memory while it is running captures it half-written");

        string? directory = Path.GetDirectoryName(Path.GetFullPath(fixturePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        byte[] memory = File.ReadAllBytes(source);

        using (FileStream output = File.Create(fixturePath))
        using (var compressor = new GZipStream(output, CompressionLevel.SmallestSize))
            compressor.Write(memory);

        return new NvramSnapshot(fixturePath, memory.Length, Digest(memory));
    }

    /// <summary>
    /// Writes a saved copy back over the machine's memory, keeping what was there.
    /// </summary>
    /// <remarks>
    /// The previous contents are always kept beside the file rather than discarded. A memory this
    /// tool did not write is somebody's configured machine, and there is no way to tell from the
    /// bytes whether it took five minutes or an afternoon to produce.
    /// </remarks>
    /// <returns>Where the previous contents were kept.</returns>
    /// <exception cref="QpmUnavailableException">The machine is running, or the file is not one.</exception>
    public static string Restore(
        string? memoryPath, string fixturePath, string? regionPath = null)
    {
        string target = Resolve(memoryPath, mustExist: false);
        RequireStoppedMachine(regionPath,
            "restoring a machine's memory underneath a running machine would leave it reading "
            + "one configuration and writing another");

        if (!File.Exists(fixturePath))
            throw new QpmUnavailableException($"there is no saved memory at '{fixturePath}'");

        byte[] memory = Decompress(fixturePath);

        string kept = $"{target}.replaced-{DateTime.UtcNow:yyyyMMddTHHmmssZ}";
        if (File.Exists(target))
            File.Copy(target, kept, overwrite: false);
        else
            kept = "<nothing was there>";

        string? directory = Path.GetDirectoryName(Path.GetFullPath(target));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllBytes(target, memory);
        return kept;
    }

    /// <summary>Describes a saved copy without restoring it.</summary>
    /// <exception cref="QpmUnavailableException">It is missing or not a saved copy.</exception>
    public static NvramSnapshot Describe(string fixturePath)
    {
        if (!File.Exists(fixturePath))
            throw new QpmUnavailableException($"there is no saved memory at '{fixturePath}'");

        byte[] memory = Decompress(fixturePath);
        return new NvramSnapshot(fixturePath, memory.Length, Digest(memory));
    }

    /// <summary>Describes the machine's memory as it is now.</summary>
    /// <exception cref="QpmUnavailableException">There is none.</exception>
    public static NvramSnapshot Live(string? memoryPath)
    {
        string source = Resolve(memoryPath);
        byte[] memory = File.ReadAllBytes(source);
        return new NvramSnapshot(source, memory.Length, Digest(memory));
    }

    private static byte[] Decompress(string fixturePath)
    {
        try
        {
            using FileStream input = File.OpenRead(fixturePath);
            using var decompressor = new GZipStream(input, CompressionMode.Decompress);
            using var memory = new MemoryStream();
            decompressor.CopyTo(memory);
            return memory.ToArray();
        }
        catch (InvalidDataException)
        {
            throw new QpmUnavailableException(
                $"'{fixturePath}' is not a saved machine memory: it is not compressed the way "
                + "one is written");
        }
    }

    private static string Resolve(string? memoryPath, bool mustExist = true)
    {
        string path = string.IsNullOrWhiteSpace(memoryPath) ? DefaultPath : memoryPath;

        if (mustExist && !File.Exists(path))
        {
            throw new QpmUnavailableException(
                $"there is no machine memory at '{path}': it appears once a gaming machine has "
                + "run at least once with a simulated memory");
        }

        return path;
    }

    /// <summary>
    /// Refuses while a gaming machine is running.
    /// </summary>
    /// <remarks>
    /// The machine holds its memory open and writes to it as it goes, so both directions are
    /// unsafe: a copy taken now may be a configuration nobody ever had, and a copy written now is
    /// overwritten by the machine within the second. The peripheral heartbeat is how we know one
    /// is running.
    /// </remarks>
    private static void RequireStoppedMachine(string? regionPath, string why)
    {
        QpmSharedRegion region;
        try
        {
            region = QpmSharedRegion.Open(regionPath);
        }
        catch (QpmUnavailableException)
        {
            // No shared region at all means no machine has ever run here, let alone now.
            return;
        }

        using (region)
        {
            uint first = region.Heartbeat;
            Thread.Sleep(400);
            if (region.Heartbeat == first)
                return;
        }

        throw new QpmUnavailableException($"a gaming machine is running, and {why}. Close it first");
    }

    private static string Digest(byte[] memory) =>
        Convert.ToHexStringLower(SHA256.HashData(memory));
}
