using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Transport;

namespace SasWorkbench.Core.Scripting;

/// <summary>What one step of a test came to.</summary>
/// <param name="Location">The line the step was written on.</param>
/// <param name="Description">What the step said to do.</param>
/// <param name="Verdict">How it went.</param>
/// <param name="Expected">What was expected, in words a reader can check.</param>
/// <param name="Actual">What actually happened.</param>
/// <param name="Reason">Why it was skipped, or why it failed, when a sentence helps.</param>
/// <param name="At">When it happened, measured from the start of the test.</param>
public sealed record StepResult(
    SourceLocation Location,
    string Description,
    Verdict Verdict,
    string? Expected = null,
    string? Actual = null,
    string? Reason = null,
    TimeSpan At = default)
{
    /// <summary>True when this step made a claim about the machine, rather than just acting.</summary>
    public bool IsAssertion { get; init; }

    public override string ToString()
    {
        string suffix = Verdict switch
        {
            Verdict.Fail => $"  expected {Expected}, got {Actual}",
            Verdict.Skip => $"  {Reason}",
            Verdict.Error => $"  {Reason}",
            _ => string.Empty,
        };

        return $"{Verdict,-5} {Description}{suffix}";
    }
}

/// <summary>What one test came to.</summary>
public sealed record TestOutcome
{
    public required string Name { get; init; }

    public required SourceLocation Location { get; init; }

    public required Verdict Verdict { get; init; }

    /// <summary>Every step, in order.</summary>
    public IReadOnlyList<StepResult> Steps { get; init; } = [];

    /// <summary>How long the test took.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Why the whole test was skipped, when it was.</summary>
    public string? SkipReason { get; init; }

    /// <summary>The test's labels, carried into the report.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// What the test declared it needs, carried into the report rather than left in the script.
    /// </summary>
    /// <remarks>
    /// A record that holds the verdict but not the conditions the case claimed to need cannot be
    /// read back into a description of what the case was for. Everything here was in the script;
    /// what changes is that the record now carries it, so anything rendered from the record can
    /// say it without reading the script again — and without inventing prose when the script is
    /// thin.
    /// </remarks>
    public IReadOnlyList<Requirement> Requirements { get; init; } = [];

    /// <summary>The configuration the case assumed the machine was in, when it said.</summary>
    public MachineDeclaration? Machine { get; init; }

    /// <summary>When the test began, in the world.</summary>
    /// <remarks>
    /// The anchor that puts a run's relative times on somebody's watch. Every other timestamp in
    /// the record is an elapsed time from a zero inside the run, which is what makes them
    /// comparable with each other and useless for comparing with anything outside it.
    /// </remarks>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>Every message that crossed the wire, so a failure can be read back byte by byte.</summary>
    public IReadOnlyList<WireRecord> Wire { get; init; } = [];

    /// <summary>
    /// What this test in particular was carried over, and how much it is worth.
    /// </summary>
    /// <remarks>
    /// Per test rather than only per run, so a row that is copied, filtered or sorted out of the
    /// report cannot shed the claim it was made under.
    /// </remarks>
    public RunProvenance? Provenance { get; init; }

    /// <summary>How many steps made a claim and held.</summary>
    public int Passed => Steps.Count(s => s.IsAssertion && s.Verdict == Verdict.Pass);

    /// <summary>How many steps made a claim and did not hold.</summary>
    public int Failed => Steps.Count(s => s.Verdict == Verdict.Fail);

    /// <summary>How many steps could not be run.</summary>
    public int Skipped => Steps.Count(s => s.Verdict == Verdict.Skip);

    /// <summary>How many claims this test actually made.</summary>
    public int Assertions => Steps.Count(s => s.IsAssertion);

    public override string ToString()
    {
        string detail = Verdict == Verdict.Skip && SkipReason is not null
            ? $"\n        {SkipReason}"
            : $" ({Duration.TotalSeconds:F1}s, {Assertions} assert(s))";
        return $"  {Verdict.ToString().ToUpperInvariant(),-4}  {Name}{detail}";
    }
}

/// <summary>
/// How much a whole run is worth, stated plainly enough that nobody has to guess.
/// </summary>
/// <param name="Transport">What the run was carried over.</param>
/// <param name="Fidelity">How much a pass on that link is worth.</param>
/// <param name="Stimulus">What was driving the machine.</param>
/// <param name="Caveat">One sentence a reader needs in order not to over-read the result.</param>
public sealed record RunProvenance(
    string Transport,
    TransportFidelity Fidelity,
    StimulusBackend Stimulus,
    string? Caveat)
{
    /// <summary>
    /// Whether the ninth bit was recorded at all. False only for imported traffic: two of the
    /// three capture formats keep the eight data bits and nothing else.
    /// </summary>
    /// <remarks>
    /// A property of the link and not of any one message, which is why it lives here. Without it,
    /// a reading of such a capture would report every host message as one no machine would answer —
    /// a finding about the recorder dressed up as a finding about the machine.
    /// </remarks>
    public bool WakeupBitCaptured { get; init; } = true;

    /// <summary>
    /// How the machine was divided between hosts: a poll address per channel, and the channel
    /// owning each category.
    /// </summary>
    /// <remarks>
    /// Part of the provenance rather than of any one packet, because "which host should have sent
    /// this poll" and "which host owns this exception" are unanswerable without it — and the map
    /// lives in the machine's own configuration, not here.
    /// </remarks>
    public string? Channels { get; init; }

    /// <summary>
    /// True when the arrangement was read from the machine, false when the case declared it.
    /// </summary>
    /// <remarks>
    /// The difference is the whole value of recording it. Against a machine this build made, the
    /// map is a reading. Against anybody else's, no long poll reports it, so what is on record is
    /// what the case said it would be — which a case can then check against the machine's answers
    /// but must not be taken as having checked on its own.
    /// </remarks>
    public bool ChannelsRead { get; init; }

    /// <summary>Why the arrangement asked for was refused, when it was.</summary>
    public string? ChannelRefusal { get; init; }

    public override string ToString()
    {
        string caveat = Caveat is null ? string.Empty : $" — {Caveat}";
        string channels = Channels is null
            ? string.Empty
            : $", {Channels} ({(ChannelsRead ? "read from the machine" : "declared by the case")})";
        return $"{Transport}, {Fidelity.ToString().ToLowerInvariant()}, stimulus "
               + $"{Stimulus.ToString().ToLowerInvariant()}{channels}{caveat}";
    }
}

/// <summary>Everything a run produced.</summary>
public sealed record RunReport
{
    /// <summary>Every test, in the order it ran.</summary>
    public IReadOnlyList<TestOutcome> Tests { get; init; } = [];

    /// <summary>When the run started.</summary>
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>How long the whole run took.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>What the run was carried over, and how much it is worth.</summary>
    public IReadOnlyList<RunProvenance> Provenance { get; init; } = [];

    /// <summary>What the environment could do.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public int Passed => Tests.Count(t => t.Verdict == Verdict.Pass);

    public int Failed => Tests.Count(t => t.Verdict == Verdict.Fail);

    public int Skipped => Tests.Count(t => t.Verdict == Verdict.Skip);

    public int Errored => Tests.Count(t => t.Verdict == Verdict.Error);

    /// <summary>
    /// The verdict for the whole run. A run in which nothing could be tested is a skip, never a
    /// pass: a suite that passed because every test was skipped would be the most misleading
    /// result this tool could produce.
    /// </summary>
    public Verdict Verdict => Tests.Count switch
    {
        0 => Verdict.Skip,
        _ when Errored > 0 => Verdict.Error,
        _ when Failed > 0 => Verdict.Fail,
        _ when Passed == 0 => Verdict.Skip,
        _ => Verdict.Pass,
    };

    /// <summary>The process exit code this run should produce.</summary>
    public int ExitCode => Verdict switch
    {
        Verdict.Pass => 0,
        Verdict.Fail => 1,
        Verdict.Error => 2,

        // A run that tested nothing is not a success, and a build should be able to notice.
        Verdict.Skip => 3,
        _ => 2,
    };

    /// <summary>One line summarising the run.</summary>
    public string Summary()
    {
        var parts = new List<string> { $"{Passed} passed" };
        if (Failed > 0)
            parts.Add($"{Failed} failed");
        if (Skipped > 0)
            parts.Add($"{Skipped} skipped");
        if (Errored > 0)
            parts.Add($"{Errored} errored");

        return $"{string.Join(", ", parts)} in {Duration.TotalSeconds:F1}s";
    }
}
