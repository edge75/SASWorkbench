using SasWorkbench.Cli;
using SasWorkbench.Core.Scripting;

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    Help.Print();
    return 0;
}

if (args[0] is "--version" or "-v")
{
    Console.WriteLine(Help.Version);
    return 0;
}

CommandLine line = CommandLine.Parse(args);

try
{
    int exit = line.Command switch
    {
        "run" => RunCommand.Execute(line),
        "probe" => ProbeCommand.Execute(line),
        "catalogue" or "catalog" => CatalogueCommand.Execute(line),
        "egm" => EgmCommand.Execute(line),
        "qpm" => QpmCommand.Execute(line),
        "nvram" => NvramCommand.Execute(line),
        "find" => FindCommand.Execute(line),
        _ => Unknown(line.Command),
    };

    IReadOnlyList<string> unrecognised = line.Unrecognised();
    if (unrecognised.Count > 0)
    {
        // Reported after the command has run rather than before, so the run still happens and the
        // operator is told plainly that a flag they typed did nothing.
        Console.Error.WriteLine();
        foreach (string option in unrecognised)
            Console.Error.WriteLine($"warning: '--{option}' is not an option of 'sasctl {line.Command}'");
        Console.Error.WriteLine("Run 'sasctl --help' for what is available.");
        return exit == 0 ? 2 : exit;
    }

    return exit;
}
catch (ScriptParseException parse)
{
    Console.Error.WriteLine($"error: {parse.Message}");
    return 2;
}
catch (FileNotFoundException missing)
{
    Console.Error.WriteLine($"error: {missing.Message}");
    return 2;
}
catch (DirectoryNotFoundException missing)
{
    Console.Error.WriteLine($"error: {missing.Message}");
    return 2;
}

static int Unknown(string? command)
{
    Console.Error.WriteLine($"error: '{command}' is not a command.");
    Console.Error.WriteLine();
    Help.Print(Console.Error);
    return 2;
}
