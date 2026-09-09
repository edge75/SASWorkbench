using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Sweep;
using SasWorkbench.Core.Transactions;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Replay;

/// <summary>How a replay is run.</summary>
/// <param name="AskTwice">
/// Whether to ask each command a second time. On by default, because it is the only thing here
/// that turns "this field is state" from a claim into a measurement, and it costs one extra poll
/// per distinct request.
/// </param>
/// <param name="Limit">The most distinct requests to replay, or null for all of them.</param>
public sealed record ReplayOptions(bool AskTwice = true, int? Limit = null);

/// <summary>What a whole replay came to.</summary>
public sealed record ReplayResult
{
    public required IReadOnlyList<ReplayFinding> Findings { get; init; }

    /// <summary>How many exchanges in the recording each row of the replay stands for.</summary>
    public required int RecordedExchanges { get; init; }

    /// <summary>How many exchanges were general or synchronisation polls, which are not replayed.</summary>
    public required int Polls { get; init; }

    /// <summary>
    /// Configuration fields the two machines are measured to disagree on, and what each said.
    /// </summary>
    public IReadOnlyDictionary<string, (string Recorded, string Live)> Disagreements { get; init; }
        = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the machine said about itself, asked by the replay rather than the recording.</summary>
    public IReadOnlyDictionary<string, (string Rendered, long? Number)> MachineSaysOfItself
    {
        get;
        init;
    } = new Dictionary<string, (string, long?)>(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many rows came to each cause.</summary>
    public int Count(ReplayCause cause) => Findings.Count(f => f.Cause == cause);

    /// <summary>The rows nothing here explains, which are the only ones that make a replay red.</summary>
    public IEnumerable<ReplayFinding> Unexplained => Findings.Where(f => f.IsFinding);
}

/// <summary>
/// Sends a recorded session's host messages to a machine and says what the differences mean.
/// </summary>
/// <remarks>
/// <para>
/// This is the only test in the project whose expectation was written by somebody else. Every
/// other case here says what a machine ought to do because we decided it ought to; a replay says
/// a machine did something, once, and asks whether this one does the same. That is worth having
/// precisely because we did not author it — and it is also why the whole difficulty is in the
/// classification rather than in the sending.
/// </para>
/// <para>
/// Three things it will not do.
/// </para>
/// <list type="bullet">
/// <item>
/// <b>It does not send a command that changes the machine.</b> The recording is full of them — a
/// conformance capture configures a machine before it measures it — and every one is withheld by
/// name, with what it would have changed, out of the same plan the catalogue sweep uses. So a
/// replay of a capture that redeems tickets and moves money replays the reads and says plainly
/// which parts it left alone.
/// </item>
/// <item>
/// <b>It does not replay general polls.</b> What a general poll returns is whichever exception the
/// machine had queued, which is a fact about what happened to that machine at that moment. There
/// is nothing to reproduce and a diff of it would be noise.
/// </item>
/// <item>
/// <b>It does not repair a recording.</b> A host message whose own check sequence does not check
/// out is not sent, because sending this build's guess at what it was meant to be would be
/// measuring the guess.
/// </item>
/// </list>
/// <para>
/// The requests are grouped, so a recording that polls one command a thousand times is one row
/// standing for a thousand exchanges. That is not only for brevity: where the recording holds
/// several answers to one request, the differences between <em>those</em> are a measurement of
/// what moves on the recorded machine, and it is free.
/// </para>
/// </remarks>
public sealed class ReplayRunner(
    PollEngine engine, ReplayOptions? options = null, Action<ReplayFinding>? progress = null)
{
    private readonly PollEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly ReplayOptions _options = options ?? new ReplayOptions();

    /// <summary>Replays a split recording against the machine the engine is polling.</summary>
    public ReplayResult Run(TransactionSplit recorded, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(recorded);

        int polls = recorded.Exchanges.Count(e =>
            e.Kind is ExchangeKind.GeneralPoll or ExchangeKind.SyncPoll);

        // Two passes, and the reason is one row of a real replay. What a machine's silence on a
        // validation command means depends on what its features reply said, and the features
        // reply may well be sent after it — so everything is asked first, then what the two
        // machines are measured to disagree on is worked out, and only then is anything
        // classified. Nothing extra is sent for it.
        var asked = new List<(RequestGroup Group, ReplaySubject? Subject, ReplayFinding? Skipped)>();
        int sent = 0;
        foreach (RequestGroup group in Group(recorded))
        {
            cancel.ThrowIfCancellationRequested();
            if (_options.Limit is { } limit && sent >= limit)
                break;

            sent++;
            if (Refuse(group) is { } refusal)
            {
                asked.Add((group, null, refusal));
                progress?.Invoke(refusal);
                continue;
            }

            asked.Add((group, Ask(group), null));
        }

        // One poll of the machine's own features, so a row about a command the library gates can
        // say what this machine is even where the recording never asked. A recording of a single
        // test case usually never asked.
        LongPollResult features = _engine.Execute(
            0xA0, [0x00, 0x00], purpose: "replay $A0, to read what this machine says it is");

        ReplayContext context = ReplayContext.From(
            asked.Select(entry => entry.Subject).OfType<ReplaySubject>(),
            features.Outcome == LongPollOutcome.Answered ? features.Frame : null);

        var findings = new List<ReplayFinding>(asked.Count);
        foreach ((RequestGroup group, ReplaySubject? subject, ReplayFinding? skipped) in asked)
        {
            ReplayFinding finding = skipped
                                    ?? ReplayDiff.Classify(subject!, group.Occurrences, context);
            findings.Add(finding);
            if (skipped is null)
                progress?.Invoke(finding);
        }

        return new ReplayResult
        {
            Findings = findings,
            RecordedExchanges = recorded.Exchanges.Count,
            Polls = polls,
            Disagreements = context.Disagreements,
            MachineSaysOfItself = context.Machine,
        };
    }

    /// <summary>
    /// The distinct requests in a recording, each with every answer the recording holds to it.
    /// </summary>
    /// <remarks>
    /// Grouped by the command and the request bytes together, never by the command alone: the same
    /// command with two different game numbers in it asks two different questions, and merging
    /// them would compare one machine's answer about game one with another's about game two.
    /// </remarks>
    public static IReadOnlyList<RequestGroup> Group(TransactionSplit recorded)
    {
        ArgumentNullException.ThrowIfNull(recorded);

        var groups = new Dictionary<string, RequestGroup>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (SasExchange exchange in recorded.Exchanges)
        {
            if (exchange.Kind != ExchangeKind.LongPoll || exchange.Command is not { } command)
                continue;

            byte[] host = exchange.RequestBytes;
            byte[] request = RequestDataOf(host);
            string key = $"{command:X2}:{Convert.ToHexString(request)}";

            if (!groups.TryGetValue(key, out RequestGroup? group))
            {
                group = new RequestGroup(command, request, host);
                groups[key] = group;
                order.Add(key);
            }

            group.Occurrences++;
            if (exchange.Answer == ExchangeAnswer.Data && exchange.ReplyBytes.Length > 0)
            {
                byte[] reply = exchange.ReplyBytes;
                if (!group.Replies.Any(seen => seen.AsSpan().SequenceEqual(reply)))
                    group.Replies.Add(reply);
            }

            group.Answers.Add(exchange.Answer);
        }

        return [.. order.Select(key => groups[key])];
    }

    /// <summary>
    /// The request bytes a recorded host message carried, after the address and the command and
    /// before any check sequence.
    /// </summary>
    public static byte[] RequestDataOf(byte[] hostMessage)
    {
        ArgumentNullException.ThrowIfNull(hostMessage);

        if (hostMessage.Length <= 2)
            return [];

        int end = HostMessage.CarriesCrc(hostMessage.Length)
            ? hostMessage.Length - 2
            : hostMessage.Length;

        return end <= 2 ? [] : hostMessage[2..end];
    }

    /// <summary>Why this request will not be sent, or null when it will be.</summary>
    private static ReplayFinding? Refuse(RequestGroup group)
    {
        var skeleton = new ReplayFinding
        {
            Command = group.Command,
            Request = group.Request,
            Occurrences = group.Occurrences,
            RecordedAnswer = group.Answers.Count > 0 ? group.Answers[0] : ExchangeAnswer.Silent,
            RecordedReply = group.Replies.Count > 0 ? group.Replies[0] : [],
            Cause = ReplayCause.NotReplayed,
            Explanation = string.Empty,
        };

        if (HostMessage.CarriesCrc(group.HostMessage.Length) && !SasCrc.Verify(group.HostMessage))
        {
            return skeleton with
            {
                Explanation = "the recorded host message's own check sequence does not check out, "
                              + "so what it asked is not established and this build will not send "
                              + "its guess at it",
            };
        }

        SweepEntry entry = SweepPlan.For(group.Command);
        if (entry.Disposition == SweepDisposition.Withheld)
        {
            return skeleton with
            {
                Explanation = $"not sent: it {entry.Reason}",
            };
        }

        return null;
    }

    private ReplaySubject Ask(RequestGroup group)
    {
        LongPollResult live = _engine.Execute(
            group.Command, group.Request,
            purpose: $"replay ${group.Command:X2} with {group.Request.Length} request byte(s)");

        LongPollResult? again = _options.AskTwice
            ? _engine.Execute(
                group.Command, group.Request,
                purpose: $"replay ${group.Command:X2} again, to measure what moves on its own")
            : null;

        return new ReplaySubject(
            group.Command, group.Request,
            group.Answers.Count > 0 ? group.Answers[0] : ExchangeAnswer.Silent,
            group.Replies,
            live, again);
    }

    /// <summary>One distinct request in a recording, and every answer the recording holds to it.</summary>
    public sealed class RequestGroup(byte command, byte[] request, byte[] hostMessage)
    {
        public byte Command { get; } = command;

        public byte[] Request { get; } = request;

        /// <summary>The host message as recorded, so its own framing can be checked.</summary>
        public byte[] HostMessage { get; } = hostMessage;

        public int Occurrences { get; set; }

        /// <summary>The distinct replies the recording holds, in the order first seen.</summary>
        public List<byte[]> Replies { get; } = [];

        /// <summary>What kind of answer each occurrence got.</summary>
        public List<ExchangeAnswer> Answers { get; } = [];
    }
}
