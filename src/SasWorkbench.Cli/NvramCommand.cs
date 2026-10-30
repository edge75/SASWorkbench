using System.Globalization;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;
using SasWorkbench.Qpm;

namespace SasWorkbench.Cli;

/// <summary>
/// Saves and restores the gaming machine's configured memory.
/// </summary>
/// <remarks>
/// The configuration a conformance test depends on cannot be set over the wire and is not in any
/// file a person can edit: it is set through the machine's own menu, by hand, once. This command
/// is how that afternoon becomes a file the next person restores in a second.
/// </remarks>
internal static class NvramCommand
{
    /// <summary>Where a saved memory is kept when nobody names one.</summary>
    private const string DefaultFixture = "fixtures/nvram/golden.nvram.gz";

    public static int Execute(CommandLine line)
    {
        string? memory = line.Value("nvram");
        string fixture = line.Value("fixture") ?? DefaultFixture;

        if (line.Arguments.Count == 0)
        {
            Console.Error.WriteLine(
                "error: 'nvram' needs an action: status, show, save, restore, validation, "
                + "denomination or channels.");
            return 2;
        }

        try
        {
            return line.Arguments[0].ToLowerInvariant() switch
            {
                "status" => Status(memory, fixture),
                "show" => Show(memory, fixture),
                "save" => Save(memory, fixture),
                "restore" => Restore(memory, fixture),
                "validation" => Validation(fixture, line),
                "denomination" => Denomination(fixture, line),
                "channels" => Channels(fixture, line),
                _ => Unknown(line.Arguments[0]),
            };
        }
        catch (QpmUnavailableException unavailable)
        {
            Console.Error.WriteLine($"error: {unavailable.Reason}");
            return 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static int Status(string? memory, string fixture)
    {
        try
        {
            Console.WriteLine($"  machine   {NvramFixture.Live(memory)}");
        }
        catch (QpmUnavailableException unavailable)
        {
            Console.WriteLine($"  machine   {unavailable.Reason}");
        }

        try
        {
            NvramSnapshot saved = NvramFixture.Describe(fixture);
            Console.WriteLine($"  saved     {saved}");
        }
        catch (QpmUnavailableException unavailable)
        {
            Console.WriteLine($"  saved     {unavailable.Reason}");
        }

        return 0;
    }

    /// <summary>
    /// Prints what the machine is configured to be, from its own memory and from a saved copy.
    /// </summary>
    /// <remarks>
    /// Both, side by side, because the interesting question is nearly always whether they agree.
    /// A machine that has cleared its memory since a fixture was saved reads back as an
    /// unconfigured machine while the fixture beside it still describes a configured one.
    /// </remarks>
    private static int Show(string? memory, string fixture)
    {
        Describe("machine", () => NvramFixture.LiveConfiguration(memory));
        Console.WriteLine();
        Describe("saved", () => NvramFixture.Configuration(fixture));
        return 0;
    }

    private static void Describe(string what, Func<NvramMachineConfiguration> read)
    {
        NvramMachineConfiguration configuration;
        try
        {
            configuration = read();
        }
        catch (QpmUnavailableException unavailable)
        {
            Console.WriteLine($"  {what,-10} {unavailable.Reason}");
            return;
        }

        string addresses = string.Join(", ", configuration.PollAddresses.Select(a => $"${a:X2}"));
        Console.WriteLine($"  {what,-10} memory arrangement {configuration.LayoutVersion}");
        Console.WriteLine($"             note acceptor    {Peripheral(configuration.BillValidatorProtocol)}");
        Console.WriteLine($"             printer          {Peripheral(configuration.PrinterProtocol)}");
        Console.WriteLine($"             maker            {configuration.ManufacturerText}");
        Console.WriteLine($"             host addresses   {addresses}");
        Console.WriteLine($"             asset number     {configuration.AssetNumber}");
        Console.WriteLine($"             validation       "
            + SasNames.Underscore(configuration.Validation.ToString()));
        Console.WriteLine($"             takes tickets    {(configuration.TicketInEnabled ? "yes" : "no")}");
        Console.WriteLine($"             validation id    "
            + (configuration.SecureEnhancedConfigured ? "set by a host" : "not set"));
        Console.WriteLine($"             accounting unit  {Unit(configuration)}");

        if (configuration.ConfigurationRequired != 0)
        {
            Console.WriteLine("             still to configure "
                + $"${configuration.ConfigurationRequired:X4}");
        }

        (SasChannelPlan? plan, string? refusal) = configuration.Channels;
        Console.WriteLine($"             channels         "
            + (plan?.Describe() ?? $"refused - {refusal}"));

        if (configuration.Caveat is { } caveat)
            Console.WriteLine($"             note             {caveat}");
    }

    private static string Peripheral(ushort protocol) =>
        protocol == 0 ? "none, so it cannot be used" : $"protocol {protocol}";

    private static string Unit(NvramMachineConfiguration configuration) =>
        configuration.StoredAccountingDenominationCents == 0
            ? "1c (never written)"
            : $"{configuration.AccountingDenominationCents}c";

    /// <summary>
    /// Writes a saved copy out again with the machine counting in another accounting denomination.
    /// </summary>
    private static int Denomination(string fixture, CommandLine line)
    {
        // Read first, so a refusal below is not followed by a warning that '--out' went unused.
        string output = line.Value("out") ?? fixture;

        if (line.Arguments.Count != 2)
        {
            Console.Error.WriteLine(
                "error: expected 'nvram denomination <amount> --fixture <in> --out <out>', such "
                + "as 0.05 or 5c.");
            return 2;
        }

        if (!TryReadCents(line.Arguments[1], out int cents))
        {
            Console.Error.WriteLine(
                $"error: '{line.Arguments[1]}' is not an amount. Write it in currency units, such "
                + "as 0.05, or in cents, such as 5c.");
            return 2;
        }

        NvramMachineConfiguration written = NvramFixture.ReconfigureDenomination(fixture, output, cents);

        Console.WriteLine($"  wrote {output}");
        Console.WriteLine($"  accounting unit {Unit(written)} "
            + $"(long poll 1F code ${SasDenomination.CodeFor(cents):X2})");
        Console.WriteLine(
            "  QSP v2.3.6.1 applies it only when its configuration enables "
            + "Sas.AccountingDenomination.Enable and the unit divides every enabled game's "
            + "denomination; otherwise it reports in cents. Restore it, boot a machine on it, and "
            + "check long poll 1F reports that code before trusting it.");
        return 0;
    }

    /// <summary>Reads 0.05, 0,05 or 5c as five cents.</summary>
    private static bool TryReadCents(string text, out int cents)
    {
        cents = 0;
        if (text.EndsWith('c') || text.EndsWith('C'))
        {
            return int.TryParse(text[..^1], NumberStyles.None, CultureInfo.InvariantCulture,
                       out cents)
                   && cents > 0;
        }

        if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal units)
            || units > int.MaxValue / 100m)
        {
            return false;
        }

        decimal exact = units * 100;
        if (exact != decimal.Truncate(exact) || exact <= 0 || exact > int.MaxValue)
            return false;

        cents = (int)exact;
        return true;
    }

    /// <summary>
    /// Writes a saved copy out again with the machine put into a validation style.
    /// </summary>
    private static int Validation(string fixture, CommandLine line)
    {
        if (line.Arguments.Count != 2)
        {
            Console.Error.WriteLine(
                "error: expected 'nvram validation <standard|secure_enhanced|system> "
                + "--fixture <in> --out <out>'.");
            return 2;
        }

        if (!SasNames.TryParseValidation(line.Arguments[1], out SasCashoutValidation validation))
        {
            Console.Error.WriteLine(
                $"error: '{line.Arguments[1]}' is not a validation style. Expected standard, "
                + "secure_enhanced or system.");
            return 2;
        }

        string output = line.Value("out") ?? fixture;
        NvramMachineConfiguration written =
            NvramFixture.Reconfigure(fixture, output, validation);

        Console.WriteLine($"  wrote {output}");
        Console.WriteLine($"  validation    {SasNames.Underscore(written.Validation.ToString())}");
        Console.WriteLine($"  takes tickets {(written.TicketInEnabled ? "yes" : "no")}");
        Console.WriteLine(
            "  Restore it, boot a machine on it, and check the machine reports that style in its "
            + "enabled-features reply before trusting it.");
        return 0;
    }

    /// <summary>
    /// Writes a saved copy out again with the machine divided between two or more hosts.
    /// </summary>
    /// <remarks>
    /// Written in the same words a run's provenance prints the arrangement in, so the line out of
    /// a report can be pasted straight back in. An arrangement no machine could serve is refused
    /// here rather than written, because a machine booted on it would open no channel for the
    /// orphaned category and a host polling for it would get silence with no reason attached.
    /// </remarks>
    private static int Channels(string fixture, CommandLine line)
    {
        if (line.Arguments.Count < 2)
        {
            Console.Error.WriteLine(
                "error: expected 'nvram channels \"1@01 2@02 validation=2\" --fixture <in> "
                + "--out <out>'.");
            Console.Error.WriteLine(
                $"  Channels are 1 to {SasChannels.Count}; addresses are hex, and 00 means the "
                + "channel has none.");
            Console.Error.WriteLine($"  Categories: {SasCategories.Names}.");
            return 2;
        }

        string wanted = string.Join(' ', line.Arguments.Skip(1));
        if (!SasChannelPlan.TryParse(wanted, out SasChannelPlan plan, out string? error))
        {
            Console.Error.WriteLine($"error: {error}");
            return 2;
        }

        string output = line.Value("out") ?? fixture;
        NvramMachineConfiguration written = NvramFixture.Reconfigure(fixture, output, plan);

        Console.WriteLine($"  wrote {output}");
        Console.WriteLine($"  {written.Channels.Plan?.Describe() ?? written.Channels.Refusal}");
        Console.WriteLine(
            "  Restore it, boot a machine on it, and check the machine actually opens the second "
            + "channel before trusting it: a cabinet with one serial port cannot, whatever this "
            + "memory says.");
        return 0;
    }

    private static int Save(string? memory, string fixture)
    {
        NvramSnapshot saved = NvramFixture.Save(memory, fixture);
        Console.WriteLine($"  saved {saved}");
        Console.WriteLine("  Commit it, and say in the message what was configured to produce it.");
        return 0;
    }

    private static int Restore(string? memory, string fixture)
    {
        string kept = NvramFixture.Restore(memory, fixture);
        Console.WriteLine($"  restored from {fixture}");
        Console.WriteLine($"  what was there is kept at {kept}");
        return 0;
    }

    private static int Unknown(string action)
    {
        Console.Error.WriteLine(
            $"error: '{action}' is not a memory action. Expected status, show, save, restore, "
            + "validation, denomination or channels.");
        return 2;
    }
}
