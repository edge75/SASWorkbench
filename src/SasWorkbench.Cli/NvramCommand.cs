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
            Console.Error.WriteLine("error: 'nvram' needs an action: status, save or restore.");
            return 2;
        }

        try
        {
            return line.Arguments[0].ToLowerInvariant() switch
            {
                "status" => Status(memory, fixture),
                "save" => Save(memory, fixture),
                "restore" => Restore(memory, fixture),
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
            $"error: '{action}' is not a memory action. Expected status, save or restore.");
        return 2;
    }
}
