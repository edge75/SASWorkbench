namespace SasWorkbench.Core.Scripting;

/// <summary>A peripheral that a script can put into a fault and take out of it again.</summary>
public enum PeripheralDevice
{
    /// <summary>The note acceptor, with its cashbox.</summary>
    Note,

    /// <summary>The ticket printer.</summary>
    Printer,
}

/// <summary>Puts the note acceptor or the printer into one fault, or clears every fault it holds.</summary>
/// <remarks>
/// Named for what happens at the cabinet rather than for the exception it should draw, because which
/// exception a machine raises for a fault is exactly what the case is asserting.
/// </remarks>
public sealed record PeripheralFaultStep(SourceLocation Location, PeripheralDevice Device, string Fault)
    : StimulusStep(Location)
{
    /// <summary>The word that clears every fault the device holds: reconnected, refilled, unjammed.</summary>
    public const string Cleared = "cleared";

    /// <summary>The faults each device can be given, by the word a script uses.</summary>
    public static IReadOnlyDictionary<PeripheralDevice, IReadOnlyList<string>> Faults { get; } =
        new Dictionary<PeripheralDevice, IReadOnlyList<string>>
        {
            [PeripheralDevice.Note] = ["cashbox_full", "jam", "failure", "reversed", "rejected", "counterfeit", Cleared],
            [PeripheralDevice.Printer] = ["disconnected", "jam", "failure", "paper_out", "paper_low", "ribbon", Cleared],
        };

    /// <summary>The script word for a device.</summary>
    public static string WordFor(PeripheralDevice device) => device == PeripheralDevice.Note ? "note" : "printer";

    /// <summary>The faults some machines cannot have at all, each needing a capability of its own.</summary>
    public static IReadOnlySet<string> OwnCapability { get; } = new HashSet<string> { "reversed", "ribbon" };

    public override string RequiredCapability =>
        (Device == PeripheralDevice.Note ? "stimulus.note_fault" : "stimulus.printer_fault")
        + (OwnCapability.Contains(Fault) ? "." + Fault : string.Empty);

    public override string Describe() => $"{WordFor(Device)} fault {Fault}";

    public override string? Guidance => (Device, Fault) switch
    {
        (_, Cleared) => "Clear every fault on it: reconnect it, refill it, free whatever is stuck, and reset the machine if it asks.",
        (PeripheralDevice.Note, "cashbox_full") => "Make the acceptor report its cashbox full, then insert a note.",
        (PeripheralDevice.Note, "jam") => "Feed a note so that it stops part way.",
        (PeripheralDevice.Note, "failure") => "Disconnect the acceptor or block one of its switches.",
        (PeripheralDevice.Note, "reversed") => "Pull a note back out just as it finishes feeding.",
        (PeripheralDevice.Note, "rejected") => "Insert a note the acceptor refuses: a disabled denomination, a foreign note, or one there is no room for.",
        (PeripheralDevice.Note, "counterfeit") => "Insert a counterfeit note.",
        (PeripheralDevice.Printer, "disconnected") => "Disconnect the printer or switch it off.",
        (PeripheralDevice.Printer, "jam") => "Restrict the printer's carriage or paper path.",
        (PeripheralDevice.Printer, "failure") => "Make the printer report a hardware failure.",
        (PeripheralDevice.Printer, "paper_out") => "Take the paper out and run the printer's self-test.",
        (PeripheralDevice.Printer, "paper_low") => "Fit less paper than the low-paper threshold and run the self-test twice.",
        (PeripheralDevice.Printer, "ribbon") => "Fit a worn ribbon.",
        _ => null,
    };
}
