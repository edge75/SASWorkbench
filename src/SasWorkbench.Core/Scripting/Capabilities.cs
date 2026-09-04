using System.Runtime.InteropServices;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;

namespace SasWorkbench.Core.Scripting;

/// <summary>
/// What the environment a run is happening in can actually do.
/// </summary>
/// <remarks>
/// A capability is either present with a value or absent, and absent is always a skip rather
/// than a failure. This is what stops an environment problem being reported as a defect: a
/// suite run without a note acceptor should say so on the tests that needed one and stay green
/// on the rest, because a red suite that everybody knows to ignore is no suite at all.
/// </remarks>
public sealed class CapabilitySet
{
    private readonly Dictionary<string, string?> _values;

    public CapabilitySet(IEnumerable<KeyValuePair<string, string?>> values)
    {
        _values = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Nothing is available. Every requirement is unmet.</summary>
    public static CapabilitySet Empty { get; } = new([]);

    /// <summary>Every capability, sorted, for a report or an error message.</summary>
    public IReadOnlyList<string> Names =>
        _values.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>True when the capability is present at all.</summary>
    public bool Has(string key) => _values.ContainsKey(key);

    /// <summary>The value of a capability, or null when it is present without one.</summary>
    public string? Value(string key) => _values.GetValueOrDefault(key);

    /// <summary>Adds a capability, returning a new set. The original is untouched.</summary>
    public CapabilitySet With(string key, string? value = null)
    {
        var copy = new Dictionary<string, string?>(_values, StringComparer.OrdinalIgnoreCase)
        {
            [key] = value,
        };
        return new CapabilitySet(copy);
    }

    /// <summary>Adds several capabilities, returning a new set.</summary>
    public CapabilitySet With(IEnumerable<KeyValuePair<string, string?>> more)
    {
        var copy = new Dictionary<string, string?>(_values, StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string? value) in more)
            copy[key] = value;
        return new CapabilitySet(copy);
    }

    /// <summary>
    /// Checks a requirement, and says in one sentence why it is unmet when it is.
    /// </summary>
    public bool Satisfies(Requirement requirement, out string reason)
    {
        if (!_values.TryGetValue(requirement.Key, out string? actual))
        {
            reason = $"this environment provides no '{requirement.Key}'";
            return false;
        }

        if (requirement.ExpectedValue is null)
        {
            reason = string.Empty;
            return true;
        }

        if (string.Equals(actual, requirement.ExpectedValue, StringComparison.OrdinalIgnoreCase))
        {
            reason = string.Empty;
            return true;
        }

        reason = actual is null
            ? $"'{requirement.Key}' is available but its value is unknown, and the test needs "
              + $"'{requirement.ExpectedValue}'"
            : $"'{requirement.Key}' is '{actual}', not '{requirement.ExpectedValue}'";
        return false;
    }

    /// <summary>The first unmet requirement, with its reason, or null when all are met.</summary>
    public (Requirement Requirement, string Reason)? FirstUnmet(IEnumerable<Requirement> requirements)
    {
        foreach (Requirement requirement in requirements)
        {
            if (!Satisfies(requirement, out string reason))
                return (requirement, reason);
        }

        return null;
    }
}

/// <summary>Works out what this machine and this build can do.</summary>
public static class CapabilityProbe
{
    /// <summary>
    /// Everything that is true regardless of which machine is on the other end of the link.
    /// </summary>
    public static CapabilitySet Baseline()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["transport.loopback"] = "yes",
            ["transport.tcp"] = "yes",

            // Only the first channel is wired. The rest are carried through the model and the
            // reports so that adding them later is not a rewrite, but claiming them now would be
            // a claim about a link nobody has opened.
            ["channel.1"] = "yes",
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string[] ports = SerialTransport.AvailablePorts();
            if (ports.Length > 0)
                values["transport.serial"] = string.Join(';', ports);
        }

        // A reply this build cannot decode cannot be asserted on, so a test that needs one is
        // skipped rather than run against a guess.
        foreach (byte command in SasLayouts.All.Keys)
            values[$"decoder.lp{command:X2}"] = "yes";

        return new CapabilitySet(values);
    }

    /// <summary>What a stimulus backend adds.</summary>
    public static IReadOnlyDictionary<string, string?> ForStimulus(StimulusBackend backend) =>
        backend switch
        {
            StimulusBackend.Software => new Dictionary<string, string?>
            {
                ["stimulus"] = "software",
                ["stimulus.bill"] = "yes",
                ["stimulus.play"] = "yes",
                ["stimulus.door.slot"] = "yes",
            },

            StimulusBackend.Manual => new Dictionary<string, string?>
            {
                ["stimulus"] = "manual",
                ["stimulus.bill"] = "yes",
                ["stimulus.play"] = "yes",
                ["stimulus.door.slot"] = "yes",
                ["stimulus.button.collect"] = "yes",
            },

            // The peripheral simulator is not driven yet, so it provides nothing. Reporting it as
            // present and then failing to act would be the silent degradation this project refuses.
            StimulusBackend.Qpm => new Dictionary<string, string?> { ["stimulus"] = "qpm" },

            _ => new Dictionary<string, string?> { ["stimulus"] = "none" },
        };
}
