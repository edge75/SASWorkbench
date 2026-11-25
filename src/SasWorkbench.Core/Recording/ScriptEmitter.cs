using System.Text;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Replay;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transactions;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Recording;

/// <summary>What came out of a recording: a script, and everything the script cannot say.</summary>
/// <param name="Script">The script text, ready to run.</param>
/// <param name="Caveats">Everything the recording could not capture, each with a reason.</param>
/// <param name="Steps">How many steps were written.</param>
/// <param name="Assertions">How many of them make a claim.</param>
/// <param name="Episodes">The transactions the recording was split into.</param>
public sealed record EmittedScript(
    string Script,
    IReadOnlyList<RecordingCaveat> Caveats,
    int Steps,
    int Assertions,
    IReadOnlyList<SasEpisode> Episodes)
{
    /// <summary>Whether anything at all can be replayed from this recording.</summary>
    public bool Runnable => Steps > 0;

    public override string ToString() =>
        $"{Steps} step(s), {Assertions} assertion(s), {Episodes.Count} episode(s), "
        + $"{Caveats.Count} caveat(s)";
}

/// <summary>
/// Turns what a person did into a script that does it again, and says what it left out.
/// </summary>
/// <remarks>
/// <para>
/// The unit is the <b>episode</b>, not the packet. A session's wire is nine hundred exchanges of
/// which most are the poll loop waiting; the transactions inside it are a couple of dozen, and a
/// step per episode is the only rendering a person can check against what they remember doing. The
/// episodes come from the same splitter <c>sasctl transactions</c> uses, which means the places
/// where an episode's boundary is a guess are already named — and a step written over one of those
/// carries the same warning rather than presenting the guess as a reading.
/// </para>
/// <para>
/// Three things are refused rather than written down, because writing them would produce a script
/// that quietly does something else:
/// </para>
/// <list type="number">
/// <item>
/// <b>A stimulus nobody wired.</b> A person may press a button in a window with no backend behind
/// it. The action is kept with the refusal and no step is written: a step would open a door the
/// recorded session never opened, and every assertion after it would be about a machine that had
/// not had one opened.
/// </item>
/// <item>
/// <b>A value the machine chose.</b> A meter, a clock, a ticket's own number. Which fields are the
/// machine's settings and which are its condition is not guessed at here — it is the table the
/// replay classifier established, where a name in neither list is reported rather than assumed
/// harmless. A ticket's number is the one machine-chosen value that <em>can</em> be asserted,
/// because the language can compare a reply against the ticket the printer filed rather than
/// against a literal.
/// </item>
/// <item>
/// <b>A person's hesitation.</b> The gaps between clicks are somebody thinking. No wait is
/// written; where the person was plainly waiting for the machine, an expectation with a window is
/// written instead, and the window is this tool's number rather than theirs.
/// </item>
/// </list>
/// </remarks>
public static class ScriptEmitter
{
    /// <summary>
    /// The shortest window an emitted expectation carries.
    /// </summary>
    /// <remarks>
    /// A machine that answered in eighty milliseconds while somebody watched is not promising to
    /// answer in eighty milliseconds on a loaded box. The floor, and the rounding up below it, are
    /// this tool's numbers and the script header says so.
    /// </remarks>
    public static readonly TimeSpan ShortestWindow = TimeSpan.FromSeconds(5);

    /// <summary>How much longer than the measured time an emitted window is.</summary>
    public const double WindowFactor = 3.0;

    public static EmittedScript Emit(Recorded recorded, string name)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var caveats = new List<RecordingCaveat>();
        TransactionSplit split = TransactionSplitter.Split(
            recorded.Wire, recorded.Connect?.Address);

        var body = new List<string>();
        int assertions = 0;

        //  Whether this script prints a ticket of its own decides whether a reply about one can be
        //  asserted at all: the comparison is against the ticket the printer filed, and a script
        //  with no ticket in it has nothing to compare against.
        bool ticketIsInTheScript = recorded.Actions.Any(a =>
            a.Reproducible && a.Step is PressButtonStep { Button: "collect" } or InsertTicketStep);

        foreach (RecordedAction action in recorded.Actions)
        {
            if (action.Refused is { } refused)
            {
                caveats.Add(new RecordingCaveat(CaveatKind.UnwiredStimulus, action.What, refused));
                body.Add($"  # not recorded: {action.What} — {refused}");
                continue;
            }

            if (action.Step is null)
            {
                caveats.Add(new RecordingCaveat(
                    CaveatKind.NotExpressible, action.What,
                    RecordingCaveat.Describe(CaveatKind.NotExpressible)));
                body.Add($"  note \"{Escape(action.What)}\"");
                continue;
            }

            // The episode this action fell inside, so a step can carry what the split says about
            // it — including that its place in a transaction is a guess.
            SasEpisode? episode = EpisodeAt(split, action.At);
            if (episode is { Ambiguity: { } ambiguity })
            {
                caveats.Add(new RecordingCaveat(
                    CaveatKind.AmbiguousEpisode,
                    $"{action.What}, inside {episode.Rule.Name}",
                    ambiguity));
                body.Add($"  # {episode.Rule.Name}: {ambiguity}");
            }
            else if (episode is not null && Opens(split, episode, action.At))
            {
                string key = episode.Key.Length == 0 ? string.Empty : $" {episode.Key}";
                body.Add($"  # {episode.Rule.Name}{key}");
            }

            body.Add("  " + Line(action.Step));

            foreach (string claim in After(action, recorded, caveats, ticketIsInTheScript))
            {
                body.Add("  " + claim);
                assertions++;
            }
        }

        foreach (RecordedAction gap in Hesitations(recorded))
        {
            caveats.Add(new RecordingCaveat(
                CaveatKind.HumanHesitation,
                $"{gap.At.TotalSeconds:F1}s before {gap.What}",
                RecordingCaveat.Describe(CaveatKind.HumanHesitation)));
        }

        //  One line per distinct thing, with the values it was seen holding. Eleven copies of one
        //  field is a report nobody reads, and it hides the other ten things beside it.
        List<RecordingCaveat> distinct = Distinct(caveats);

        var text = new StringBuilder();
        Header(text, recorded, split, distinct, name);
        text.AppendLine($"test \"{Escape(name)}\" {{");
        foreach (string line in Requirements(recorded))
            text.AppendLine("  " + line);
        if (Requirements(recorded).Count > 0)
            text.AppendLine();
        foreach (string line in body)
            text.AppendLine(line);
        text.AppendLine("}");

        return new EmittedScript(
            text.ToString(),
            distinct,
            body.Count(line => !line.TrimStart().StartsWith('#')),
            assertions,
            split.Episodes);
    }

    /// <summary>
    /// The claims worth making about what the machine answered one action with.
    /// </summary>
    /// <remarks>
    /// This is where the recording decides what it may honestly say. A field the replay
    /// classifier calls a configuration is what the machine <em>is</em>, so a literal holds on any
    /// machine set up the same way and a script may assert it. A field it calls state is what the
    /// machine is <em>doing</em>, and a literal for one would pass here and fail on the next run
    /// of the same script against the same machine. A name it classifies as neither is left alone
    /// and reported, because assuming a name harmless is how a recorder starts inventing
    /// expectations.
    /// </remarks>
    private static List<string> After(
        RecordedAction action,
        Recorded recorded,
        List<RecordingCaveat> caveats,
        bool ticketIsInTheScript)
    {
        var claims = new List<string>();

        if (action.Step is StimulusStep stimulus)
        {
            //  The exceptions that arrived after a stimulus are the machine's answer to it, and
            //  the one thing about them a script can hold: this happened, within a window.
            foreach (byte code in Following(recorded, action))
            {
                claims.Add(
                    $"host expect exception {SasNames.ExceptionName(code)} "
                    + $"within {Window(recorded, action, code)}");
            }

            if (stimulus is PressButtonStep { Button: "collect" })
                claims.Add($"egm expect ticket printed within {Format(TimeSpan.FromSeconds(20))}");

            return claims;
        }

        if (action.Step is not SendLongPollStep send)
            return claims;

        SasMessageLayout? layout = SasLayouts.For(send.Command);
        byte[]? reply = ReplyTo(recorded, action, send.Command);
        if (layout is null || reply is null)
            return claims;

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, reply);
        if (!decoded.Succeeded)
            return claims;

        foreach (SasValue value in decoded.Values)
        {
            //  The ticket's own number is the one value the machine chooses that a script can
            //  still pin down, because the language compares a reply against the ticket the
            //  printer filed rather than against a number nobody could know in advance.
            if (Ticketed(send.Command, value.Path) is { } ticketField)
            {
                if (ticketIsInTheScript)
                {
                    claims.Add($"host expect field {value.Path} of lp {send.Command:X2} "
                               + $"== ticket.{ticketField}");
                }
                else
                {
                    //  The comparison is against the ticket the printer filed, and this script
                    //  prints none: whatever printed the recorded one was not something the
                    //  recording could capture. Asserting the number it happened to hold instead
                    //  would be asserting a number nobody chose.
                    caveats.Add(new RecordingCaveat(
                        CaveatKind.MachineChosenValue,
                        $"{value.Path} of lp ${send.Command:X2} = {Literal(value)}",
                        "this is a ticket the machine numbered, and this script prints no ticket "
                        + "to compare it against, because whatever printed the recorded one is "
                        + "not something a recording captures"));
                }

                continue;
            }

            //  A field the protocol reserves is one the machine may leave as it likes - the
            //  classifier says so in as many words - so it is the one configuration field that
            //  must not be asserted. Nothing else is excluded by name.
            if (value.Path.Contains("reserved", StringComparison.Ordinal))
                continue;

            FieldCause? cause = ReplayFields.Explains(value.Path, send.Command);
            if (cause is { Cause: ReplayCause.Configuration })
            {
                claims.Add(
                    $"host expect field {value.Path} of lp {send.Command:X2} == {Literal(value)}");
                continue;
            }

            caveats.Add(new RecordingCaveat(
                CaveatKind.MachineChosenValue,
                $"{value.Path} of lp ${send.Command:X2} = {Literal(value)}",
                cause is { } known
                    ? known.Why
                    : "this build classifies this field as neither the machine's settings nor its "
                      + "condition, so nothing here can say whether a literal would hold"));
        }

        return claims;
    }

    /// <summary>
    /// Which field of the last printed ticket a reply's field can be compared against.
    /// </summary>
    /// <remarks>
    /// Only the cashout ticket information reply, and only its two fields. Stated rather than
    /// derived because the correspondence is a judgement about what a ticket is, the same
    /// judgement the episode rules make.
    /// </remarks>
    private static string? Ticketed(byte command, string path) =>
        command == (byte)SasLongPollCode.LP_SENDCASHOUTTICKETINFO
            ? path switch
            {
                "validation_number" => "validation_number",
                "ticket_amount" => "amount_cents",
                _ => null,
            }
            : null;

    /// <summary>
    /// The exceptions one stimulus caused, worked out from what the stimulus was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not from the timing. A machine reports one exception per poll, so what a stimulus caused
    /// arrives whole poll cycles after it, by which time a person driving by hand has clicked two
    /// or three more things — and a window measured between one click and the next attributes a
    /// door's exception to whatever was clicked after the door. That was the first version of
    /// this, and it wrote scripts that asserted nothing at all about the doors in them.
    /// </para>
    /// <para>
    /// Which exception a door or a note produces is read out of the gaming machine library's own
    /// symbol names, through the same derived tables the transaction splitter uses. So a code
    /// added to the library is attributed correctly here without anybody noticing, and a stimulus
    /// this build cannot map falls back to the time window with the reason said out loud.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<byte> Following(Recorded recorded, RecordedAction action)
    {
        IReadOnlyList<(TimeSpan At, SasChannel Channel, byte Code)> after =
        [
            .. recorded.Exceptions.Where(e => e.At >= action.At && e.Channel == action.Channel),
        ];

        return action.Step switch
        {
            DoorStep door =>
            [
                .. First(after, code =>
                    EpisodeRules.DoorOf(code) == door.Door
                    && (door.Open ? EpisodeRules.DoorsOpened : EpisodeRules.DoorsClosed)
                        .Contains(code)),
            ],

            InsertBillStep => [.. First(after, EpisodeRules.NoteExceptions.Contains)],

            PressButtonStep { Button: "collect" } =>
                [.. First(after, code => code == (byte)SasExceptionCode.CashoutTicketPrinted)],

            InsertTicketStep =>
                [.. First(after, code => code == (byte)SasExceptionCode.TicketInserted)],

            //  Anything else: the first exception after it and before the next action, which is
            //  adjacency and therefore a guess wherever two things happened close together.
            _ => Adjacent(recorded, action),
        };
    }

    /// <summary>The first exception matching a rule, or none.</summary>
    private static IEnumerable<byte> First(
        IReadOnlyList<(TimeSpan At, SasChannel Channel, byte Code)> exceptions,
        Func<byte, bool> matches) =>
        exceptions.Where(e => matches(e.Code)).Select(e => e.Code).Take(1);

    private static IReadOnlyList<byte> Adjacent(Recorded recorded, RecordedAction action)
    {
        TimeSpan until = recorded.Actions
            .Where(later => later.At > action.At)
            .Select(later => later.At)
            .DefaultIfEmpty(recorded.To)
            .First();

        return
        [
            .. recorded.Exceptions
                .Where(e => e.At > action.At && e.At <= until && e.Channel == action.Channel)
                .Select(e => e.Code)
                .Distinct(),
        ];
    }

    /// <summary>
    /// The window an expectation carries: what was measured, tripled, with a floor.
    /// </summary>
    /// <remarks>
    /// The number is this tool's and not the person's, and the script header says so. A recorded
    /// latency is a fact about one run on one box; a script that carried it as a deadline would
    /// fail on a slower one and prove nothing about the machine.
    /// </remarks>
    private static string Window(Recorded recorded, RecordedAction action, byte code)
    {
        TimeSpan measured = recorded.Exceptions
            .Where(e => e.At >= action.At && e.Code == code)
            .Select(e => e.At - action.At)
            .DefaultIfEmpty(ShortestWindow)
            .First();

        var scaled = TimeSpan.FromSeconds(Math.Ceiling(measured.TotalSeconds * WindowFactor));
        return Format(scaled < ShortestWindow ? ShortestWindow : scaled);
    }

    /// <summary>The reply the machine sent to one long poll, when it sent one.</summary>
    private static byte[]? ReplyTo(Recorded recorded, RecordedAction action, byte command)
    {
        foreach (Diagnostics.WireRecord record in recorded.Wire)
        {
            if (record.Elapsed < action.At
                || record.Direction != Diagnostics.WireDirection.Receive
                || record.Channel != action.Channel)
            {
                continue;
            }

            byte[] frame = [.. record.Symbols.Select(s => s.Data)];
            if (frame.Length > 2 && frame[1] == command)
                return frame;
        }

        return null;
    }

    /// <summary>The gaps between actions that were a person thinking rather than a machine working.</summary>
    /// <remarks>
    /// A gap is a hesitation when nothing arrived from the machine during it. Where something did,
    /// the person was waiting for the machine and the expectation written for it carries the wait.
    /// </remarks>
    private static IReadOnlyList<RecordedAction> Hesitations(Recorded recorded)
    {
        var found = new List<RecordedAction>();
        for (int index = 1; index < recorded.Actions.Count; index++)
        {
            RecordedAction previous = recorded.Actions[index - 1];
            RecordedAction current = recorded.Actions[index];
            TimeSpan gap = current.At - previous.At;
            if (gap < TimeSpan.FromSeconds(2))
                continue;

            bool machineWasBusy = recorded.Exceptions.Any(
                e => e.At > previous.At && e.At < current.At);

            if (!machineWasBusy)
                found.Add(current with { At = gap });
        }

        return found;
    }

    private static SasEpisode? EpisodeAt(TransactionSplit split, TimeSpan at) =>
        split.Episodes.FirstOrDefault(episode => episode.At <= at && at <= episode.Until);

    private static bool Opens(TransactionSplit split, SasEpisode episode, TimeSpan at) =>
        split.Episodes.All(other => other == episode || other.At > at || other.Until < at)
        && episode.Exchanges.Count > 0
        && at <= episode.Exchanges[0].At + TimeSpan.FromMilliseconds(500);

    private static string Line(ScriptStep step) => step switch
    {
        ConnectStep connect => connect.Channel == SasChannel.One
            ? $"host connect {connect.Transport.ToString().ToLowerInvariant()}"
              + $"{Target(connect)} address {connect.Address}"
            : $"host connect {connect.Transport.ToString().ToLowerInvariant()}"
              + $"{Target(connect)} address {connect.Address} channel {(int)connect.Channel}",
        UseChannelStep use => $"host channel {(int)use.Channel}",
        PollEveryStep poll => $"host poll every {Format(poll.Interval)}",
        SendLongPollStep send => send.Data.Length == 0
            ? $"host send lp {send.Command:X2}"
            : $"host send lp {send.Command:X2} data {Convert.ToHexString(send.Data)}",
        InsertBillStep bill => $"egm bill insert {bill.Cents}",
        PressButtonStep press => $"egm press {press.Button}",
        DoorStep door => $"egm door {door.Door} {(door.Open ? "open" : "close")}",
        SettleStep => "egm settle",
        HandpayStep handpay => handpay.Kind == HandpayKind.Jackpot
            ? $"egm handpay jackpot {handpay.AmountCents}"
            : $"egm handpay {handpay.AmountCents}",
        HandpayResetStep => "egm handpay reset",
        PowerCycleStep { Resynchronise: false } => "egm power cycle without sync",
        PowerCycleStep => "egm power cycle",
        NvramClearStep => "egm nvram clear",
        MemoryFaultStep fault =>
            $"egm memory fault {MemoryFaultStep.Words.First(w => w.Value == fault.Kind).Key}",
        InsertTicketStep ticket =>
            $"egm ticket insert amount={ticket.Ticket.AmountCents} "
            + $"validation={ticket.Ticket.ValidationNumber}",
        PlayGamesStep play => $"egm play games={play.Games} bet={play.BetCredits}",
        ForgetExceptionsStep => "host forget exceptions",
        NoteStep note => $"note \"{Escape(note.Text)}\"",
        _ => $"note \"{Escape(step.Describe())}\"",
    };

    private static string Target(ConnectStep connect) =>
        connect.Target.Length == 0 ? string.Empty : $" {connect.Target}";

    /// <summary>What a case needs, taken from the steps it actually contains.</summary>
    private static List<string> Requirements(Recorded recorded)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (ScriptStep? step in recorded.Actions.Select(a => a.Step))
        {
            switch (step)
            {
                case StimulusStep stimulus:
                    keys.Add(stimulus.RequiredCapability);
                    break;
                case SendLongPollStep send when SasLayouts.For(send.Command) is not null:
                    keys.Add($"decoder.lp{send.Command:X2}");
                    break;
            }

            //  A cashout is asserted against the ticket the printer filed, so the case needs the
            //  directory all three programs share. Without it the assertion fails rather than
            //  skipping, which would report a missing directory as a machine that printed nothing.
            if (step is PressButtonStep { Button: "collect" })
            {
                keys.Add("printer.wallet");
            }
        }

        var lines = new List<string>();
        if (recorded.Actions.Any(a => a.Step is StimulusStep))
            lines.Add("tag @needs-stimulus");

        lines.AddRange(keys.Select(key => $"requires {key}"));
        return lines;
    }

    private static void Header(
        StringBuilder text,
        Recorded recorded,
        TransactionSplit split,
        IReadOnlyList<RecordingCaveat> caveats,
        string name)
    {
        text.AppendLine($"# Recorded by hand: {name}.");
        text.AppendLine("#");
        text.AppendLine(
            $"# {recorded.Actions.Count} action(s) over {recorded.Duration.TotalSeconds:F1}s, "
            + $"split into {split.Episodes.Count} transaction(s) and "
            + $"{split.Exchanges.Count} exchange(s).");
        text.AppendLine("#");
        text.AppendLine(
            "# Every 'within' below is this tool's number and not the person's: what the machine");
        text.AppendLine(
            $"# was measured taking, multiplied by {WindowFactor:F0} and floored at "
            + $"{ShortestWindow.TotalSeconds:F0}s. The gaps");
        text.AppendLine(
            "# between what a person clicks are that person thinking, and none of them is written");
        text.AppendLine("# down as a wait.");

        if (caveats.Count == 0)
        {
            text.AppendLine("#");
            text.AppendLine("# Nothing in this session was left out.");
            text.AppendLine();
            return;
        }

        text.AppendLine("#");
        text.AppendLine("# What this recording could not capture:");
        foreach (CaveatKind kind in caveats.Select(c => c.Kind).Distinct())
        {
            text.AppendLine($"#");
            text.AppendLine($"#   {RecordingCaveat.Describe(kind)}");
            foreach (RecordingCaveat caveat in caveats.Where(c => c.Kind == kind).Take(12))
                text.AppendLine($"#     - {caveat.What}");

            int more = caveats.Count(c => c.Kind == kind) - 12;
            if (more > 0)
                text.AppendLine($"#     - and {more} more");
        }

        text.AppendLine();
    }

    /// <summary>
    /// One caveat per distinct thing, with the values it was seen holding.
    /// </summary>
    /// <remarks>
    /// A session that read one meter forty times produced forty identical lines, which buried the
    /// three that mattered. The values are kept because "this field held 0, then 20, then 30" is
    /// the evidence that it is the machine's condition and not its configuration.
    /// </remarks>
    private static List<RecordingCaveat> Distinct(List<RecordingCaveat> caveats)
    {
        var kept = new List<RecordingCaveat>();
        var seen = new Dictionary<(CaveatKind, string), List<string>>();

        foreach (RecordingCaveat caveat in caveats)
        {
            string subject = caveat.What;
            string? value = null;
            int equals = subject.LastIndexOf(" = ", StringComparison.Ordinal);
            if (equals > 0)
            {
                value = subject[(equals + 3)..];
                subject = subject[..equals];
            }

            var key = (caveat.Kind, subject);
            if (seen.TryGetValue(key, out List<string>? values))
            {
                if (value is not null && !values.Contains(value))
                    values.Add(value);
                continue;
            }

            seen[key] = value is null ? [] : [value];
            kept.Add(caveat with { What = subject });
        }

        return
        [
            .. kept.Select(caveat => seen[(caveat.Kind, caveat.What)] is { Count: > 0 } values
                ? caveat with { What = $"{caveat.What} = {string.Join(", ", values.Take(6))}" }
                : caveat),
        ];
    }

    private static string Literal(SasValue value) =>
        value.Text is { } text ? text : value.Number?.ToString() ?? value.ToHex();

    private static string Format(TimeSpan span) =>
        span.TotalMilliseconds < 1000
            ? $"{span.TotalMilliseconds:F0}ms"
            : $"{span.TotalSeconds:0.##}s";

    private static string Escape(string text) => text.Replace("\"", "'");
}
