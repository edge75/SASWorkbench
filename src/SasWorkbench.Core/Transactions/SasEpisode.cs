using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Transactions;

/// <summary>A kind of episode — a business transaction spanning several exchanges.</summary>
public enum EpisodeKind
{
    /// <summary>A ticket the machine printed, and the host reading and validating it.</summary>
    TicketOut,

    /// <summary>A ticket a player inserted, and the host deciding what it is worth.</summary>
    TicketIn,

    /// <summary>A note the machine took or refused, and the meters that moved after it.</summary>
    NoteIn,

    /// <summary>Money owed to a player, and its reset.</summary>
    Handpay,

    /// <summary>A door opened and shut again.</summary>
    Door,
}

/// <summary>
/// How one kind of episode is recognised, stated once so it can be read and argued with.
/// </summary>
/// <param name="Kind">Which episode this describes.</param>
/// <param name="Name">What to call it in a report.</param>
/// <param name="Opens">Exception codes that begin one.</param>
/// <param name="Closes">Exception codes that end one.</param>
/// <param name="Commands">Long polls that belong to one while it is open.</param>
/// <param name="ClosedByReading">
/// Long polls whose answer ends the episode, for the kinds the machine has no closing exception
/// for. A ticket the machine printed is finished with when the host has read what was on it, and
/// nothing is reported afterwards to say so.
/// </param>
/// <param name="KeyField">
/// The decoded field whose value tells one episode of this kind from another, or null when
/// nothing in the bytes does. Null is not a shortcoming to be papered over: it means episodes of
/// this kind are separated by adjacency alone, which is a guess wherever two of them overlap, and
/// the split says so rather than presenting the guess as a reading.
/// </param>
/// <param name="How">One sentence a report prints, saying how this kind is split.</param>
public sealed record EpisodeRule(
    EpisodeKind Kind,
    string Name,
    IReadOnlySet<byte> Opens,
    IReadOnlySet<byte> Closes,
    IReadOnlySet<byte> Commands,
    string? KeyField,
    string How,
    IReadOnlySet<byte>? ClosedByReading = null)
{
    /// <summary>The long polls whose answer ends an episode of this kind.</summary>
    public IReadOnlySet<byte> Reads => ClosedByReading ?? new HashSet<byte>();

    /// <summary>True when two episodes of this kind can be told apart from the bytes.</summary>
    public bool KeyedByBytes => KeyField is not null;
}

/// <summary>
/// The episodes this build recognises, and the rule for each.
/// </summary>
/// <remarks>
/// <para>
/// Two of these tables are <b>derived</b> and the rest are <b>stated</b>, and the difference is
/// worth keeping in view. Which exceptions say a note was accepted, and which say a door opened,
/// come out of the gaming machine library's own symbol names — <c>usd_20_bill_accepted</c>,
/// <c>drop_door_opened</c> — so a code added to the library joins the right episode without
/// anybody here noticing. Which long polls belong to a ticket's life is a judgement about what a
/// person means by "a ticket", and no amount of reading the library produces it, so it is written
/// down here where it can be disagreed with.
/// </para>
/// <para>
/// Every code named here is checked against the generated catalogue by a test, so a rename in the
/// library arrives as a failure rather than as an episode that quietly stopped matching anything.
/// </para>
/// </remarks>
public static class EpisodeRules
{
    /// <summary>
    /// What keys a door episode: the exception code itself, rather than a field of a reply.
    /// </summary>
    /// <remarks>
    /// Named so a report can print something true about it. Every other kind is keyed on a field
    /// of a decoded reply or on nothing at all; this one is keyed on which door the machine said
    /// it was, which is in the code and cannot be missing.
    /// </remarks>
    public const string DoorKey = "the door named by the exception code";

    // Built on first use rather than in a field initialiser: the rules read the derived sets
    // below, and a static field initialiser runs before them whatever order it is written in.
    private static readonly Lazy<IReadOnlyList<EpisodeRule>> Rules = new(Build);

    /// <summary>Every rule, in the order a report prints them.</summary>
    public static IReadOnlyList<EpisodeRule> All => Rules.Value;

    /// <summary>The rule for one kind.</summary>
    public static EpisodeRule For(EpisodeKind kind) => All.First(rule => rule.Kind == kind);

    /// <summary>
    /// The exception codes that say the machine took or refused a note, read out of the library's
    /// own symbol names rather than listed here.
    /// </summary>
    public static IReadOnlySet<byte> NoteExceptions { get; } = Derive(
        symbol => symbol.EndsWith("_bill_accepted", StringComparison.Ordinal)
                  || symbol is "bill_accepted" or "bill_rejected" or "counterfeit_bill"
                      or "reverse_bill");

    /// <summary>The exception codes that say a door opened, or a cashbox came out.</summary>
    public static IReadOnlySet<byte> DoorsOpened { get; } = Derive(
        symbol => symbol.EndsWith("_door_opened", StringComparison.Ordinal)
                  || symbol == "cashbox_removed");

    /// <summary>The exception codes that say a door shut, or a cashbox went back in.</summary>
    public static IReadOnlySet<byte> DoorsClosed { get; } = Derive(
        symbol => symbol.EndsWith("_door_closed", StringComparison.Ordinal)
                  || symbol == "cashbox_installed");

    /// <summary>
    /// Which door an exception is about, taken from the library's symbol for it.
    /// </summary>
    /// <remarks>
    /// This is what makes the door episode the one kind that needs no judgement at all: the key
    /// telling one door's episode from another's is in the exception code itself, so two doors
    /// open at once are still two episodes and neither is a guess.
    /// </remarks>
    public static string? DoorOf(byte code)
    {
        if (!SasCatalogue.Exceptions.TryGetValue(code, out SasExceptionInfo? info))
            return null;

        string symbol = SasNames.Underscore(info.Symbol).ToLowerInvariant();
        foreach (string suffix in (string[])["_door_opened", "_door_closed"])
        {
            if (symbol.EndsWith(suffix, StringComparison.Ordinal))
                return symbol[..^suffix.Length];
        }

        return symbol is "cashbox_removed" or "cashbox_installed" ? "cashbox" : null;
    }

    /// <summary>
    /// The polls that read what the machine has taken in notes: the two totals, the last note's
    /// own record, the stacker, and every per-denomination count.
    /// </summary>
    /// <remarks>
    /// The per-denomination counts are derived rather than written as a range, and the reason is a
    /// bug this had first: they occupy $31 to $45 <em>except</em> $3D, which is the cashout ticket
    /// information, so a range put a ticket poll into the note episode. The library's own name for
    /// each of them says what it is.
    /// </remarks>
    private static IReadOnlySet<byte> BillCommands { get; } = new HashSet<byte>(
    [
        (byte)0x1E, // send bill meters
        0x20,       // send total dollar value of bills
        0x46,       // send credit amount of all bills accepted
        0x48,       // send last accepted bill information
        0x49,       // send number of bills in the stacker
        0x4A,       // send credit value of the bills in the stacker
        .. SasCatalogue.LongPolls
            .Where(entry => entry.Value.Symbol.Contains("BIL", StringComparison.Ordinal)
                            && entry.Value.Symbol.Contains("INMETER", StringComparison.Ordinal))
            .Select(entry => entry.Key),
    ]);

    // The library's symbols are written the way its own headers write them, so they are put
    // through the same underscoring a report uses before being matched. Matching the raw form
    // silently derived nothing, which is exactly the failure a derived table is supposed to avoid.
    private static IReadOnlySet<byte> Derive(Func<string, bool> matches) =>
        SasCatalogue.Exceptions
            .Where(entry => matches(SasNames.Underscore(entry.Value.Symbol).ToLowerInvariant()))
            .Select(entry => entry.Key)
            .ToHashSet();

    private static List<EpisodeRule> Build() =>
    [
        new EpisodeRule(
            EpisodeKind.TicketOut,
            "cashout ticket",
            Opens: new HashSet<byte>
            {
                0x3D, // a cashout ticket has been printed
                0x66, // cash out button pressed
                0x57, // system validation request
            },
            Closes: new HashSet<byte>(),
            Commands: new HashSet<byte>
            {
                0x3D, // send cashout ticket information
                0x4C, // set secure enhanced validation id
                0x4D, // send enhanced validation info
                0x57, // send pending cashout information
                0x58, // receive validation number
            },
            KeyField: "validation_number",
            How: "opens when the machine says it printed a ticket, asked for a validation number "
                 + "or had its cashout button pressed; carries the polls that read and number that "
                 + "ticket; and is keyed on the validation number in the reply, so two tickets "
                 + "are two episodes even where they overlap. The machine reports nothing when a "
                 + "ticket is finished with, so it ends when the host has read what was on it.",
            ClosedByReading: new HashSet<byte> { 0x3D, 0x4D }),

        new EpisodeRule(
            EpisodeKind.TicketIn,
            "ticket redemption",
            Opens: new HashSet<byte> { 0x67 }, // ticket has been inserted
            Closes: new HashSet<byte> { 0x68 }, // ticket transfer complete
            Commands: new HashSet<byte>
            {
                0x70, // send ticket validation data
                0x71, // redeem ticket
            },
            KeyField: "validation_data",
            How: "opens when a ticket is inserted and closes when the transfer completes; keyed on "
                 + "the ticket's own validation data, which both the machine's reply and the "
                 + "host's verdict carry."),

        new EpisodeRule(
            EpisodeKind.NoteIn,
            "note accepted",
            Opens: NoteExceptions,
            Closes: new HashSet<byte>(),
            Commands: BillCommands,
            KeyField: null,
            How: "opens when the machine says it took or refused a note, and gathers the bill "
                 + "meters read after it. Nothing in those replies names which note they are "
                 + "about, so this kind is split by adjacency and two notes taken close together "
                 + "cannot be told apart — which is said rather than guessed."),

        new EpisodeRule(
            EpisodeKind.Handpay,
            "handpay",
            Opens: new HashSet<byte> { 0x51 }, // handpay is pending
            Closes: new HashSet<byte>
            {
                0x52, // handpay was reset
                0x55, // player cancelled the handpay request
            },
            Commands: new HashSet<byte>
            {
                0x1B, // send handpay information
                0x2D, // send total handpay cancelled credits
                0x94, // remote handpay reset
            },
            KeyField: null,
            How: "opens when a handpay is pending and closes when it is reset or cancelled. The "
                 + "handpay reply carries an amount and no identifier, so two handpays in one "
                 + "session are separated by adjacency alone."),

        new EpisodeRule(
            EpisodeKind.Door,
            "door",
            Opens: DoorsOpened,
            Closes: DoorsClosed,
            Commands: new HashSet<byte>(),
            KeyField: DoorKey,
            How: "opens on a door-opened exception and closes on the matching door-closed one. "
                 + "The door is named by the library's own code for it, so this is the one kind "
                 + "whose episodes never have to be told apart by adjacency."),
    ];
}

/// <summary>How an episode came to an end.</summary>
public static class Ends
{
    /// <summary>The machine reported an exception that closes this kind.</summary>
    public const string Reported = "the machine reported it closed";

    /// <summary>The host read what this kind ends on.</summary>
    public const string Read = "the host read what was on it";

    /// <summary>The host moved on to a poll belonging to no episode of this kind.</summary>
    public const string MovedOn = "the host moved on to another kind of poll";

    /// <summary>The recording stops inside it.</summary>
    public const string Truncated = "the recording ends here";
}

/// <summary>
/// One episode: several exchanges that are one thing happening.
/// </summary>
public sealed record SasEpisode
{
    /// <summary>Index within the split.</summary>
    public required int Index { get; init; }

    public required EpisodeRule Rule { get; init; }

    /// <summary>
    /// What tells this episode from another of its kind: a validation number, a door. Empty where
    /// the bytes carry nothing to key on.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The exchanges that belong to it, in order.</summary>
    public IReadOnlyList<SasExchange> Exchanges { get; init; } = [];

    /// <summary>The exception that opened it.</summary>
    public byte? OpenedBy { get; init; }

    /// <summary>The exception that closed it, when one did.</summary>
    public byte? ClosedBy { get; init; }

    /// <summary>
    /// How the episode ended, in a report's own words.
    /// </summary>
    /// <remarks>
    /// Four ways, and they are not the same thing. The machine reported it closed; the host read
    /// what it needed and moved on; the host moved on to a poll of another kind, which is the only
    /// end a note episode has; or the recording stops inside it, which is a fact about the capture
    /// and not about the machine. Reporting the last as a closed episode would be a small lie that
    /// makes a truncated recording look whole.
    /// </remarks>
    public string EndReason { get; init; } = Ends.Truncated;

    /// <summary>True when the episode's end was actually seen.</summary>
    public bool Complete => EndReason != Ends.Truncated;

    /// <summary>Why this episode's boundaries are not certain, when they are not.</summary>
    public string? Ambiguity { get; init; }

    public TimeSpan At => Exchanges.Count == 0 ? TimeSpan.Zero : Exchanges[0].At;

    public TimeSpan Until => Exchanges.Count == 0 ? TimeSpan.Zero : Exchanges[^1].At;

    public override string ToString()
    {
        string key = Key.Length == 0 ? string.Empty : $" {Key}";
        string open = Complete ? string.Empty : ", still open where the recording ends";
        return $"{Rule.Name}{key}: {Exchanges.Count} exchange(s) over "
               + $"{(Until - At).TotalMilliseconds:F0}ms{open}";
    }
}
