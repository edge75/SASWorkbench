using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Replay;

/// <summary>What configuration the machine library gates one family of commands on.</summary>
/// <param name="Commands">The commands it gates.</param>
/// <param name="Field">
/// The reply field that says what the machine is configured to do, as this build's layouts name
/// it. The whole point of naming a field rather than describing the setting is that the replay can
/// then <em>measure</em> whether the two machines differ on it.
/// </param>
/// <param name="Gate">What the configuration is, in a report's own words.</param>
/// <param name="SilentWhen">
/// The value of that field at which the library's handler returns without sending anything, where
/// the library says so plainly. It is what lets a replay conclude something from measuring only
/// the machine in front of it: a machine whose own reply says it is in the configuration the
/// library refuses in is behaving exactly as written, whatever the recording turns out to have
/// been. Null where the library does not put it that simply.
/// </param>
public sealed record ReplayGate(
    IReadOnlySet<byte> Commands, string Field, string Gate, long? SilentWhen = null);

/// <summary>
/// Which commands a machine answers only when it is configured to.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of one row of a real replay. A recorded machine answered
/// <c>$4D</c> — one entry of its validation buffer — with a zeroed record, and the machine in
/// front of the replay answered nothing at all. Nothing in the first draft of the classifier
/// explained that, so it came out as a possible defect; and it is not one. The machine library's
/// handler for a validation command <b>returns without sending anything</b> when it reaches a
/// machine that does not perform that style of validation, which this project measured and wrote
/// down when it built the software machine.
/// </para>
/// <para>
/// So silence there is a machine in another configuration, and the table below is what lets a
/// replay say so. It is <b>stated</b>, because which configuration gates which handler is not
/// something this build can read out of the library — and it is used only alongside a
/// <b>measurement</b>: the cause is configuration only where the two machines are observed to
/// disagree on the very field named here. Without that observation the row stays unexplained and
/// says which field would settle it, because a table on its own is a claim and not evidence.
/// </para>
/// </remarks>
public static class ReplayGates
{
    /// <summary>Every gate this build knows about.</summary>
    public static IReadOnlyList<ReplayGate> All { get; } =
    [
        new ReplayGate(
            new HashSet<byte> { 0x4C, 0x4D, 0x58, 0x7B },
            "validation_style",
            "which style of validation the machine performs",
            SilentWhen: 0),

        // Read out of the library, not out of the name of the feature bit. Its own comment says
        // a redemption cycle needs the redemption bit set AND a validation style that is not
        // standard - "a machine with no style configured is not exempt" - and the style is the
        // discriminating half, because a machine can advertise redemption and still be standard.
        new ReplayGate(
            new HashSet<byte> { 0x70, 0x71 },
            "validation_style",
            "whether the machine may run a ticket redemption cycle, which needs the redemption "
            + "feature and a validation style that is not standard",
            SilentWhen: 0),

        new ReplayGate(
            new HashSet<byte> { 0xB2, 0xB3 },
            "feature_codes_2",
            "whether the machine advertises more than one player denomination"),

        new ReplayGate(
            new HashSet<byte> { 0x8C, 0x95, 0x96, 0x97, 0x98, 0x99 },
            "feature_codes_1",
            "whether the machine advertises tournament mode"),

        new ReplayGate(
            new HashSet<byte> { 0x2D, 0x52, 0x53, 0x83, 0x84, 0xB4, 0xB5 },
            "games",
            "how many games the machine has, since a question about a game it has not got is "
            + "answered with silence"),
    ];

    /// <summary>The gate on one command, when there is one.</summary>
    public static ReplayGate? For(byte command) =>
        All.FirstOrDefault(gate => gate.Commands.Contains(command));
}

/// <summary>
/// What a replay learned about the two machines from the whole recording, rather than from one
/// request.
/// </summary>
/// <remarks>
/// Built after every request has been sent and before anything is classified, which is why the
/// replay is two passes rather than one. A row about <c>$4D</c> is only explicable in the light of
/// what <c>$A0</c> said about both machines, and <c>$A0</c> may well have been sent after it.
/// </remarks>
public sealed record ReplayContext
{
    /// <summary>
    /// Configuration fields the two machines are measured to disagree on, and what each answered.
    /// </summary>
    public IReadOnlyDictionary<string, (string Recorded, string Live)> Disagreements { get; init; }
        = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every configuration field read from both machines, whether or not they agreed.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="Disagreements"/> because "they agree on this" and "nobody asked"
    /// are different answers and only one of them rules a cause out.
    /// </remarks>
    public IReadOnlySet<string> Read { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nothing learned, for a replay that reached no configuration reply.</summary>
    public static ReplayContext Empty { get; } = new();

    /// <summary>
    /// What the machine in front of the replay says its own configuration is, read from a features
    /// poll of its own rather than from the recording.
    /// </summary>
    /// <remarks>
    /// One extra poll, and it is what lets a row conclude anything at all where the recording is
    /// silent about the machine's configuration — which a recording of one test case usually is.
    /// </remarks>
    public IReadOnlyDictionary<string, (string Rendered, long? Number)> Machine { get; init; }
        = new Dictionary<string, (string, long?)>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether this field was read from both machines at all.</summary>
    public bool Observed(string field) => Read.Contains(field);

    /// <summary>What the machine itself said about one configuration field, when it was asked.</summary>
    public (string Rendered, long? Number)? OfTheMachine(string field) =>
        Machine.TryGetValue(field, out (string Rendered, long? Number) value) ? value : null;

    /// <summary>
    /// Whether the two machines are known to differ on the field a command's gate names.
    /// </summary>
    public bool Differs(string field) => Disagreements.ContainsKey(field);

    /// <summary>What each machine answered for one field, for a report.</summary>
    public (string Recorded, string Live)? Values(string field) =>
        Disagreements.TryGetValue(field, out (string Recorded, string Live) both) ? both : null;

    /// <summary>
    /// Reads what both machines said about their configuration out of the replies already sent.
    /// </summary>
    /// <remarks>
    /// <b>Every</b> answer the recording holds is read, not the first, and that is not tidiness.
    /// The conformance toolkit's traces hold the same machine's features reply twice with two
    /// different validation styles in it, because the corpus reconfigures the machine between
    /// steps; reading only the first ruled out the very configuration that explained a silence,
    /// and turned a machine behaving exactly as the library says into a possible defect.
    /// </remarks>
    /// <param name="subjects">Everything both machines answered.</param>
    /// <param name="machineFeatures">
    /// The machine's own answer to the enabled-features poll, asked by the replay itself. Null
    /// when it would not answer one.
    /// </param>
    public static ReplayContext From(
        IEnumerable<ReplaySubject> subjects, byte[]? machineFeatures = null)
    {
        ArgumentNullException.ThrowIfNull(subjects);

        var recordedValues =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var liveValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var differs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (ReplaySubject subject in subjects)
        {
            if (SasLayouts.For(subject.Command) is not { } layout)
                continue;
            if (subject.RecordedReplies.Count == 0 || subject.Live.Frame.Length == 0)
                continue;

            SasDecoded live = SasMessageDecoder.DecodeResponse(layout, subject.Live.Frame);
            if (!live.Succeeded)
                continue;

            foreach (byte[] reply in subject.RecordedReplies)
            {
                SasDecoded recorded = SasMessageDecoder.DecodeResponse(layout, reply);
                if (!recorded.Succeeded)
                    continue;

                foreach (SasValue value in recorded.Values)
                {
                    SasValue? other = live[value.Path];
                    if (other is null)
                        continue;
                    if (ReplayFields.Explains(value.Path)
                        is not { Cause: ReplayCause.Configuration })
                    {
                        continue;
                    }

                    liveValues[value.Path] = Render(other);
                    if (!recordedValues.TryGetValue(value.Path, out List<string>? seen))
                        recordedValues[value.Path] = seen = [];

                    string rendered = Render(value);
                    if (!seen.Contains(rendered))
                        seen.Add(rendered);

                    if (!ReplayDiff.Same(value, other))
                        differs.Add(value.Path);
                }
            }
        }

        var disagreements =
            new Dictionary<string, (string Recorded, string Live)>(StringComparer.OrdinalIgnoreCase);
        foreach (string field in differs)
        {
            disagreements[field] =
                (string.Join(" and ", recordedValues[field]), liveValues[field]);
        }

        return new ReplayContext
        {
            Disagreements = disagreements,
            Read = new HashSet<string>(liveValues.Keys, StringComparer.OrdinalIgnoreCase),
            Machine = OwnFeatures(machineFeatures),
        };
    }

    private static Dictionary<string, (string, long?)> OwnFeatures(byte[]? frame)
    {
        var own = new Dictionary<string, (string, long?)>(StringComparer.OrdinalIgnoreCase);
        if (frame is null || frame.Length == 0 || SasLayouts.For(0xA0) is not { } layout)
            return own;

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, frame);
        if (!decoded.Succeeded)
            return own;

        foreach (SasValue value in decoded.Values)
        {
            if (ReplayFields.Explains(value.Path, 0xA0) is { Cause: ReplayCause.Configuration })
                own[value.Path] = (Render(value), value.Number);
        }

        return own;
    }

    private static string Render(SasValue value) =>
        value.Text ?? value.Number?.ToString() ?? value.ToHex();
}
