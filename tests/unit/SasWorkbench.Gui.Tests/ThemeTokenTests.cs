using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SasWorkbench.Gui.Tests;

/// <summary>
/// Guards the token dictionary against the failure it is most prone to: a key that exists in one
/// theme variant and not the other resolves to nothing at runtime, silently, with no error and no
/// fallback — so the check is mechanical rather than a matter of remembering.
/// </summary>
public sealed class ThemeTokenTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void BothThemeVariantsDefineTheSameKeys()
    {
        IReadOnlySet<string> light = KeysIn("Light");
        IReadOnlySet<string> dark = KeysIn("Dark");

        Assert.Empty(light.Except(dark));
        Assert.Empty(dark.Except(light));
        Assert.NotEmpty(light);
    }

    [Fact]
    public void EveryDynamicResourceTheInterfaceAsksForIsDefined()
    {
        IReadOnlySet<string> defined = KeysIn("Light");
        var missing = new List<string>();

        foreach (FileInfo file in Markup())
        {
            string text = File.ReadAllText(file.FullName);
            foreach (Match match in Regex.Matches(text, @"\{DynamicResource\s+([A-Za-z0-9_.]+)\}"))
            {
                string key = match.Groups[1].Value;
                if (!defined.Contains(key))
                    missing.Add($"{file.Name}: {key}");
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryStaticResourceTheInterfaceAsksForIsDefined()
    {
        IReadOnlySet<string> defined = TopLevelKeys();
        var missing = new List<string>();

        foreach (FileInfo file in Markup())
        {
            string text = File.ReadAllText(file.FullName);
            foreach (Match match in Regex.Matches(text, @"\{StaticResource\s+([A-Za-z0-9_.]+)\}"))
            {
                string key = match.Groups[1].Value;
                if (!defined.Contains(key))
                    missing.Add($"{file.Name}: {key}");
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>
    /// Opacity is not a hierarchy. It is permitted only on a genuinely disabled control, and the
    /// four text tokens carry the whole prose and data hierarchy instead.
    /// </summary>
    [Fact]
    public void NoViewExpressesHierarchyWithOpacity()
    {
        var offenders = new List<string>();

        foreach (FileInfo file in Markup())
        {
            string text = File.ReadAllText(file.FullName);
            foreach (Match match in Regex.Matches(text, @"\sOpacity\s*=\s*""([^""]+)"""))
                offenders.Add($"{file.Name}: Opacity={match.Groups[1].Value}");
        }

        Assert.Empty(offenders);
    }

    private static IReadOnlySet<string> KeysIn(string variant)
    {
        XElement root = XDocument.Load(Path.Combine(Gui(), "Themes", "Tokens.axaml")).Root!;
        XElement themes = root.Element(Avalonia + "ResourceDictionary.ThemeDictionaries")
                          ?? throw new InvalidOperationException("no theme dictionaries");

        XElement dictionary = themes.Elements(Avalonia + "ResourceDictionary")
            .Single(d => (string?)d.Attribute(Xaml + "Key") == variant);

        return dictionary.Elements()
            .Select(e => (string?)e.Attribute(Xaml + "Key"))
            .Where(k => k is not null)
            .Select(k => k!)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Keys outside the variants, such as the font stacks, which both variants share.</summary>
    private static IReadOnlySet<string> TopLevelKeys()
    {
        XElement root = XDocument.Load(Path.Combine(Gui(), "Themes", "Tokens.axaml")).Root!;
        return root.Elements()
            .Select(e => (string?)e.Attribute(Xaml + "Key"))
            .Where(k => k is not null)
            .Select(k => k!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<FileInfo> Markup() =>
        new DirectoryInfo(Gui()).GetFiles("*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && f.Name != "Tokens.axaml");

    private static string Gui()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "SasWorkbench.Gui");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("could not find src/SasWorkbench.Gui above the test binaries");
    }
}
