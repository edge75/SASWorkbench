using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Transactions;

/// <summary>One place the split could not be decided from the bytes, and what was done instead.</summary>
/// <param name="At">When, on the record's own clock.</param>
/// <param name="Sequence">Which record in the log.</param>
/// <param name="What">What was ambiguous, in one sentence.</param>
/// <param name="Taken">What this build did about it, so a reader can disagree with it.</param>
public sealed record SplitAmbiguity(TimeSpan At, long Sequence, string What, string Taken)
{
    public override string ToString() =>
        $"[{At.TotalMilliseconds,9:F1}ms] #{Sequence}  {What}  — {Taken}";
}

/// <summary>What splitting a capture came to.</summary>
public sealed record TransactionSplit
{
    /// <summary>The exchanges, in order.</summary>
    public required IReadOnlyList<SasExchange> Exchanges { get; init; }

    /// <summary>The episodes, in the order they opened.</summary>
    public required IReadOnlyList<SasEpisode> Episodes { get; init; }

    /// <summary>Every place the split was not decidable from the bytes.</summary>
    public IReadOnlyList<SplitAmbiguity> Ambiguities { get; init; } = [];

    /// <summary>The first machine address the record showed, which is what a one-byte answer is read against.</summary>
    public SasAddress? Address { get; init; }

    /// <summary>
    /// Every machine address the host polled, in the order first seen.
    /// </summary>
    /// <remarks>
    /// More than one is not a curiosity. The conformance toolkit's own traces poll a machine at
    /// $7F that never answers and one at $01 that does, so a reader who assumed one machine per
    /// recording would read the unanswered polls as a machine ignoring its host. A replay has to
    /// know, because it can only send to the one machine in front of it.
    /// </remarks>
    public IReadOnlyList<SasAddress> Addresses { get; init; } = [];

    /// <summary>How many records went into the split.</summary>
    public required int Records { get; init; }

    /// <summary>
    /// Exchanges that are a general poll answered with nothing queued: the idle loop.
    /// </summary>
    /// <remarks>
    /// Counted rather than listed. A two-hundred-millisecond poll loop is five of these a second
    /// and they are the overwhelming majority of any recording — the legacy tester's large capture
    /// is over a million of them — so printing them buries everything that happened. They are
    /// still exchanges and still in <see cref="Exchanges"/>; this is the number that says how much
    /// of the session was the host waiting.
    /// </remarks>
    public int IdlePolls => Exchanges.Count(e =>
        e.Kind == ExchangeKind.GeneralPoll
        && e.Answer is ExchangeAnswer.NoException or ExchangeAnswer.Silent);

    /// <summary>Exchanges belonging to no episode this build recognises.</summary>
    public int OutsideAnyEpisode
    {
        get
        {
            var inside = Episodes.SelectMany(e => e.Exchanges).Select(e => e.Index).ToHashSet();
            return Exchanges.Count(e => !inside.Contains(e.Index));
        }
    }
}

/// <summary>
/// Splits a flat list of packets into the exchanges and the episodes inside it.
/// </summary>
/// <remarks>
/// <para>
/// A capture is a list of packets and the interesting unit is never the packet. Two levels are
/// produced because two different questions are being asked, and conflating them is what makes a
/// session log unreadable:
/// </para>
/// <list type="number">
/// <item>
/// <b>The exchange</b> — one host message and the machine messages that answer it. This level is
/// mechanical: the protocol says what answers what, so nothing here is a judgement. What it costs
/// is the discipline of attributing by what a message says rather than by where it sits.
/// </item>
/// <item>
/// <b>The episode</b> — a ticket printed and read, a note taken and metered, a handpay raised and
/// reset. This level is a judgement about what a person means by "a transaction", and it is
/// stated in <see cref="EpisodeRules"/> where it can be argued with rather than buried here.
/// </item>
/// </list>
/// <para>
/// Everywhere the split cannot be decided from the bytes, it says so and says what it did instead.
/// That is the whole point of the second level: the note episode has nothing in its replies naming
/// which note they are about, so two notes taken seconds apart genuinely cannot be told apart, and
/// a splitter that presented an adjacency guess as a reading would be inventing the very
/// transactions it was asked to find.
/// </para>
/// </remarks>
public static class TransactionSplitter
{
    /// <summary>Splits a recorded session.</summary>
    /// <param name="records">The packets, in the order they were recorded.</param>
    /// <param name="address">
    /// The machine's poll address, when the caller knows it. Left out it is learned from the
    /// host's own polls, and until something shows it a one-byte answer to a long poll has two
    /// readings and is reported as having two.
    /// </param>
    public static TransactionSplit Split(
        IReadOnlyList<WireRecord> records, SasAddress? address = null)
    {
        ArgumentNullException.ThrowIfNull(records);

        var state = new SplitState(address);
        foreach (WireRecord record in records)
        {
            if (record.Direction == WireDirection.Transmit)
                state.Host(record);
            else
                state.Machine(record);
        }

        state.Finish();
        IReadOnlyList<SasExchange> exchanges = state.Exchanges;

        var episodes = new EpisodeBuilder(exchanges, state.Ambiguities);
        return new TransactionSplit
        {
            Exchanges = exchanges,
            Episodes = episodes.Build(),
            Ambiguities = state.Ambiguities,
            Address = state.Address,
            Addresses = state.Addresses,
            Records = records.Count,
        };
    }

    /// <summary>Builds the level-one exchanges, one record at a time.</summary>
    private sealed class SplitState(SasAddress? address)
    {
        private readonly List<SasExchange> _exchanges = [];
        private readonly List<WireRecord> _replies = [];

        // Machine messages that answer nothing, held until the exchange they arrived during has
        // been closed. Without that they would be numbered before the poll they followed, and a
        // reader would see an answer listed above the question.
        private readonly List<SasExchange> _unattributed = [];
        private WireRecord? _request;
        private ExchangeKind _kind;
        private byte? _command;
        private int _retries;
        private ExchangeAnswer _answer;
        private byte? _exception;
        private string? _ambiguity;

        public SasAddress? Address { get; private set; } = address;

        /// <summary>Every machine address a host message here was addressed to.</summary>
        public List<SasAddress> Addresses { get; } = [];

        /// <summary>The address of the message being read now, which need not be the first one.</summary>
        private SasAddress? _polled;

        public List<SplitAmbiguity> Ambiguities { get; } = [];

        public IReadOnlyList<SasExchange> Exchanges => _exchanges;

        public void Host(WireRecord record)
        {
            byte[] bytes = [.. record.Symbols.Select(s => s.Data)];

            // A repeated long poll that has not been answered is the protocol's own retry — the
            // repeat means "send it again" — so it stays inside the exchange it is retrying and
            // only the count moves. A repeated general poll is not a retry of anything: it is the
            // poll loop, and each turn of it is its own exchange.
            if (_request is not null && _kind == ExchangeKind.LongPoll
                && _answer == ExchangeAnswer.Silent
                && Same(bytes, [.. _request.Symbols.Select(s => s.Data)]))
            {
                _retries++;
                return;
            }

            Close();
            _request = record;
            _replies.Clear();
            _retries = 0;
            _answer = ExchangeAnswer.Silent;
            _exception = null;
            _ambiguity = null;

            if (bytes.Length == 0)
            {
                _kind = ExchangeKind.Orphan;
                _command = null;
                _ambiguity = "nothing was recorded for this host message, so what it asked is not "
                             + "in the file";
                Note(record, _ambiguity, "kept as an exchange that asked nothing");
                return;
            }

            if (bytes.Length == 1)
            {
                byte value = bytes[0];
                _command = null;
                if ((value & HostMessage.GeneralPollFlag) == 0)
                {
                    _kind = ExchangeKind.Orphan;
                    _ambiguity = $"one host symbol ${value:X2} with the general-poll bit clear, "
                                 + "which is no host message this build writes";
                    Note(record, _ambiguity, "kept as an exchange whose question cannot be read");
                    return;
                }

                var polled = SasAddress.Of((byte)(value & ~HostMessage.GeneralPollFlag));
                if (polled.IsSync)
                {
                    _kind = ExchangeKind.SyncPoll;
                    _polled = polled;
                    return;
                }

                _kind = ExchangeKind.GeneralPoll;
                _polled = polled;
                Seen(polled);
                Address ??= polled;
                return;
            }

            _kind = ExchangeKind.LongPoll;
            _command = bytes[1];
            _polled = bytes[0] <= SasAddress.MaxValue ? SasAddress.Of(bytes[0]) : null;
            if (bytes[0] is not 0 and <= SasAddress.MaxValue)
            {
                Seen(SasAddress.Of(bytes[0]));
                Address ??= SasAddress.Of(bytes[0]);
            }

            if (HostMessage.CarriesCrc(bytes.Length) && !SasCrc.Verify(bytes))
            {
                _ambiguity = "the host message's own check sequence does not check out, so which "
                             + "command it carried is not established";
                Note(record, _ambiguity,
                    "the second byte is still reported as the command, and the exchange is marked");
            }
        }

        public void Machine(WireRecord record)
        {
            byte[] bytes = [.. record.Symbols.Select(s => s.Data)];

            if (_request is null)
            {
                // A recording that begins mid-exchange starts with an answer to a poll nobody
                // kept. Reported as answering nothing rather than attached to the next poll,
                // which it cannot be answering.
                _exchanges.Add(new SasExchange
                {
                    Index = _exchanges.Count,
                    Kind = ExchangeKind.Orphan,
                    Request = record,
                    Address = Address,
                    Answer = Read(bytes, ExchangeKind.Orphan, out byte? code, out _),
                    Replies = [record],
                    Exception = code,
                    Ambiguity = "a machine message with no host message before it in the record",
                });

                Note(record, "a machine message with no host message before it",
                    "reported as answering nothing; the recording begins inside an exchange");
                return;
            }

            bool alreadyAnswered = _replies.Count > 0 && _answer != ExchangeAnswer.NotAMessage;
            ExchangeAnswer reading = Read(bytes, _kind, out byte? exception, out string? why);

            if (reading == ExchangeAnswer.Data && bytes[1] != _command)
            {
                // Attributed by what it says: this echoes another command, so it is not this
                // poll's answer and this poll goes down as unanswered.
                _unattributed.Add(new SasExchange
                {
                    Index = 0,
                    Kind = ExchangeKind.Orphan,
                    Request = record,
                    Address = Address,
                    Answer = reading,
                    Replies = [record],
                    Ambiguity = $"echoes long poll ${bytes[1]:X2} and the poll before it was "
                                + $"${_command ?? 0:X2}",
                });

                Note(record,
                    $"a reply echoing ${bytes[1]:X2} arrived in the window of ${_command ?? 0:X2}",
                    "attributed to neither: a reply is placed by the command it echoes, so this "
                    + "answers nothing here and the poll before it is unanswered");
                return;
            }

            _replies.Add(record);
            if (alreadyAnswered)
            {
                _ambiguity = "more than one machine message in this exchange's window";
                Note(record, "a second machine message in one exchange's window",
                    "kept with the exchange; a reply printed in pieces and two replies look the "
                    + "same in a file that does not say");
            }

            if (why is not null)
            {
                _ambiguity = why;
                Note(record, why, "the reading is not chosen");
            }

            if (reading == ExchangeAnswer.NotAMessage)
            {
                _answer = reading;
                _ambiguity ??= "the bytes that came back do not stand as a message, so which poll "
                               + "they belong to is decided by position and not by what they say";
                Note(record, "a run of bytes that does not stand as a message",
                    "placed with the poll it followed, and marked as placed by position");
                return;
            }

            _answer = reading;
            _exception ??= exception;
        }

        public void Finish() => Close();

        private void Close()
        {
            if (_request is null)
            {
                Flush();
                return;
            }

            _exchanges.Add(new SasExchange
            {
                Index = _exchanges.Count,
                Kind = _kind,
                Request = _request,
                Command = _command,
                Address = _polled,
                Answer = _answer,
                Replies = [.. _replies],
                Retries = _retries,
                Exception = _exception,
                Ambiguity = _ambiguity,
            });

            _request = null;
            _replies.Clear();
            Flush();
        }

        private void Flush()
        {
            foreach (SasExchange orphan in _unattributed)
                _exchanges.Add(orphan with { Index = _exchanges.Count });

            _unattributed.Clear();
        }

        private void Seen(SasAddress polled)
        {
            if (!Addresses.Contains(polled))
                Addresses.Add(polled);
        }

        private void Note(WireRecord record, string what, string taken) =>
            Ambiguities.Add(new SplitAmbiguity(record.Elapsed, record.Sequence, what, taken));

        /// <summary>
        /// Reads what a machine message is, in the context of the poll it may be answering.
        /// </summary>
        private ExchangeAnswer Read(
            byte[] bytes, ExchangeKind kind, out byte? exception, out string? why)
        {
            exception = null;
            why = null;

            if (bytes.Length == 0)
                return ExchangeAnswer.NotAMessage;

            if (bytes.Length == 1)
            {
                byte value = bytes[0];
                if (value == 0x00)
                    return ExchangeAnswer.NoException;

                if (kind == ExchangeKind.GeneralPoll)
                {
                    // After a general poll there is only one reading: a machine answers with the
                    // next exception it has queued.
                    exception = value;
                    return ExchangeAnswer.Exception;
                }

                if (Address is { } known)
                {
                    if (value == known.Value)
                        return ExchangeAnswer.Acknowledged;
                    if (value == (byte)(known.Value | HostMessage.GeneralPollFlag))
                        return ExchangeAnswer.Refused;

                    exception = value;
                    return ExchangeAnswer.Exception;
                }

                why = $"one byte ${value:X2}: either exception ${value:X2} or an acknowledgement "
                      + $"from a machine at address ${value:X2}, and no poll in this record shows "
                      + "which address is being polled";
                return ExchangeAnswer.Ambiguous;
            }

            if (bytes.Length == 2 && bytes[1] == HostMessage.BusyResponseByte
                && (Address is not { } busyAt || bytes[0] == busyAt.Value))
            {
                return ExchangeAnswer.Busy;
            }

            if (HostMessage.CarriesCrc(bytes.Length) && !SasCrc.Verify(bytes))
                return ExchangeAnswer.NotAMessage;

            if (bytes.Length < 4)
                return ExchangeAnswer.NotAMessage;

            if (bytes[1] == SasEventLayouts.Marker)
            {
                exception = bytes.Length > 2 && bytes[2] != 0 ? bytes[2] : null;
                return ExchangeAnswer.RealTimeEvent;
            }

            if (kind == ExchangeKind.GeneralPoll)
            {
                why = $"a {bytes.Length}-byte machine message answered a general poll and does not "
                      + "carry the real-time event marker, so it is neither an exception nor an "
                      + "event";
                return ExchangeAnswer.Data;
            }

            return ExchangeAnswer.Data;
        }

        private static bool Same(byte[] left, byte[] right) => left.AsSpan().SequenceEqual(right);
    }

    /// <summary>Groups the exchanges into episodes, by the rules that are stated rather than here.</summary>
    private sealed class EpisodeBuilder(
        IReadOnlyList<SasExchange> exchanges, List<SplitAmbiguity> ambiguities)
    {
        private readonly List<SasEpisode> _closed = [];
        private readonly List<Open> _open = [];

        public IReadOnlyList<SasEpisode> Build()
        {
            foreach (SasExchange exchange in exchanges)
            {
                if (exchange.Exception is { } code)
                    OnException(exchange, code);

                if (exchange.Command is { } command)
                    OnCommand(exchange, command);
            }

            foreach (Open open in _open)
                _closed.Add(open.ToEpisode(_closed.Count));

            return [.. _closed.OrderBy(e => e.At).Select((e, i) => e with { Index = i })];
        }

        private void OnException(SasExchange exchange, byte code)
        {
            foreach (EpisodeRule rule in EpisodeRules.All)
            {
                if (rule.Closes.Contains(code))
                {
                    Open? match = Find(rule, code);
                    if (match is not null)
                    {
                        match.Exchanges.Add(exchange);
                        match.ClosedBy = code;
                        match.EndReason = Ends.Reported;
                        _open.Remove(match);
                        _closed.Add(match.ToEpisode(_closed.Count));
                        continue;
                    }

                    ambiguities.Add(new SplitAmbiguity(exchange.At, exchange.Request.Sequence,
                        $"exception ${code:X2} ends a {rule.Name} episode and none was open",
                        "an episode of its own is opened and closed at once, because the record "
                        + "begins after this one started"));

                    var orphan = new Open(rule, code) { ClosedBy = code, EndReason = Ends.Reported };
                    orphan.Exchanges.Add(exchange);
                    orphan.Ambiguity = "the exception that opened this episode is not in the "
                                       + "record, so where it began is not known";
                    _closed.Add(orphan.ToEpisode(_closed.Count));
                }

                if (!rule.Opens.Contains(code))
                    continue;

                var opened = new Open(rule, code);
                opened.Exchanges.Add(exchange);
                if (rule.Kind == EpisodeKind.Door && EpisodeRules.DoorOf(code) is { } door)
                    opened.Key = door;

                // Two open at once, with nothing in the bytes to tell them apart, is the honest
                // limit of this level. Said here rather than resolved by picking one.
                if (!rule.KeyedByBytes && _open.Any(o => o.Rule.Kind == rule.Kind && o.Key.Length == 0))
                {
                    opened.Ambiguity =
                        $"a second {rule.Name} episode opened while one was still open, and "
                        + "nothing in these replies says which of the two any of them is about";
                    ambiguities.Add(new SplitAmbiguity(exchange.At, exchange.Request.Sequence,
                        $"two {rule.Name} episodes open at once and this kind has no key in the "
                        + "bytes",
                        "later exchanges go to the one opened most recently, which is a guess and "
                        + "is marked as one on both episodes"));

                    foreach (Open other in _open.Where(o => o.Rule.Kind == rule.Kind))
                        other.Ambiguity ??= opened.Ambiguity;
                }

                _open.Add(opened);
            }
        }

        private void OnCommand(SasExchange exchange, byte command)
        {
            // Most recently opened first: where two episodes of one kind are open the newer one is
            // the one being worked on, and the guess is already recorded as a guess.
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                Open open = _open[i];
                if (!open.Rule.Commands.Contains(command))
                    continue;

                open.Exchanges.Add(exchange);
                if (open.Key.Length == 0 && open.Rule.KeyField is { } field)
                    open.Key = KeyFrom(exchange, command, field) ?? string.Empty;

                if (open.Rule.Reads.Contains(command) && exchange.Answer == ExchangeAnswer.Data)
                {
                    open.EndReason = Ends.Read;
                    _open.RemoveAt(i);
                    _closed.Add(open.ToEpisode(_closed.Count));
                }

                return;
            }

            // The command belongs to no open episode, so the host has moved on. That is the only
            // end a kind with no closing exception and nothing to read has — a note episode is
            // over when the host stops reading bill meters — and it is reported as that rather
            // than as an episode the machine closed.
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                Open open = _open[i];
                if (open.Rule.Closes.Count > 0 || open.Rule.Reads.Count > 0)
                    continue;

                open.EndReason = Ends.MovedOn;
                _open.RemoveAt(i);
                _closed.Add(open.ToEpisode(_closed.Count));
            }
        }

        /// <summary>Reads the field that tells one episode of a kind from another.</summary>
        private static string? KeyFrom(SasExchange exchange, byte command, string field)
        {
            if (exchange.Answer != ExchangeAnswer.Data
                || SasLayouts.For(command) is not { } layout)
            {
                return null;
            }

            SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, exchange.ReplyBytes);
            SasValue? value = decoded[field];
            if (value is null)
                return null;

            return value.Number?.ToString() ?? value.ToHex();
        }

        private Open? Find(EpisodeRule rule, byte code)
        {
            string? door = rule.Kind == EpisodeKind.Door ? EpisodeRules.DoorOf(code) : null;
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                if (_open[i].Rule.Kind != rule.Kind)
                    continue;
                if (door is not null && _open[i].Key != door)
                    continue;
                return _open[i];
            }

            return null;
        }

        private sealed class Open(EpisodeRule rule, byte openedBy)
        {
            public EpisodeRule Rule { get; } = rule;

            public byte OpenedBy { get; } = openedBy;

            public byte? ClosedBy { get; set; }

            public string EndReason { get; set; } = Ends.Truncated;

            public string Key { get; set; } = string.Empty;

            public string? Ambiguity { get; set; }

            public List<SasExchange> Exchanges { get; } = [];

            public SasEpisode ToEpisode(int index) => new()
            {
                Index = index,
                Rule = Rule,
                Key = Key,
                Exchanges = [.. Exchanges],
                OpenedBy = OpenedBy,
                ClosedBy = ClosedBy,
                EndReason = EndReason,
                Ambiguity = Ambiguity ?? (Rule.KeyedByBytes && Key.Length == 0
                    ? $"a {Rule.Name} episode is keyed on its {Rule.KeyField} and none was read, "
                      + "so it cannot be told from another of its kind"
                    : null),
            };
        }
    }
}
