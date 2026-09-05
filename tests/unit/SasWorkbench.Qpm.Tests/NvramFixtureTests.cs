namespace SasWorkbench.Qpm.Tests;

/// <summary>
/// A saved machine memory is the one artefact in this project that cannot be regenerated from
/// source: it is produced by a person walking a menu. So the tests that matter are the ones about
/// not losing it — the round trip is exact, a restore keeps what it replaced, and neither
/// direction happens while a machine is running and writing to the same bytes.
/// </summary>
public class NvramFixtureTests
{
    [Fact]
    public void WhatIsSavedIsExactlyWhatComesBack()
    {
        using var directory = new TemporaryDirectory();
        string memory = Path.Combine(directory.Path, "nvram.bin");
        string saved = Path.Combine(directory.Path, "golden.nvram.gz");
        byte[] contents = Contents();
        File.WriteAllBytes(memory, contents);

        NvramSnapshot snapshot = NvramFixture.Save(memory, saved, RegionOf(directory));

        File.WriteAllBytes(memory, new byte[contents.Length]);
        NvramFixture.Restore(memory, saved, RegionOf(directory));

        Assert.Equal(contents, File.ReadAllBytes(memory));
        Assert.Equal(contents.Length, snapshot.Bytes);
    }

    [Fact]
    public void AMostlyEmptyMemoryIsSmallEnoughToKeepBesideTheTests()
    {
        using var directory = new TemporaryDirectory();
        string memory = Path.Combine(directory.Path, "nvram.bin");
        string saved = Path.Combine(directory.Path, "golden.nvram.gz");
        File.WriteAllBytes(memory, Contents());

        NvramFixture.Save(memory, saved, RegionOf(directory));

        Assert.True(new FileInfo(saved).Length < 64 * 1024,
            "a saved memory that does not fit in a repository is one nobody will commit");
    }

    [Fact]
    public void RestoringKeepsWhatItReplaced()
    {
        using var directory = new TemporaryDirectory();
        string memory = Path.Combine(directory.Path, "nvram.bin");
        string saved = Path.Combine(directory.Path, "golden.nvram.gz");
        File.WriteAllBytes(memory, Contents());
        NvramFixture.Save(memory, saved, RegionOf(directory));

        byte[] somebodyElses = Contents(seed: 99);
        File.WriteAllBytes(memory, somebodyElses);

        string kept = NvramFixture.Restore(memory, saved, RegionOf(directory));

        Assert.True(File.Exists(kept));
        Assert.Equal(somebodyElses, File.ReadAllBytes(kept));
    }

    [Fact]
    public void NeitherDirectionHappensWhileAMachineIsRunning()
    {
        using var directory = new TemporaryDirectory();
        using var egm = FakeGamingMachine.Start(directory.Path);
        string memory = Path.Combine(directory.Path, "nvram.bin");
        string saved = Path.Combine(directory.Path, "golden.nvram.gz");
        File.WriteAllBytes(memory, Contents());

        QpmUnavailableException refusedSave = Assert.Throws<QpmUnavailableException>(
            () => NvramFixture.Save(memory, saved, egm.Path));
        QpmUnavailableException refusedRestore = Assert.Throws<QpmUnavailableException>(
            () => NvramFixture.Restore(memory, saved, egm.Path));

        Assert.Contains("running", refusedSave.Reason, StringComparison.Ordinal);
        Assert.Contains("running", refusedRestore.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatIsNotASavedMemoryIsRefusedRatherThanWrittenOverTheMachine()
    {
        using var directory = new TemporaryDirectory();
        string memory = Path.Combine(directory.Path, "nvram.bin");
        string notSaved = Path.Combine(directory.Path, "notes.txt");
        byte[] original = Contents();
        File.WriteAllBytes(memory, original);
        File.WriteAllText(notSaved, "this is not a machine memory");

        QpmUnavailableException refused = Assert.Throws<QpmUnavailableException>(
            () => NvramFixture.Restore(memory, notSaved, RegionOf(directory)));

        Assert.Contains("not a saved machine memory", refused.Reason, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(memory));
    }

    [Fact]
    public void TwoDifferentMemoriesDoNotLookAlike()
    {
        using var directory = new TemporaryDirectory();
        string one = Path.Combine(directory.Path, "one.bin");
        string two = Path.Combine(directory.Path, "two.bin");
        File.WriteAllBytes(one, Contents(seed: 1));
        File.WriteAllBytes(two, Contents(seed: 2));

        Assert.NotEqual(NvramFixture.Live(one).Digest, NvramFixture.Live(two).Digest);
    }

    /// <summary>A memory shaped like a real one: eight megabytes, almost all of it zero.</summary>
    private static byte[] Contents(int seed = 7)
    {
        var memory = new byte[8 * 1024 * 1024];
        var random = new Random(seed);
        random.NextBytes(memory.AsSpan(0, 50_000));
        return memory;
    }

    /// <summary>A region file with nothing reading it, so the liveness check sees no machine.</summary>
    private static string RegionOf(TemporaryDirectory directory) =>
        FakeGamingMachine.StartNothing(directory.Path);
}
