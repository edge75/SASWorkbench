using SasWorkbench.Qpm;

namespace SasWorkbench.Cli;

/// <summary>
/// Drives the machine's peripherals by hand, one action at a time.
/// </summary>
/// <remarks>
/// The same driver a run uses, without a script around it. It exists because some things a
/// machine needs doing to it are not tests — turning the audit key to configure a note acceptor,
/// or shutting the doors a cold start left open — and doing those through a throwaway script is
/// worse than having a command for them.
/// </remarks>
internal static class QpmCommand
{
    public static int Execute(CommandLine line)
    {
        var options = new QpmOptions(line.Value("qpm-shm"), line.Value("egm-resources"));

        if (line.Arguments.Count == 0)
        {
            Console.Error.WriteLine(
                "error: 'qpm' needs an action: status, settle, audit, door, press, hold or bill.");
            return 2;
        }

        QpmMachine machine;
        try
        {
            machine = QpmMachine.Attach(options.RegionPath, options.ResourcesDirectory);
        }
        catch (QpmUnavailableException unavailable)
        {
            Console.Error.WriteLine($"error: {unavailable.Reason}");
            return 2;
        }

        using (machine)
        {
            try
            {
                return Act(machine, line.Arguments);
            }
            catch (QpmUnavailableException unavailable)
            {
                Console.Error.WriteLine($"error: {unavailable.Reason}");
                return 2;
            }
        }
    }

    private static int Act(QpmMachine machine, IReadOnlyList<string> arguments)
    {
        string action = arguments[0].ToLowerInvariant();
        string[] rest = [.. arguments.Skip(1)];

        switch (action)
        {
            case "status":
                return Status(machine);

            case "settle":
                foreach (string line in machine.Settle())
                    Console.WriteLine($"  {line}");

                return 0;

            case "audit":
                return Audit(machine, rest);

            case "door":
                return Door(machine, rest);

            case "press":
            case "hold":
                return Button(machine, action, rest);

            case "bill":
                return Bill(machine, rest);

            default:
                Console.Error.WriteLine(
                    $"error: '{action}' is not a peripheral action. Expected status, settle, "
                    + "audit, door, press, hold or bill.");
                return 2;
        }
    }

    private static int Status(QpmMachine machine)
    {
        EgmHardwareProfile profile = machine.Profile;

        Console.WriteLine($"  region     {machine.RegionPath}");
        Console.WriteLine($"  machine    running (its heartbeat is advancing)");
        Console.WriteLine($"  profile    {profile.UnavailableBecause ?? profile.Source}");

        foreach (EgmDoor door in profile.Doors)
            Console.WriteLine($"  door       {door.Type} on intrusion {door.Intrusion}");

        foreach (EgmButton button in profile.Buttons)
            Console.WriteLine($"  button     {button.Label} ({button.Kind}) on input {button.DioLine}");

        IReadOnlyList<QpmLampState> lamps = machine.Lamps;
        for (int i = 0; i < lamps.Count; i++)
        {
            if (lamps[i] != QpmLampState.Off)
                Console.WriteLine($"  lamp       {i} is {lamps[i].ToString().ToLowerInvariant()}");
        }

        return 0;
    }

    /// <summary>
    /// Opens or closes the machine's configuration menu.
    /// </summary>
    /// <remarks>
    /// Three things at once, because the menu needs all three: the machine will not let anything
    /// be configured unless the main door and the logic door are both open, and it says so on
    /// screen rather than anywhere a person would look first. Doing it by hand and forgetting one
    /// leaves a menu that opens and refuses to change anything, with no obvious reason why.
    /// </remarks>
    private static int Audit(QpmMachine machine, string[] rest)
    {
        if (rest.Length != 1 || rest[0] is not ("open" or "close"))
        {
            Console.Error.WriteLine("error: expected 'qpm audit open|close'.");
            return 2;
        }

        if (!machine.Profile.TryFindButton("audit", out EgmButton key))
            return Missing(machine, "audit keyswitch", "audit", machine.Profile.ButtonNames);

        bool opening = rest[0] == "open";
        string[] doors = ["main", "logic"];

        if (opening)
        {
            foreach (string name in doors)
                MoveDoor(machine, name, open: true);

            machine.SetButton(key, pressed: true);
            Console.WriteLine("  audit key turned; the configuration menu should be on screen");
            return 0;
        }

        machine.SetButton(key, pressed: false);
        foreach (string name in doors)
            MoveDoor(machine, name, open: false);

        Console.WriteLine("  audit key returned, doors shut");
        return 0;
    }

    private static void MoveDoor(QpmMachine machine, string name, bool open)
    {
        if (!machine.Profile.TryFindDoor(name, out EgmDoor door))
            return;

        machine.SetDoor(door, open);
        Console.WriteLine($"  door {door.Type} {(open ? "opened" : "shut")}");
    }

    private static int Door(QpmMachine machine, string[] rest)
    {
        if (rest.Length != 2 || rest[1] is not ("open" or "close"))
        {
            Console.Error.WriteLine("error: expected 'qpm door <name> open|close'.");
            return 2;
        }

        if (!machine.Profile.TryFindDoor(rest[0], out EgmDoor door))
            return Missing(machine, "door", rest[0], machine.Profile.DoorNames);

        bool open = rest[1] == "open";
        machine.SetDoor(door, open);
        Console.WriteLine($"  door {door.Type} (intrusion {door.Intrusion}) is now "
            + (open ? "open" : "closed"));
        return 0;
    }

    private static int Button(QpmMachine machine, string action, string[] rest)
    {
        bool holding = action == "hold";
        if (rest.Length != (holding ? 2 : 1) || (holding && rest[1] is not ("on" or "off")))
        {
            Console.Error.WriteLine(holding
                ? "error: expected 'qpm hold <button> on|off'."
                : "error: expected 'qpm press <button>'.");
            return 2;
        }

        if (!machine.Profile.TryFindButton(rest[0], out EgmButton button))
            return Missing(machine, "button", rest[0], machine.Profile.ButtonNames);

        if (!holding)
        {
            machine.PressButton(button);
            Console.WriteLine($"  {button.Label} (input {button.DioLine}) pressed and released");
            return 0;
        }

        bool on = rest[1] == "on";
        machine.SetButton(button, on);
        Console.WriteLine($"  {button.Label} (input {button.DioLine}) is now "
            + (on ? "held down" : "released"));
        return 0;
    }

    /// <summary>
    /// Says why a door or a button could not be found, and tells apart the two reasons.
    /// </summary>
    /// <remarks>
    /// A machine whose configuration was never read declares nothing at all, so every name looks
    /// missing — and "this machine has no audit keyswitch" is a confusing thing to be told about a
    /// machine that plainly has one. The two cases have different fixes, so they get different
    /// sentences.
    /// </remarks>
    private static int Missing(
        QpmMachine machine, string kind, string name, IReadOnlyList<string> available)
    {
        if (machine.Profile.UnavailableBecause is { } reason)
        {
            Console.Error.WriteLine($"error: which {kind} '{name}' is cannot be known: {reason}.");
            Console.Error.WriteLine(
                "Name it with --egm-resources <machine>/Resources, which is where the machine "
                + "declares its own doors and buttons.");
            return 2;
        }

        Console.Error.WriteLine($"error: this machine declares no {kind} called '{name}'. "
            + $"It has: {string.Join(", ", available)}");
        return 2;
    }

    private static int Bill(QpmMachine machine, string[] rest)
    {
        if (rest.Length != 1 || !int.TryParse(rest[0], out int cents))
        {
            Console.Error.WriteLine("error: expected 'qpm bill <cents>', such as 'qpm bill 2000'.");
            return 2;
        }

        machine.InsertNote(cents);
        Console.WriteLine($"  a note of {cents} cents was offered to the acceptor");
        return 0;
    }
}
