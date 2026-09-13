using System.Globalization;
using SasWorkbench.Core.Captures;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Reporting;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transactions;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Cli;

/// <summary>
/// Splits a recorded session into the exchanges and the transactions inside it.
/// </summary>
/// <remarks>
/// A capture is a flat list of packets and no question worth asking is about one packet. This
/// reads either a run of our own or a capture another tool wrote — the same two things
/// <c>sasctl explain</c> reads, and for the same reason — and prints the two levels the protocol
/// actually has: the exchange, which is mechanical, and the episode, which is a judgement that is
/// stated where it can be argued with. Everywhere the split cannot be decided from the bytes it
/// says so and says what it did instead.
/// </remarks>
internal static class TransactionsCommand
{
    public static int Execute(CommandLine line)
    {
        if (line.Flag("rules"))
        {
            PrintRules();
            return 0;
        }

        if (line.Arguments.Count == 0)
        {
            Console.Error.WriteLine(
                "error: which recording? Expected: sasctl transactions <run.jsonl or capture> "
                + "[--limit <n>] [--address <hh>] [--exchanges] [--rules]");
            return 2;
        }

        string path = line.Arguments[0];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"error: there is no file at '{path}'.");
            return 2;
        }

        if (!TryReadLimit(line, out int? limit))
            return 2;

        SasAddress? address = null;
        if (line.Value("address") is { } given)
        {
            if (!SasAddress.TryParse(given, out SasAddress parsed) || parsed.IsSync)
            {
                Console.Error.WriteLine(
                    $"error: '{given}' is not a machine address. Expected 01 to 7F.");
                return 2;
            }

            address = parsed;
        }

        if (!RecordedSessions.TryRead(path, limit, out IReadOnlyList<RecordedSession> sessions,
                out string source))
            return 2;

        Console.WriteLine($"TRANSACTIONS IN {path}");
        Console.WriteLine($"  read as      {source}");
        Console.WriteLine($"  sessions     {sessions.Count}");
        if (limit is { } capped)
            Console.WriteLine($"  limited to   the first {capped:N0} packet(s) of each session");

        Console.WriteLine();
        Console.WriteLine("HOW THIS WAS SPLIT");
        Console.WriteLine("  level one, the exchange   one host message and the machine messages");
        Console.WriteLine("                            that answer it. A reply is attributed to");
        Console.WriteLine("                            the poll whose command it echoes, never to");
        Console.WriteLine("                            whatever happened to precede it.");
        Console.WriteLine("  level two, the episode    several exchanges that are one thing");
        Console.WriteLine("                            happening. 'sasctl transactions --rules'");
        Console.WriteLine("                            prints the rule for each kind.");

        var totals = new Totals();
        foreach (RecordedSession session in sessions)
        {
            TransactionSplit split = TransactionSplitter.Split(session.Records, address);
            totals.Add(split);
            Print(session, split, line.Flag("exchanges"));
        }

        Console.WriteLine();
        Console.WriteLine("==============================================================================");
        Console.WriteLine();
        totals.Print();

        return 0;
    }

    private static void Print(RecordedSession session, TransactionSplit split, bool exchanges)
    {
        Console.WriteLine();
        Console.WriteLine("------------------------------------------------------------------------------");
        Console.WriteLine($"{session.Name}");
        Console.WriteLine("------------------------------------------------------------------------------");
        Console.WriteLine($"  {split.Records:N0} packet(s) → {split.Exchanges.Count:N0} exchange(s), "
                          + $"{split.Episodes.Count:N0} episode(s)");
        if (split.Address is { } known)
            Console.WriteLine($"  the machine being polled is at ${known.Value:X2}");
        else
            Console.WriteLine("  no poll in this session shows the machine's address");

        if (split.Episodes.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  EPISODES");
            foreach (SasEpisode episode in split.Episodes)
            {
                string key = episode.Key.Length == 0 ? "(no key in the bytes)" : episode.Key;
                Console.WriteLine($"    [{episode.At.TotalMilliseconds,9:F1}ms] "
                                  + $"{episode.Rule.Name,-18} {key,-24} "
                                  + $"{episode.Exchanges.Count,3} exchange(s)  {episode.EndReason}");
                Console.WriteLine($"        opened by exception ${episode.OpenedBy ?? 0:X2} "
                                  + $"{SasNames.ExceptionName(episode.OpenedBy ?? 0)}");
                foreach (SasExchange exchange in episode.Exchanges.Where(e => e.Command is not null))
                {
                    Console.WriteLine($"        ${exchange.Command:X2} {Name(exchange.Command!.Value),-46} "
                                      + $"{exchange.Answer}");
                }

                if (episode.Ambiguity is { } ambiguity)
                    Console.WriteLine($"        ambiguous: {ambiguity}");
            }
        }

        if (exchanges)
        {
            Console.WriteLine();
            Console.WriteLine("  EXCHANGES");
            foreach (SasExchange exchange in split.Exchanges)
            {
                Console.WriteLine($"    {exchange}");
                if (exchange.Ambiguity is { } ambiguity)
                    Console.WriteLine($"        ambiguous: {ambiguity}");
            }
        }

        if (split.Ambiguities.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  WHERE THE SPLIT WAS NOT DECIDABLE FROM THE BYTES");
            foreach (SplitAmbiguity ambiguity in split.Ambiguities.Take(20))
                Console.WriteLine($"    {ambiguity}");

            if (split.Ambiguities.Count > 20)
                Console.WriteLine($"    and {split.Ambiguities.Count - 20:N0} more");
        }
    }

    private static void PrintRules()
    {
        Console.WriteLine("HOW A CAPTURE IS SPLIT");
        Console.WriteLine();
        Console.WriteLine("LEVEL ONE — THE EXCHANGE");
        Console.WriteLine("  One host message and the machine messages that answer it. Mechanical:");
        Console.WriteLine("  the protocol says what answers what, so nothing here is a judgement.");
        Console.WriteLine();
        Console.WriteLine("  A reply is attributed to the poll whose address and command it echoes.");
        Console.WriteLine("  A reply that echoes another command is reported as answering nothing,");
        Console.WriteLine("  and the poll before it as unanswered — attributing by position is what");
        Console.WriteLine("  reports one command's answer under another command's name.");
        Console.WriteLine();
        Console.WriteLine("  A repeated long poll that has not been answered is the protocol's own");
        Console.WriteLine("  retry, so it stays in the exchange it repeats and only the count moves.");
        Console.WriteLine("  A repeated general poll is not a retry: it is the poll loop, and each");
        Console.WriteLine("  turn of it is its own exchange.");
        Console.WriteLine();
        Console.WriteLine("LEVEL TWO — THE EPISODE");
        Console.WriteLine("  Several exchanges that are one thing happening. A judgement about what a");
        Console.WriteLine("  person means by a transaction, so it is written down rather than buried.");
        Console.WriteLine();
        foreach (EpisodeRule rule in EpisodeRules.All)
        {
            Console.WriteLine($"  {rule.Name.ToUpperInvariant()}");
            Console.WriteLine($"    keyed by      {rule.KeyField ?? "nothing in the bytes - adjacency alone"}");
            Console.WriteLine($"    opens on      {Codes(rule.Opens)}");
            Console.WriteLine($"    closes on     {(rule.Closes.Count == 0
                ? rule.Reads.Count > 0
                    ? $"nothing; it ends when the host reads {Commands(rule.Reads)}"
                    : "nothing; it ends when the host moves on to another kind of poll"
                : Codes(rule.Closes))}");
            Console.WriteLine($"    carries       {(rule.Commands.Count == 0 ? "no long polls" : Commands(rule.Commands))}");
            Console.WriteLine($"    how           {Wrap(rule.How, 14)}");
            Console.WriteLine();
        }

        Console.WriteLine("  Two of these tables are derived from the machine library's own symbol");
        Console.WriteLine("  names — which exceptions say a note was taken, and which say a door");
        Console.WriteLine("  opened — so a code added to the library joins the right episode with no");
        Console.WriteLine("  change here. The rest are stated, because no amount of reading the");
        Console.WriteLine("  library says what a person means by a ticket's life.");
    }

    private static string Codes(IReadOnlySet<byte> codes) =>
        codes.Count == 0 ? "nothing" : string.Join(' ', codes.Order().Select(c => $"${c:X2}"));

    private static string Commands(IReadOnlySet<byte> commands) =>
        string.Join(' ', commands.Order().Select(c => $"${c:X2}"));

    private static string Name(byte command) =>
        SasCatalogue.LongPolls.TryGetValue(command, out SasLongPollInfo? info)
            ? info.Description
            : "not in the catalogue";

    private static string Wrap(string text, int indent)
    {
        var lines = new List<string>();
        var current = new List<string>();
        int width = 0;
        foreach (string word in text.Split(' '))
        {
            if (width + word.Length + 1 > 78 - indent && current.Count > 0)
            {
                lines.Add(string.Join(' ', current));
                current.Clear();
                width = 0;
            }

            current.Add(word);
            width += word.Length + 1;
        }

        if (current.Count > 0)
            lines.Add(string.Join(' ', current));

        return string.Join(Environment.NewLine + new string(' ', indent + 4), lines);
    }

    private static bool TryReadLimit(CommandLine line, out int? limit)
    {
        limit = null;
        if (line.Value("limit") is not { } asked)
            return true;

        if (!int.TryParse(asked, CultureInfo.InvariantCulture, out int count) || count <= 0)
        {
            Console.Error.WriteLine($"error: '--limit {asked}' is not a number of packets.");
            return false;
        }

        limit = count;
        return true;
    }


    /// <summary>The one number a reader wants after all the detail: what was in there.</summary>
    private sealed class Totals
    {
        private readonly Dictionary<EpisodeKind, int> _episodes = [];
        private int _records;
        private int _exchanges;
        private int _syncPolls;
        private int _generalPolls;
        private int _longPolls;
        private int _idle;
        private int _unanswered;
        private int _retried;
        private int _orphans;
        private int _outside;
        private int _ambiguities;
        private int _ambiguousExchanges;
        private int _ambiguousEpisodes;

        public void Add(TransactionSplit split)
        {
            _records += split.Records;
            _exchanges += split.Exchanges.Count;
            _syncPolls += split.Exchanges.Count(e => e.Kind == ExchangeKind.SyncPoll);
            _generalPolls += split.Exchanges.Count(e => e.Kind == ExchangeKind.GeneralPoll);
            _longPolls += split.Exchanges.Count(e => e.Kind == ExchangeKind.LongPoll);
            _orphans += split.Exchanges.Count(e => e.Kind == ExchangeKind.Orphan);
            _idle += split.IdlePolls;
            // A synchronisation poll is unanswered by design - it is addressed to every machine
            // on the link and none of them replies - so counting it here would put the largest
            // number in the table next to the word that means something went wrong.
            _unanswered += split.Exchanges.Count(e =>
                !e.Answered && e.Kind is ExchangeKind.GeneralPoll or ExchangeKind.LongPoll);
            _retried += split.Exchanges.Count(e => e.Retries > 0);
            _outside += split.OutsideAnyEpisode;
            _ambiguities += split.Ambiguities.Count;
            _ambiguousExchanges += split.Exchanges.Count(e => e.Ambiguity is not null);
            _ambiguousEpisodes += split.Episodes.Count(e => e.Ambiguity is not null);

            foreach (SasEpisode episode in split.Episodes)
            {
                _episodes.TryGetValue(episode.Rule.Kind, out int seen);
                _episodes[episode.Rule.Kind] = seen + 1;
            }
        }

        public void Print()
        {
            Console.WriteLine("WHAT WAS IN THERE, COUNTED");
            Console.WriteLine($"  packets                  {_records,10:N0}");
            Console.WriteLine($"  exchanges                {_exchanges,10:N0}");
            Console.WriteLine($"    synchronisation polls  {_syncPolls,10:N0}  addressed to every "
                              + "machine on the link, and answered by none");
            Console.WriteLine($"    general polls          {_generalPolls,10:N0}");
            Console.WriteLine($"      of which idle        {_idle,10:N0}  the host waiting: a poll "
                              + "answered with nothing queued");
            Console.WriteLine($"    long polls             {_longPolls,10:N0}");
            Console.WriteLine($"    answering nothing      {_orphans,10:N0}  machine messages with "
                              + "no poll of theirs in the record");
            Console.WriteLine($"    unanswered             {_unanswered,10:N0}  polls that "
                              + "expected an answer and got none");
            Console.WriteLine($"    retried by the host    {_retried,10:N0}");
            Console.WriteLine();
            Console.WriteLine($"  episodes                 {_episodes.Values.Sum(),10:N0}");
            foreach (EpisodeRule rule in EpisodeRules.All)
            {
                _episodes.TryGetValue(rule.Kind, out int count);
                Console.WriteLine($"    {rule.Name,-22} {count,10:N0}"
                                  + (rule.KeyedByBytes
                                      ? $"  keyed on {rule.KeyField}"
                                      : "  keyed on nothing: adjacency alone"));
            }

            Console.WriteLine($"  exchanges in no episode  {_outside,10:N0}");
            Console.WriteLine();
            Console.WriteLine("WHAT COULD NOT BE DECIDED FROM THE BYTES");
            Console.WriteLine($"  places                   {_ambiguities,10:N0}");
            Console.WriteLine($"  exchanges marked         {_ambiguousExchanges,10:N0}");
            Console.WriteLine($"  episodes marked          {_ambiguousEpisodes,10:N0}");
            Console.WriteLine();
            Console.WriteLine("  An ambiguity is not a failure of the recording or of this build.");
            Console.WriteLine("  Some of these splits are genuinely not in the bytes — nothing in a");
            Console.WriteLine("  bill meter's reply says which note it is about — and the honest");
            Console.WriteLine("  form of that is to say so rather than to present a guess.");
        }
    }
}
