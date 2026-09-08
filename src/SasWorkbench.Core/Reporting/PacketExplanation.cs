using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Reporting;

/// <summary>How completely one packet could be explained from its own bytes.</summary>
/// <remarks>
/// The distinction between the first two is the promise. "Complete meaning" is claimed only where
/// every byte was read against an established layout; where the command is known and its layout is
/// not, the honest answer is to name it and say so. A guess that decodes plausibly is the worst
/// outcome available, because nothing downstream can tell it from a reading.
/// </remarks>
public enum PacketMeaning
{
    /// <summary>Every byte was read, and each field's meaning is stated.</summary>
    Explained,

    /// <summary>The command is named in the catalogue and this build has no layout for its bytes.</summary>
    NamedOnly,

    /// <summary>A layout was applied and the bytes did not fit it. A finding, not a guess.</summary>
    LayoutDidNotFit,

    /// <summary>
    /// The bytes do not stand as a SAS message, so nothing is read out of them.
    /// </summary>
    /// <remarks>
    /// The check sequence is the protocol's own test of whether a run of bytes is one message. A
    /// frame that fails it gives no assurance about any byte in it, the command byte included, so
    /// naming the long poll would be a guess dressed as a reading. The bytes and the check that
    /// failed are both recorded; nothing is repaired and nothing is dropped.
    /// </remarks>
    NotAMessage,

    /// <summary>Nothing in this build names it at all.</summary>
    Unknown,
}

/// <summary>One decoded field of a packet, as a line of prose.</summary>
/// <param name="Path">How a script would name it.</param>
/// <param name="Hex">The bytes it came from.</param>
/// <param name="Rendered">The value, as a number, as text, or as bytes.</param>
/// <param name="Meaning">What the field carries, in our own words, when the layout says.</param>
/// <param name="Violation">Why the value breaks the field's constraint, when it does.</param>
public sealed record PacketField(
    string Path,
    string Hex,
    string Rendered,
    string? Meaning = null,
    string? Violation = null);

/// <summary>
/// What one packet was, worked out from the bytes the record holds and nothing else.
/// </summary>
/// <remarks>
/// Everything here is derived. Nothing is captured a second time, so an explanation can be
/// regenerated, corrected and re-rendered from a recorded run without re-running anything — which
/// is the whole reason the extended log is a rendering rather than a second recording.
/// </remarks>
public sealed record PacketExplanation
{
    /// <summary>Where in the record it sits.</summary>
    public required long Sequence { get; init; }

    /// <summary>Which way it went.</summary>
    public required WireDirection Direction { get; init; }

    /// <summary>Which channel carried it.</summary>
    public required SasChannel Channel { get; init; }

    /// <summary>When, on whichever clock the record measures.</summary>
    public required TimeSpan At { get; init; }

    /// <summary>The symbols, as the record renders them.</summary>
    public required string Hex { get; init; }

    /// <summary>One line saying what this packet is.</summary>
    public required string Headline { get; init; }

    /// <summary>How completely it could be explained.</summary>
    public required PacketMeaning Meaning { get; init; }

    /// <summary>The long poll this is, or answers, when it is one.</summary>
    public byte? Command { get; init; }

    /// <summary>The exception code the machine reported, when it reported one.</summary>
    public byte? Exception { get; init; }

    /// <summary>
    /// True when the machine reported this exception in the real-time event format, so the
    /// question of what it carries after the code arises at all.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="Exception"/> on purpose. A one-byte exception carries nothing by
    /// construction and there is no payload to have established; an event may carry a payload, so
    /// only these are counted against the payloads this build holds.
    /// </remarks>
    public bool IsRealTimeEvent { get; init; }

    /// <summary>Why the host sent this itself, where no step of a script asked for it.</summary>
    public string? Purpose { get; init; }

    /// <summary>What the engine called it when it recorded it, kept verbatim.</summary>
    public string? Note { get; init; }

    /// <summary>Every field that was read, in order.</summary>
    public IReadOnlyList<PacketField> Fields { get; init; } = [];

    /// <summary>
    /// Things wrong with the packet itself: a wakeup bit where none belongs, a check sequence that
    /// does not check out, an address that is not the one being polled.
    /// </summary>
    public IReadOnlyList<string> Findings { get; init; } = [];

    /// <summary>Why this could not be explained in full, when it could not.</summary>
    public string? Limit { get; init; }

    /// <summary>Which handler the layout that was applied was read from, so the claim is checkable.</summary>
    public string? DerivedFrom { get; init; }

    /// <summary>True when the host sent this on its own behalf rather than because a step asked.</summary>
    public bool IsHostsOwn => Purpose is not null;

    /// <summary>The words a log prints for how completely this was explained.</summary>
    public string MeaningLabel => Meaning switch
    {
        PacketMeaning.Explained => "explained",
        PacketMeaning.NamedOnly => "known but not decoded",
        PacketMeaning.LayoutDidNotFit => "layout did not fit",
        PacketMeaning.NotAMessage => "not a message",
        _ => "unknown",
    };

    public override string ToString() =>
        $"{(Direction == WireDirection.Transmit ? "TX" : "RX")} {Hex}  {Headline}";
}

/// <summary>
/// How much of a run's traffic could be explained, counted rather than claimed.
/// </summary>
/// <remarks>
/// <para>
/// Two tallies, because one of them is misleading on its own. A general poll loop is the
/// overwhelming majority of any session's packets and every one of them is explained in full, so a
/// count over all packets reads as near-total coverage however little of the protocol this build
/// understands. The count that means something is over the distinct long polls a run actually
/// exchanged, and that is the number breadth work moves.
/// </para>
/// <para>
/// Both are reported. A reader who wants to know how much of the log is prose gets the first; a
/// reader deciding what to model next gets the second, and the list of commands behind it.
/// </para>
/// </remarks>
/// <param name="Packets">How many packets there were.</param>
/// <param name="Explained">How many were explained in full.</param>
/// <param name="NamedOnly">How many were named and not decoded.</param>
/// <param name="LayoutDidNotFit">How many had a layout applied that the bytes did not fit.</param>
/// <param name="NotAMessage">How many did not stand as a message at all.</param>
/// <param name="Unknown">How many nothing in this build names.</param>
/// <param name="HostsOwn">How many the host sent itself, with no step asking.</param>
/// <param name="CommandsExplained">The long polls whose bytes were read in full.</param>
/// <param name="CommandsNamedOnly">The long polls that could only be named — what to model next.</param>
/// <param name="CommandsUnknown">The long polls nothing in this build names.</param>
/// <param name="EventsExplained">The real-time events whose bytes were read in full.</param>
/// <param name="EventsNamedOnly">The real-time events that could only be named.</param>
/// <param name="EventsUnknown">The real-time events nothing in this build names.</param>
public sealed record PacketCoverage(
    int Packets,
    int Explained,
    int NamedOnly,
    int LayoutDidNotFit,
    int NotAMessage,
    int Unknown,
    int HostsOwn,
    IReadOnlyList<byte> CommandsExplained,
    IReadOnlyList<byte> CommandsNamedOnly,
    IReadOnlyList<byte> CommandsUnknown,
    IReadOnlyList<byte> EventsExplained,
    IReadOnlyList<byte> EventsNamedOnly,
    IReadOnlyList<byte> EventsUnknown)
{
    /// <summary>How many distinct long polls the run exchanged.</summary>
    public int Commands =>
        CommandsExplained.Count + CommandsNamedOnly.Count + CommandsUnknown.Count;

    /// <summary>
    /// How many distinct exceptions the machine reported in the real-time event format.
    /// </summary>
    /// <remarks>
    /// The third tally, and it moves for the same reason the second one does: an event carrying
    /// bytes this build cannot read is a payload nobody has established. Zero over traffic in
    /// which reporting was never enabled, which is most traffic, and that is not a gap.
    /// </remarks>
    public int Events =>
        EventsExplained.Count + EventsNamedOnly.Count + EventsUnknown.Count;

    /// <summary>Counts a set of explanations.</summary>
    public static PacketCoverage Of(IEnumerable<PacketExplanation> packets)
    {
        ArgumentNullException.ThrowIfNull(packets);

        var all = packets.ToList();

        // A command counts as explained only if every packet carrying it was explained. One
        // command whose request decodes and whose reply does not is not an explained command,
        // and rounding it up would hide exactly the gap this tally exists to expose.
        var worst = new Dictionary<byte, PacketMeaning>();
        foreach (PacketExplanation packet in all.Where(p => p.Command is not null))
        {
            byte command = packet.Command!.Value;
            worst[command] = worst.TryGetValue(command, out PacketMeaning seen)
                              && Severity(seen) > Severity(packet.Meaning)
                ? seen
                : packet.Meaning;
        }

        var worstEvent = new Dictionary<byte, PacketMeaning>();
        foreach (PacketExplanation packet in all.Where(p => p.IsRealTimeEvent && p.Exception is not null))
        {
            byte code = packet.Exception!.Value;
            worstEvent[code] = worstEvent.TryGetValue(code, out PacketMeaning seen)
                               && Severity(seen) > Severity(packet.Meaning)
                ? seen
                : packet.Meaning;
        }

        byte[] With(params PacketMeaning[] meanings) =>
            worst.Where(e => meanings.Contains(e.Value)).Select(e => e.Key).Order().ToArray();

        byte[] WithEvent(params PacketMeaning[] meanings) =>
            worstEvent.Where(e => meanings.Contains(e.Value)).Select(e => e.Key).Order().ToArray();

        return new PacketCoverage(
            all.Count,
            all.Count(p => p.Meaning == PacketMeaning.Explained),
            all.Count(p => p.Meaning == PacketMeaning.NamedOnly),
            all.Count(p => p.Meaning == PacketMeaning.LayoutDidNotFit),
            all.Count(p => p.Meaning == PacketMeaning.NotAMessage),
            all.Count(p => p.Meaning == PacketMeaning.Unknown),
            all.Count(p => p.IsHostsOwn),
            With(PacketMeaning.Explained),
            With(PacketMeaning.NamedOnly, PacketMeaning.LayoutDidNotFit),
            With(PacketMeaning.Unknown),
            WithEvent(PacketMeaning.Explained),
            WithEvent(PacketMeaning.NamedOnly, PacketMeaning.LayoutDidNotFit),
            WithEvent(PacketMeaning.Unknown));
    }

    /// <summary>How badly a packet went unexplained, worst first, stated rather than assumed.</summary>
    private static int Severity(PacketMeaning meaning) => meaning switch
    {
        PacketMeaning.Explained => 0,
        PacketMeaning.NamedOnly => 1,
        PacketMeaning.LayoutDidNotFit => 2,
        _ => 3,
    };
}
