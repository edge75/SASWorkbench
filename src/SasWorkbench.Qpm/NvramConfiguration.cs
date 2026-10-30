using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Qpm;

/// <summary>
/// What a saved machine memory says about the peripherals it has and the host link it answers.
/// </summary>
/// <param name="LayoutVersion">Which arrangement of records the memory was written in.</param>
/// <param name="ConfigurationRequired">
/// Which parts of the machine still have to be configured before it will run. Zero means none.
/// </param>
/// <param name="BillValidatorProtocol">Which note acceptor it has, or zero for none.</param>
/// <param name="PrinterProtocol">Which printer it has, or zero for none.</param>
/// <param name="ManufacturerId">The two characters the machine identifies its maker by.</param>
/// <param name="PollAddresses">The address it answers on, one per channel.</param>
/// <param name="AssetNumber">Its asset number, which funds transfer needs.</param>
/// <param name="Flags">The host-link options, as the machine stores them.</param>
/// <param name="Caveat">What is worth saying about the memory itself, when anything is.</param>
public sealed record NvramMachineConfiguration(
    uint LayoutVersion,
    ushort ConfigurationRequired,
    ushort BillValidatorProtocol,
    ushort PrinterProtocol,
    ushort ManufacturerId,
    IReadOnlyList<ushort> PollAddresses,
    uint AssetNumber,
    uint Flags,
    string? Caveat = null)
{
    /// <summary>Which validation style the machine performs when it cashes out.</summary>
    public SasCashoutValidation Validation => NvramConfiguration.ValidationOnTheWire(Flags);

    /// <summary>
    /// How this memory divides the machine between hosts, or the reason it divides it in a way
    /// no machine could serve.
    /// </summary>
    /// <remarks>
    /// Read rather than assumed, and reported as a refusal where it is one: a category pointed at
    /// a channel with no poll address reaches no host, and a memory carrying that describes a
    /// machine that would answer a poll nobody can send. The machine's own menu cannot write one,
    /// so a memory that has it was written by something else.
    /// </remarks>
    public (SasChannelPlan? Plan, string? Refusal) Channels
    {
        get
        {
            SasAddress?[] addresses =
                [.. PollAddresses.Select(a => a == 0 ? null : (SasAddress?)SasAddress.Of((byte)a))];

            return SasChannelPlan.TryCreate(
                addresses, NvramConfiguration.ChannelOwners(Flags),
                out SasChannelPlan plan, out string? refusal)
                ? (plan, null)
                : (null, refusal);
        }
    }

    /// <summary>Whether the operator has enabled taking a ticket at all.</summary>
    public bool TicketInEnabled => (Flags & NvramConfiguration.TicketInMask) != 0;

    /// <summary>
    /// Whether the host has already set the number the machine derives its tickets from.
    /// </summary>
    /// <remarks>
    /// The machine sets this itself, when a host sends it a validation identifier. A fixture that
    /// claimed it would be describing a conversation that never happened.
    /// </remarks>
    public bool SecureEnhancedConfigured =>
        (Flags & NvramConfiguration.SecureEnhancedConfiguredMask) != 0;

    /// <summary>The manufacturer identifier as the two characters it stands for.</summary>
    public string ManufacturerText =>
        ManufacturerId == 0 ? "<unset>" : $"{(char)(ManufacturerId >> 8)}{(char)(ManufacturerId & 0xFF)}";
}

/// <summary>
/// Reads and rewrites the configuration records inside a machine's saved memory.
/// </summary>
/// <remarks>
/// <para>
/// The configuration a ticket test depends on is set through the machine's own menu, by hand, on
/// a screen a person clicks. That is fine once and unbearable every time the memory is cleared,
/// so this reads and writes the same records directly — which is possible because the memory
/// protects itself with an arrangement simple enough to maintain from outside: it is two
/// identical banks, and the first sixteen-bit word of each is the exclusive-or of every other
/// word in it.
/// </para>
/// <para>
/// Where the records sit is not discoverable from the bytes, so it is pinned to the layout
/// revision the memory itself records, and a memory written in any other revision is refused
/// rather than written at a guess. That revision is the machine's own guard: it clears a memory
/// whose revision is not the one it was built for, so a fixture and a build that disagree already
/// cannot be used together.
/// </para>
/// </remarks>
public static class NvramConfiguration
{
    /// <summary>How large a machine's memory is, in bytes.</summary>
    public const int MemoryBytes = 0x800000;

    /// <summary>How large one of its two identical banks is.</summary>
    public const int BankBytes = MemoryBytes / 2;

    /// <summary>The operator's own ticket-in option.</summary>
    internal const uint TicketInMask = 1u << 1;

    /// <summary>Whether a host has already set the secure enhanced validation identifier.</summary>
    internal const uint SecureEnhancedConfiguredMask = 1u << 4;

    private const uint ValidationStyleMask = 3u << 2;
    private const int ValidationStyleShift = 2;

    /// <summary>
    /// Where the first category's channel number sits in the option word.
    /// </summary>
    /// <remarks>
    /// The platform stores the seven categories as seven consecutive two-bit fields, in the order
    /// the gaming machine library's own channel-control structure names them, holding a
    /// zero-based channel number each. So a category's field is found by its position in that
    /// order and nothing here has to name the categories again.
    /// </remarks>
    private const int FirstChannelShift = 14;

    private const int ChannelFieldBits = 2;

    // The machine numbers its stored validation styles differently from the way it reports them
    // on the wire: it stores none, secure enhanced, system, and it reports standard, system,
    // secure enhanced. Two of the three swap places, so a stored style cast straight to a wire
    // style is wrong for both of the styles that can redeem a ticket - and wrong in the quietest
    // possible way, since the value stays in range. Both directions go through this table.
    private const uint StoredNone = 0;
    private const uint StoredSecureEnhanced = 1;
    private const uint StoredSystem = 2;

    /// <summary>Where each record sits, for one arrangement of the machine's memory.</summary>
    /// <param name="LayoutVersion">The revision these offsets were established against.</param>
    /// <param name="ConfigurationMeters">Start of the configuration records.</param>
    /// <param name="SasNetwork">Start of the host-link records.</param>
    private sealed record NvramMap(uint LayoutVersion, int ConfigurationMeters, int SasNetwork);

    // Established by reading the machine's own records out of a memory it wrote: the venue name,
    // the address lines and the note acceptor's hash pin the configuration block, and the maker's
    // two characters followed by the poll address, the asset number and the option word pin the
    // host-link block. Both were then confirmed against the arrangement the platform declares.
    // Revision 15 was established the same way, from a memory QSP v2.3.6.1 wrote on 2026-10-30.
    private static readonly NvramMap[] KnownMaps =
    [
        new(LayoutVersion: 7, ConfigurationMeters: 0x000A00, SasNetwork: 0x00FE62),
        new(LayoutVersion: 15, ConfigurationMeters: 0x000A00, SasNetwork: 0x00FF1E),
    ];

    private const int ConfigurationRequiredOffset = 0x52;
    private const int BillValidatorProtocolOffset = 0x5E;
    private const int PrinterProtocolOffset = 0x60;

    private const int ManufacturerIdOffset = 0x00;
    private const int PollAddressOffset = 0x02;
    private const int PollAddressCount = 4;
    private const int AssetNumberOffset = 0x0A + 2;
    private const int FlagsOffset = AssetNumberOffset + 4;

    /// <summary>Which validation style a stored option word means on the wire.</summary>
    internal static SasCashoutValidation ValidationOnTheWire(uint flags) =>
        ((flags & ValidationStyleMask) >> ValidationStyleShift) switch
        {
            StoredSecureEnhanced => SasCashoutValidation.SecureEnhanced,
            StoredSystem => SasCashoutValidation.System,
            _ => SasCashoutValidation.Standard,
        };

    /// <summary>The option word that puts a machine in a validation style.</summary>
    /// <exception cref="QpmUnavailableException">The style is not one a machine can be put in.</exception>
    internal static uint WithValidation(uint flags, SasCashoutValidation validation)
    {
        uint stored = validation switch
        {
            SasCashoutValidation.Standard => StoredNone,
            SasCashoutValidation.SecureEnhanced => StoredSecureEnhanced,
            SasCashoutValidation.System => StoredSystem,
            _ => throw new QpmUnavailableException(
                $"'{SasNames.Underscore(validation.ToString())}' is not a validation style a "
                + "machine can be configured in"),
        };

        return (flags & ~ValidationStyleMask) | (stored << ValidationStyleShift);
    }

    /// <summary>The two-bit field in the option word that holds one category's channel.</summary>
    private static int ChannelShift(SasCategory category) =>
        FirstChannelShift + ((int)category * ChannelFieldBits);

    /// <summary>Which channel the option word puts each category on.</summary>
    internal static Dictionary<SasCategory, SasChannel> ChannelOwners(uint flags)
    {
        var owners = new Dictionary<SasCategory, SasChannel>();
        foreach (SasCategory category in SasChannelPlan.Categories)
        {
            // Stored zero-based, so channel one is a zero. Reading it as a channel number
            // directly would put every category one channel too high.
            uint stored = (flags >> ChannelShift(category)) & 0x03;
            owners[category] = (SasChannel)(stored + 1);
        }

        return owners;
    }

    /// <summary>The option word that puts each category on the channel a plan gives it.</summary>
    internal static uint WithChannelOwners(uint flags, SasChannelPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        uint written = flags;
        foreach (SasCategory category in SasChannelPlan.Categories)
        {
            int shift = ChannelShift(category);
            written &= ~(0x03u << shift);
            written |= (uint)((int)plan.OwnerOf(category) - 1) << shift;
        }

        return written;
    }

    /// <summary>Reads the configuration out of a machine's memory.</summary>
    /// <exception cref="QpmUnavailableException">It is not a machine memory this build knows.</exception>
    public static NvramMachineConfiguration Read(byte[] memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        int bank = SoundBank(memory);
        NvramMap map = MapFor(memory, bank);

        int configuration = bank + map.ConfigurationMeters;
        int network = bank + map.SasNetwork;

        var addresses = new ushort[PollAddressCount];
        for (int channel = 0; channel < PollAddressCount; channel++)
            addresses[channel] = Word(memory, network + PollAddressOffset + (channel * 2));

        return new NvramMachineConfiguration(
            LayoutVersion: LayoutVersion(memory),
            ConfigurationRequired: Word(memory, configuration + ConfigurationRequiredOffset),
            BillValidatorProtocol: Word(memory, configuration + BillValidatorProtocolOffset),
            PrinterProtocol: Word(memory, configuration + PrinterProtocolOffset),
            ManufacturerId: Word(memory, network + ManufacturerIdOffset),
            PollAddresses: addresses,
            AssetNumber: DoubleWord(memory, network + AssetNumberOffset),
            Flags: DoubleWord(memory, network + FlagsOffset),
            Caveat: Caveat(memory, bank));
    }

    /// <summary>
    /// Rewrites the poll addresses and the category map, leaving everything else alone.
    /// </summary>
    /// <remarks>
    /// The one configuration a person cannot set through the machine's own menu, because the menu
    /// skips a channel with no poll address while cycling and so can never point a category at
    /// one. A memory written here can, which is why the arrangement is resolved first and an
    /// unserveable one is refused rather than written: a machine booted on it would open no
    /// channel for that category and answer a host that never existed.
    /// </remarks>
    /// <returns>The memory with the change applied, resealed. The original is untouched.</returns>
    /// <exception cref="QpmUnavailableException">It is not a machine memory this build knows.</exception>
    public static byte[] WithChannels(byte[] memory, SasChannelPlan plan)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(plan);

        int bank = SoundBank(memory);
        NvramMap map = MapFor(memory, bank);

        uint flags = WithChannelOwners(
            DoubleWord(memory, bank + map.SasNetwork + FlagsOffset), plan);

        var written = new byte[MemoryBytes];
        memory.AsSpan(bank, BankBytes).CopyTo(written);
        for (int channel = 0; channel < PollAddressCount; channel++)
        {
            WriteWord(
                written,
                map.SasNetwork + PollAddressOffset + (channel * 2),
                plan.AddressOf((SasChannel)(channel + 1))?.Value ?? 0);
        }

        WriteDoubleWord(written, map.SasNetwork + FlagsOffset, flags);
        Reseal(written);
        return written;
    }

    /// <summary>
    /// Rewrites the validation style and the ticket-in option, leaving everything else alone.
    /// </summary>
    /// <remarks>
    /// Two things follow the style rather than being asked for separately. A machine that
    /// performs no host validation may not redeem a ticket at all, so the operator's ticket-in
    /// option is set with the style and cleared with it — which is what the machine's own menu
    /// does, and a memory carrying one without the other describes a machine that cannot exist.
    /// And the mark saying a host has already set the validation identifier is always cleared,
    /// because only a host setting one may raise it: a fixture that raised it would silence the
    /// machine's request for the very number that makes its tickets valid.
    /// </remarks>
    /// <returns>The memory with the change applied, resealed. The original is untouched.</returns>
    /// <exception cref="QpmUnavailableException">It is not a machine memory this build knows.</exception>
    public static byte[] WithValidation(byte[] memory, SasCashoutValidation validation)
    {
        ArgumentNullException.ThrowIfNull(memory);
        int bank = SoundBank(memory);
        NvramMap map = MapFor(memory, bank);

        uint flags = DoubleWord(memory, bank + map.SasNetwork + FlagsOffset);
        flags = WithValidation(flags, validation);
        flags &= ~SecureEnhancedConfiguredMask;

        if (validation == SasCashoutValidation.Standard)
            flags &= ~TicketInMask;
        else
            flags |= TicketInMask;

        // The sound bank becomes both banks, which is the same repair the machine makes when it
        // finds one of them stale. Writing into a memory and leaving the other bank as it was
        // would hand the machine a choice between two configurations.
        var written = new byte[MemoryBytes];
        memory.AsSpan(bank, BankBytes).CopyTo(written);
        WriteDoubleWord(written, map.SasNetwork + FlagsOffset, flags);
        Reseal(written);
        return written;
    }

    /// <summary>
    /// Which of the two banks may be believed, taking the same view the machine takes.
    /// </summary>
    /// <remarks>
    /// A memory saved from underneath a machine that was killed rather than closed is routinely
    /// caught between the record it had just written and the mark that covers it. That is not
    /// damage and the machine does not treat it as damage: one bank still carries a mark matching
    /// its own bytes, and the machine copies that bank over the other on the way up. So does this.
    /// A memory in which neither bank agrees with itself is the case that has to be refused.
    /// </remarks>
    /// <returns>The offset of the bank to read.</returns>
    /// <exception cref="QpmUnavailableException">Neither bank agrees with itself.</exception>
    public static int SoundBank(byte[] memory)
    {
        ArgumentNullException.ThrowIfNull(memory);

        if (memory.Length != MemoryBytes)
        {
            throw new QpmUnavailableException(
                $"this is not a machine memory: it is {memory.Length:N0} bytes, and a machine's "
                + $"memory is {MemoryBytes:N0}");
        }

        for (int bank = 0; bank < MemoryBytes; bank += BankBytes)
        {
            if (Word(memory, bank) == BankMark(memory, bank))
                return bank;
        }

        throw new QpmUnavailableException(
            "neither bank of this memory carries a mark matching its own bytes, so there is no "
            + "configuration in it to be read. A machine given it clears it and starts again");
    }

    /// <summary>What is worth saying about a memory beyond the values in it.</summary>
    private static string? Caveat(byte[] memory, int bank)
    {
        bool identical = memory.AsSpan(0, BankBytes).SequenceEqual(memory.AsSpan(BankBytes, BankBytes));
        if (identical && Word(memory, 0) == BankMark(memory, 0))
            return null;

        // Seen both after a kill and after a QSP v2.3.6.1 demo cleared its memory and closed normally.
        return $"read from bank {bank / BankBytes}: the other one differs from it. "
               + "A machine killed rather than closed leaves that behind, and so does a QSP v2.3.6.1 "
               + "demo that has just cleared its memory; the machine starts from the sound bank "
               + "either way";
    }

    /// <summary>The arrangement revision the memory records.</summary>
    public static uint LayoutVersion(byte[] memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        return memory.Length < 8 ? 0 : DoubleWord(memory, SoundBank(memory) + 4);
    }

    private static NvramMap MapFor(byte[] memory, int bank)
    {
        uint version = DoubleWord(memory, bank + 4);
        NvramMap? map = KnownMaps.FirstOrDefault(m => m.LayoutVersion == version);
        if (map is null)
        {
            string known = string.Join(", ", KnownMaps.Select(m => m.LayoutVersion));
            throw new QpmUnavailableException(
                $"this memory is arranged in revision {version}, and where the configuration sits "
                + $"has only been established for revision {known}. The two are not interchangeable "
                + "in either direction: a machine clears any memory not written in the revision it "
                + "was built for. Configure that machine by hand once and save its memory, rather "
                + "than have this one written at a guess");
        }

        // The offsets are pinned to a revision, and a revision number is one number: two builds
        // can agree on it and disagree about everything else. So the records are read back before
        // they are trusted, and a value no machine could hold says the offsets are not this
        // memory's, whatever its revision says.
        ushort address = Word(memory, bank + map.SasNetwork + PollAddressOffset);
        uint flags = DoubleWord(memory, bank + map.SasNetwork + FlagsOffset);
        if (address >= 128)
        {
            throw new QpmUnavailableException(
                $"the host address in this memory reads {address}, and SAS addresses stop at 127, "
                + "so the configuration is not where revision "
                + $"{map.LayoutVersion} puts it. Refusing to read it rather than report a number "
                + "taken from the wrong bytes");
        }

        if (((flags & ValidationStyleMask) >> ValidationStyleShift) == 3)
        {
            throw new QpmUnavailableException(
                "the validation style in this memory reads as the value no style is assigned to, "
                + $"so the configuration is not where revision {map.LayoutVersion} puts it");
        }

        return map;
    }

    /// <summary>
    /// Copies bank zero over bank one and rewrites both marks, so the memory reads whole.
    /// </summary>
    private static void Reseal(byte[] memory)
    {
        memory.AsSpan(0, BankBytes).CopyTo(memory.AsSpan(BankBytes, BankBytes));

        for (int bank = 0; bank < MemoryBytes; bank += BankBytes)
            WriteWord(memory, bank, BankMark(memory, bank));
    }

    /// <summary>
    /// The exclusive-or of every sixteen-bit word of a bank except the mark itself.
    /// </summary>
    private static ushort BankMark(byte[] memory, int bank)
    {
        ushort mark = 0;
        ReadOnlySpan<byte> span = memory.AsSpan(bank + 2, BankBytes - 2);
        for (int offset = 0; offset + 1 < span.Length; offset += 2)
            mark ^= (ushort)(span[offset] | (span[offset + 1] << 8));

        return mark;
    }

    private static ushort Word(byte[] memory, int offset) =>
        (ushort)(memory[offset] | (memory[offset + 1] << 8));

    private static uint DoubleWord(byte[] memory, int offset) =>
        (uint)(memory[offset] | (memory[offset + 1] << 8)
               | (memory[offset + 2] << 16) | (memory[offset + 3] << 24));

    private static void WriteWord(byte[] memory, int offset, ushort value)
    {
        memory[offset] = (byte)value;
        memory[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteDoubleWord(byte[] memory, int offset, uint value)
    {
        memory[offset] = (byte)value;
        memory[offset + 1] = (byte)(value >> 8);
        memory[offset + 2] = (byte)(value >> 16);
        memory[offset + 3] = (byte)(value >> 24);
    }
}
