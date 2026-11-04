namespace SasWorkbench.Cli;

/// <summary>
/// A small argument reader, written by hand so that every message the tool prints is ours.
/// </summary>
/// <remarks>
/// An option this tool does not know is an error, not something to ignore. A mistyped flag that
/// was silently dropped would change what a run measured without telling anyone — the same class
/// of mistake as a script step the parser did not understand.
/// </remarks>
public sealed class CommandLine
{
    /// <summary>The options that never take a value, so the word after one is never swallowed as its value.</summary>
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "all", "causes", "caveats", "dial", "exchanges", "framing", "h", "help", "list", "loopback",
        "no-sync-polls", "once", "quiet", "real-time-events", "rules", "verbose",
    };

    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _positional = [];
    private readonly HashSet<string> _read = new(StringComparer.OrdinalIgnoreCase);

    private CommandLine()
    {
    }

    /// <summary>The command word, or null when none was given.</summary>
    public string? Command => _positional.Count > 0 ? _positional[0] : null;

    /// <summary>Everything after the command word.</summary>
    public IReadOnlyList<string> Arguments => _positional.Count > 0 ? _positional[1..] : [];

    /// <summary>Reads arguments of the form <c>--name value</c>, <c>--name=value</c> and <c>--flag</c>.</summary>
    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        var line = new CommandLine();

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                line._positional.Add(arg);
                continue;
            }

            string name = arg[2..];
            string? value = null;

            int equals = name.IndexOf('=');
            if (equals >= 0)
            {
                value = name[(equals + 1)..];
                name = name[..equals];
            }
            else if (!Flags.Contains(name)
                     && i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            line._options[name] = value;
        }

        return line;
    }

    /// <summary>True when the flag was given.</summary>
    public bool Flag(string name)
    {
        _read.Add(name);
        return _options.ContainsKey(name);
    }

    /// <summary>The value of an option, or null when it was not given.</summary>
    public string? Value(string name)
    {
        _read.Add(name);
        return _options.GetValueOrDefault(name);
    }

    /// <summary>Every option given, by name, whether or not anything has read it.</summary>
    public IReadOnlyCollection<string> Given => _options.Keys;

    /// <summary>
    /// Any option that was given but never read, so a mistyped flag is reported rather than
    /// quietly changing nothing.
    /// </summary>
    public IReadOnlyList<string> Unrecognised() =>
        _options.Keys.Where(k => !_read.Contains(k)).OrderBy(k => k).ToList();
}
