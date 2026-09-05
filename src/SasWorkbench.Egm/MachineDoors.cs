using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Egm;

/// <summary>A door, and the pair of exceptions it reports with.</summary>
/// <param name="Name">The name a script writes.</param>
/// <param name="Opened">What the machine reports when it opens.</param>
/// <param name="Closed">What it reports when it shuts.</param>
public sealed record MachineDoor(string Name, SasExceptionCode Opened, SasExceptionCode Closed);

/// <summary>
/// The doors the protocol can report, and the names a script may use for each.
/// </summary>
/// <remarks>
/// The protocol names five doors while a machine may have sixteen switches, so several physical
/// doors report as one protocol door — the top box reports as the slot door, and every tamper
/// switch reports as the card cage. Modelling them by protocol door rather than by switch is what
/// lets one script run against both the software machine and a real one: the software machine has
/// no switches at all, and the script is written about what the host can see.
/// </remarks>
public static class MachineDoors
{
    private static readonly MachineDoor[] Doors =
    [
        new("slot", SasExceptionCode.SlotDoorOpened, SasExceptionCode.SlotDoorClosed),
        new("drop", SasExceptionCode.DropDoorOpened, SasExceptionCode.DropDoorClosed),
        new("card_cage", SasExceptionCode.CardCageOpened, SasExceptionCode.CardCageClosed),
        new("cashbox", SasExceptionCode.CashboxDoorOpened, SasExceptionCode.CashboxDoorClosed),
        new("belly", SasExceptionCode.BellyDoorOpened, SasExceptionCode.BellyDoorClosed),
    ];

    private static readonly Dictionary<string, MachineDoor> ByName = Build();

    /// <summary>Every name a script may use, sorted.</summary>
    public static IReadOnlyList<string> Names =>
        [.. ByName.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Finds a door by any of its names.</summary>
    public static bool TryFind(string? name, out MachineDoor door)
    {
        door = null!;
        return !string.IsNullOrWhiteSpace(name) && ByName.TryGetValue(name.Trim(), out door!);
    }

    private static Dictionary<string, MachineDoor> Build()
    {
        var index = new Dictionary<string, MachineDoor>(StringComparer.OrdinalIgnoreCase);
        foreach (MachineDoor door in Doors)
            index[door.Name] = door;

        // The words a machine's own configuration uses for the switches behind each one.
        index["main"] = index["slot"];
        index["topbox"] = index["slot"];
        index["opto"] = index["slot"];
        index["dropbox"] = index["drop"];
        index["logic"] = index["card_cage"];
        index["stacker"] = index["cashbox"];
        index["spare"] = index["belly"];

        for (int i = 1; i <= 8; i++)
            index[$"tamper{i}"] = index["card_cage"];

        return index;
    }
}
