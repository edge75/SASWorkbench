using System.Runtime.InteropServices;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;

namespace SasWorkbench.Core.Scripting;

/// <summary>
/// What the environment a run is happening in can actually do.
/// </summary>
/// <remarks>
/// A capability is either present with a value or absent, and absent is always a skip rather
/// than a failure. This is what stops an environment problem being reported as a defect: a
/// suite run without a note acceptor should say so on the tests that needed one and stay green
/// on the rest, because a red suite that everybody knows to ignore is no suite at all.
/// </remarks>
public sealed class CapabilitySet
{
    private readonly Dictionary<string, string?> _values;

    public CapabilitySet(IEnumerable<KeyValuePair<string, string?>> values)
    {
        _values = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Nothing is available. Every requirement is unmet.</summary>
    public static CapabilitySet Empty { get; } = new([]);

    /// <summary>Every capability, sorted, for a report or an error message.</summary>
    public IReadOnlyList<string> Names =>
        _values.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>True when the capability is present at all.</summary>
    public bool Has(string key) => _values.ContainsKey(key);

    /// <summary>The value of a capability, or null when it is present without one.</summary>
    public string? Value(string key) => _values.GetValueOrDefault(key);

    /// <summary>Adds a capability, returning a new set. The original is untouched.</summary>
    public CapabilitySet With(string key, string? value = null)
    {
        var copy = new Dictionary<string, string?>(_values, StringComparer.OrdinalIgnoreCase)
        {
            [key] = value,
        };
        return new CapabilitySet(copy);
    }

    /// <summary>Adds several capabilities, returning a new set.</summary>
    public CapabilitySet With(IEnumerable<KeyValuePair<string, string?>> more)
    {
        var copy = new Dictionary<string, string?>(_values, StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string? value) in more)
            copy[key] = value;
        return new CapabilitySet(copy);
    }

    /// <summary>
    /// Checks a requirement, and says in one sentence why it is unmet when it is.
    /// </summary>
    public bool Satisfies(Requirement requirement, out string reason)
    {
        if (!_values.TryGetValue(requirement.Key, out string? actual))
        {
            reason = $"this environment provides no '{requirement.Key}'";
            return false;
        }

        if (requirement.ExpectedValue is null)
        {
            reason = string.Empty;
            return true;
        }

        if (string.Equals(actual, requirement.ExpectedValue, StringComparison.OrdinalIgnoreCase))
        {
            reason = string.Empty;
            return true;
        }

        reason = actual is null
            ? $"'{requirement.Key}' is available but its value is unknown, and the test needs "
              + $"'{requirement.ExpectedValue}'"
            : $"'{requirement.Key}' is '{actual}', not '{requirement.ExpectedValue}'";
        return false;
    }

    /// <summary>The first unmet requirement, with its reason, or null when all are met.</summary>
    public (Requirement Requirement, string Reason)? FirstUnmet(IEnumerable<Requirement> requirements)
    {
        foreach (Requirement requirement in requirements)
        {
            if (!Satisfies(requirement, out string reason))
                return (requirement, reason);
        }

        return null;
    }
}

/// <summary>What the machine on the link said about itself when it was asked.</summary>
/// <param name="Values">The capabilities its answer establishes.</param>
/// <param name="Reasons">Why each capability asked about went unanswered, by its key.</param>
/// <param name="WhyNot">
/// Why there are none, when there are none. Kept apart from the values because "the machine
/// stayed quiet", "the poll never reached the wire" and "the reply made no sense" are three
/// different things to be told, and a report that flattens them into an absent capability sends
/// the reader to the wrong place.
/// </param>
public sealed record MachineAnswers(
    IReadOnlyDictionary<string, string?> Values,
    string? WhyNot,
    IReadOnlyDictionary<string, string>? Reasons = null)
{
    /// <summary>Why this capability went unanswered, falling back to the reason for the whole probe.</summary>
    public string? ReasonFor(string key) =>
        Reasons is not null && Reasons.TryGetValue(key, out string? reason) ? reason : WhyNot;
}

/// <summary>Works out what this machine and this build can do.</summary>
public static partial class CapabilityProbe
{
    /// <summary>
    /// Everything that is true regardless of which machine is on the other end of the link.
    /// </summary>
    public static CapabilitySet Baseline()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["transport.loopback"] = "yes",
            ["transport.tcp"] = "yes",

            // Only the first channel is wired. The rest are carried through the model and the
            // reports so that adding them later is not a rewrite, but claiming them now would be
            // a claim about a link nobody has opened.
            ["channel.1"] = "yes",

            // The host can stop its round and put an exact sequence of named polls on the line,
            // with the synchronisation poll under the case's control. True of this build and not
            // of the machine, so it is baseline rather than probed.
            ["host.poll_sequence"] = "yes",

            // A case can keep a value from one reply and compare a later reply with it, or with a
            // sum or difference of such values; also true of this build, not of the machine.
            ["host.compare_replies"] = "yes",
            ["host.meter_arithmetic"] = "yes",

            // With those two a case adds up each game's movement and compares it with the machine's; it asserts
            // silence on a poll that carries its own length once that length matches its data; and it tells the
            // busy reply from an answer (D59).
            [PlayedGameCapabilities.GameMeterDelta] = "yes",
            [PlayedGameCapabilities.LengthPrefixedSilence] = "yes",
            [PlayedGameCapabilities.BusyReply] = "yes",

            // In a sequence the host can listen to a quiet line for the loop-break chirp, send a poll
            // whose address byte carries no wakeup bit, and time how often an exception comes again.
            // Every transport this build opens carries the bit per symbol, so each can leave it off.
            ["host.chirp"] = "yes",
            ["host.poll_without_wakeup"] = "yes",
            ["host.timing"] = "yes",

            // The host can carry each validation exchange step by step, $4C, $4D, $57 and $58 included.
            ["host.validation_sequence"] = "yes",

            // The host can keep progressive levels broadcast in the background while a case waits.
            [BroadcastStep.Capability] = "yes",
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || LinuxTermiosDevice.IsSupported)
        {
            string[] ports = SerialTransport.AvailablePorts();
            if (ports.Length > 0)
                values["transport.serial"] = string.Join(';', ports);
        }

        // A reply this build cannot decode cannot be asserted on, so a test that needs one is
        // skipped rather than run against a guess.
        foreach (byte command in SasLayouts.All.Keys)
            values[$"decoder.lp{command:X2}"] = "yes";

        // The same for what an exception carries. Named apart from the long polls because they are
        // different things: 'lp7F' is the command that sets the machine's clock and 'event7F' is
        // the exception that reports what a game paid, and a case naming one when it means the
        // other skips for a reason that is true and not the one it meant.
        foreach (byte code in SasEventLayouts.All.Keys)
            values[$"decoder.event{code:X2}"] = "yes";

        return new CapabilitySet(values);
    }

    /// <summary>
    /// What being able to read the wallet adds.
    /// </summary>
    /// <remarks>
    /// Deliberately not called 'printer'. Whether the machine has a printer, and whether it is
    /// configured well enough to use one, is a property of the machine and is not knowable from
    /// here - on fresh memory the printer protocol is unset and no ticket can be produced at all.
    /// What this host can honestly claim is narrower: that there is a directory a printed ticket
    /// would land in and that it can read it. A machine that then prints nothing fails the
    /// assertion that was waiting, which is a true statement about that machine rather than a
    /// missing capability of ours.
    /// </remarks>
    public static IReadOnlyDictionary<string, string?> ForWallet(string directory)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            Directory.CreateDirectory(directory);
            values["printer.wallet"] = directory;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            // Absent, so a test that needs it skips naming the directory rather than failing.
        }

        return values;
    }

    /// <summary>
    /// What being able to write into the acceptor adds.
    /// </summary>
    /// <remarks>
    /// Named for what this host can do, not for what the machine will do with it, for the same
    /// reason the wallet is. Whether the machine accepts tickets at all is a property of its
    /// configuration and it says so on the wire; what is knowable from here is only that there is
    /// a directory to put one in.
    /// </remarks>
    public static IReadOnlyDictionary<string, string?> ForAcceptor(string directory)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            Directory.CreateDirectory(directory);
            values["stimulus.ticket"] = directory;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            // Absent, so a test that needs it skips naming the directory rather than failing.
        }

        return values;
    }

    /// <summary>
    /// The capabilities only the machine can answer for, which are therefore asked over the wire.
    /// </summary>
    /// <remarks>
    /// These are checked after the link is up rather than before it, because there is nowhere
    /// else to get them. A machine's validation style is not a property of this host, of the
    /// stimulus backend or of the build: it is set in the machine's own memory, and the only
    /// thing it can be read from is the machine.
    /// </remarks>
    public static readonly IReadOnlySet<string> AskedOfTheMachine =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "egm.validation",

            // The accounting denomination is the same kind of fact and is read the same way. A
            // memory clear establishes it, so no host can set it and no build can know it; the
            // identification reply is where the machine publishes it.
            MachineDeclaration.Capability,

            // Whether the machine performs funds transfer is set in its own configuration, and the
            // enabled-features reply is where it says so (D45).
            AftCapability,

            // Bonus awards over funds transfer have their own enabled-features bit, and receipts
            // their own bit of the AFT status long poll $74 reports.
            AftBonusCapability,
            AftReceiptsCapability,

            // The transfer limit a machine enforces is published in the $74 status it reports.
            TransferLimitCapability,
            // Legacy bonusing and multiplied jackpots have enabled-features bits of their own.
            LegacyBonusCapability,
            MultipliedJackpotCapability,

            // The progressive group is set in the machine's menu and published in its identification reply.
            MachineProgressiveDeclaration.SasCapability,

            // Component authentication has its enabled-features bit, and the component list says how many there are.
            MachineAuthenticationDeclaration.Capability,
            MultipleComponentsCapability,

            // How many games and player denominations a machine offers is in its $51 and $B2 replies.
            MultipleGamesCapability,
            MultipleDenominationsCapability,

            // A cash-out limit is reported by $A4 only on a machine that has one.
            CashoutLimitCapability,
        };

    /// <summary>The capability a machine that performs funds transfers answers with.</summary>
    public const string AftCapability = "machine.aft";

    /// <summary>The capability a machine that takes bonus awards over funds transfer answers with.</summary>
    public const string AftBonusCapability = "machine.aft_bonus";

    /// <summary>The capability a machine that prints transfer receipts answers with.</summary>
    public const string AftReceiptsCapability = "machine.aft_receipts";

    /// <summary>The capability a machine that publishes a transfer limit answers with, its value the limit in cents.</summary>
    public const string TransferLimitCapability = "machine.transfer_limit";

    /// <summary>True when a requirement can only be answered by the machine on the link.</summary>
    public static bool IsAskedOfTheMachine(string key) => AskedOfTheMachine.Contains(key);

    /// <summary>
    /// Asks the machine what it is, over the link, once it is up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The enabled-features reply is where a machine publishes its validation style, so this is
    /// the same question asked of both peers rather than one peer being asked and the other being
    /// told what our own fixture happens to be in. A machine that does not answer the poll leaves
    /// the capability absent, and a case that needs it skips saying the machine did not answer —
    /// never saying it is in some style by default.
    /// </para>
    /// <para>
    /// The answer is about this channel, not about the machine. The gaming machine library clears
    /// the bits of any category the channel it answers on has not been given, so a link that does
    /// not carry validation reports the standard style whatever the machine is configured in.
    /// That is the honest answer for a test running on that link, and it is all one link can know.
    /// </para>
    /// </remarks>
    public static MachineAnswers FromMachine(PollEngine engine, IEnumerable<string>? wanted = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // Only what a case actually asked about is put on the link. A probe nobody needed is
        // traffic in somebody's log and a long poll in somebody's coverage tally, and neither
        // belongs to the run that person is reading.
        IReadOnlySet<string> asked = wanted is null
            ? AskedOfTheMachine
            : new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase);

        string? whyNot = null;

        //  A floor has no "the machine" to ask. These probes read what a machine is, and the
        //  answer is that machine's — asking whichever one the round starts at and publishing it
        //  as the floor's would be exactly the head-of-round guess every other refusal here
        //  avoids. Said plainly, so the skip names the floor rather than reading as a link that
        //  would not carry a poll.
        if (engine.Round.Count > 1)
        {
            return new MachineAnswers(values,
                $"this host polls {engine.Round.Count} machines, and what a machine is can only be "
                + "asked of one of them. A floor has no machine to answer for it, so this was not "
                + "asked at all.");
        }

        // Each reason is kept against its own capability, so a skip names what went wrong with the
        // requirement it is about rather than whichever probe happened to run last.
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Note(string key, string? reason)
        {
            if (reason is null)
                return;
            reasons[key] = reason;
            whyNot ??= reason;
        }

        if (asked.Contains("egm.validation"))
            Note("egm.validation", AskValidation(engine, values));

        if (asked.Contains(MachineDeclaration.Capability))
            Note(MachineDeclaration.Capability, AskAccountingDenomination(engine, values));

        if (asked.Contains(AftCapability))
            Note(AftCapability, AskAft(engine, values));

        if (asked.Contains(AftBonusCapability))
            Note(AftBonusCapability, AskAftBonus(engine, values));

        if (asked.Contains(AftReceiptsCapability))
            Note(AftReceiptsCapability, AskAftReceipts(engine, values));

        if (asked.Contains(TransferLimitCapability))
            Note(TransferLimitCapability, AskTransferLimit(engine, values));
        if (asked.Contains(LegacyBonusCapability))
            Note(LegacyBonusCapability, AskLegacyBonus(engine, values));

        if (asked.Contains(MultipliedJackpotCapability))
            Note(MultipliedJackpotCapability, AskMultipliedJackpot(engine, values));

        if (asked.Contains(MachineProgressiveDeclaration.SasCapability))
            Note(MachineProgressiveDeclaration.SasCapability, AskSasProgressiveGroup(engine, values));

        if (asked.Contains(MachineAuthenticationDeclaration.Capability))
            Note(MachineAuthenticationDeclaration.Capability, AskComponentAuthentication(engine, values));

        if (asked.Contains(MultipleComponentsCapability))
            Note(MultipleComponentsCapability, AskMultipleComponents(engine, values));

        if (asked.Contains(MultipleGamesCapability))
            Note(MultipleGamesCapability, AskMultipleGames(engine, values));

        if (asked.Contains(MultipleDenominationsCapability))
            Note(MultipleDenominationsCapability, AskMultipleDenominations(engine, values));

        if (asked.Contains(CashoutLimitCapability))
            Note(CashoutLimitCapability, AskCashoutLimit(engine, values));

        return new MachineAnswers(values, whyNot, reasons);
    }

    /// <summary>Reads the machine's validation style out of its enabled-features reply.</summary>
    private static string? AskValidation(PollEngine engine, Dictionary<string, string?> values)
    {
        SasMessageLayout layout = SasLayouts.For((byte)SasLongPollCode.LP_SENDENABLEFEATURES)!;

        // Game zero, which is the machine as a whole rather than any one of its games.
        if (Ask(engine, layout, [0x00, 0x00]) is not { } result)
        {
            return "the enabled-features poll could not be put on the link, so nothing was asked "
                   + "of the machine at all" + Because(engine);
        }

        if (!result.Succeeded)
        {
            return $"the machine did not answer the enabled-features poll ({result.Outcome}), so "
                   + "what it is cannot be read off this link";
        }

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, result.Frame);
        if (decoded.Number("validation_style") is not { } style)
        {
            return "the machine's enabled-features reply could not be decoded: "
                   + (decoded.Failure ?? "it carries no validation style");
        }

        // The fourth value is assigned to no style. A machine reporting it is misconfigured, and
        // saying so beats naming a style nobody defined.
        if (style is < 0 or > 2)
            return $"the machine reports validation style {style}, which is assigned to no style";

        values["egm.validation"] = SasNames.Underscore(((SasCashoutValidation)style).ToString());
        return null;
    }

    /// <summary>
    /// Reads whether the machine performs funds transfer off the AFT bit of its enabled-features
    /// reply, which the library clears on a channel that does not own the AFT category.
    /// </summary>
    private static string? AskAft(PollEngine engine, Dictionary<string, string?> values)
    {
        SasMessageLayout layout = SasLayouts.For((byte)SasLongPollCode.LP_SENDENABLEFEATURES)!;

        if (Ask(engine, layout, [0x00, 0x00]) is not { } result)
        {
            return "the enabled-features poll could not be put on the link, so whether the machine "
                   + "performs funds transfer was never asked" + Because(engine);
        }

        if (!result.Succeeded)
        {
            return $"the machine did not answer the enabled-features poll ({result.Outcome}), so "
                   + "whether it performs funds transfer cannot be read off this link";
        }

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, result.Frame);
        if (decoded.Number("feature_codes_2") is not { } features)
        {
            return "the machine's enabled-features reply could not be decoded: "
                   + (decoded.Failure ?? "it carries no second feature byte");
        }

        // Bit 6 of the second feature byte is the library's AFT_BIT.
        if ((features & 0x40) == 0)
            return "the machine reports funds transfer off in its enabled-features reply on this channel";

        values[AftCapability] = "yes";
        return null;
    }

    /// <summary>
    /// Reads whether the machine takes bonus awards over funds transfer off bit 1 of the first
    /// feature byte, which the library clears on a channel that does not own the AFT category.
    /// </summary>
    private static string? AskAftBonus(PollEngine engine, Dictionary<string, string?> values)
    {
        SasMessageLayout layout = SasLayouts.For((byte)SasLongPollCode.LP_SENDENABLEFEATURES)!;

        if (Ask(engine, layout, [0x00, 0x00]) is not { } result)
        {
            return "the enabled-features poll could not be put on the link, so whether the machine "
                   + "takes bonus awards over funds transfer was never asked" + Because(engine);
        }

        if (!result.Succeeded)
        {
            return $"the machine did not answer the enabled-features poll ({result.Outcome}), so "
                   + "whether it takes bonus awards over funds transfer cannot be read off this link";
        }

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, result.Frame);
        if (decoded.Number("feature_codes_1") is not { } features)
        {
            return "the machine's enabled-features reply could not be decoded: "
                   + (decoded.Failure ?? "it carries no first feature byte");
        }

        // Bit 1 of the first feature byte is the library's AFT_BONUS_AWARDS_BIT.
        if ((features & 0x02) == 0)
            return "the machine reports AFT bonus awards off in its enabled-features reply on this channel";

        values[AftBonusCapability] = "yes";
        return null;
    }

    /// <summary>
    /// Reads whether the machine prints transfer receipts off bit 0 of the AFT status a $74 read
    /// reports, which a machine that cannot take a transfer right now reports as zero.
    /// </summary>
    private static string? AskAftReceipts(PollEngine engine, Dictionary<string, string?> values)
    {
        SasMessageLayout layout = SasLayouts.For((byte)SasLongPollCode.LP_AFT_LOCKANDSTATUS)!;

        // Lock code $FF reads the status and changes nothing.
        if (Ask(engine, layout, [0xFF, 0x00, 0x00, 0x00]) is not { } result)
        {
            return "the funds transfer status poll could not be put on the link, so whether the "
                   + "machine prints transfer receipts was never asked" + Because(engine);
        }

        if (!result.Succeeded)
        {
            return $"the machine did not answer the funds transfer status poll ({result.Outcome}), "
                   + "so whether it prints transfer receipts cannot be read off this link";
        }

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, result.Frame);
        if (decoded.Number("aft_status") is not { } status)
        {
            return "the machine's funds transfer status reply could not be decoded: "
                   + (decoded.Failure ?? "it carries no AFT status");
        }

        // Bit 0 of the AFT status is the library's AS_PRINTER_AVAILABLE_FOR_TRANSACTION_RECEIPTS.
        if ((status & 0x01) == 0)
            return "the machine reports no printer available for transfer receipts in its $74 status";

        values[AftReceiptsCapability] = "yes";
        return null;
    }

    /// <summary>
    /// Reads the transfer limit off the $74 status, which the library fills from what the platform publishes (lp.cpp:6636).
    /// </summary>
    private static string? AskTransferLimit(PollEngine engine, Dictionary<string, string?> values)
    {
        SasMessageLayout layout = SasLayouts.For((byte)SasLongPollCode.LP_AFT_LOCKANDSTATUS)!;

        if (Ask(engine, layout, [0xFF, 0x00, 0x00, 0x00]) is not { } result)
        {
            return "the funds transfer status poll could not be put on the link, so the machine's "
                   + "transfer limit was never asked" + Because(engine);
        }

        if (!result.Succeeded)
        {
            return $"the machine did not answer the funds transfer status poll ({result.Outcome}), "
                   + "so its transfer limit cannot be read off this link";
        }

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, result.Frame);
        if (decoded.Number("gaming_machine_transfer_limit") is not { } limit)
        {
            return "the machine's funds transfer status reply could not be decoded: "
                   + (decoded.Failure ?? "it carries no transfer limit");
        }

        values[TransferLimitCapability] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return null;
    }

    /// <summary>
    /// Reads the accounting denomination the machine is in force at, off its identification reply.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Verified against the library's own handler rather than argued from the specification: the
    /// identification reply's denomination byte is filled from the machine's stored accounting
    /// denomination, and so is the same byte in the per-game configuration reply. The denomination
    /// the player has currently selected is a different value, kept in a different place, answered
    /// by a different poll, and silent altogether unless the machine is multi-denomination - so it
    /// cannot stand in for this one.
    /// </para>
    /// <para>
    /// The answer is in cents rather than as a code, because a code names a unit only to somebody
    /// holding Table C-4, and this value ends up in a skip a person has to act on.
    /// </para>
    /// </remarks>
    private static string? AskAccountingDenomination(
        PollEngine engine, Dictionary<string, string?> values)
    {
        SasMessageLayout layout = SasLayouts.For((byte)SasLongPollCode.LP_SENDMACHINEID_INFO)!;

        if (Ask(engine, layout, []) is not { } result)
        {
            return "the machine identification poll could not be put on the link, so what unit "
                   + "the machine counts in was never asked" + Because(engine);
        }

        if (!result.Succeeded)
        {
            return $"the machine did not answer the identification poll ({result.Outcome}), so "
                   + "the unit it counts in cannot be read off this link";
        }

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, result.Frame);
        if (decoded.Number("denomination") is not { } code)
        {
            return "the machine's identification reply could not be decoded: "
                   + (decoded.Failure ?? "it carries no denomination");
        }

        if (code is < 0 or > SasDenomination.LastCode
            || SasDenomination.CentsOf((byte)code) is not { } cents)
        {
            return $"the machine reports denomination code ${code:X2}, which Table C-4 gives no "
                   + "value as an accounting unit";
        }

        values[MachineDeclaration.Capability] = MachineDeclaration.Describe(cents);
        return null;
    }

    /// <summary>
    /// Puts one probe on the link, and null when it never got there.
    /// </summary>
    /// <remarks>
    /// A command the engine never got as far as sending says nothing about the machine, so it is
    /// asked again rather than reported as a machine that stayed quiet. Once: a second failure to
    /// reach the wire is a link worth hearing about, not something to grind on.
    /// </remarks>
    private static LongPollResult? Ask(PollEngine engine, SasMessageLayout layout, byte[] data)
    {
        LongPollResult result = engine.Execute(
            layout.Command, data, purpose: WirePurpose.CapabilityProbe);

        if (result.Outcome == LongPollOutcome.NotSent)
            result = engine.Execute(layout.Command, data, purpose: WirePurpose.CapabilityProbe);

        return result.Outcome == LongPollOutcome.NotSent ? null : result;
    }

    /// <summary>The link's failure as the end of a reason, or nothing when the link has not failed.</summary>
    private static string Because(PollEngine engine) =>
        engine.LinkFault is { } fault ? $": {fault}" : string.Empty;

    /// <summary>
    /// The value a hand pay capability carries when the host must first take the printer out of use
    /// as the cash-out device (long poll 7B bit 0) for a cash-out to become a hand pay.
    /// </summary>
    public const string HandpayByWithdrawnPrinter = "printer_withdrawn";
}
