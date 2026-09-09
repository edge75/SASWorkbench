using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transactions;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Replay;

/// <summary>What the classifier was given about one request.</summary>
/// <param name="Command">The long poll.</param>
/// <param name="Request">The request bytes after the address and the command.</param>
/// <param name="RecordedAnswer">What kind of answer the recording holds.</param>
/// <param name="RecordedReplies">
/// Every reply the recording holds to this exact request. More than one is a measurement in its
/// own right: a field that differs between two of them moves on the recorded machine.
/// </param>
/// <param name="Live">What the machine answered.</param>
/// <param name="LiveAgain">
/// What the machine answered when the same request was sent a second time, or null when it was not
/// asked twice. Where it is present, a field that moved between the two moves on this machine.
/// </param>
public sealed record ReplaySubject(
    byte Command,
    byte[] Request,
    ExchangeAnswer RecordedAnswer,
    IReadOnlyList<byte[]> RecordedReplies,
    LongPollResult Live,
    LongPollResult? LiveAgain);

/// <summary>
/// Decides what a difference between a recording and a machine means.
/// </summary>
/// <remarks>
/// <para>
/// The rules are applied in order and the first that fits decides, so the strongest evidence wins:
/// a field measured to move beats a field a table calls state, and a table beats nothing at all.
/// Every rule names the evidence it used, because a classification nobody can check is worth less
/// than the raw diff it replaced.
/// </para>
/// <para>
/// The last rule is the one that matters, and it is deliberately not called a defect on its own
/// authority. <b>Nothing here explains this difference</b> is what it says, and it is the only
/// outcome that makes a replay red. Everything else is a machine legitimately answering
/// differently, which the recording gives no grounds to complain about.
/// </para>
/// </remarks>
public static class ReplayDiff
{
    /// <summary>Classifies one request's difference.</summary>
    /// <param name="subject">What both machines answered.</param>
    /// <param name="occurrences">How many recorded exchanges this row stands for.</param>
    /// <param name="context">
    /// What the replay learned about the two machines from the rest of the recording. A silence on
    /// a command the library gates is only attributable to configuration where the two are
    /// measured to differ on the setting that gates it, and that measurement lives here.
    /// </param>
    public static ReplayFinding Classify(
        ReplaySubject subject, int occurrences, ReplayContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        context ??= ReplayContext.Empty;

        byte[] recorded = subject.RecordedReplies.Count > 0 ? subject.RecordedReplies[0] : [];
        byte[] live = subject.Live.Frame;

        var finding = new ReplayFinding
        {
            Command = subject.Command,
            Request = subject.Request,
            Occurrences = occurrences,
            RecordedAnswer = subject.RecordedAnswer,
            RecordedReply = recorded,
            LiveReply = live,
            LiveOutcome = subject.Live.Outcome,
            Cause = ReplayCause.Defect,
            Explanation = string.Empty,
        };

        // Rule 1 — the same bytes after the address. The echoed address is excluded on purpose:
        // the replay re-addresses every message to the machine it is talking to, so the address
        // byte differing is this tool's own doing and reporting it would be noise on every row.
        if (SameAfterAddress(recorded, live))
        {
            return finding with
            {
                Cause = ReplayCause.Agrees,
                Explanation = live.Length == 0
                    ? "neither machine answered"
                    : "the same reply, byte for byte after the echoed address",
            };
        }

        // Rule 2 — one side refused. A machine answering its own address with bit 7 set is the
        // machine saying it will not do this, which is a statement about the configuration it is
        // in rather than a fault.
        if (Refused(subject.Live.Outcome) || subject.RecordedAnswer == ExchangeAnswer.Refused)
        {
            bool live_ = Refused(subject.Live.Outcome);
            return finding with
            {
                Cause = ReplayCause.Configuration,
                Explanation = live_
                    ? "the machine refused a command the recorded machine answered, which is it "
                      + "saying it will not do this in the configuration it is in"
                    : "the machine answered a command the recorded machine refused, so the "
                      + "recorded machine was configured not to honour it",
            };
        }

        // Rule 3 — one side said nothing, and the library this machine runs has no handler for the
        // command. A command the dispatcher does not carry cannot be answered by this revision.
        if ((live.Length == 0 || recorded.Length == 0) && NoHandler(subject.Command))
        {
            return finding with
            {
                Cause = ReplayCause.Revision,
                Explanation = "one of the two answered nothing, and the machine library this "
                              + "build reads has no handler for this command at all, so a machine "
                              + "built from another revision of it may or may not answer",
            };
        }

        // Rule 4 — one side said nothing on a command the library answers only when the machine is
        // configured for it, and the two machines are measured to disagree on the very setting
        // that gates it. The library's handler returns without sending anything there, so silence
        // is the machine's configuration speaking.
        if (live.Length == 0 && ReplayGates.For(subject.Command) is { } gate)
        {
            if (context.Values(gate.Field) is { } both)
            {
                return finding with
                {
                    Cause = ReplayCause.Configuration,
                    Explanation = $"the machine answered nothing, and the library answers "
                                  + $"${subject.Command:X2} only according to {gate.Gate} — which "
                                  + $"the two machines are measured to differ on: {gate.Field} "
                                  + $"reads {both.Recorded} in the recording and {both.Live} here",
                };
            }

            // The gate was read from both machines and they agree on it, so it is ruled out
            // rather than left hanging. That is a stronger statement than having no evidence, and
            // it is where a real finding lives: a machine answering nothing where another machine
            // in the same configuration answered.
            // What the machine itself says about the setting, asked by the replay rather than
            // taken out of the recording. Where the library states the value it refuses at, a
            // machine reporting that value is behaving exactly as written — so its silence is
            // accounted for whatever the recording turns out to have been, and the difference
            // belongs to the machine that was recorded.
            if (gate.SilentWhen is { } refusesAt
                && context.OfTheMachine(gate.Field) is { Number: { } own } reported
                && own == refusesAt)
            {
                return finding with
                {
                    Cause = ReplayCause.Configuration,
                    Explanation = $"the machine answered nothing, and it says of itself that "
                                  + $"{gate.Field} is {reported.Rendered} — the value the library "
                                  + $"answers ${subject.Command:X2} with silence at. So this "
                                  + "machine did what the library says; the recorded machine was "
                                  + "in another configuration",
                };
            }

            if (context.Observed(gate.Field))
            {
                return finding with
                {
                    Explanation = $"the machine answered nothing where the recorded machine "
                                  + $"answered, and {gate.Gate} — which is what the library gates "
                                  + $"${subject.Command:X2} on — reads the same on both, so that "
                                  + "does not explain it",
                };
            }

            return finding with
            {
                Explanation = $"the machine answered nothing, and the library answers "
                              + $"${subject.Command:X2} only according to {gate.Gate}. Nothing in "
                              + $"this replay read {gate.Field} from both machines, so whether "
                              + "that is the reason cannot be said — reading it would settle this "
                              + "row",
            };
        }

        // Rule 5 — one side said nothing and the library does answer it unconditionally. That is
        // not something a configuration or a revision accounts for on the evidence here.
        if (live.Length == 0 || recorded.Length == 0)
        {
            if (live.Length == 0)
            {
                return finding with
                {
                    Explanation = "the machine answered nothing to a command the recorded machine "
                                  + "answered, and the library it runs does have a handler for "
                                  + "it: nothing here explains that",
                };
            }

            return finding with
            {
                Cause = ReplayCause.NotComparable,
                Explanation = "the recording holds no answer to this request, so there is nothing "
                              + "to compare. Silence in a file is not a machine having answered "
                              + "nothing: a filtered recorder, a poll addressed to another "
                              + "machine and a machine that truly said nothing all look the same "
                              + "in one",
            };
        }

        // Rule 6 — the lengths differ and the established layout fixes one. A reply that is the
        // length the library's own handler writes, against one that is not, is the shape of a
        // command whose reply changed between revisions.
        if (recorded.Length != live.Length
            && SasLayouts.For(subject.Command) is { FixedResponseLength: { } fixedLength })
        {
            bool liveFits = live.Length == fixedLength;
            bool recordedFits = recorded.Length == fixedLength;
            if (liveFits != recordedFits)
            {
                return finding with
                {
                    Cause = ReplayCause.Revision,
                    Explanation = $"the replies are {recorded.Length} and {live.Length} bytes, and "
                                  + $"the layout read from the machine library writes {fixedLength}"
                                  + $": the {(liveFits ? "machine" : "recording")} matches the "
                                  + "library and the other does not, which is a reply that "
                                  + "changed shape between revisions",
                };
            }
        }

        // Rule 7 — both answered with data and the layout can read both. This is where the field
        // classification runs, and where a difference stops being about the message and starts
        // being about one number in it.
        return Fields(subject, finding, recorded, live);
    }

    private static ReplayFinding Fields(
        ReplaySubject subject, ReplayFinding finding, byte[] recorded, byte[] live)
    {
        SasMessageLayout? layout = SasLayouts.For(subject.Command);
        if (layout is null)
        {
            return finding with
            {
                Explanation = $"the two replies differ and this build has no layout for ${subject.Command:X2}"
                              + ", so which field differs cannot be said and neither can what it "
                              + "means",
            };
        }

        SasDecoded left = SasMessageDecoder.DecodeResponse(layout, recorded);
        SasDecoded right = SasMessageDecoder.DecodeResponse(layout, live);
        if (!left.Succeeded || !right.Succeeded)
        {
            return finding with
            {
                Explanation = "the two replies differ and "
                              + (left.Succeeded
                                  ? $"the machine's answer does not fit the layout: {right.Failure}"
                                  : $"the recorded answer does not fit the layout: {left.Failure}"),
            };
        }

        // Measured first: a field that moved between two reads of one machine moves on its own,
        // whatever any table says about it. Only the live machine's two reads count as that
        // measurement — they are provably the same machine, seconds apart. A recording holding
        // two answers to one request is reported and deliberately not used as evidence: where
        // several captures are read together those two answers may be two machines, and this
        // once turned a serial number into a field that "moves on its own".
        IReadOnlyList<string> movedLive = Moved(layout, live, subject.LiveAgain?.Frame);
        IReadOnlyList<string> movedRecorded = MovedAcross(layout, subject.RecordedReplies);
        var moving = new HashSet<string>(movedLive, StringComparer.Ordinal);

        var differences = new List<ReplayFieldDiff>();
        foreach (SasValue value in left.Values)
        {
            SasValue? other = right[value.Path];
            if (other is null || Same(value, other))
                continue;

            FieldCause cause = CauseOf(subject.Command, value, moving.Contains(value.Path));
            differences.Add(new ReplayFieldDiff(
                value.Path, Render(value), Render(other), cause));
        }

        List<string> unclassified = [];

        differences = Reframe(layout, differences);
        unclassified = [.. differences
            .Where(d => d.Cause.Cause == ReplayCause.Defect)
            .Select(d => d.Path)];

        finding = finding with
        {
            Fields = differences,
            MovedOnTheMachine = movedLive,
            MovedInTheRecording = movedRecorded,
            Unclassified = unclassified,
        };

        if (differences.Count == 0)
        {
            // The bytes differ and no decoded field does, which means the difference is somewhere
            // the layout does not describe. Worth saying plainly rather than calling it agreement.
            return finding with
            {
                Explanation = "the replies differ in bytes the layout does not describe, so what "
                              + "differs is outside every field this build reads",
            };
        }

        ReplayCause worst = Worst(differences);
        string several = subject.RecordedReplies.Count > 1
            ? $" The recording holds {subject.RecordedReplies.Count} different answers to this "
              + "request and this compares against the first, so the row describes one of them."
            : string.Empty;

        return finding with
        {
            Cause = worst,
            Explanation = Summarise(worst, differences) + several,
        };
    }

    /// <summary>
    /// Gives a message's own framing length the cause of the field it measures.
    /// </summary>
    /// <remarks>
    /// A length byte counting the bytes that follow it differs exactly when they do, and carries
    /// no meaning of its own — so reporting it as a difference in its own right double-counts one
    /// disagreement and, worse, leaves it unexplained. The reply carrying a machine's serial
    /// number is where this showed up: the serial numbers differ, which is a machine's identity
    /// and nothing more, and the length byte in front of them differed for no other reason.
    ///
    /// A framing length is recognised from the layout rather than by its name alone: the layout
    /// must carry a field whose own length is either the rest of the message or read out of this
    /// one. A reply with no such field has no framing to explain away.
    /// </remarks>
    private static List<ReplayFieldDiff> Reframe(
        SasMessageLayout layout, List<ReplayFieldDiff> differences)
    {
        var framing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(layout.Response);

        // A reply whose total the layout cannot fix carries its own length byte, and that byte is
        // framing whatever else is in the message.
        if (layout.FixedResponseLength is null)
            framing.Add("length");

        void Collect(IReadOnlyList<SasField> fields)
        {
            foreach (SasField field in fields)
            {
                if (field.LengthFromField is { } named)
                    framing.Add(named);
                if (field.Length == SasField.Remaining)
                    framing.Add("length");
                if (field.IsGroup)
                    Collect(field.Fields);
            }
        }

        bool IsFraming(string path)
        {
            string name = path;
            int dot = name.LastIndexOf('.');
            if (dot >= 0)
                name = name[(dot + 1)..];
            return framing.Contains(name);
        }

        if (framing.Count == 0 || !differences.Any(d => IsFraming(d.Path)))
            return differences;

        List<ReplayFieldDiff> measured = [.. differences.Where(d => !IsFraming(d.Path))];
        if (measured.Count == 0)
            return differences;

        ReplayCause worst = Worst(measured);
        return
        [
            .. differences.Select(difference => IsFraming(difference.Path)
                ? difference with
                {
                    Cause = new FieldCause(worst,
                        "the message's own framing: it counts the bytes that follow it, so it "
                        + "differs exactly when they do and for the same reason"),
                }
                : difference),
        ];
    }

    /// <summary>What one field's difference is, strongest evidence first.</summary>
    private static FieldCause CauseOf(byte command, SasValue value, bool measuredMoving)
    {
        if (measuredMoving)
        {
            return new FieldCause(ReplayCause.State,
                "measured to move between two reads of the same machine", Measured: true);
        }

        if (value.Field.IsMeter || value.Field.Meter is not null)
        {
            return new FieldCause(ReplayCause.State,
                "a meter, which only ever accumulates, so two machines that have run different "
                + "games hold different values");
        }

        return ReplayFields.Explains(value.Path, command)
               ?? new FieldCause(ReplayCause.Defect,
                   "a field this build classifies neither as configuration nor as state, so "
                   + "nothing here explains it differing");
    }

    /// <summary>Which fields moved between two replies to the same request.</summary>
    private static IReadOnlyList<string> Moved(
        SasMessageLayout layout, byte[] first, byte[]? second)
    {
        if (second is null || second.Length == 0 || first.Length == 0)
            return [];

        return MovedAcross(layout, [first, second]);
    }

    private static IReadOnlyList<string> MovedAcross(
        SasMessageLayout layout, IReadOnlyList<byte[]> replies)
    {
        if (replies.Count < 2)
            return [];

        var moved = new List<string>();
        SasDecoded first = SasMessageDecoder.DecodeResponse(layout, replies[0]);
        if (!first.Succeeded)
            return [];

        foreach (SasValue value in first.Values)
        {
            for (int i = 1; i < replies.Count; i++)
            {
                SasDecoded next = SasMessageDecoder.DecodeResponse(layout, replies[i]);
                SasValue? other = next[value.Path];
                if (other is not null && !Same(value, other))
                {
                    moved.Add(value.Path);
                    break;
                }
            }
        }

        return moved;
    }

    private static string Summarise(ReplayCause cause, IReadOnlyList<ReplayFieldDiff> differences)
    {
        // For an unexplained row, name only the fields that are unexplained. Naming every field
        // that differed made a row with one unclassified name among three look three times worse
        // than it was, and pointed a reader at fields the table had in fact accounted for.
        IReadOnlyList<ReplayFieldDiff> blamed = cause == ReplayCause.Defect
            ? [.. differences.Where(d => d.Cause.Cause == ReplayCause.Defect)]
            : differences;

        string names = string.Join(", ", blamed.Select(d => d.Path).Distinct().Take(6));
        string more = blamed.Select(d => d.Path).Distinct().Count() > 6 ? ", and more" : string.Empty;

        return cause switch
        {
            ReplayCause.State =>
                $"the fields that differ move on their own — {names}{more} — so this is the same "
                + "kind of machine having done different things",
            ReplayCause.Configuration =>
                $"the fields that differ are what the machine is and how it was set up — {names}"
                + $"{more} — so this is a machine in another configuration",
            ReplayCause.Revision =>
                $"the fields that differ are only explained by another revision — {names}{more}",
            _ =>
                $"nothing here explains {names}{more} differing: a defect in one of the two "
                + "machines, or a gap in this build's classification of these fields",
        };
    }

    /// <summary>
    /// Whether two decodings of one field agree.
    /// </summary>
    /// <remarks>
    /// Compared as values and not as bytes, and the difference matters for a whole class of field.
    /// A two-bit validation style is a second reading of a status byte rather than bytes of its
    /// own, so its bytes are the whole byte it lives in: comparing those reports the style as
    /// differing whenever anything else in that byte does, which put a field reading the same
    /// value on both machines into a list of disagreements.
    /// </remarks>
    public static bool Same(SasValue left, SasValue right)
    {
        if (left.Number is { } leftNumber && right.Number is { } rightNumber)
            return leftNumber == rightNumber;

        if (left.Text is { } leftText && right.Text is { } rightText)
            return string.Equals(leftText, rightText, StringComparison.Ordinal);

        return left.Bytes.AsSpan().SequenceEqual(right.Bytes);
    }

    private static bool SameAfterAddress(byte[] recorded, byte[] live)
    {
        if (recorded.Length != live.Length)
            return false;
        if (recorded.Length == 0)
            return true;

        // The check sequence covers the address, so two replies that agree in every other byte
        // still end in different check bytes. Both are dropped from the comparison and the check
        // itself is verified elsewhere, on each frame in its own right.
        int end = HostMessage.CarriesCrc(recorded.Length) ? recorded.Length - 2 : recorded.Length;
        return recorded.AsSpan(1, end - 1).SequenceEqual(live.AsSpan(1, end - 1));
    }

    private static bool Refused(LongPollOutcome outcome) => outcome == LongPollOutcome.Rejected;

    private static bool NoHandler(byte command) =>
        !SasCatalogue.LongPolls.TryGetValue(command, out SasLongPollInfo? info)
        || info.Handler is null;

    private static string Render(SasValue value) =>
        value.Text ?? value.Number?.ToString() ?? value.ToHex();

    /// <summary>The worst field's cause decides the row, so nothing is rounded down.</summary>
    private static ReplayCause Worst(IReadOnlyList<ReplayFieldDiff> differences)
    {
        ReplayCause worst = ReplayCause.Agrees;
        foreach (ReplayFieldDiff difference in differences)
        {
            if (Rank(difference.Cause.Cause) > Rank(worst))
                worst = difference.Cause.Cause;
        }

        return worst;
    }

    private static int Rank(ReplayCause cause) => cause switch
    {
        ReplayCause.Agrees => 0,
        ReplayCause.State => 1,
        ReplayCause.Configuration => 2,
        ReplayCause.Revision => 3,
        _ => 4,
    };
}
