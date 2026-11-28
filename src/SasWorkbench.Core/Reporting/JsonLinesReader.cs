using System.Text.Json;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Reporting;

/// <summary>
/// Reads a run back out of the file <see cref="JsonLinesReportWriter"/> wrote.
/// </summary>
/// <remarks>
/// <para>
/// The other half of what makes an extended log a rendering rather than a second recording. A run
/// asked to explain itself and a run explained afterwards from its own record go through the same
/// renderer, and they can only do that if the record turns back into the same thing the renderer
/// was given live. Anything the record does not carry is therefore absent from both, which is the
/// property worth having: it is impossible for the live rendering to be richer than the one a
/// recording can produce, so nobody can come to rely on something that will not survive a reload.
/// </para>
/// <para>
/// A line this reader cannot understand is reported, never skipped in silence. A file written by
/// another build may carry a verdict or a link this one does not know, and a rendering that
/// quietly dropped such a line would be a lie about a run somebody still has the file for.
/// </para>
/// </remarks>
public static class JsonLinesReader
{
    /// <summary>What a file turned into, and what could not be read out of it.</summary>
    /// <param name="Report">The run, as far as the file describes one.</param>
    /// <param name="Problems">Every line or field this build could not read, in order.</param>
    public sealed record Result(RunReport Report, IReadOnlyList<string> Problems)
    {
        /// <summary>True when the file held no run at all.</summary>
        public bool IsEmpty => Report.Tests.Count == 0;
    }

    /// <summary>
    /// Reads a run from a file, whether it is plain JSON lines or a session container.
    /// </summary>
    /// <remarks>
    /// Decided by what is in the file rather than by its name, and here rather than in each
    /// command, so every reader of a run — the extended log, a replay, a recording turned into a
    /// script — takes a container without any of them knowing the format exists. That is what
    /// keeps the container a framing over the canonical record instead of a second kind of run.
    /// </remarks>
    public static Result ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return IsContainer(path) ? ReadContainer(path) : Read(File.ReadLines(path));
    }

    /// <summary>Whether a file begins with the container's own mark.</summary>
    public static bool IsContainer(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using FileStream file = File.OpenRead(path);
        if (file.Length < SessionContainer.Magic.Length)
            return false;

        Span<byte> head = stackalloc byte[SessionContainer.Magic.Length];
        file.ReadExactly(head);
        return head.SequenceEqual(SessionContainer.Magic);
    }

    private static Result ReadContainer(string path)
    {
        using FileStream file = File.OpenRead(path);
        SessionContainer.Directory directory = SessionContainer.ReadDirectory(file);
        return Read(SessionContainer.ReadAll(file, directory));
    }

    /// <summary>Reads a run from the lines of one.</summary>
    public static Result Read(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var problems = new List<string>();
        var report = new RunReport();
        var tests = new List<Builder>();
        int number = 0;

        foreach (string line in lines)
        {
            number++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonElement root;
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                root = document.RootElement.Clone();
            }
            catch (JsonException json)
            {
                problems.Add($"line {number} is not JSON: {json.Message}");
                continue;
            }

            if (root.ValueKind != JsonValueKind.Object || Text(root, "kind") is not { } kind)
            {
                problems.Add($"line {number} carries no record kind");
                continue;
            }

            switch (kind)
            {
                case "run":
                    report = ReadRun(root, problems, number);
                    break;

                case "test":
                    tests.Add(ReadTest(root, problems, number));
                    break;

                case "step":
                    Attach(tests, root, number, problems, (builder, element) =>
                        builder.Steps.Add(ReadStep(element, problems, number)));
                    break;

                case "wire":
                    Attach(tests, root, number, problems, (builder, element) =>
                    {
                        if (ReadWire(element, builder.Wire.Count, problems, number) is { } record)
                            builder.Wire.Add(record);
                    });
                    break;

                default:
                    problems.Add($"line {number} is a '{kind}' record, which this build does not read");
                    break;
            }
        }

        return new Result(report with { Tests = tests.Select(t => t.Build()).ToList() }, problems);
    }

    /// <summary>
    /// Puts a step or a packet on the test it names.
    /// </summary>
    /// <remarks>
    /// By name, as the file records it, rather than by "the test above this line". A file somebody
    /// has filtered, concatenated or sorted is still readable that way, and an orphan is reported
    /// rather than attached to whichever test happens to be last.
    /// </remarks>
    private static void Attach(
        List<Builder> tests,
        JsonElement element,
        int number,
        List<string> problems,
        Action<Builder, JsonElement> add)
    {
        string? name = Text(element, "test");
        Builder? owner = name is null
            ? tests.LastOrDefault()
            : tests.LastOrDefault(t => t.Name == name);

        if (owner is null)
        {
            problems.Add(name is null
                ? $"line {number} belongs to no test and none had been read yet"
                : $"line {number} belongs to test '{name}', which this file does not describe");
            return;
        }

        add(owner, element);
    }

    private static RunReport ReadRun(JsonElement element, List<string> problems, int number)
    {
        var provenance = new List<RunProvenance>();
        if (element.TryGetProperty("provenance", out JsonElement list)
            && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in list.EnumerateArray())
            {
                if (ReadProvenance(entry, problems, number) is { } one)
                    provenance.Add(one);
            }
        }

        var capabilities = new List<string>();
        if (element.TryGetProperty("capabilities", out JsonElement names)
            && names.ValueKind == JsonValueKind.Array)
        {
            capabilities.AddRange(names.EnumerateArray()
                .Select(n => n.GetString())
                .Where(n => n is not null)!);
        }

        return new RunReport
        {
            StartedAt = When(element, "startedAt") ?? default,
            Duration = Duration(element, "durationMs"),
            Provenance = provenance,
            Capabilities = capabilities,
        };
    }

    private static RunProvenance? ReadProvenance(
        JsonElement element, List<string> problems, int number)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        string transport = Text(element, "transport") ?? "an unnamed link";
        string? fidelity = Text(element, "fidelity");
        string? stimulus = Text(element, "stimulus");

        // A weight or a stimulus this build does not know is the one thing that must not be
        // rounded off: reading an unknown fidelity as 'proven' would make a run look worth more
        // than it is, which is the exact failure honest labelling exists to prevent.
        if (!Enum.TryParse(fidelity, ignoreCase: true, out TransportFidelity weight))
        {
            weight = TransportFidelity.Unverified;
            problems.Add($"line {number} claims link weight '{fidelity}', which this build does "
                         + "not know; it is read as unverified");
        }

        if (!Enum.TryParse(stimulus, ignoreCase: true, out StimulusBackend backend))
        {
            backend = StimulusBackend.None;
            problems.Add($"line {number} claims stimulus '{stimulus}', which this build does not "
                         + "know; it is read as none");
        }

        // Absent means the bit was recorded, because every run this project writes records it.
        // Only an import says otherwise, and a reader that defaulted the other way would call
        // every one of our own runs a capture that lost the ninth bit.
        bool wakeup = !element.TryGetProperty("wakeupBitCaptured", out JsonElement captured)
                      || captured.ValueKind != JsonValueKind.False;

        // The arrangement is read back as written or not at all. A record that does not carry one
        // leaves it null, and a record that carries one but does not say it was read from the
        // machine is read as declared: claiming a reading the file never made would turn what a
        // case asserted into what a machine confirmed.
        string? channels = Text(element, "channels");
        bool read = channels is not null
                    && element.TryGetProperty("channelsRead", out JsonElement fromMachine)
                    && fromMachine.ValueKind == JsonValueKind.True;

        if (channels is not null && !SasChannelPlan.TryParse(channels, out _, out string? unreadable))
        {
            problems.Add($"line {number} describes the arrangement as '{channels}', which this "
                         + $"build cannot read back: {unreadable}");
        }

        return new RunProvenance(transport, weight, backend, Text(element, "caveat"))
        {
            WakeupBitCaptured = wakeup,
            Channels = channels,
            ChannelsRead = read,
            ChannelRefusal = Text(element, "channelRefusal"),
            Round = Text(element, "round"),
            MachineBuild = Text(element, "machineBuild"),
        };
    }

    private static Builder ReadTest(JsonElement element, List<string> problems, int number)
    {
        string name = Text(element, "name") ?? $"the test on line {number}";

        if (!Enum.TryParse(Text(element, "verdict"), ignoreCase: true, out Verdict verdict))
        {
            verdict = Verdict.Error;
            problems.Add($"line {number} claims verdict '{Text(element, "verdict")}' for "
                         + $"'{name}', which this build does not know; it is read as an error");
        }

        var requirements = new List<Requirement>();
        if (element.TryGetProperty("requires", out JsonElement needs)
            && needs.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement need in needs.EnumerateArray())
            {
                if (Text(need, "key") is { } key)
                    requirements.Add(new Requirement(Where(need), key, Text(need, "value")));
            }
        }

        return new Builder
        {
            Name = name,
            Location = Where(element),
            Verdict = verdict,
            Machine = ReadDeclaration(element),
            StartedAt = When(element, "startedAt") ?? default,
            Duration = Duration(element, "durationMs"),
            SkipReason = Text(element, "skipReason"),
            Tags = Strings(element, "tags"),
            Requirements = requirements,
            Provenance = element.TryGetProperty("provenance", out JsonElement how)
                         && how.ValueKind == JsonValueKind.Object
                ? ReadProvenance(how, problems, number)
                : null,
        };
    }

    /// <summary>
    /// Reads back what the case assumed, so a record rendered from this file says the same as one
    /// rendered from the run it came from.
    /// </summary>
    private static MachineDeclaration? ReadDeclaration(JsonElement element)
    {
        if (!element.TryGetProperty("assumes", out JsonElement assumes)
            || assumes.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!assumes.TryGetProperty("accountingDenominationCents", out JsonElement cents)
            || !cents.TryGetInt32(out int amount)
            || !assumes.TryGetProperty("accountingDenominationCode", out JsonElement code)
            || !code.TryGetInt32(out int number))
        {
            return null;
        }

        var players = new List<int>();
        if (assumes.TryGetProperty("playerDenominationsInCents", out JsonElement list)
            && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement player in list.EnumerateArray())
            {
                if (player.TryGetInt32(out int value))
                    players.Add(value);
            }
        }

        return new MachineDeclaration(Where(assumes), (byte)number, amount, players);
    }

    private static StepResult ReadStep(JsonElement element, List<string> problems, int number)
    {
        if (!Enum.TryParse(Text(element, "verdict"), ignoreCase: true, out Verdict verdict))
        {
            verdict = Verdict.Error;
            problems.Add($"line {number} claims step verdict '{Text(element, "verdict")}', which "
                         + "this build does not know; it is read as an error");
        }

        return new StepResult(
            Where(element),
            Text(element, "description") ?? "an unnamed step",
            verdict,
            Text(element, "expected"),
            Text(element, "actual"),
            Text(element, "reason"),
            Duration(element, "atMs"))
        {
            IsAssertion = element.TryGetProperty("isAssertion", out JsonElement assertion)
                          && assertion.ValueKind == JsonValueKind.True,
        };
    }

    private static WireRecord? ReadWire(
        JsonElement element, int fallbackSequence, List<string> problems, int number)
    {
        if (!SasSymbol.TryParseMessage(Text(element, "symbols"), out SasSymbol[] symbols))
        {
            problems.Add($"line {number} carries symbols this build cannot read back: "
                         + $"'{Text(element, "symbols")}'");
            return null;
        }

        int channel = element.TryGetProperty("channel", out JsonElement lane)
                      && lane.TryGetInt32(out int value)
            ? value
            : 1;

        if (!Enum.IsDefined((SasChannel)channel))
        {
            problems.Add($"line {number} names channel {channel}, which SAS does not define; it "
                         + "is read as channel 1");
            channel = 1;
        }

        // Read as written or left unknown. A record with no address is one whose recorder did not
        // know which machine it was, and filling it in here would attribute a floor's traffic to
        // whichever machine happened to be first.
        SasAddress? machine = null;
        if (element.TryGetProperty("address", out JsonElement at)
            && at.ValueKind == JsonValueKind.Number
            && at.TryGetInt32(out int machineValue))
        {
            if (machineValue is >= 0 and <= SasAddress.MaxValue)
            {
                machine = SasAddress.Of((byte)machineValue);
            }
            else
            {
                problems.Add($"line {number} names machine address {machineValue}, which SAS does "
                             + "not define; the message is read as belonging to no known machine");
            }
        }

        return new WireRecord(
            element.TryGetProperty("sequence", out JsonElement sequence)
            && sequence.TryGetInt64(out long recorded)
                ? recorded
                : fallbackSequence,
            Text(element, "direction") == nameof(WireDirection.Receive)
                ? WireDirection.Receive
                : WireDirection.Transmit,
            (SasChannel)channel,
            Duration(element, "atMs"),
            symbols,
            Text(element, "note"))
        {
            Address = machine,
        };
    }

    private static SourceLocation Where(JsonElement element) =>
        Text(element, "file") is { } file
            ? new SourceLocation(file,
                element.TryGetProperty("line", out JsonElement line)
                && line.TryGetInt32(out int value) ? value : 0)
            : SourceLocation.None;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> Strings(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement list)
            || list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return list.EnumerateArray()
            .Select(e => e.GetString())
            .Where(e => e is not null)
            .ToList()!;
    }

    private static TimeSpan Duration(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out double milliseconds)
            ? TimeSpan.FromMilliseconds(milliseconds)
            : TimeSpan.Zero;

    private static DateTimeOffset? When(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && value.TryGetDateTimeOffset(out DateTimeOffset moment)
            ? moment
            : null;

    /// <summary>A test being assembled as its steps and packets arrive.</summary>
    private sealed class Builder
    {
        public required string Name { get; init; }

        public required SourceLocation Location { get; init; }

        public required Verdict Verdict { get; init; }

        public DateTimeOffset StartedAt { get; init; }

        public TimeSpan Duration { get; init; }

        public string? SkipReason { get; init; }

        public IReadOnlyList<string> Tags { get; init; } = [];

        public IReadOnlyList<Requirement> Requirements { get; init; } = [];

        public MachineDeclaration? Machine { get; init; }

        public RunProvenance? Provenance { get; init; }

        public List<StepResult> Steps { get; } = [];

        public List<WireRecord> Wire { get; } = [];

        public TestOutcome Build() => new()
        {
            Name = Name,
            Location = Location,
            Verdict = Verdict,
            StartedAt = StartedAt,
            Duration = Duration,
            SkipReason = SkipReason,
            Tags = Tags,
            Requirements = Requirements,
            Machine = Machine,
            Provenance = Provenance,
            Steps = Steps,
            Wire = Wire,
        };
    }
}
