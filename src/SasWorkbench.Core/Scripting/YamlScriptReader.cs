using YamlDotNet.RepresentationModel;

namespace SasWorkbench.Core.Scripting;

/// <summary>
/// Reads a test written as YAML: the structure comes from the document, and each step is a line
/// of the same language the purpose-built form uses.
/// </summary>
/// <remarks>
/// <para>
/// One grammar, two shapes. YAML earns its place for linear flows a person wants to generate or
/// template, and for carrying a test's name, requirements and labels as data. What it does not
/// do is describe steps in a second notation: a step here is exactly the line the other form
/// would hold, so the two cannot drift apart and a step that works in one works in the other.
/// </para>
/// <para>
/// The consequence worth knowing: an error inside a step reports the line of the YAML file it
/// appeared on, and says the same thing it would say about the same step written directly.
/// </para>
/// </remarks>
public static class YamlScriptReader
{
    /// <summary>File extensions this reader handles.</summary>
    public static readonly string[] Extensions = [".yaml", ".yml"];

    /// <summary>True when a path looks like a YAML script.</summary>
    public static bool Handles(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads a YAML script from disk.</summary>
    public static ScriptDocument ParseFile(string path) => Parse(File.ReadAllText(path), path);

    /// <summary>Reads a YAML script from text.</summary>
    /// <exception cref="ScriptParseException">The document could not be read.</exception>
    public static ScriptDocument Parse(string text, string path = "<text>")
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new ScriptParseException(
                new SourceLocation(path, (int)ex.Start.Line),
                $"this is not valid YAML: {ex.Message}");
        }

        if (stream.Documents.Count == 0)
            throw new ScriptParseException(new SourceLocation(path, 1), "the document is empty");

        if (stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new ScriptParseException(new SourceLocation(path, 1),
                "expected a mapping at the top level, with a 'tests' or 'test' key");

        var tests = new List<ScriptTest>();

        if (TryGet(root, "tests", out YamlNode? list))
        {
            if (list is not YamlSequenceNode sequence)
                throw new ScriptParseException(Where(path, list!),
                    "'tests' must be a list of tests");

            foreach (YamlNode entry in sequence)
                tests.Add(ReadTest(entry, path));
        }
        else
        {
            tests.Add(ReadTest(root, path));
        }

        if (tests.Count == 0)
            throw new ScriptParseException(new SourceLocation(path, 1), "the document holds no tests");

        return new ScriptDocument { Path = path, Tests = tests };
    }

    private static ScriptTest ReadTest(YamlNode node, string path)
    {
        if (node is not YamlMappingNode test)
            throw new ScriptParseException(Where(path, node), "expected a mapping describing a test");

        SourceLocation location = Where(path, node);
        string name = TryGet(test, "name", out YamlNode? nameNode) && nameNode is YamlScalarNode scalar
            ? scalar.Value ?? Path.GetFileNameWithoutExtension(path)
            : Path.GetFileNameWithoutExtension(path);

        var requirements = new List<Requirement>();
        if (TryGet(test, "requires", out YamlNode? requires))
        {
            foreach ((string clause, SourceLocation at) in ReadStrings(requires!, path, "requires"))
            {
                string[] parts = clause.Split("==", StringSplitOptions.TrimEntries);
                requirements.Add(parts.Length switch
                {
                    1 => new Requirement(at, parts[0]),
                    2 => new Requirement(at, parts[0], parts[1]),
                    _ => throw new ScriptParseException(at,
                        $"'{clause}' is not a requirement. Expected '<capability>' or "
                        + "'<capability> == <value>'"),
                });
            }
        }

        var tags = new List<string>();
        if (TryGet(test, "tags", out YamlNode? tagNode))
        {
            foreach ((string tag, _) in ReadStrings(tagNode!, path, "tags"))
                tags.Add(tag.TrimStart('@'));
        }

        if (!TryGet(test, "steps", out YamlNode? stepsNode))
            throw new ScriptParseException(location, $"test '{name}' has no 'steps'");

        var steps = new List<ScriptStep>();
        foreach ((string line, SourceLocation at) in ReadStrings(stepsNode!, path, "steps"))
        {
            // Each step is a line of the same language the purpose-built form uses, so it is
            // parsed by the same parser and reports the same errors.
            ScriptDocument parsed = ScriptParser.Parse(line, path);
            ScriptTest inner = parsed.Tests[0];

            foreach (ScriptStep step in inner.Steps)
                steps.Add(Relocate(step, at));

            foreach (Requirement requirement in inner.Requirements)
                requirements.Add(requirement with { Location = at });

            tags.AddRange(inner.Tags);
        }

        return new ScriptTest
        {
            Name = name,
            Location = location,
            Requirements = requirements,
            Tags = tags,
            Steps = steps,
        };
    }

    /// <summary>
    /// Moves a step's location to the YAML line it came from, so a failure points at the file the
    /// person actually edited rather than at line one of a one-line fragment.
    /// </summary>
    private static ScriptStep Relocate(ScriptStep step, SourceLocation location) => step switch
    {
        ConnectStep s => s with { Location = location },
        PollEveryStep s => s with { Location = location },
        WaitStep s => s with { Location = location },
        NoteStep s => s with { Location = location },
        SendLongPollStep s => s with { Location = location },
        ExpectExceptionStep s => s with { Location = location },
        ExpectMeterStep s => s with { Location = location },
        ExpectFieldStep s => s with { Location = location },
        ExpectLinkStep s => s with { Location = location },
        InsertBillStep s => s with { Location = location },
        PlayGamesStep s => s with { Location = location },
        PressButtonStep s => s with { Location = location },
        DoorStep s => s with { Location = location },
        _ => step,
    };

    private static IEnumerable<(string Value, SourceLocation At)> ReadStrings(
        YamlNode node, string path, string key)
    {
        switch (node)
        {
            case YamlScalarNode scalar when scalar.Value is not null:
                yield return (scalar.Value, Where(path, scalar));
                break;

            case YamlSequenceNode sequence:
                foreach (YamlNode item in sequence)
                {
                    if (item is not YamlScalarNode entry || entry.Value is null)
                        throw new ScriptParseException(Where(path, item),
                            $"every entry under '{key}' must be a line of text");
                    yield return (entry.Value, Where(path, entry));
                }

                break;

            default:
                throw new ScriptParseException(Where(path, node),
                    $"'{key}' must be a line of text or a list of them");
        }
    }

    private static bool TryGet(YamlMappingNode mapping, string key, out YamlNode? value)
    {
        foreach ((YamlNode name, YamlNode node) in mapping.Children)
        {
            if (name is YamlScalarNode scalar
                && string.Equals(scalar.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                value = node;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static SourceLocation Where(string path, YamlNode node) =>
        new(path, (int)node.Start.Line);
}
