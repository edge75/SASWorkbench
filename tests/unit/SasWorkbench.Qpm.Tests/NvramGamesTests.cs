namespace SasWorkbench.Qpm.Tests;

/// <summary>
/// The games' enable records in a layout-19 memory, read and switched on from outside the machine.
/// </summary>
/// <remarks>
/// QSP 2.3.6.32 keeps one enable record per denomination a game has had, and a denomination chosen in
/// the audit menu after the memory was cleared starts switched off. The hand-configured fixtures
/// were saved in exactly that state, so every game read as disabled and long poll 56 reported none.
/// The offsets were established from the fixtures themselves, so the fixtures are the evidence here.
/// </remarks>
public class NvramGamesTests
{
    [Fact]
    public void TheHandConfiguredMemoryHasEachGameAtTwoDenominationsWithOnlyTheFirstSwitchedOn()
    {
        byte[] memory = Load(Path.Combine(RepoRoot(), "fixtures", "nvram", "arrangement-19-as-saved", "golden.nvram.gz"));

        IReadOnlyList<NvramGameSlot> slots = NvramGames.Read(memory);

        Assert.Equal(
            [
                new NvramGameSlot(1, 0, 1, true, true), new NvramGameSlot(1, 1, 5, false, false),
                new NvramGameSlot(2, 0, 2, true, true), new NvramGameSlot(2, 1, 10, false, false),
                new NvramGameSlot(3, 0, 5, true, true), new NvramGameSlot(3, 1, 50, false, false),
                new NvramGameSlot(4, 0, 10, true, true), new NvramGameSlot(4, 1, 100, false, false),
            ],
            slots);
    }

    [Fact]
    public void SwitchingTheGamesOnEnablesEveryDenominationTheUnitDividesAndTouchesNothingElse()
    {
        byte[] before = Load(Path.Combine(RepoRoot(), "fixtures", "nvram", "arrangement-19-as-saved", "golden.nvram.gz"));

        byte[] after = NvramGames.WithGamesEnabled(before);

        // A unit of five cents cannot express one or two cents; those slots were on and stay on,
        // because the machine's own rule blocks enabling them, not keeping them enabled.
        Assert.All(NvramGames.Read(after), slot =>
        {
            Assert.True(slot.MasterEnabled, $"game {slot.Game} at {slot.DenominationCents}c");
            Assert.True(slot.NetworkEnabled, $"game {slot.Game} at {slot.DenominationCents}c");
        });

        int[] changed =
            [.. Enumerable.Range(0, NvramConfiguration.BankBytes).Where(i => before[i] != after[i])];
        // The enable record of each game's second slot: two bytes, after its eight denominations.
        int[] expected =
        [
            .. Enumerable.Range(0, 4).SelectMany(game =>
            {
                int mask = NvramGames.FirstGameSlots + (game * NvramGames.GameStride) + 32 + 2;
                return new[] { mask, mask + 1 };
            }),
        ];
        Assert.All(changed, offset =>
            Assert.True(offset < 2 || expected.Contains(offset),
                $"offset 0x{offset:X6} is neither a mark nor an enable record that was off"));
        Assert.True(after.AsSpan(0, NvramConfiguration.BankBytes)
            .SequenceEqual(after.AsSpan(NvramConfiguration.BankBytes)));
        NvramMachineConfiguration was = NvramConfiguration.Read(before);
        Assert.Equal(was with { Caveat = null },
            NvramConfiguration.Read(after) with { Caveat = null, PollAddresses = was.PollAddresses });
    }

    [Fact]
    public void TheCommittedGoldenMemoryIsTheHandConfiguredOneWithItsGamesSwitchedOn()
    {
        byte[] rebuilt = NvramGames.WithGamesEnabled(
            Load(Path.Combine(RepoRoot(), "fixtures", "nvram", "arrangement-19-as-saved", "golden.nvram.gz")));
        byte[] committed = Load(Path.Combine(RepoRoot(), "fixtures", "nvram", "golden.nvram.gz"));

        Assert.True(rebuilt.AsSpan().SequenceEqual(committed),
            "the committed golden memory is not the hand-configured one with its games switched on");
    }

    [Fact]
    public void AMemoryInAnotherLayoutIsRefused()
    {
        byte[] memory = Load(Path.Combine(RepoRoot(), "fixtures", "nvram", "arrangement-15", "golden.nvram.gz"));

        QpmUnavailableException refused =
            Assert.Throws<QpmUnavailableException>(() => NvramGames.WithGamesEnabled(memory));

        Assert.Contains("revision 15", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AGameRecordHoldingNoRealDenominationIsRefusedRatherThanWritten()
    {
        byte[] memory = Load(Path.Combine(RepoRoot(), "fixtures", "nvram", "golden.nvram.gz"));
        int slots = NvramGames.FirstGameSlots;
        memory[slots] = 7; // Seven cents has no code, so these are not a game's denominations.
        Reseal(memory);

        QpmUnavailableException refused =
            Assert.Throws<QpmUnavailableException>(() => NvramGames.WithGamesEnabled(memory));

        Assert.Contains("game 1", refused.Reason, StringComparison.Ordinal);
    }

    /// <summary>Copies bank zero over bank one and rewrites both marks, as a machine would.</summary>
    private static void Reseal(byte[] memory)
    {
        int size = NvramConfiguration.BankBytes;
        memory.AsSpan(0, size).CopyTo(memory.AsSpan(size, size));
        for (int bank = 0; bank < memory.Length; bank += size)
        {
            ushort mark = 0;
            for (int offset = bank + 2; offset + 1 < bank + size; offset += 2)
                mark ^= (ushort)(memory[offset] | (memory[offset + 1] << 8));
            memory[bank] = (byte)mark;
            memory[bank + 1] = (byte)(mark >> 8);
        }
    }

    private static byte[] Load(string path)
    {
        using FileStream file = File.OpenRead(path);
        using var decompressor = new System.IO.Compression.GZipStream(
            file, System.IO.Compression.CompressionMode.Decompress);
        using var memory = new MemoryStream();
        decompressor.CopyTo(memory);
        return memory.ToArray();
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "fixtures", "nvram")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root from the test output.");
    }
}
