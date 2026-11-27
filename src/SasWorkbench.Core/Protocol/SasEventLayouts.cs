using System.Collections.Frozen;

namespace SasWorkbench.Core.Protocol;

/// <summary>
/// What one exception carries after its code, when the machine is reporting in the real-time
/// event format.
/// </summary>
/// <remarks>
/// <para>
/// The same discipline as <see cref="SasMessageLayout"/>, for the other half of the protocol.
/// <see cref="DerivedFrom"/> names the gaming machine library function that builds the payload, so
/// the claim can be checked in one step; <see cref="ConfirmedByCapture"/> says whether the bytes
/// have also been seen coming off a link.
/// </para>
/// <para>
/// <see cref="ConfirmedByCapture"/> is the honest half. An event this build has a layout for and
/// has never seen is a layout read out of source alone, and saying which is which matters more
/// here than it does for long polls: a long poll can be asked for, and an event can only be
/// waited for.
/// </para>
/// </remarks>
public sealed record SasEventLayout
{
    /// <summary>The exception code this describes.</summary>
    public required byte Code { get; init; }

    /// <summary>A short name a script can use instead of the code.</summary>
    public required string Name { get; init; }

    /// <summary>One sentence, in our own words, on what the event reports.</summary>
    public required string Description { get; init; }

    /// <summary>The fields that follow the exception code, in order.</summary>
    public IReadOnlyList<SasField> Payload { get; init; } = [];

    /// <summary>The library function that builds this payload, so the claim can be checked.</summary>
    public required string DerivedFrom { get; init; }

    /// <summary>True when these bytes have been read off a link, not only out of source.</summary>
    /// <remarks>
    /// False on every payload here, and the reason each one has never been seen is in the
    /// measurement's own plan rather than repeated here — one place to keep true.
    /// </remarks>
    public bool ConfirmedByCapture { get; init; }

    /// <summary>How many payload bytes the library writes, when every field is fixed-width.</summary>
    public int? PayloadLength
    {
        get
        {
            int total = 0;
            foreach (SasField part in Payload)
            {
                if (part.Length <= 0 || part.IsGroup || part.PresentWhen is not null)
                    return null;
                total += part.Length;
            }

            return total;
        }
    }

    public override string ToString() => $"${Code:X2} {Name}";
}

/// <summary>
/// The exception payloads this build can read, each established from the gaming machine library's
/// own builder for it.
/// </summary>
/// <remarks>
/// <para>
/// A machine told to report in real time answers a general poll with a whole message rather than
/// one byte: its address, the marker <c>$FF</c>, the exception code, whatever that exception
/// carries, and a check sequence. Most exceptions carry nothing, and those need no layout — the
/// three bytes are the whole event. Ten carry data, and this is what each of the ten holds.
/// </para>
/// <para>
/// Read from the library's own builders rather than from any other description of SAS. Each
/// builder fills <c>exception.data</c> byte by byte and sets <c>exception.length</c>, and the
/// transmit path copies exactly that many bytes after the code, so the layout is the assignment
/// list and nothing is inferred from field names.
/// </para>
/// <para>
/// Two of the ten are written differently from the other eight and it is recorded rather than
/// smoothed over: the session builders write their numbers as plain binary, while every other
/// builder converts to binary coded decimal first. They also begin with a byte holding the length
/// of what follows, which no other event does.
/// </para>
/// </remarks>
public static class SasEventLayouts
{
    /// <summary>The marker a machine puts in place of a command byte on a real-time event.</summary>
    /// <remarks>
    /// The library's own value, and the reason a machine's multi-byte answer to a general poll can
    /// be told from a long poll reply without knowing which poll it answers.
    /// </remarks>
    public const byte Marker = 0xFF;

    /// <summary>
    /// How many bytes a real-time event message holds before its payload: the address, the marker
    /// and the exception code.
    /// </summary>
    public const int HeaderLength = 3;

    private static SasField Bcd(string name, int bytes, SasUnit unit, string description) => new()
    {
        Name = name,
        Length = bytes,
        Encoding = SasFieldEncoding.Bcd,
        ByteOrder = SasByteOrder.BigEndian,
        Unit = unit,
        Description = description,
    };

    private static SasField Binary(string name, int bytes, SasUnit unit, string description) => new()
    {
        Name = name,
        Length = bytes,
        Encoding = SasFieldEncoding.Binary,
        ByteOrder = bytes > 1 ? SasByteOrder.BigEndian : SasByteOrder.NotApplicable,
        Unit = unit,
        Description = description,
    };

    /// <summary>Every established payload, by exception code.</summary>
    public static FrozenDictionary<byte, SasEventLayout> All { get; } = Build();

    /// <summary>Every established payload, by the short name a script uses.</summary>
    public static FrozenDictionary<string, SasEventLayout> ByName { get; } =
        All.Values.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The payload for an exception, or null when this build has established none.</summary>
    public static SasEventLayout? For(byte code) =>
        All.TryGetValue(code, out SasEventLayout? layout) ? layout : null;

    /// <summary>
    /// Exceptions the library will only ever send in the real-time event format, whatever else is
    /// queued.
    /// </summary>
    /// <remarks>
    /// The transmit path drops these four rather than sending the bare code when reporting is off,
    /// so a host that never enables reporting cannot see them at all. Two of the four have a
    /// payload here and two do not — see <see cref="NeverQueued"/>.
    /// </remarks>
    public static IReadOnlySet<byte> RealTimeOnly { get; } = new HashSet<byte>
    {
        (byte)SasExceptionCode.PlayerRequestedDrawCards,
        (byte)SasExceptionCode.ReelNStopped,
        (byte)SasExceptionCode.CoinOrCreditWagered,
        (byte)SasExceptionCode.CardHeldOrNotHeld,
    };

    /// <summary>
    /// Exceptions no path in the library ever queues, with what that means for a layout.
    /// </summary>
    /// <remarks>
    /// Both are in the library's enumeration and both are on its real-time-only list, so the
    /// transmit path is written as though they could arrive — but nothing in the library or in the
    /// platform above it ever queues one. No machine running this library can produce either, so
    /// no layout for their payloads can be established here at all, and writing one from another
    /// description of the protocol is the guess this project refuses.
    /// </remarks>
    public static FrozenDictionary<byte, string> NeverQueued { get; } = new Dictionary<byte, string>
    {
        [(byte)SasExceptionCode.PlayerRequestedDrawCards] =
            "no function in the machine library queues it, so no machine running this library can "
            + "report it and its payload cannot be established here",
        [(byte)SasExceptionCode.CoinOrCreditWagered] =
            "no function in the machine library queues it, so no machine running this library can "
            + "report it and its payload cannot be established here",
    }.ToFrozenDictionary();

    private static FrozenDictionary<byte, SasEventLayout> Build()
    {
        SasEventLayout[] events =
        [
            new()
            {
                Code = (byte)SasExceptionCode.BillAccepted,
                Name = "bill_accepted",
                Description = "A note was taken, with what it was and how many of them there are.",
                Payload =
                [
                    Bcd("country_code", 1, SasUnit.Code, "Which currency the note is in."),
                    Bcd("denomination_code", 1, SasUnit.Code, "Which note it was."),
                    Bcd("bill_meter", 4, SasUnit.Count,
                        "How many notes of this denomination the machine has taken, this one "
                        + "included."),
                ],
                DerivedFrom = "SAS_BillAccepted",
                // Measured 2026-09-13 as 370400000002: denomination code 04 is the twenty, and the meter
                // read 2 after exactly two twenties had been put in.
                ConfirmedByCapture = true,
            },
            new()
            {
                Code = (byte)SasExceptionCode.LegacyBonusPayOrJackpotAwarded,
                Name = "legacy_bonus_awarded",
                Description = "A legacy bonus was paid, or a multiplied jackpot was awarded.",
                Payload =
                [
                    Binary("multiplier", 1, SasUnit.None,
                        "What the win was multiplied by before the bonus was worked out."),
                    Bcd("multiplied_win", 4, SasUnit.Credits, "The win after multiplication."),
                    Binary("tax_status", 1, SasUnit.Code,
                        "Which of the machine's legacy bonus meters this went to."),
                    Bcd("bonus_amount", 4, SasUnit.Credits, "The bonus itself."),
                ],
                DerivedFrom = "SAS_LegacyBonusAward",
            },
            new()
            {
                Code = (byte)SasExceptionCode.GameStarted,
                Name = "game_started",
                Description = "A game began, with what was staked on it.",
                Payload =
                [
                    // Two fields in two different units in the same event, which is the whole
                    // reason a validation case wanted this one: a wager is a number of game
                    // credits and does not move when the accounting denomination does, while the
                    // meter beside it is in accounting units.
                    Bcd("credits_wagered", 2, SasUnit.Count, "How many game credits were staked."),
                    Bcd("total_coin_in", 4, SasUnit.Credits,
                        "The machine's coin in meter, in accounting units, after this wager."),
                    Binary("wager_type", 1, SasUnit.Code, "What kind of wager it was."),
                    Binary("progressive_group", 1, SasUnit.Code,
                        "Which progressive group the machine is in."),
                ],
                DerivedFrom = "SAS_RTE_GameStart",
                // Measured 2026-09-13 over nine games: the first field held 50 every time while the
                // second climbed 50, 100, 150 and on, which is what tells a wager from a meter.
                ConfirmedByCapture = true,
            },
            new()
            {
                Code = (byte)SasExceptionCode.GameEnded,
                Name = "game_ended",
                Description = "A game finished, with what it paid.",
                Payload =
                [
                    Bcd("win_amount", 4, SasUnit.Cents, "What this game won."),
                ],
                DerivedFrom = "SAS_RTE_GameEnd",
                // Measured 2026-09-13 across eight games, four of which won nothing and reported zero.
                ConfirmedByCapture = true,
            },
            new()
            {
                Code = (byte)SasExceptionCode.ReelNStopped,
                Name = "reel_stopped",
                Description = "One reel came to rest, and where it stopped.",
                Payload =
                [
                    Binary("reel_number", 1, SasUnit.Index,
                        "Which reel, counted from one."),
                    Binary("physical_stop", 1, SasUnit.Index,
                        "Which physical position it stopped at."),
                ],
                DerivedFrom = "SAS_RTE_ReelStopped",
                // Measured 2026-09-13 forty-five times: the first byte ran 1 to 5 in order, one reel at
                // a time.
                ConfirmedByCapture = true,
            },
            new()
            {
                Code = (byte)SasExceptionCode.GameRecallEntryDisplayed,
                Name = "game_recall_entry_displayed",
                Description = "A past game was put on screen for someone to look at.",
                Payload =
                [
                    Bcd("game_number", 2, SasUnit.Index, "Which game was recalled."),
                    Bcd("recall_index", 2, SasUnit.Index, "How far back in that game's history."),
                ],
                DerivedFrom = "SAS_RTE_GameRecallEntry",
            },
            new()
            {
                Code = (byte)SasExceptionCode.CardHeldOrNotHeld,
                Name = "card_held_or_not_held",
                Description = "A card the player held or released.",
                Payload =
                [
                    Binary("card", 1, SasUnit.Code,
                        "The top bit set for held and clear for released, the low bits the card's place from 0 on the left."),
                ],
                DerivedFrom = "SAS_RTE_CardHeld",
            },
            new()
            {
                Code = (byte)SasExceptionCode.GameSelected,
                Name = "game_selected",
                Description = "The player chose a different game.",
                Payload =
                [
                    Bcd("game_number", 2, SasUnit.Index, "Which game is now selected."),
                ],
                DerivedFrom = "SAS_RTE_GameSelected",
                // Measured 2026-09-13 when the game menu was pressed.
                ConfirmedByCapture = true,
            },
            new()
            {
                Code = (byte)SasExceptionCode.SessionStarted,
                Name = "session_started",
                Description = "A play session began.",
                Payload =
                [
                    // The only two events that begin with a length, and the only two whose numbers
                    // the library writes without converting to binary coded decimal first. Both
                    // are read as the library writes them rather than as the other eight are.
                    Binary("length", 1, SasUnit.Count, "How many bytes follow."),
                    Binary("game_number", 2, SasUnit.Index, "Which game the session is on."),
                    Binary("denomination", 1, SasUnit.Code, "What the player is betting in."),
                    Binary("wager", 4, SasUnit.Credits, "What was staked."),
                    Binary("games_in_session", 2, SasUnit.Count,
                        "How many games have been played in this session."),
                ],
                DerivedFrom = "SAS_RTE_SessionStart",
            },
            new()
            {
                Code = (byte)SasExceptionCode.SessionEnd,
                Name = "session_end",
                Description = "A play session finished, with what it came to.",
                Payload =
                [
                    Binary("length", 1, SasUnit.Count, "How many bytes follow."),
                    Binary("games", 2, SasUnit.Count, "Games played in the session."),
                    Binary("sessions", 4, SasUnit.Count, "Sessions the machine has had."),
                    Binary("session_wins", 4, SasUnit.Credits, "What the session paid."),
                    Binary("extra_coin_in", 4, SasUnit.Credits,
                        "Coin in beyond what the session itself staked."),
                ],
                DerivedFrom = "SAS_RTE_SessionEnd",
            },
        ];

        return events.ToFrozenDictionary(e => e.Code);
    }
}
