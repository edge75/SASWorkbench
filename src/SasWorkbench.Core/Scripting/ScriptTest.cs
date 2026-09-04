namespace SasWorkbench.Core.Scripting;

/// <summary>
/// Something a test needs from the environment before it can mean anything.
/// </summary>
/// <remarks>
/// Requirements exist so that a missing capability produces a skip with a reason rather than a
/// failure. They are checked rather than declared, so unlike a tag they cannot quietly go stale:
/// a test that says it needs a printer is skipped on a machine with no printer, and runs on one
/// that has it, without anybody remembering to update a list.
/// </remarks>
/// <param name="Location">Where it was declared.</param>
/// <param name="Key">The capability, such as <c>stimulus.bill</c> or <c>egm.validation</c>.</param>
/// <param name="ExpectedValue">
/// When set, the capability must not merely exist but hold this value.
/// </param>
public sealed record Requirement(SourceLocation Location, string Key, string? ExpectedValue = null)
{
    public override string ToString() =>
        ExpectedValue is null ? Key : $"{Key} == {ExpectedValue}";
}

/// <summary>One test: what it needs, how it is labelled, and what it does.</summary>
public sealed record ScriptTest
{
    /// <summary>The name that appears in a report.</summary>
    public required string Name { get; init; }

    /// <summary>Where the test begins.</summary>
    public required SourceLocation Location { get; init; }

    /// <summary>What the environment must provide.</summary>
    public IReadOnlyList<Requirement> Requirements { get; init; } = [];

    /// <summary>Labels that cut across the suite, such as <c>@slow</c> or <c>@destructive</c>.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The steps, in order.</summary>
    public IReadOnlyList<ScriptStep> Steps { get; init; } = [];

    /// <summary>Every meter this test asserts on, so a baseline can be taken for all of them.</summary>
    public IReadOnlyList<string> WatchedMeters => Steps
        .OfType<ExpectMeterStep>()
        .Select(s => s.Meter)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public override string ToString() => $"{Name} ({Steps.Count} step(s))";
}

/// <summary>Everything one script file holds.</summary>
public sealed record ScriptDocument
{
    /// <summary>The file it was read from.</summary>
    public required string Path { get; init; }

    /// <summary>The tests in it, in order.</summary>
    public IReadOnlyList<ScriptTest> Tests { get; init; } = [];

    public override string ToString() => $"{Path} ({Tests.Count} test(s))";
}

/// <summary>A script that could not be read, with the line that stopped it.</summary>
public sealed class ScriptParseException : Exception
{
    public ScriptParseException(SourceLocation location, string message)
        : base($"{location}: {message}")
    {
        Location = location;
        Reason = message;
    }

    /// <summary>Where the trouble is.</summary>
    public SourceLocation Location { get; }

    /// <summary>What is wrong, without the location prefix.</summary>
    public string Reason { get; }
}
