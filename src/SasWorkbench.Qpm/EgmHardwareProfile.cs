using System.Text.Json;

namespace SasWorkbench.Qpm;

/// <summary>One door of the machine, and which intrusion input reports it.</summary>
/// <param name="Type">The machine's own word for it, such as <c>main</c> or <c>stacker</c>.</param>
/// <param name="Intrusion">Which bit of the intrusion mask carries it.</param>
public sealed record EgmDoor(string Type, int Intrusion);

/// <summary>One button, and which input line it is wired to.</summary>
/// <param name="Label">What the machine calls it, such as <c>Collect</c>.</param>
/// <param name="Kind">What it does, such as <c>play</c> or <c>keyswitch</c>.</param>
/// <param name="DioLine">Which bit of the input mask carries it.</param>
/// <param name="Data">What the machine passes with it, such as the multiplier of a bet button; zero when nothing.</param>
public sealed record EgmButton(string Label, string Kind, int DioLine, int Data = 0);

/// <summary>
/// What the machine under test says its own peripherals are.
/// </summary>
/// <remarks>
/// Every index here is read from the machine's configuration rather than from the peripheral
/// simulator's labels, because the two disagree. The simulator names its intrusion checkboxes in
/// one order and this machine wires them in another, so a test that opened "the third door"
/// according to the simulator would open a different door in the machine and then assert on the
/// exception the wrong one produces. Where the configuration cannot be read, no door and no
/// button is offered at all: a skip naming the missing profile is recoverable, and a stimulus
/// applied to the wrong index is not.
/// </remarks>
public sealed class EgmHardwareProfile
{
    /// <summary>
    /// The names a script may use for a door, beyond the machine's own word for it.
    /// </summary>
    /// <remarks>
    /// The protocol names five doors and the machine names sixteen, so several of the machine's
    /// map onto one protocol door. These aliases follow the machine's configuration file, which
    /// states the mapping itself: main, opto and topbox all report as the slot door; logic and
    /// every tamper switch report as the card cage; stacker reports as the cashbox; dropbox as
    /// the drop door; and spare as the belly door.
    /// </remarks>
    private static readonly Dictionary<string, string[]> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["main"] = ["slot"],
            ["opto"] = ["slot"],
            ["logic"] = ["card_cage"],
            ["stacker"] = ["cashbox"],
            ["dropbox"] = ["drop"],
            ["spare"] = ["belly"],
        };

    private readonly Dictionary<string, EgmDoor> _doorsByName;
    private readonly Dictionary<string, EgmButton> _buttonsByName;

    private EgmHardwareProfile(
        string source,
        string? unavailableBecause,
        IReadOnlyList<EgmDoor> doors,
        IReadOnlyList<EgmButton> buttons)
    {
        Source = source;
        UnavailableBecause = unavailableBecause;
        Doors = doors;
        Buttons = buttons;
        _doorsByName = IndexDoors(doors);
        _buttonsByName = IndexButtons(buttons);
    }

    /// <summary>Where this was read from, for the run record.</summary>
    public string Source { get; }

    /// <summary>Why nothing is known about the machine, or null when it is known.</summary>
    public string? UnavailableBecause { get; }

    /// <summary>Every door the machine declares.</summary>
    public IReadOnlyList<EgmDoor> Doors { get; }

    /// <summary>Every button the machine declares, including its keyswitches.</summary>
    public IReadOnlyList<EgmButton> Buttons { get; }

    /// <summary>Every name a script may write for a door, sorted.</summary>
    public IReadOnlyList<string> DoorNames => Writable(_doorsByName.Keys);

    /// <summary>Every name a script may write for a button, sorted.</summary>
    public IReadOnlyList<string> ButtonNames => Writable(_buttonsByName.Keys);

    /// <summary>
    /// Keeps only the names a person could actually type into a script.
    /// </summary>
    /// <remarks>
    /// The machine calls a button "Bet x1" and a door "tamper#1", and both remain valid ways to
    /// ask for one — but neither can appear in a script, where a step is read word by word and a
    /// requirement is a single token. Offering a capability nobody can write, and then skipping
    /// tests for not requiring it, would be a slower way of hiding the same thing.
    /// </remarks>
    private static IReadOnlyList<string> Writable(IEnumerable<string> names) =>
    [
        .. names
            .Where(n => n.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-'))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>A profile that knows nothing, and says why.</summary>
    public static EgmHardwareProfile Unknown(string reason) =>
        new("<unknown>", reason, [], []);

    /// <summary>
    /// Reads the machine's configuration from the directory it keeps its resources in.
    /// </summary>
    /// <remarks>
    /// Never throws. A machine whose configuration cannot be read produces a profile that offers
    /// nothing and carries the reason, so the failure surfaces as a skip against the tests that
    /// needed a door or a button rather than as an exception in the middle of a run.
    /// </remarks>
    public static EgmHardwareProfile ReadFrom(string? resourcesDirectory)
    {
        if (string.IsNullOrWhiteSpace(resourcesDirectory))
            return Unknown("no gaming machine resource directory was given, so its door and "
                + "button indices are unknown");

        if (!Directory.Exists(resourcesDirectory))
            return Unknown($"'{resourcesDirectory}' is not a directory, so the machine's door "
                + "and button indices could not be read");

        string hardware = Path.Combine(resourcesDirectory, "configuration.json");
        string game = Path.Combine(resourcesDirectory, "game_configuration.json");

        try
        {
            IReadOnlyList<EgmDoor> doors = ReadDoors(hardware);
            IReadOnlyList<EgmButton> buttons = ReadButtons(game);

            if (doors.Count == 0 && buttons.Count == 0)
                return Unknown($"'{resourcesDirectory}' holds no door or button configuration");

            return new EgmHardwareProfile(resourcesDirectory, null, doors, buttons);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return Unknown($"the machine's configuration in '{resourcesDirectory}' could not be "
                + $"read: {ex.Message}");
        }
    }

    /// <summary>Finds a door by any name a script may write for it.</summary>
    public bool TryFindDoor(string name, out EgmDoor door) =>
        _doorsByName.TryGetValue(name.Trim(), out door!);

    /// <summary>Finds a button by its label, its kind, or an underscored form of either.</summary>
    public bool TryFindButton(string name, out EgmButton button) =>
        _buttonsByName.TryGetValue(name.Trim(), out button!);

    private static IReadOnlyList<EgmDoor> ReadDoors(string path)
    {
        if (!File.Exists(path))
            return [];

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        if (!document.RootElement.TryGetProperty("Hardware", out JsonElement hardware)
            || !hardware.TryGetProperty("Doors", out JsonElement doors)
            || doors.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var found = new List<EgmDoor>();
        foreach (JsonElement entry in doors.EnumerateArray())
        {
            if (entry.TryGetProperty("type", out JsonElement type)
                && entry.TryGetProperty("intrusion", out JsonElement intrusion)
                && type.ValueKind == JsonValueKind.String
                && intrusion.TryGetInt32(out int bit))
            {
                found.Add(new EgmDoor(type.GetString()!, bit));
            }
        }

        return found;
    }

    private static IReadOnlyList<EgmButton> ReadButtons(string path)
    {
        if (!File.Exists(path))
            return [];

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        if (!document.RootElement.TryGetProperty("buttons", out JsonElement buttons)
            || !buttons.TryGetProperty("digital_inputs", out JsonElement inputs)
            || inputs.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var found = new List<EgmButton>();
        foreach (JsonElement entry in inputs.EnumerateArray())
        {
            if (!entry.TryGetProperty("dio_line", out JsonElement line)
                || !line.TryGetInt32(out int dio))
            {
                continue;
            }

            string kind = entry.TryGetProperty("button_type", out JsonElement type)
                          && type.ValueKind == JsonValueKind.String
                ? type.GetString()!
                : "none";

            // A line the machine declares as doing nothing is not a button, and offering it
            // would let a test press something and assert on a machine that never moved.
            if (kind.Equals("none", StringComparison.OrdinalIgnoreCase))
                continue;

            string label = entry.TryGetProperty("label", out JsonElement text)
                           && text.ValueKind == JsonValueKind.String
                ? text.GetString()!
                : kind;

            int data = entry.TryGetProperty("data", out JsonElement extra) && extra.TryGetInt32(out int value) ? value : 0;
            found.Add(new EgmButton(label, kind, dio, data));
        }

        return found;
    }

    private static Dictionary<string, EgmDoor> IndexDoors(IReadOnlyList<EgmDoor> doors)
    {
        var index = new Dictionary<string, EgmDoor>(StringComparer.OrdinalIgnoreCase);
        foreach (EgmDoor door in doors)
        {
            // "tamper#1" is not something anybody types, so it is offered as "tamper1" as well.
            index.TryAdd(door.Type, door);
            index.TryAdd(door.Type.Replace("#", string.Empty), door);

            if (door.Type.StartsWith("tamper", StringComparison.OrdinalIgnoreCase))
                index.TryAdd("card_cage", door);

            if (Aliases.TryGetValue(door.Type, out string[]? aliases))
            {
                foreach (string alias in aliases)
                    index.TryAdd(alias, door);
            }
        }

        return index;
    }

    private static Dictionary<string, EgmButton> IndexButtons(IReadOnlyList<EgmButton> buttons)
    {
        var index = new Dictionary<string, EgmButton>(StringComparer.OrdinalIgnoreCase);
        foreach (EgmButton button in buttons)
        {
            index.TryAdd(button.Label, button);
            index.TryAdd(Simplify(button.Label), button);

            // The kind is a useful name only while one button has it: two bet buttons would both
            // answer to "bet", and pressing whichever was read first is not what a test meant.
            if (buttons.Count(b => b.Kind == button.Kind) == 1)
                index.TryAdd(button.Kind, button);
        }

        return index;
    }

    private static string Simplify(string label)
    {
        Span<char> buffer = stackalloc char[label.Length];
        int length = 0;
        foreach (char c in label)
        {
            if (char.IsLetterOrDigit(c))
                buffer[length++] = char.ToLowerInvariant(c);
            else if (length > 0 && buffer[length - 1] != '_')
                buffer[length++] = '_';
        }

        return new string(buffer[..length]).TrimEnd('_');
    }
}
