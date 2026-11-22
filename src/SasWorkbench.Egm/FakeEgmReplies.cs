using System.Globalization;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Tickets;

namespace SasWorkbench.Egm;

/// <summary>
/// What the software gaming machine answers each long poll with.
/// </summary>
/// <remarks>
/// <para>
/// One rule decides whether a command is here at all: this machine answers where the answer is
/// something it actually holds - a meter it keeps, or a meter that is genuinely zero because it
/// has no such machinery - and refuses where it would have to invent one. It has no progressive,
/// no legacy bonus, no reels and no cards, so the commands that report those are refused and stay
/// refused; a zero from a machine that cannot count the thing is not an honest zero.
/// </para>
/// <para>
/// Where the machine library answers a command with silence rather than with a reply - a
/// validation command on a machine not configured for that validation, a question about a game it
/// does not have - this machine is silent too. Silence and a refusal say different things, and a
/// host has to be able to tell them apart.
/// </para>
/// </remarks>
public sealed partial class FakeEgm
{
    private Dictionary<string, object>? ValuesFor(SasMessageLayout layout, ReadOnlySpan<byte> request) => layout.Command switch
    {
        0x0F => new Dictionary<string, object>
        {
            ["total_cancelled_credits"] = Meters.Read(SasMeterCode.TotalCancelledCredits),
            ["total_coin_in"] = Meters.Read(SasMeterCode.TotalCoinInCredits),
            ["total_coin_out"] = Meters.Read(SasMeterCode.TotalCoinOutCredits),
            ["total_drop"] = Meters.Read(SasMeterCode.TotalDropIn_Crd),
            ["total_jackpot"] = Meters.Read(SasMeterCode.TotalJackpotCredits),
            ["games_played"] = Meters.Read(SasMeterCode.GamesPlayed),
        },
        0x10 => Single("total_cancelled_credits", SasMeterCode.TotalCancelledCredits),
        0x11 => Single("total_coin_in", SasMeterCode.TotalCoinInCredits),
        0x12 => Single("total_coin_out", SasMeterCode.TotalCoinOutCredits),
        0x13 => Single("total_drop", SasMeterCode.TotalDropIn_Crd),
        0x14 => Single("total_jackpot", SasMeterCode.TotalJackpotCredits),
        0x15 => Single("games_played", SasMeterCode.GamesPlayed),
        0x16 => Single("games_won", SasMeterCode.GamesWon),
        0x17 => Single("games_lost", SasMeterCode.GamesLost),
        0x1A => Single("current_credits", SasMeterCode.CurrentCredits),
        0x1E => new Dictionary<string, object>
        {
            ["bills_1"] = Meters.Read(SasMeterCode.TotalNumberOfUSD1BillsAccepted),
            ["bills_5"] = Meters.Read(SasMeterCode.TotalNumberOfUSD5BillsAccepted),
            ["bills_10"] = Meters.Read(SasMeterCode.TotalNumberOfUSD10BillsAccepted),
            ["bills_20"] = Meters.Read(SasMeterCode.TotalNumberOfUSD20BillsAccepted),
            ["bills_50"] = Meters.Read(SasMeterCode.TotalNumberOfUSD50BillsAccepted),
            ["bills_100"] = Meters.Read(SasMeterCode.TotalNumberOfUSD100BillsAccepted),
        },
        0x1F => new Dictionary<string, object>
        {
            ["game_id"] = _options.GameId,
            ["additional_id"] = _options.AdditionalId,
            ["denomination"] = DenominationCode,
            ["max_bet"] = _options.MaxBet,
            ["progressive_group"] = _options.ProgressiveGroup,
            ["game_options"] = _options.GameOptions,
            ["paytable_id"] = _options.PaytableId,
            ["base_percentage"] = _options.BasePercentage,
        },
        0x20 => new Dictionary<string, object>
        {
            ["total_bill_value"] = Interlocked.Read(ref _billValueInDollars),
        },
        0x2C => new Dictionary<string, object> { ["hopper_level"] = (long)_options.HopperLevel },
        0x54 => new Dictionary<string, object>
        {
            ["length"] = _options.SasVersion.Length + _options.SerialNumber.Length,
            ["sas_version"] = _options.SasVersion,
            ["serial_number"] = _options.SerialNumber,
        },
        0x7E => DateAndTime(),
        0xB1 => new Dictionary<string, object> { ["denomination"] = DenominationCode },
        0xA0 => EnabledFeatures(request),
        0x6F or 0xAF => SelectedMeters(request),
        0x3D => LastCashout(),
        0x70 => TicketInEscrow(),
        0x71 => RedeemTicket(request),
        0x73 => RegisterGamingMachine(request),
        0x74 => LockAndStatus(request),
        0x90 => LegacyBonusWin(),
        0x9A => LegacyBonusMeters(layout, request),
        0x7B => ExtendedValidationStatus(),

        0x18 => GamesSince(),
        0x19 => new Dictionary<string, object>
        {
            ["total_coin_in"] = Meters.Read(SasMeterCode.TotalCoinInCredits),
            ["total_coin_out"] = Meters.Read(SasMeterCode.TotalCoinOutCredits),
            ["total_drop"] = Meters.Read(SasMeterCode.TotalDropIn_Crd),
            ["total_jackpot"] = Meters.Read(SasMeterCode.TotalJackpotCredits),
            ["games_played"] = Meters.Read(SasMeterCode.GamesPlayed),
        },
        0x1B => HandpayInformation(),
        0x1C => new Dictionary<string, object>
        {
            ["total_coin_in"] = Meters.Read(SasMeterCode.TotalCoinInCredits),
            ["total_coin_out"] = Meters.Read(SasMeterCode.TotalCoinOutCredits),
            ["total_drop"] = Meters.Read(SasMeterCode.TotalDropIn_Crd),
            ["total_jackpot"] = Meters.Read(SasMeterCode.TotalJackpotCredits),
            ["games_played"] = Meters.Read(SasMeterCode.GamesPlayed),
            ["games_won"] = Meters.Read(SasMeterCode.GamesWon),
            ["slot_door_opened"] = Meters.Read(SasMeterCode.NumberSlotDoorOpened),
            ["power_reset"] = Meters.Read(SasMeterCode.NumberACPowerLost),
        },
        0x2A => Single("true_coin_in", SasMeterCode.TotalCreditsFromCoinAcceptor),
        0x2B => Single("true_coin_out", SasMeterCode.TotalCreditsPaidFromHopper),
        0x2D => HandpayCancelledCredits(layout, request),
        0x2F => MetersForGame(request),
        0x46 => Single("bills_accepted_credits", SasMeterCode.TotalCreditsFromBillsAccepted),

        // The same library meter as long poll $2A, so this machine's external coin acceptor and
        // its coin acceptor can never disagree. Recorded rather than modelled around.
        0x47 => Single("external_coin_acceptor_credits", SasMeterCode.TotalCreditsFromCoinAcceptor),
        0x48 => LastAcceptedBill(),
        0x49 => Single("bills_in_stacker", SasMeterCode.NumberOfBillsCurrentlyInTheStacker),
        0x4A => Single("stacker_credits", SasMeterCode.TotalValueOfBillsCurrentlyInTheStacker_Crd),
        0x4C => SecureEnhancedValidationId(layout, request),
        0x4D => NoAnswer,
        0x4F => HopperStatus(),
        0x50 => ValidationMeters(layout, request),
        0x51 => new Dictionary<string, object> { ["games"] = Games },
        0x52 => GameMeters(layout, request),
        0x53 => GameConfiguration(layout, request),

        // Nothing here selects a game, so nothing is selected. The machine the catalogue was
        // swept against answered the same zero for the same reason.
        0x55 => new Dictionary<string, object> { ["selected_game"] = 0L },
        0x56 => EnabledGames(),
        0x57 => PendingCashout(),
        0xB2 => EnabledPlayerDenominations(),
        0xB3 => new Dictionary<string, object>
        {
            ["token_denomination"] = _options.TokenDenomination,
        },
        0xB4 => WagerCategory(layout, request),
        0xB5 => ExtendedGameInformation(layout, request),

        // Twenty dedicated bill counts, one meter each. Seven are denominations this machine's
        // note acceptor takes and counts; the other thirteen are notes it cannot be offered, so
        // they read zero because it has never taken one and not because it fails to count them.
        _ when BillCounts.TryGetValue(layout.Command, out (string Field, SasMeterCode Meter) bill)
            => Single(bill.Field, bill.Meter),

        // Everything else has a layout but no source of values here.
        _ => null,
    };

    /// <summary>How many games this machine has. One, and its paytable is that one.</summary>
    private const long Games = 1;

    /// <summary>
    /// The answer to a command this machine does not answer at all.
    /// </summary>
    /// <remarks>
    /// Distinct from a refusal, and the difference is the machine library's. A refusal says the
    /// command was understood and declined; silence says it was never acted on. The library is
    /// silent where a validation command reaches a machine not configured for that validation, and
    /// where a question names a game the machine does not have, so this machine is silent there
    /// too.
    /// </remarks>
    private static readonly Dictionary<string, object> NoAnswer = [];

    /// <summary>
    /// The dedicated bill-count commands, each naming its field and the meter behind it.
    /// </summary>
    /// <remarks>
    /// Read from the library's own handlers, every one of which is a single call naming one meter.
    /// The fields are deliberately not offered as meters a script may read by name: the library
    /// answers these through a shared handler that divides by the accounting denomination under
    /// two of its meter modes, while long poll <c>$1E</c> answers six of the same meters undivided.
    /// </remarks>
    private static readonly Dictionary<byte, (string Field, SasMeterCode Meter)> BillCounts = new()
    {
        [0x31] = ("bills_1", SasMeterCode.TotalNumberOfUSD1BillsAccepted),
        [0x32] = ("bills_2", SasMeterCode.TotalNumberOfUSD2BillsAccepted),
        [0x33] = ("bills_5", SasMeterCode.TotalNumberOfUSD5BillsAccepted),
        [0x34] = ("bills_10", SasMeterCode.TotalNumberOfUSD10BillsAccepted),
        [0x35] = ("bills_20", SasMeterCode.TotalNumberOfUSD20BillsAccepted),
        [0x36] = ("bills_50", SasMeterCode.TotalNumberOfUSD50BillsAccepted),
        [0x37] = ("bills_100", SasMeterCode.TotalNumberOfUSD100BillsAccepted),
        [0x38] = ("bills_500", SasMeterCode.TotalNumberOfUSD500BillsAccepted),
        [0x39] = ("bills_1000", SasMeterCode.TotalNumberOfUSD1000BillsAccepted),
        [0x3A] = ("bills_200", SasMeterCode.TotalNumberOfUSD200BillsAccepted),
        [0x3B] = ("bills_25", SasMeterCode.TotalNumberOfUSD25BillsAccepted),
        [0x3C] = ("bills_2000", SasMeterCode.TotalNumberOfUSD2000BillsAccepted),
        [0x3E] = ("bills_2500", SasMeterCode.TotalNumberOfUSD2500BillsAccepted),
        [0x3F] = ("bills_5000", SasMeterCode.TotalNumberOfUSD5000BillsAccepted),
        [0x40] = ("bills_10000", SasMeterCode.TotalNumberOfUSD10KBillsAccepted),
        [0x41] = ("bills_20000", SasMeterCode.TotalNumberOfUSD20KBillsAccepted),
        [0x42] = ("bills_25000", SasMeterCode.TotalNumberOfUSD25KBillsAccepted),
        [0x43] = ("bills_50000", SasMeterCode.TotalNumberOfUSD50KBillsAccepted),
        [0x44] = ("bills_100000", SasMeterCode.TotalNumberOfUSD100KBillsAccepted),
        [0x45] = ("bills_250", SasMeterCode.TotalNumberOfUSD250BillsAccepted),
    };

    /// <summary>
    /// Games played since this machine came up, and since its slot door was last shut.
    /// </summary>
    /// <remarks>
    /// Both are meters the library keeps a code for, and its own handler for this command reads
    /// them by code like any other - which an earlier note here denied. They are kept in the meter
    /// store for that reason, so a selected-meter poll asking for either by code gets the same
    /// number this reply gives. Each field is two bytes of binary coded decimal, so a count past
    /// 9999 is capped here rather than written into a field that cannot hold it.
    /// </remarks>
    private Dictionary<string, object> GamesSince() => new()
    {
        ["games_since_power_up"] =
            Math.Min(Meters.Read(SasMeterCode.GamesSinceLastReset), 9999),
        ["games_since_door_closure"] =
            Math.Min(Meters.Read(SasMeterCode.GamesSinceSlotDoorClosure), 9999),
    };

    /// <summary>
    /// Credits cancelled by a handpay for one game, which on this machine is none.
    /// </summary>
    /// <remarks>
    /// A different meter from the one long poll <c>$10</c> answers with, which the library settles
    /// by reading a different code, and zero here for the same reason: nothing on this machine
    /// hands anything back.
    /// </remarks>
    private Dictionary<string, object>? HandpayCancelledCredits(
        SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        if (GameNumber(layout, request) is not { } game)
            return NoAnswer;

        return new Dictionary<string, object>
        {
            ["game_number"] = game,
            ["handpay_cancelled_credits"] =
                Meters.Read(SasMeterCode.TotalHandPaidCancelledCredits),
        };
    }

    /// <summary>
    /// The four accounting meters for one game, which on a machine with one game are its own.
    /// </summary>
    private Dictionary<string, object>? GameMeters(
        SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        if (GameNumber(layout, request) is not { } game)
            return NoAnswer;

        return new Dictionary<string, object>
        {
            ["game_number"] = game,
            ["total_coin_in"] = Meters.Read(SasMeterCode.TotalCoinInCredits),
            ["total_coin_out"] = Meters.Read(SasMeterCode.TotalCoinOutCredits),
            ["total_jackpot"] = Meters.Read(SasMeterCode.TotalJackpotCredits),
            ["games_played"] = Meters.Read(SasMeterCode.GamesPlayed),
        };
    }

    /// <summary>
    /// How one game is configured, which is the configuration the identification reply publishes
    /// because this machine has one game.
    /// </summary>
    private Dictionary<string, object>? GameConfiguration(
        SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        if (GameNumber(layout, request) is not { } game)
            return NoAnswer;

        return new Dictionary<string, object>
        {
            ["game_number"] = game,
            ["game_id"] = _options.GameId,
            ["additional_id"] = _options.AdditionalId,
            ["denomination"] = DenominationCode,
            ["max_bet"] = _options.MaxBet,
            ["progressive_group"] = _options.ProgressiveGroup,
            ["game_options"] = _options.GameOptions,
            ["paytable_id"] = _options.PaytableId,
            ["base_percentage"] = FourDigits(_options.BasePercentage),
        };
    }

    /// <summary>
    /// What the last note taken was, and how many of that denomination the machine has.
    /// </summary>
    /// <remarks>
    /// Zero, zero and the one-dollar count until a note has been taken. That is not a stand-in for
    /// an answer: the library reads a stored denomination code that starts at zero, and zero is the
    /// one-dollar note, so a machine that has taken nothing answers exactly this.
    /// </remarks>
    private Dictionary<string, object> LastAcceptedBill()
    {
        byte country;
        byte denomination;
        lock (_gate)
        {
            country = _lastBillCountryCode;
            denomination = _lastBillDenominationCode;
        }

        return new Dictionary<string, object>
        {
            ["country_code"] = country,
            ["denomination_code"] = denomination,
            ["count"] = Meters.Read(CountMeterFor(denomination)),
        };
    }

    /// <summary>How this machine has been told to validate, and how it is set now.</summary>
    /// <remarks>
    /// Only the form with an empty control mask reaches here, so nothing is being set and the
    /// reply is a reading. Every status bit reads zero, and that is a statement rather than a
    /// placeholder: nothing in this machine sets one, and the bits are not derived from the
    /// validation style it does model — the two are different things in the library, and guessing
    /// a bit pattern from the style is exactly the kind of invention a layout is meant to stop.
    /// </remarks>
    private Dictionary<string, object> ExtendedValidationStatus() => new()
    {
        ["length"] = 0x0A,
        ["asset_number"] = (long)AssetNumber,
        ["current_validation_status"] = 0L,
        ["cashable_ticket_expiration"] = 0L,
        ["restricted_ticket_expiration"] = 0L,
    };

    /// <summary>
    /// What the hopper is doing, on a machine that has no hopper to ask.
    /// </summary>
    /// <remarks>
    /// The status is the library's own "unknown", and the percentage full is the value its handler
    /// reads as "this machine cannot detect it", which leaves the level out of the reply
    /// altogether. Answering a percentage would be inventing a measurement from a device that is
    /// not there; saying it cannot be measured is a fact about this machine.
    /// </remarks>
    private static Dictionary<string, object> HopperStatus() => new()
    {
        ["length"] = 2,
        ["status"] = (byte)0xFF,
        ["percent_full"] = (byte)0xFF,
    };

    /// <summary>
    /// How many tickets of one validation type this machine has passed, and their value.
    /// </summary>
    /// <remarks>
    /// The meter pairs are the library's, from the branch it takes when its meters are not in the
    /// immediate manual mode a game drives itself. A type it names no pair for is answered with
    /// zeroes rather than refused, which is also what the library does.
    /// </remarks>
    private Dictionary<string, object>? ValidationMeters(
        SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        SasDecoded asked = SasMessageDecoder.DecodeRequest(layout, request);
        if (asked.Number("validation_type") is not { } rawType)
            return null;

        var type = (byte)rawType;
        (SasMeterCode Quantity, SasMeterCode Cents)? pair = type switch
        {
            0x00 or 0x02 => (SasMeterCode.RegularCashableTicketOut_Qty,
                SasMeterCode.RegularCashableTicketOut_Cents),
            0x01 or 0x03 => (SasMeterCode.RestrictedTicketOut_Qty,
                SasMeterCode.RestrictedTicketOut_Cents),
            0x04 => (SasMeterCode.DebitTicketOut_Qty, SasMeterCode.DebitTicketOut_Cents),
            0x10 => (SasMeterCode.ValidatedCancelledCreditHandpayReceiptPrinted_Qty,
                SasMeterCode.ValidatedCancelledCreditHandpayReceiptPrinted_Cents),
            0x20 => (SasMeterCode.ValidatedJackpotHandpayReceiptPrinted_Qty,
                SasMeterCode.ValidatedJackpotHandpayReceiptPrinted_Cents),
            0x40 => (SasMeterCode.ValidatedCancelledCreditHandpayNoReceipt_Qty,
                SasMeterCode.ValidatedCancelledCreditHandpayNoReceipt_Cents),
            0x60 => (SasMeterCode.ValidatedJackpotHandpayNoReceipt_Qty,
                SasMeterCode.ValidatedJackpotHandpayNoReceipt_Cents),
            0x80 => (SasMeterCode.RegularCashableTicketIn_Qty,
                SasMeterCode.RegularCashableTicketIn_Cents),
            0x81 => (SasMeterCode.RestrictedTicketIn_Qty, SasMeterCode.RestrictedTicketIn_Cents),
            0x82 => (SasMeterCode.NonRestrictedTicketIn_Qty,
                SasMeterCode.NonRestrictedTicketIn_Cents),
            _ => null,
        };

        return new Dictionary<string, object>
        {
            ["validation_type"] = type,
            ["quantity"] = pair is { } known ? Meters.Read(known.Quantity) : 0L,
            ["amount"] = pair is { } counted ? Meters.Read(counted.Cents) : 0L,
        };
    }

    /// <summary>Which games are enabled: the one this machine has.</summary>
    private static Dictionary<string, object> EnabledGames() => new()
    {
        ["length"] = 1 + (Games * 2),
        ["count"] = Games,
        ["games.count"] = Games,
        ["games[0].game_number"] = 1L,
    };

    /// <summary>
    /// What this machine is waiting to be told how to pay out, which is nothing.
    /// </summary>
    /// <remarks>
    /// This machine performs standard validation and prints its ticket the moment it is asked to,
    /// so no cashout is ever held waiting for a host to validate one. The library's own name for
    /// that state is the code answered here, and the amount that goes with it is zero.
    /// </remarks>
    private static Dictionary<string, object> PendingCashout() => new()
    {
        ["cashout_type"] = (byte)0x80,
        ["amount"] = 0L,
    };

    /// <summary>
    /// Which denominations a player may choose, on a machine that was told.
    /// </summary>
    /// <remarks>
    /// The library answers this only on a machine advertising multiple denominations, and is
    /// silent otherwise. A machine built without a list of player denominations has none to
    /// publish, so it is silent here too rather than answering an empty list, which would be a
    /// claim that a player may choose nothing.
    /// </remarks>
    private Dictionary<string, object> EnabledPlayerDenominations()
    {
        byte[] codes =
        [
            .. _options.PlayerDenominationsInCents
                .Select(SasDenomination.CodeFor)
                .Where(code => code is not null)
                .Select(code => code!.Value)
                .Distinct(),
        ];

        if (codes.Length == 0)
            return NoAnswer;

        var values = new Dictionary<string, object>
        {
            ["length"] = 1 + codes.Length,
            ["count"] = codes.Length,
            ["denominations.count"] = codes.Length,
        };

        for (int i = 0; i < codes.Length; i++)
            values[$"denominations[{i}].denomination"] = codes[i];

        return values;
    }

    /// <summary>
    /// The identifier and sequence number a machine performing secure enhanced validation puts on
    /// its tickets.
    /// </summary>
    /// <remarks>
    /// The library answers this only on a machine configured for that validation, and is silent
    /// otherwise; so is this one. What comes back is what the machine now holds, which is what the
    /// host just sent unless the host sent a zero identifier - the library's way of asking without
    /// setting.
    /// </remarks>
    private Dictionary<string, object>? SecureEnhancedValidationId(
        SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        if (_options.CashoutValidation != SasCashoutValidation.SecureEnhanced)
            return NoAnswer;

        SasDecoded asked = SasMessageDecoder.DecodeRequest(layout, request);
        if (asked.Number("validation_id") is not { } identifier
            || asked.Number("sequence_number") is not { } sequence)
        {
            return null;
        }

        lock (_gate)
        {
            if (identifier != 0)
            {
                _secureValidationId = (uint)identifier;
                _secureSequenceNumber = (uint)sequence;
            }

            return new Dictionary<string, object>
            {
                ["validation_id"] = (long)_secureValidationId,
                ["sequence_number"] = (long)_secureSequenceNumber,
            };
        }
    }

    /// <summary>
    /// What one wager category pays back, and how much has been wagered in it.
    /// </summary>
    /// <remarks>
    /// This machine has one paytable and no wager categories, which the library has an answer for
    /// rather than a gap: asked about category zero it reports the machine's own payback
    /// percentage, and for the machine as a whole the coin-in meter beside it is the machine's own.
    /// Asked about a category the game does not have it answers a zero percentage and a meter zero
    /// bytes wide; asked about any category of the machine as a whole it answers nothing. All
    /// three are the handler's, not a choice made here.
    /// </remarks>
    private Dictionary<string, object>? WagerCategory(
        SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        SasDecoded asked = SasMessageDecoder.DecodeRequest(layout, request);
        if (GameNumber(layout, request) is not { } game
            || asked.Number("wager_category") is not { } category)
        {
            return NoAnswer;
        }

        if (category != 0)
        {
            if (game == 0)
                return NoAnswer;

            return new Dictionary<string, object>
            {
                ["length"] = 9,
                ["game_number"] = game,
                ["wager_category"] = category,
                ["payback_percentage"] = "0000",
                ["meter_size"] = 0,
                ["coin_in"] = 0L,
            };
        }

        // Category zero of the machine as a whole reports the coin-in meter; category zero of a
        // game reports what its own wager categories have taken, and this machine has none.
        long coinIn = game == 0 ? Meters.Read(SasMeterCode.TotalCoinInCredits) : 0;
        int width = WidthOf((ushort)SasMeterCode.TotalCoinInCredits, coinIn);

        return new Dictionary<string, object>
        {
            ["length"] = 9 + width,
            ["game_number"] = game,
            ["wager_category"] = category,
            ["payback_percentage"] = FourDigits(_options.BasePercentage),
            ["meter_size"] = width,
            ["coin_in"] = coinIn,
        };
    }

    /// <summary>
    /// One game's name, paytable, largest bet and progressive levels.
    /// </summary>
    /// <remarks>
    /// The progressive levels are zero because this machine belongs to no progressive group, and
    /// the library zeroes them for exactly that reason rather than leaving whatever a game
    /// configured. The wager category count is zero because it has none.
    /// </remarks>
    private Dictionary<string, object>? ExtendedGameInformation(
        SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        if (GameNumber(layout, request) is not { } game)
            return NoAnswer;

        string name = _options.GameName;
        string paytable = _options.PaytableId;

        return new Dictionary<string, object>
        {
            ["length"] = 13 + name.Length + paytable.Length,
            ["game_number"] = game,
            ["max_bet"] = (long)_options.MaxBet,
            ["progressive_group"] = _options.ProgressiveGroup,
            ["progressive_levels"] = 0L,
            ["name_length"] = name.Length,
            ["game_name"] = name,
            ["paytable_length"] = paytable.Length,
            ["paytable_name"] = paytable,
            ["wager_categories"] = 0L,
        };
    }

    /// <summary>
    /// Answers a fixed-width selected-meter poll: one byte of meter code, and a value as wide as
    /// the library's own table makes that meter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The narrower relative of long poll <c>$6F</c>: nothing in this reply states a width, so a
    /// reader that does not hold the library's table cannot walk one. The same two meters are left
    /// out here as there, because the library's handler leaves them out in both.
    /// </para>
    /// <para>
    /// What this machine does not model is the library's per-game meter classification, which
    /// omits a meter that is not a multi-game meter when the game number is not zero. It has one
    /// game, so its game meters and its machine meters are the same values, and a case that needs
    /// that distinction needs a machine with more than one game.
    /// </para>
    /// </remarks>
    private Dictionary<string, object>? MetersForGame(ReadOnlySpan<byte> request)
    {
        // Address, command, length, and two bytes of game number at the very least.
        if (request.Length < 5)
            return null;

        int declared = request[2];
        int codes = declared - 2;
        int firstCode = 5;
        if (codes < 0 || firstCode + codes > request.Length)
            return null;

        long game = ((request[3] >> 4) * 1000) + ((request[3] & 0x0F) * 100)
                    + ((request[4] >> 4) * 10) + (request[4] & 0x0F);

        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        int entries = 0;
        int payload = 2;

        for (int i = 0; i < codes; i++)
        {
            ushort code = request[firstCode + i];
            if (!IsReportableMeter(code))
                continue;

            values[$"meters[{entries}].code"] = code;
            values[$"meters[{entries}].value"] = Meters.Read(code);
            entries++;
            payload += 1 + SasMeterWidths.Of(code);
        }

        values["length"] = payload;
        values["game_number"] = game;
        values["meters.count"] = entries;
        return values;
    }

    /// <summary>
    /// Which game a request names, or null when it names one this machine does not have.
    /// </summary>
    /// <remarks>
    /// The library returns without answering when the game number is past the games the machine
    /// holds, so a caller turns a null here into silence rather than into a refusal.
    /// </remarks>
    private static long? GameNumber(SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        SasDecoded asked = SasMessageDecoder.DecodeRequest(layout, request);
        return asked.Number("game_number") is { } game && game >= 0 && game <= Games
            ? game
            : null;
    }

    /// <summary>A payback percentage as the four characters the reply carries it as.</summary>
    private static string FourDigits(int hundredthsOfPercent) =>
        Math.Clamp(hundredthsOfPercent, 0, 9999).ToString("D4", CultureInfo.InvariantCulture);

    /// <summary>Which count meter a note of this denomination code moves.</summary>
    private static SasMeterCode CountMeterFor(byte denomination) => denomination switch
    {
        1 => SasMeterCode.TotalNumberOfUSD2BillsAccepted,
        2 => SasMeterCode.TotalNumberOfUSD5BillsAccepted,
        3 => SasMeterCode.TotalNumberOfUSD10BillsAccepted,
        4 => SasMeterCode.TotalNumberOfUSD20BillsAccepted,
        5 => SasMeterCode.TotalNumberOfUSD25BillsAccepted,
        6 => SasMeterCode.TotalNumberOfUSD50BillsAccepted,
        7 => SasMeterCode.TotalNumberOfUSD100BillsAccepted,
        8 => SasMeterCode.TotalNumberOfUSD200BillsAccepted,
        9 => SasMeterCode.TotalNumberOfUSD250BillsAccepted,
        10 => SasMeterCode.TotalNumberOfUSD500BillsAccepted,
        11 => SasMeterCode.TotalNumberOfUSD1000BillsAccepted,
        _ => SasMeterCode.TotalNumberOfUSD1BillsAccepted,
    };

    /// <summary>
    /// What this machine says it can do.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the validation half of it is real. The validation style is the machine's own, and the
    /// ticket redemption bit follows it rather than standing on its own, because section 15 does
    /// not allow redemption alongside standard validation — a machine claiming both would be
    /// describing something no host may act on. Everything else is reported as absent: this
    /// fixture does not do funds transfer, component authentication, tournaments or bonusing, and
    /// a bit set here would be a claim a test could come to rely on.
    /// </para>
    /// <para>
    /// The validation extensions bit is one a real machine sets alongside the enhanced styles and
    /// this one leaves clear, deliberately: the commands it advertises are not implemented here.
    /// It is the one place the two peers are expected to disagree about this reply, so a case
    /// that runs against both should not assert on it until those commands exist.
    /// </para>
    /// <para>
    /// The game number is echoed rather than looked up, because this machine has one game. A
    /// number past the games it has is refused, which is what a machine does with a question
    /// about a game it does not have.
    /// </para>
    /// </remarks>
    private Dictionary<string, object>? EnabledFeatures(ReadOnlySpan<byte> request)
    {
        SasDecoded asked = SasMessageDecoder.DecodeRequest(SasLayouts.For(0xA0)!, request);
        if (asked.Number("game_number") is not { } game || game > 1)
            return null;

        SasCashoutValidation validation = _options.CashoutValidation;
        bool redemption = validation is SasCashoutValidation.SecureEnhanced
            or SasCashoutValidation.System;

        return new Dictionary<string, object>
        {
            ["game_number"] = game,
            // Bit 1 is AFT bonus awards (AFT_BONUS_AWARDS_BIT) and bit 2 legacy bonus awards, each set while the operator has it on.
            ["feature_codes_1"] = (byte)((_aftOn && AftBonusOn ? 0x02 : 0x00) | LegacyBonusFeatureBits()),
            ["validation_style"] = (byte)validation,
            ["validation_extensions"] = (byte)0,
            ["ticket_redemption"] = (byte)(redemption ? 1 : 0),
            // Bit 6 is AFT, set on a machine that performs transfers as the library tests it (lp.cpp:6430).
            ["feature_codes_2"] = (byte)(_aftOn ? 0x40 : 0x00),
            ["feature_codes_3"] = (byte)0x00,
            ["reserved"] = new byte[3],
        };
    }

    /// <summary>
    /// The number and value of the ticket this machine printed most recently.
    /// </summary>
    /// <remarks>
    /// Zero and zero until something has been cashed out, which is what a machine on fresh
    /// memory answers: the record is real and empty rather than absent, so a host asking before
    /// anything has been printed gets a well-formed reply saying nothing has.
    /// </remarks>
    private Dictionary<string, object> LastCashout()
    {
        lock (_gate)
        {
            return new Dictionary<string, object>
            {
                ["validation_number"] = (long)_lastValidationNumber,
                ["ticket_amount"] = _lastTicketAmountCents,
            };
        }
    }

    /// <summary>
    /// Says whether a ticket is being held, and quotes the number printed on it.
    /// </summary>
    /// <remarks>
    /// The amount is zero and stays zero. The machine has read a barcode, not a value: what the
    /// ticket is worth is the host's to declare, and a machine that filled this in would be
    /// answering a question it was never asked.
    /// </remarks>
    private Dictionary<string, object> TicketInEscrow()
    {
        SasTicketStatus status;
        ulong number;
        lock (_gate)
        {
            status = _redemption.Ask();
            number = _redemption.ValidationNumber;
        }

        if (status != SasTicketStatus.TICKET_STATUS_TICKET_IN_ESCROW)
        {
            return new Dictionary<string, object>
            {
                ["length"] = 1,
                ["ticket_status"] = (byte)status,
            };
        }

        return new Dictionary<string, object>
        {
            ["length"] = 16,
            ["ticket_status"] = (byte)status,
            ["transfer_amount"] = 0L,
            ["parsing_code"] = 0,
            ["validation_data"] = (long)number,
        };
    }

    /// <summary>
    /// Takes the host's verdict on the ticket being held, and reports what became of it.
    /// </summary>
    /// <remarks>
    /// The money moves inside this call, before the reply is built, because the reply is what the
    /// host reads to learn that it moved. The exception that announces the same thing is queued
    /// rather than sent, so it arrives on a following general poll — which is the order a real
    /// machine produces and the order a script written against one expects.
    /// </remarks>
    private Dictionary<string, object>? RedeemTicket(ReadOnlySpan<byte> request)
    {
        SasMessageLayout layout = SasLayouts.All[0x71];
        SasDecoded command = SasMessageDecoder.DecodeRequest(layout, request);
        if (command.Failure is not null || command.Number("transfer_code") is not { } rawCode)
            return null;

        var code = (SasTicketTransferCode)(byte)rawCode;
        long amount = command.Number("transfer_amount") ?? 0;
        var number = (ulong)(command.Number("validation_data") ?? 0);

        bool redeemed;
        SasTicketMachineStatus status;
        long recorded;
        ulong quoted;

        lock (_gate)
        {
            redeemed = _redemption.Redeem(code, amount, number);

            // Read before anything else happens. The answer to the command that accepts a ticket
            // is "redemption pending", not "redeemed": the machine has agreed to take it and has
            // not taken it yet, and a host told otherwise would have no reason ever to ask again.
            status = _redemption.MachineStatus;
            recorded = _redemption.AmountCents;
            quoted = _redemption.ValidationNumber;

            if (redeemed)
            {
                TakeTicket(_redemption.AmountCents, code);
                _redemption.Settle(code);
            }
            else if (code == SasTicketTransferCode.TTC_REQUEST_FOR_CURRENT_TICKET_STATUS)
            {
                // Reading the outcome is what closes the cycle, so the acceptor is free again
                // only once the host has actually been told.
                _redemption.CloseIfRead();
            }
        }

        if (redeemed)
            RaiseException(SasExceptionCode.TicketTransferComplete);

        return Answer(status, recorded, quoted);

        Dictionary<string, object> Answer(SasTicketMachineStatus answered, long cents, ulong validation)
        {
            if (!StatusCarries(answered))
            {
                return new Dictionary<string, object>
                {
                    ["length"] = 1,
                    ["machine_status"] = (byte)answered,
                };
            }

            return new Dictionary<string, object>
            {
                ["length"] = 16,
                ["machine_status"] = (byte)answered,
                ["transfer_amount"] = cents,
                ["parsing_code"] = 0,
                ["validation_data"] = (long)validation,
            };
        }
    }

    /// <summary>Whether a status answers with the ticket's amount and number, or with itself alone.</summary>
    private static bool StatusCarries(SasTicketMachineStatus status) =>
        (byte)status < (byte)SasTicketMachineStatus.GMSC_NOT_COMPATIBLE_WITH_CURRENT_REDEMPTION_CYCLE;

    /// <summary>
    /// Moves the meters a redeemed ticket moves, and puts its value on the machine.
    /// </summary>
    /// <remarks>
    /// Which meters depends on what the host said the ticket was, not on what the ticket claims to
    /// be: a restricted ticket redeemed as cashable is money the host has agreed to pay in cash,
    /// and the machine counts what it was told.
    /// </remarks>
    private void TakeTicket(long cents, SasTicketTransferCode code)
    {
        switch (code)
        {
            case SasTicketTransferCode.TTC_VALID_RESTRICTED_PROMOTIONAL_TICKET:
                Meters.Increment(SasMeterCode.TotalSASRestrictedTicketIn_Cents, cents);
                Meters.Increment(SasMeterCode.TotalSASRestrictedTicketIn_Qty, 1);
                Meters.Increment(SasMeterCode.RestrictedTicketIn_Qty, 1);
                Meters.Increment(SasMeterCode.RestrictedTicketIn_Cents, cents);
                break;

            // Counted in credits rather than cents, because that is the only meter the protocol
            // keeps for it. There is no quantity meter for this kind, so none is moved.
            case SasTicketTransferCode.TTC_VALID_NONRESTRICTED_PROMOTIONAL_TICKET:
                Meters.Increment(SasMeterCode.TotalNonrestrictedPromotionalTicketIn_Crd,
                    cents / AccountingCents);
                Meters.Increment(SasMeterCode.NonRestrictedTicketIn_Qty, 1);
                Meters.Increment(SasMeterCode.NonRestrictedTicketIn_Cents, cents);
                break;

            default:
                Meters.Increment(SasMeterCode.TotalCashableTicketIn_Cents, cents);
                Meters.Increment(SasMeterCode.TotalSASCashableTicketIn_Qty, 1);
                Meters.Increment(SasMeterCode.RegularCashableTicketIn_Qty, 1);
                Meters.Increment(SasMeterCode.RegularCashableTicketIn_Cents, cents);
                break;
        }

        // Every kind of ticket feeds the machine-wide total, which is in accounting units - so a
        // ticket carrying an amount the unit does not divide leaves a remainder here. That is
        // inherent rather than a defect: a ticket carries any number of cents, and section 16's
        // divisibility rule constrains only the denominations a player may choose.
        Meters.Increment(SasMeterCode.TotalTicketIn_Crd, cents / AccountingCents);

        Meters.SetCurrentCredits(
            Meters.Read(SasMeterCode.CurrentCredits) + (cents / AccountingCents));
    }

    /// <summary>
    /// Answers a selected-meter poll, in the shape a machine answers it: one entry per meter it
    /// supports, each stating its own width, and nothing at all for one it does not.
    /// </summary>
    /// <remarks>
    /// Meters are answered in the order they were asked for, and an unsupported code is left out
    /// entirely rather than answered with zero - a host that assumed a fixed number of entries
    /// would read the next meter's code as this one's value. The door and power counts are refused
    /// here on purpose and are read with the legacy meters command instead.
    /// </remarks>
    private Dictionary<string, object>? SelectedMeters(ReadOnlySpan<byte> request)
    {
        // Address, command, length, and two bytes of game number at the very least.
        if (request.Length < 5)
            return null;

        int declared = request[2];
        int codes = (declared - 2) / 2;
        int firstCode = 5;
        if (codes < 0 || firstCode + (codes * 2) > request.Length)
            return null;

        long game = ((request[3] >> 4) * 1000) + ((request[3] & 0x0F) * 100)
                    + ((request[4] >> 4) * 10) + (request[4] & 0x0F);

        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        int entries = 0;
        int payload = 2;

        for (int i = 0; i < codes; i++)
        {
            // Binary, least significant byte first.
            ushort code = (ushort)(request[firstCode + (i * 2)]
                                   | (request[firstCode + (i * 2) + 1] << 8));

            if (!IsReportableMeter(code))
                continue;

            long value = Meters.Read(code);
            int size = WidthOf(code, value);

            values[$"meters[{entries}].code"] = code;
            values[$"meters[{entries}].size"] = size;
            values[$"meters[{entries}].value"] = value;
            entries++;
            payload += 3 + size;
        }

        values["length"] = payload;
        values["game_number"] = game;
        values["meters.count"] = entries;
        return values;
    }

    /// <summary>
    /// Whether a selected-meter poll may answer about this meter at all. The slot door and power
    /// reset counts are excluded by the machine rather than merely absent from it.
    /// </summary>
    private static bool IsReportableMeter(ushort code) =>
        code != (ushort)SasMeterCode.NumberSlotDoorOpened
        && code != (ushort)SasMeterCode.NumberACPowerLost
        && SasCatalogue.Meters.ContainsKey(code);

    /// <summary>
    /// How many bytes this meter's value takes. Four is the floor, five for a meter counted in
    /// cents, and the field widens as the value grows so that it never rolls over silently the
    /// way the fixed-width polls do.
    /// </summary>
    private static int WidthOf(ushort code, long value)
    {
        int floor = SasMeterUnits.UnitOf(code) == SasMeterUnit.Cents ? 5 : 4;
        int digits = value <= 0 ? 1 : (int)Math.Floor(Math.Log10(value)) + 1;
        return Math.Max(floor, (digits + 1) / 2);
    }

    /// <summary>
    /// A reply carrying one meter, answered the way the library's shared single-meter helper does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The division is not decoration and it is not this machine inventing a behaviour. Every
    /// command answered through here goes through <c>lpXX_SendMeter</c> in the library, which
    /// divides the meter by the accounting denomination in cents when the machine is in either of
    /// its two default meter modes and the meter is not one the library calls multigame. The bulk
    /// replies read the same meter and write it as it stands, so the two answer different numbers
    /// — which is the whole reason a name is attached to one command rather than the other.
    /// </para>
    /// <para>
    /// This machine is in full manual by default, which is the mode the gaming machine under test
    /// is in, so by default nothing is divided and every reply is what it always was.
    /// </para>
    /// </remarks>
    private Dictionary<string, object> Single(string field, SasMeterCode meter)
    {
        long value = Meters.Read(meter);
        if (_options.MeterMode.Divides() && !SasCatalogue.MultigameMeters.Contains((ushort)meter))
            value /= AccountingCents;

        return new Dictionary<string, object> { [field] = value };
    }

    private static Dictionary<string, object> DateAndTime()
    {
        DateTime now = DateTime.Now;
        return new Dictionary<string, object>
        {
            ["month"] = now.Month,
            ["day"] = now.Day,
            ["year"] = now.Year,
            ["hour"] = now.Hour,
            ["minute"] = now.Minute,
            ["second"] = now.Second,
        };
    }
}
