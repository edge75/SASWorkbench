using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Qpm;

/// <summary>One denomination slot of one game, as the machine's memory holds it.</summary>
/// <param name="Game">The game, from 1.</param>
/// <param name="Slot">The slot the denomination was given, from 0.</param>
/// <param name="DenominationCents">The denomination the slot holds.</param>
/// <param name="MasterEnabled">Switched on from the audit menu.</param>
/// <param name="NetworkEnabled">Switched on by the host link.</param>
public sealed record NvramGameSlot(
    int Game, int Slot, uint DenominationCents, bool MasterEnabled, bool NetworkEnabled);

/// <summary>
/// Reads and switches on the games' enable records in a memory QSP 2.3.6.32 wrote (layout 19).
/// </summary>
/// <remarks>
/// That build keeps one enable record per denomination a game has had, and a denomination chosen in
/// the audit menu after the memory was cleared starts switched off, so a machine configured by hand
/// can come up with no game enabled at all. This does what the Games Enable page does, from outside.
/// </remarks>
public static class NvramGames
{
    /// <summary>Where game 1's denomination slots start, established from the layout-19 fixtures.</summary>
    public const int FirstGameSlots = 0x01034E;

    /// <summary>How far each game's records are from the previous game's.</summary>
    public const int GameStride = 0x954;

    private const uint Layout = 19;
    private const int SlotCount = 8;
    private const int MasksOffset = SlotCount * 4;
    private const int MaxGames = 64;
    private const ushort MasterBit = 0x1;
    private const ushort NetworkBit = 0x2;
    private const ushort KnownBits = 0x7;

    /// <summary>Every assigned slot of every game, in game and slot order.</summary>
    /// <exception cref="QpmUnavailableException">
    /// It is not a layout-19 memory, or game 1's records do not hold a game's denominations.
    /// </exception>
    public static IReadOnlyList<NvramGameSlot> Read(byte[] memory)
    {
        int bank = CheckedBank(memory);
        var slots = new List<NvramGameSlot>();
        for (int game = 1; game <= MaxGames; game++)
        {
            int records = bank + FirstGameSlots + ((game - 1) * GameStride);
            if (records + MasksOffset + (SlotCount * 2) > bank + NvramConfiguration.BankBytes)
                break;

            string? fault = Fault(memory, records);
            if (fault is not null)
            {
                if (game == 1)
                {
                    throw new QpmUnavailableException(
                        $"game 1's records {fault}, so the games are not where revision {Layout} "
                        + "puts them. Refusing to write them at a guess");
                }

                break;
            }

            int before = slots.Count;
            for (int slot = 0; slot < SlotCount; slot++)
            {
                uint cents = DoubleWord(memory, records + (slot * 4));
                if (cents == 0)
                    break;

                ushort mask = Word(memory, records + MasksOffset + (slot * 2));
                slots.Add(new NvramGameSlot(game, slot, cents,
                    (mask & MasterBit) != 0, (mask & NetworkBit) != 0));
            }

            if (slots.Count == before)
                break;
        }

        return slots;
    }

    /// <summary>
    /// Switches on, from the menu and the host link, every slot whose denomination the accounting
    /// unit divides; a slot already on stays on.
    /// </summary>
    /// <returns>The memory with the change applied, resealed. The original is untouched.</returns>
    /// <exception cref="QpmUnavailableException">As for <see cref="Read"/>.</exception>
    public static byte[] WithGamesEnabled(byte[] memory)
    {
        IReadOnlyList<NvramGameSlot> slots = Read(memory);
        int bank = NvramConfiguration.SoundBank(memory);
        uint unit = NvramConfiguration.Read(memory).AccountingDenominationCents;

        var written = new byte[NvramConfiguration.MemoryBytes];
        memory.AsSpan(bank, NvramConfiguration.BankBytes).CopyTo(written);
        foreach (NvramGameSlot slot in slots)
        {
            if (slot.DenominationCents % unit != 0)
                continue;

            int at = FirstGameSlots + ((slot.Game - 1) * GameStride) + MasksOffset + (slot.Slot * 2);
            ushort mask = (ushort)(Word(written, at) | MasterBit | NetworkBit);
            written[at] = (byte)mask;
            written[at + 1] = (byte)(mask >> 8);
        }

        NvramConfiguration.Reseal(written);
        return written;
    }

    private static int CheckedBank(byte[] memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        int bank = NvramConfiguration.SoundBank(memory);
        uint version = DoubleWord(memory, bank + 4);
        if (version != Layout)
        {
            throw new QpmUnavailableException(
                $"this memory is arranged in revision {version}, and where the games' enable "
                + $"records sit has only been established for revision {Layout}");
        }

        return bank;
    }

    /// <summary>Why a game's records cannot be a game's, or null when they can.</summary>
    private static string? Fault(byte[] memory, int records)
    {
        var seen = new HashSet<uint>();
        bool ended = false;
        for (int slot = 0; slot < SlotCount; slot++)
        {
            uint cents = DoubleWord(memory, records + (slot * 4));
            ushort mask = Word(memory, records + MasksOffset + (slot * 2));
            if ((mask & ~KnownBits) != 0)
                return $"carry enable bits no machine sets (slot {slot} reads ${mask:X4})";

            if (cents == 0)
            {
                ended = true;
                if (mask != 0)
                    return $"switch on slot {slot}, which holds no denomination";
                continue;
            }

            if (ended)
                return $"hold a denomination in slot {slot} after an empty one";
            if (cents > int.MaxValue || SasDenomination.CodeFor((int)cents) is null)
                return $"hold {cents} cents in slot {slot}, which is not a denomination";
            if (!seen.Add(cents))
                return $"hold {cents} cents in two slots";
        }

        return null;
    }

    private static ushort Word(byte[] memory, int offset) =>
        (ushort)(memory[offset] | (memory[offset + 1] << 8));

    private static uint DoubleWord(byte[] memory, int offset) =>
        (uint)(memory[offset] | (memory[offset + 1] << 8)
               | (memory[offset + 2] << 16) | (memory[offset + 3] << 24));
}
