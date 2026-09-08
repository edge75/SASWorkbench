namespace SasWorkbench.Core.Protocol;

/// <summary>Whether a host message for one command must end in a frame check sequence.</summary>
public enum SasCheckSequenceRule
{
    /// <summary>
    /// The machine will not act on a message with no check sequence, so one is appended even when
    /// the command carries no data and the message is two bytes long without it.
    /// </summary>
    Required,

    /// <summary>
    /// The machine answers a bare two-byte poll. A message that carries data still ends in a
    /// check sequence, because any message longer than two bytes is checked on arrival.
    /// </summary>
    NotRequired,

    /// <summary>
    /// Neither the machine library's receiver nor a handler says, so nothing here does either.
    /// </summary>
    Unknown,
}

/// <summary>
/// Says, per command, how long the host's own message is and whether it has to end in a check
/// sequence.
/// </summary>
/// <remarks>
/// <para>
/// This is a property of the command and not of the message's length, and getting that wrong is
/// what this type exists to stop. The gaming machine library states it twice, in two files, and
/// the generator reads both and reports any disagreement:
/// </para>
/// <list type="bullet">
/// <item>
/// its <b>receiver</b> holds a table of how many bytes each host message is, reads the address
/// and the command byte, and then reads exactly that many more. A message shorter than the table
/// says is <b>dropped as incomplete</b>, before any handler sees it.
/// </item>
/// <item>
/// each <b>handler</b> that verifies a check sequence itself refuses the message when it fails,
/// and on a two-byte message that check reads the address and the command byte as the sequence
/// and can never pass.
/// </item>
/// </list>
/// <para>
/// The two agree on every command where both speak, and the receiver decides what actually
/// happens: measured against a real machine, a two-byte <c>$01</c> is met with <b>silence</b>
/// rather than with a refusal, because the receiver is still waiting for two more bytes. The same
/// command with a check sequence is acknowledged.
/// </para>
/// <para>
/// It was found by the overlay diff, which is the only reason it was found at all: this host and
/// the software machine both decided from the message's length, so they agreed with each other
/// and both disagreed with a real machine. Running the same script against both peers could never
/// have caught it, which is what D7 says a disagreement is worth and what an agreement between
/// two of our own components is not.
/// </para>
/// </remarks>
public static class SasCommandFraming
{
    /// <summary>
    /// The value the receiver's table uses for a command it frames by a length byte carried in
    /// the message rather than by a fixed total.
    /// </summary>
    public const int VariableLength = -1;

    /// <summary>What the gaming machine library requires for this command.</summary>
    public static SasCheckSequenceRule Rule(byte command)
    {
        if (!SasCatalogue.LongPolls.TryGetValue(command, out SasLongPollInfo? info))
        {
            // Not in the enumeration, but the receiver may still frame it — eighteen of the
            // legacy funds transfer commands are in that position.
            return SasCatalogue.HostMessageLengths.TryGetValue(command, out int framed)
                ? framed == 2 ? SasCheckSequenceRule.NotRequired : SasCheckSequenceRule.Required
                : SasCheckSequenceRule.Unknown;
        }

        return info.RequiresCheckSequence switch
        {
            true => SasCheckSequenceRule.Required,
            false => SasCheckSequenceRule.NotRequired,
            _ => SasCheckSequenceRule.Unknown,
        };
    }

    /// <summary>
    /// True when a host message for this command must carry a check sequence whatever its length.
    /// A command whose rule is unknown is framed as the protocol's own default — its data decides —
    /// and every reader of that decision is told the rule was unknown rather than left to assume.
    /// </summary>
    public static bool RequiresCheckSequence(byte command) =>
        Rule(command) == SasCheckSequenceRule.Required;

    /// <summary>
    /// Total bytes of the host's message, address and check sequence included, or null where the
    /// library frames it by a length byte instead or says nothing about it.
    /// </summary>
    public static int? HostMessageLength(byte command)
    {
        if (!SasCatalogue.HostMessageLengths.TryGetValue(command, out int framed))
            return null;

        return framed == VariableLength ? null : framed;
    }

    /// <summary>True when the library reads a length byte out of the message rather than a table.</summary>
    public static bool IsVariableLength(byte command) =>
        SasCatalogue.HostMessageLengths.TryGetValue(command, out int framed)
        && framed == VariableLength;

    /// <summary>
    /// How many request bytes the host puts after the command byte, or null where the library does
    /// not fix a total. Two bytes total means none; anything longer is the total less the address,
    /// the command and the two check bytes.
    /// </summary>
    public static int? RequestDataBytes(byte command) => HostMessageLength(command) switch
    {
        null => null,
        2 => 0,
        int total when total >= 4 => total - 4,

        // A total of three would be an address, a command and one byte with no room for a check
        // sequence, which is not a shape the protocol has. Named rather than turned into a number.
        _ => null,
    };

    /// <summary>One sentence naming the rule and where it came from, for a log or a report.</summary>
    public static string Explain(byte command)
    {
        int? length = HostMessageLength(command);
        string framed = length is { } total
            ? $"the machine library's receiver frames it at {total} byte(s)"
            : IsVariableLength(command)
                ? "the machine library's receiver reads a length byte out of the message"
                : "the machine library's receiver does not carry this command";

        return Rule(command) switch
        {
            SasCheckSequenceRule.Required =>
                $"${command:X2} must end in a check sequence: {framed}, and a shorter message is "
                + "dropped as incomplete before any handler sees it",
            SasCheckSequenceRule.NotRequired =>
                $"${command:X2} needs no check sequence of its own: {framed}, so a two-byte poll "
                + "is a whole message",
            _ =>
                $"${command:X2} has no framing rule in the machine library, so whether it needs a "
                + "check sequence is unknown and this host frames it as its data decides",
        };
    }
}
