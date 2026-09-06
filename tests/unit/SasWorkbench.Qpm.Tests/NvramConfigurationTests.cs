using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Qpm.Tests;

/// <summary>
/// The configuration records inside a machine's memory, read and rewritten from outside it.
/// </summary>
/// <remarks>
/// Two things are being guarded here and they pull in opposite directions. The offsets are
/// established by reading a memory a machine wrote, so the committed fixtures are the evidence
/// and are asserted against directly — a test that made its own memory up would prove only that
/// this code agrees with itself. And every way of getting the offsets wrong has to end in a
/// refusal, because a wrong offset silently written into eight megabytes of a machine's
/// configuration is the worst outcome this file exists to prevent.
/// </remarks>
public class NvramConfigurationTests
{
    /// <summary>The revision of the memory arrangement these fixtures were saved in.</summary>
    private const uint FixtureLayout = 7;

    [Fact]
    public void TheCommittedGoldenMemoryIsAConfiguredMachineInStandardValidation()
    {
        NvramMachineConfiguration configuration = NvramFixture.Configuration(Fixture("golden"));

        Assert.Equal(FixtureLayout, configuration.LayoutVersion);
        Assert.Equal(0, configuration.ConfigurationRequired);
        Assert.Equal("QX", configuration.ManufacturerText);
        Assert.Equal(1, configuration.PollAddresses[0]);

        // The two that decide whether the money path exists at all. Zero means the machine has
        // no such device and cannot be given one over the wire.
        Assert.NotEqual(0, configuration.PrinterProtocol);
        Assert.NotEqual(0, configuration.BillValidatorProtocol);

        Assert.Equal(SasCashoutValidation.Standard, configuration.Validation);
        Assert.False(configuration.TicketInEnabled);
    }

    [Fact]
    public void TheCommittedSecureEnhancedMemoryIsTheSameMachineInTheOtherStyle()
    {
        NvramMachineConfiguration golden = NvramFixture.Configuration(Fixture("golden"));
        NvramMachineConfiguration enhanced = NvramFixture.Configuration(Fixture("secure-enhanced"));

        Assert.Equal(SasCashoutValidation.SecureEnhanced, enhanced.Validation);
        Assert.True(enhanced.TicketInEnabled);

        // Everything else about the machine is the one the operator configured by hand.
        Assert.Equal(golden.PrinterProtocol, enhanced.PrinterProtocol);
        Assert.Equal(golden.BillValidatorProtocol, enhanced.BillValidatorProtocol);
        Assert.Equal(golden.ManufacturerText, enhanced.ManufacturerText);
        Assert.Equal(golden.PollAddresses, enhanced.PollAddresses);
        Assert.Equal(golden.AssetNumber, enhanced.AssetNumber);
    }

    /// <summary>
    /// The mark a host sets is never set here.
    /// </summary>
    /// <remarks>
    /// A machine raises it itself when a host gives it a validation identifier, and until then it
    /// asks for one every fifteen seconds. A fixture that claimed the identifier was already set
    /// would silence that request without there being an identifier, which is the machine
    /// reporting a conversation that never happened.
    /// </remarks>
    [Fact]
    public void NoFixtureClaimsAHostAlreadySetTheValidationIdentifier()
    {
        foreach (string name in new[] { "golden", "secure-enhanced" })
            Assert.False(NvramFixture.Configuration(Fixture(name)).SecureEnhancedConfigured);
    }

    /// <summary>
    /// Changing the style touches the option word in each bank and each bank's mark, and nothing
    /// else at all.
    /// </summary>
    /// <remarks>
    /// This is the property that makes a rewritten fixture reviewable. A handful of bytes of
    /// difference from a memory a person configured by hand is something somebody can check;
    /// eight megabytes of difference is something they have to take on trust.
    /// </remarks>
    [Fact]
    public void PuttingAMachineInAStyleTouchesTheOptionWordAndTheMarksAndNothingElse()
    {
        const int optionWord = 0x00FE62 + 0x0C + 0x04;
        byte[] before = Memory("golden");
        byte[] after = NvramConfiguration.WithValidation(before, SasCashoutValidation.SecureEnhanced);

        int[] changed = [.. Enumerable.Range(0, before.Length).Where(i => before[i] != after[i])];
        int[] inTheBank = [.. changed.Select(i => i % NvramConfiguration.BankBytes).Distinct()];

        Assert.NotEmpty(changed);
        Assert.All(inTheBank, offset =>
            Assert.True(offset < 2 || (offset >= optionWord && offset < optionWord + 4),
                $"offset 0x{offset:X6} is neither the mark nor the option word"));
        Assert.Contains(optionWord, inTheBank);
    }

    [Theory]
    [InlineData(SasCashoutValidation.Standard, false)]
    [InlineData(SasCashoutValidation.SecureEnhanced, true)]
    [InlineData(SasCashoutValidation.System, true)]
    public void TheTicketInOptionFollowsTheStyleBecauseNoOtherCombinationIsAMachine(
        SasCashoutValidation validation, bool takesTickets)
    {
        byte[] written = NvramConfiguration.WithValidation(Memory("golden"), validation);
        NvramMachineConfiguration configuration = NvramConfiguration.Read(written);

        Assert.Equal(validation, configuration.Validation);
        Assert.Equal(takesTickets, configuration.TicketInEnabled);
    }

    /// <summary>
    /// The machine stores its validation styles in a different order from the one it reports them
    /// in, and the round trip is what keeps the two apart.
    /// </summary>
    /// <remarks>
    /// The stored numbering runs none, secure enhanced, system; the numbering on the wire runs
    /// standard, system, secure enhanced. Two of the three swap, and both of those are styles
    /// that can redeem a ticket — so a stored value used as a wire value stays in range and is
    /// wrong about exactly the thing a ticket test is asking.
    /// </remarks>
    [Fact]
    public void TheStyleSurvivesBeingWrittenAndReadBack()
    {
        byte[] memory = Memory("golden");

        foreach (SasCashoutValidation validation in new[]
                 {
                     SasCashoutValidation.System,
                     SasCashoutValidation.SecureEnhanced,
                     SasCashoutValidation.Standard,
                 })
        {
            memory = NvramConfiguration.WithValidation(memory, validation);
            Assert.Equal(validation, NvramConfiguration.Read(memory).Validation);
        }
    }

    [Fact]
    public void AMemoryWrittenInAnotherArrangementIsRefusedRatherThanReadAtAGuess()
    {
        byte[] memory = Memory("golden");
        memory[4] = (byte)(FixtureLayout + 1);
        memory[NvramConfiguration.BankBytes + 4] = (byte)(FixtureLayout + 1);
        Reseal(memory);

        QpmUnavailableException refused = Assert.Throws<QpmUnavailableException>(
            () => NvramConfiguration.Read(memory));

        Assert.Contains($"revision {FixtureLayout + 1}", refused.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A revision number two builds happen to agree on is not enough on its own.
    /// </summary>
    /// <remarks>
    /// Nothing stops two arrangements carrying the same revision, so the records are read back
    /// before they are believed. A host address past the largest SAS defines says the offsets
    /// belong to some other memory, whatever its revision claims.
    /// </remarks>
    [Fact]
    public void ARecordThatReadsAsSomethingNoMachineCouldHoldIsRefused()
    {
        byte[] memory = Memory("golden");
        int address = 0x00FE62 + 0x02;
        memory[address] = 0xFF;
        memory[address + 1] = 0x00;
        Reseal(memory);

        QpmUnavailableException refused = Assert.Throws<QpmUnavailableException>(
            () => NvramConfiguration.Read(memory));

        Assert.Contains("stop at 127", refused.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A memory whose two banks disagree is read the way the machine reads it, not refused.
    /// </summary>
    /// <remarks>
    /// A machine killed rather than closed leaves exactly this behind, routinely: one bank
    /// carries the record it had just written and the mark that does not yet cover it. The
    /// machine takes the bank that agrees with itself and copies it over the other on the way up.
    /// Refusing would make the common case unreadable; saying nothing about it would hide that
    /// half the memory was thrown away.
    /// </remarks>
    [Fact]
    public void AMemoryLeftPartWrittenIsReadFromTheBankThatAgreesWithItself()
    {
        byte[] memory = Memory("golden");
        memory[0x0100] ^= 0xFF;

        NvramMachineConfiguration configuration = NvramConfiguration.Read(memory);

        Assert.Equal(SasCashoutValidation.Standard, configuration.Validation);
        Assert.NotNull(configuration.Caveat);
        Assert.Contains("bank 1", configuration.Caveat, StringComparison.Ordinal);
    }

    [Fact]
    public void AMemoryWithNoSoundBankIsRefused()
    {
        byte[] memory = Memory("golden");
        memory[0x0100] ^= 0xFF;
        memory[NvramConfiguration.BankBytes + 0x0100] ^= 0xFF;

        QpmUnavailableException refused = Assert.Throws<QpmUnavailableException>(
            () => NvramConfiguration.Read(memory));

        Assert.Contains("neither bank", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SomethingThatIsNotAMachineMemoryIsRefusedBySize()
    {
        QpmUnavailableException refused = Assert.Throws<QpmUnavailableException>(
            () => NvramConfiguration.Read(new byte[16]));

        Assert.Contains("not a machine memory", refused.Reason, StringComparison.Ordinal);
    }

    /// <summary>Recomputes both marks, so a memory edited by a test still reads as whole.</summary>
    private static void Reseal(byte[] memory)
    {
        for (int bank = 0; bank < memory.Length; bank += NvramConfiguration.BankBytes)
        {
            ushort mark = 0;
            for (int offset = bank + 2; offset < bank + NvramConfiguration.BankBytes; offset += 2)
                mark ^= (ushort)(memory[offset] | (memory[offset + 1] << 8));

            memory[bank] = (byte)mark;
            memory[bank + 1] = (byte)(mark >> 8);
        }
    }

    private static byte[] Memory(string name)
    {
        // Read through the same door a caller uses, then taken apart, so the test cannot pass
        // against a memory the tool itself could not open.
        _ = NvramFixture.Configuration(Fixture(name));

        using FileStream file = File.OpenRead(Fixture(name));
        using var decompressor = new System.IO.Compression.GZipStream(
            file, System.IO.Compression.CompressionMode.Decompress);
        using var memory = new MemoryStream();
        decompressor.CopyTo(memory);
        return memory.ToArray();
    }

    private static string Fixture(string name) =>
        Path.Combine(RepoRoot(), "fixtures", "nvram", $"{name}.nvram.gz");

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
