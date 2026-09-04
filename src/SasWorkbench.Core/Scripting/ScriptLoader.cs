namespace SasWorkbench.Core.Scripting;

/// <summary>Finds and reads scripts, whichever of the two forms they are written in.</summary>
public static class ScriptLoader
{
    /// <summary>The extension the purpose-built form uses.</summary>
    public const string ScriptExtension = ".sas";

    /// <summary>Reads one script, choosing the reader by its extension.</summary>
    public static ScriptDocument LoadFile(string path) =>
        YamlScriptReader.Handles(path)
            ? YamlScriptReader.ParseFile(path)
            : ScriptParser.ParseFile(path);

    /// <summary>
    /// Reads every script under a path. A file is read directly; a directory is walked, and its
    /// scripts are returned in a stable order so two runs list their tests the same way.
    /// </summary>
    /// <exception cref="FileNotFoundException">The path does not exist.</exception>
    public static IReadOnlyList<ScriptDocument> Load(string path)
    {
        if (File.Exists(path))
            return [LoadFile(path)];

        if (!Directory.Exists(path))
            throw new FileNotFoundException($"No script or directory at '{path}'.", path);

        var documents = new List<ScriptDocument>();
        foreach (string file in Discover(path))
            documents.Add(LoadFile(file));

        return documents;
    }

    /// <summary>Every script file under a directory, sorted.</summary>
    public static IEnumerable<string> Discover(string directory)
    {
        string[] patterns = [$"*{ScriptExtension}", .. YamlScriptReader.Extensions.Select(e => $"*{e}")];

        return patterns
            .SelectMany(pattern => Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
    }
}
