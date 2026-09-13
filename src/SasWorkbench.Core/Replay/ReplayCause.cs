namespace SasWorkbench.Core.Replay;

/// <summary>
/// Why a machine answered a recorded poll differently from the machine that was recorded.
/// </summary>
/// <remarks>
/// <para>
/// The whole point of the classification. A replay that reported "matched" and "did not match"
/// would be worse than useless: almost nothing matches, because two machines are never in the
/// same configuration and have never done the same things, so a bare diff is a wall of red with
/// one real finding somewhere in it. A difference is only worth reporting once it says which kind
/// of difference it is.
/// </para>
/// <para>
/// <b>Only <see cref="Defect"/> is a fault</b>, and it is deliberately worded as what it is: the
/// difference is one nothing here explains, which is a defect in one of the two machines <em>or</em>
/// a gap in this classifier. Calling it a defect outright would be a claim this cannot support
/// from bytes alone, and every one of them needs a person.
/// </para>
/// </remarks>
public enum ReplayCause
{
    /// <summary>The two machines answered the same, so there is nothing to explain.</summary>
    Agrees,

    /// <summary>
    /// A machine in another configuration. The differing fields are settings and identity — what
    /// the machine is and how it was set up — or the machine refused a command it will not honour
    /// as it stands.
    /// </summary>
    Configuration,

    /// <summary>
    /// A machine of another revision. The command is one the library this machine runs does not
    /// implement at all, or the reply is a length the established layout does not have.
    /// </summary>
    Revision,

    /// <summary>
    /// The same kind of machine, having lived a different life. The differing fields are meters,
    /// or the machine's current condition, or fields measured to move between two reads.
    /// </summary>
    State,

    /// <summary>
    /// Nothing here explains the difference: a defect in one of the two machines, or a gap in this
    /// classifier. The only cause that makes a replay red.
    /// </summary>
    Defect,

    /// <summary>
    /// The request was sent and the recording holds no answer to compare it against.
    /// </summary>
    /// <remarks>
    /// Kept apart from a defect on evidence, not out of charity. Silence in a recording has at
    /// least three innocent readings that look identical in a file: the toolkit's tracer is
    /// filtered and holds machine answers to polls it never wrote down, its host polls a machine
    /// at $7F that never answers alongside the one that does, and any recorder can miss a reply.
    /// A machine answering now where the file is silent is therefore not evidence of anything.
    /// </remarks>
    NotComparable,

    /// <summary>The exchange was not replayed at all, and the row says why.</summary>
    NotReplayed,
}

/// <summary>What a field's difference is, and where that judgement came from.</summary>
/// <param name="Cause">Which cause the field's difference belongs to.</param>
/// <param name="Why">One sentence, in a report's own voice, on why.</param>
/// <param name="Measured">
/// True when this was established by reading the machine twice rather than read off a table. A
/// measurement beats a statement and is reported as the stronger of the two.
/// </param>
public sealed record FieldCause(ReplayCause Cause, string Why, bool Measured = false);

/// <summary>
/// Which reply fields two healthy machines may legitimately disagree on.
/// </summary>
/// <remarks>
/// <para>
/// Three sources, and they are kept apart because they are worth different amounts.
/// </para>
/// <list type="number">
/// <item>
/// <b>Measured.</b> Every command is asked twice, of both machines where the recording asked it
/// twice, and a field that moved between two reads of one machine moves on its own. Nothing has
/// to be believed about it.
/// </item>
/// <item>
/// <b>Derived.</b> A field the layout marks as a meter accumulates by construction, and the mark
/// is checked against the library's own handlers by a test that already exists.
/// </item>
/// <item>
/// <b>Stated.</b> The table below. Which fields are a machine's identity and settings, and which
/// are its current condition, is a judgement about the protocol and not something the library
/// says, so it is written where it can be read and argued with. A name in neither list is
/// reported as <b>unclassified</b> rather than assumed harmless — which is what keeps the gap in
/// this table visible and countable instead of silently green.
/// </item>
/// </list>
/// <para>
/// The table is keyed by field name and not by command, because these names are consistent across
/// this build's layouts. Names that mean different things in different replies — a <c>count</c> is
/// a number of enabled games in one reply and a number of notes in another — are deliberately
/// left out, so they land in the unclassified bucket rather than being classified wrongly in one
/// of the two places.
/// </para>
/// </remarks>
public static class ReplayFields
{
    private static readonly Dictionary<string, FieldCause> Stated = Build();
    private static readonly Dictionary<(byte Command, string Name), FieldCause> PerCommand =
        Overrides();

    /// <summary>What this build says about a field's difference, or null when it says nothing.</summary>
    /// <param name="path">The field, as a script would name it.</param>
    /// <param name="command">
    /// Which reply it came from, where the caller knows. It decides the handful of names that
    /// genuinely mean two things: a <c>count</c> is a number of enabled games in one reply and a
    /// number of notes taken in another, and those are a configuration and a meter.
    /// </param>
    public static FieldCause? Explains(string path, byte? command = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        // Members of a repeating group carry an index: meters[2].value is still a value.
        string name = path;
        int dot = name.LastIndexOf('.');
        if (dot >= 0)
            name = name[(dot + 1)..];

        int bracket = name.IndexOf('[');
        if (bracket >= 0)
            name = name[..bracket];

        if (command is { } asked && PerCommand.TryGetValue((asked, name), out FieldCause? only))
            return only;

        return Stated.GetValueOrDefault(name);
    }

    /// <summary>
    /// The names that mean different things in different replies, resolved per command.
    /// </summary>
    /// <remarks>
    /// Deliberately tiny. A name that needs an entry here is a name the rest of the table cannot
    /// classify, and the alternative — classifying it once and being wrong in the other reply —
    /// is the failure this whole table exists to avoid.
    /// </remarks>
    private static Dictionary<(byte, string), FieldCause> Overrides() => new()
    {
        [(0x48, "count")] = new FieldCause(ReplayCause.State,
            "how many notes of the last denomination the machine has taken, which accumulates"),
        [(0x56, "count")] = new FieldCause(ReplayCause.Configuration,
            "how many of the machine's games are enabled"),
    };

    /// <summary>How many names are resolved per command rather than globally.</summary>
    public static int OverrideCount => PerCommand.Count;

    /// <summary>Every field name this build classifies, for the test that checks them.</summary>
    public static IReadOnlyCollection<string> Names => Stated.Keys;

    /// <summary>How many of the names carry each cause, for the summary a replay prints.</summary>
    public static int CountOf(ReplayCause cause) =>
        Stated.Values.Count(entry => entry.Cause == cause);

    private static Dictionary<string, FieldCause> Build()
    {
        var table = new Dictionary<string, FieldCause>(StringComparer.OrdinalIgnoreCase);

        void Configuration(string why, params string[] names)
        {
            foreach (string name in names)
                table[name] = new FieldCause(ReplayCause.Configuration, why);
        }

        void State(string why, params string[] names)
        {
            foreach (string name in names)
                table[name] = new FieldCause(ReplayCause.State, why);
        }

        Configuration("what machine this is",
            "game_id", "additional_id", "serial_number", "sas_version", "game_name", "name_length");

        Configuration("how the machine was set up to pay",
            "denomination", "token_denomination", "max_bet", "base_percentage",
            "payback_percentage", "paytable_id", "paytable_name", "paytable_length");

        Configuration("which optional features the machine has",
            "game_options", "feature_codes_1", "feature_codes_2", "feature_codes_3",
            "validation_style", "validation_extensions", "ticket_redemption", "meter_model");

        Configuration("what the host configured the machine to do",
            "denominations", "action", "enabled", "validation_id", "games", "reset_id");

        Configuration("which progressive arrangement the machine belongs to",
            "progressive_group", "pool_id", "progressive_levels");

        Configuration("bytes the protocol reserves, which a machine may leave as it likes",
            "reserved");

        // The funds-transfer replies. Where a name here looks like a state and is classified as a
        // configuration, or the other way round, the deciding question was the one this whole
        // table asks: would two machines set up the same way answer the same, whatever either of
        // them happened to be doing at the time.
        Configuration("what the machine is registered as, and the limits it was set up with",
            "asset_number", "max_buffer_index", "gaming_machine_transfer_limit");

        Configuration("how the machine was told to validate, and for how long a ticket lasts",
            "current_validation_status", "cashable_ticket_expiration",
            "restricted_ticket_expiration");

        State("what the machine will accept and is holding for a transfer right now",
            "game_lock_status", "available_transfers", "host_cashout_status", "aft_status",
            "current_cashable_amount", "current_restricted_amount",
            "current_nonrestricted_amount", "restricted_expiration", "restricted_pool_id");

        State("the machine's clock, which moves whether or not anything else does",
            "month", "day", "year", "hour", "minute", "second");

        State("what the machine is doing and holding now",
            "current_credits", "machine_status", "selected_game", "status", "cashout_type",
            "amount", "partial_pay", "power_reset");

        State("what the last game came to",
            "reel_1", "reel_2", "reel_3", "reel_4", "reel_5", "reel_6", "reel_7", "reel_8",
            "reel_9", "card_1", "card_2", "card_3", "card_4", "card_5", "hand_type",
            "multiplier", "multiplied_win", "bonus", "deductible", "non_deductible",
            "session_game_pay", "session_game_win", "wager_category", "wager_match");

        State("what the machine's note acceptor and hopper are holding now",
            "hopper_level", "percent_full", "country_code", "denomination_code");

        State("a number the machine accumulates, whatever the reply calls it",
            "quantity", "coin_in");

        // A selected-meter reply carries whichever meters the host asked for by code, so the
        // layout cannot say which meter any of them is - only that a meter is what it is.
        State("a meter the host chose by code in the request, so it accumulates whichever it is",
            "value");

        State("which ticket or transfer the machine is dealing with now",
            "validation_number", "validation_data", "validation_type", "validation_system_id",
            "parsing_code", "ticket_status", "ticket_amount", "ticket_number", "transfer_amount",
            "transfer_code", "expiration_month", "expiration_day", "expiration_year",
            "restricted_expiration_month", "restricted_expiration_day",
            "restricted_expiration_year", "sequence_number", "tax_status");

        return table;
    }
}
