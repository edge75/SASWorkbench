using System.Runtime.InteropServices;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
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

/// <summary>What the machine on the link said about itself when it was asked.</summary>
/// <param name="Values">The capabilities its answer establishes.</param>
/// <param name="WhyNot">
/// Why there are none, when there are none. Kept apart from the values because "the machine
/// stayed quiet", "the poll never reached the wire" and "the reply made no sense" are three
/// different things to be told, and a report that flattens them into an absent capability sends
/// the reader to the wrong place.
/// </param>
public sealed record MachineAnswers(IReadOnlyDictionary<string, string?> Values, string? WhyNot);

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

    /// <summary>
    /// What being able to read the wallet adds.
    /// </summary>
    /// <remarks>
    /// Deliberately not called 'printer'. Whether the machine has a printer, and whether it is
    /// configured well enough to use one, is a property of the machine and is not knowable from
    /// here - on fresh memory the printer protocol is unset and no ticket can be produced at all.
    /// What this host can honestly claim is narrower: that there is a directory a printed ticket
    /// would land in and that it can read it. A machine that then prints nothing fails the
    /// assertion that was waiting, which is a true statement about that machine rather than a
    /// missing capability of ours.
    /// </remarks>
    public static IReadOnlyDictionary<string, string?> ForWallet(string directory)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            Directory.CreateDirectory(directory);
            values["printer.wallet"] = directory;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            // Absent, so a test that needs it skips naming the directory rather than failing.
        }

        return values;
    }

    /// <summary>
    /// What being able to write into the acceptor adds.
    /// </summary>
    /// <remarks>
    /// Named for what this host can do, not for what the machine will do with it, for the same
    /// reason the wallet is. Whether the machine accepts tickets at all is a property of its
    /// configuration and it says so on the wire; what is knowable from here is only that there is
    /// a directory to put one in.
    /// </remarks>
    public static IReadOnlyDictionary<string, string?> ForAcceptor(string directory)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            Directory.CreateDirectory(directory);
            values["stimulus.ticket"] = directory;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            // Absent, so a test that needs it skips naming the directory rather than failing.
        }

        return values;
    }

    /// <summary>
    /// The capabilities only the machine can answer for, which are therefore asked over the wire.
    /// </summary>
    /// <remarks>
    /// These are checked after the link is up rather than before it, because there is nowhere
    /// else to get them. A machine's validation style is not a property of this host, of the
    /// stimulus backend or of the build: it is set in the machine's own memory, and the only
    /// thing it can be read from is the machine.
    /// </remarks>
    public static readonly IReadOnlySet<string> AskedOfTheMachine =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "egm.validation" };

    /// <summary>True when a requirement can only be answered by the machine on the link.</summary>
    public static bool IsAskedOfTheMachine(string key) => AskedOfTheMachine.Contains(key);

    /// <summary>
    /// Asks the machine what it is, over the link, once it is up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The enabled-features reply is where a machine publishes its validation style, so this is
    /// the same question asked of both peers rather than one peer being asked and the other being
    /// told what our own fixture happens to be in. A machine that does not answer the poll leaves
    /// the capability absent, and a case that needs it skips saying the machine did not answer —
    /// never saying it is in some style by default.
    /// </para>
    /// <para>
    /// The answer is about this channel, not about the machine. The gaming machine library clears
    /// the bits of any category the channel it answers on has not been given, so a link that does
    /// not carry validation reports the standard style whatever the machine is configured in.
    /// That is the honest answer for a test running on that link, and it is all one link can know.
    /// </para>
    /// </remarks>
    public static MachineAnswers FromMachine(PollEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        SasMessageLayout layout = SasLayouts.For((byte)SasLongPollCode.LP_SENDENABLEFEATURES)!;

        // Game zero, which is the machine as a whole rather than any one of its games.
        LongPollResult result = engine.Execute(
            layout.Command, [0x00, 0x00], purpose: WirePurpose.CapabilityProbe);

        // A command the engine never got as far as sending says nothing about the machine, so it
        // is asked again rather than reported as a machine that stayed quiet. Once: a second
        // failure to reach the wire is a link worth hearing about, not something to grind on.
        if (result.Outcome == LongPollOutcome.NotSent)
            result = engine.Execute(
                layout.Command, [0x00, 0x00], purpose: WirePurpose.CapabilityProbe);

        if (result.Outcome == LongPollOutcome.NotSent)
        {
            return new MachineAnswers(values,
                "the enabled-features poll could not be put on the link, so nothing was asked of "
                + "the machine at all");
        }

        if (!result.Succeeded)
        {
            return new MachineAnswers(values,
                $"the machine did not answer the enabled-features poll ({result.Outcome}), so "
                + "what it is cannot be read off this link");
        }

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, result.Frame);
        if (decoded.Number("validation_style") is not { } style)
        {
            return new MachineAnswers(values,
                "the machine's enabled-features reply could not be decoded: "
                + (decoded.Failure ?? "it carries no validation style"));
        }

        // The fourth value is assigned to no style. A machine reporting it is misconfigured, and
        // saying so beats naming a style nobody defined.
        if (style is < 0 or > 2)
        {
            return new MachineAnswers(values,
                $"the machine reports validation style {style}, which is assigned to no style");
        }

        values["egm.validation"] =
            SasNames.Underscore(((SasCashoutValidation)style).ToString());
        return new MachineAnswers(values, null);
    }

    /// <summary>What a stimulus backend adds.</summary>
    public static IReadOnlyDictionary<string, string?> ForStimulus(StimulusBackend backend) =>
        backend switch
        {
            StimulusBackend.Software => new Dictionary<string, string?>
            {
                ["stimulus"] = "software",
                ["stimulus.bill"] = "yes",

                // The software machine takes a ticket handed to it directly, with no directory in
                // between, so this is true of the machine rather than of a path on disk.
                ["stimulus.ticket"] = "yes",
                ["stimulus.play"] = "yes",
                ["stimulus.settle"] = "yes",
                ["stimulus.button.collect"] = "yes",
                ["stimulus.door.slot"] = "yes",
                ["stimulus.door.main"] = "yes",
                ["stimulus.door.card_cage"] = "yes",
                ["stimulus.door.logic"] = "yes",
                ["stimulus.door.cashbox"] = "yes",
                ["stimulus.door.stacker"] = "yes",
                ["stimulus.door.drop"] = "yes",
                ["stimulus.door.belly"] = "yes",
            },

            StimulusBackend.Manual => new Dictionary<string, string?>
            {
                ["stimulus"] = "manual",
                ["stimulus.bill"] = "yes",
                ["stimulus.ticket"] = "yes",
                ["stimulus.play"] = "yes",
                ["stimulus.settle"] = "yes",
                ["stimulus.door.slot"] = "yes",
                ["stimulus.button.collect"] = "yes",
            },

            // What the peripheral driver can do is decided by the machine on the other end, which
            // declares its own doors and buttons, so the real answer is only knowable once one is
            // attached and the live backend replaces this. Claiming a door here would be claiming
            // it about a machine nobody has looked at.
            StimulusBackend.Qpm => new Dictionary<string, string?>
            {
                ["stimulus"] = "qpm",
                ["stimulus.settle"] = "yes",
            },

            _ => new Dictionary<string, string?> { ["stimulus"] = "none" },
        };
}
