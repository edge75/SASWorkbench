namespace SasWorkbench.Core.Scripting;

/// <summary>Plays one game that wins one or more progressive levels, and optionally a paytable win with them.</summary>
/// <remarks>
/// The level's value is the machine's: what the host last broadcast for a SAS level, and the
/// machine's own amount for a level it runs itself. Whether the machine or an attendant pays it is
/// the machine's jackpot limit's to decide.
/// </remarks>
public sealed record ProgressiveWinStep(SourceLocation Location, IReadOnlyList<int> Levels, long BetCredits) : StimulusStep(Location)
{
    /// <summary>What the same game wins from its paytable, in game credits.</summary>
    public long WinCredits { get; init; }

    /// <summary>True when the script wrote a bet, so a machine that cannot wager every bet must check it.</summary>
    public bool BetStated { get; init; }

    /// <summary>True when the script wrote a win, zero included, so a machine whose paytable part is its own must check it.</summary>
    public bool WinStated { get; init; }

    public override string RequiredCapability => "stimulus.progressive";

    public override string Describe() =>
        $"progressive win level={string.Join(',', Levels)} bet={BetCredits}" + (WinStated || WinCredits > 0 ? $" win={WinCredits}" : "");

    public override string? Guidance =>
        $"Play the progressive game at {BetCredits} credit(s) a game until one game wins level(s) {string.Join(", ", Levels)}"
        + (WinCredits > 0 ? $" and {WinCredits} credit(s) from the paytable." : ".");
}

/// <summary>
/// Sends one progressive broadcast to address 00 again and again, in the background, until the case
/// stops it or ends.
/// </summary>
/// <remarks>
/// A progressive host keeps every level current while anything else happens, and a machine on a SAS
/// progressive counts a level not refreshed for five seconds as lost. Left out while the case owns
/// the line with <c>host poll sequence</c>, because then every message on it must be one the case named.
/// </remarks>
public sealed record BroadcastStep(SourceLocation Location, SendLongPollStep Send, TimeSpan Every)
    : ScriptStep(Location)
{
    /// <summary>The capability this build offers for it.</summary>
    public const string Capability = "host.progressive_broadcast";

    public override string Describe() =>
        $"broadcast {Send.Describe()["send ".Length..]} every {Every.TotalMilliseconds:F0}ms";
}

/// <summary>Stops every background broadcast the case started.</summary>
public sealed record StopBroadcastsStep(SourceLocation Location) : ScriptStep(Location)
{
    public override string Describe() => "broadcast stop";
}

/// <summary>
/// Expects a long poll to be answered one way or the other - a reply, an acknowledgement or a
/// refusal - and not met with silence.
/// </summary>
/// <remarks>
/// For the places the documents accept either answer, such as a broadcast for another progressive
/// group sent to the machine's own address: what they rule out is a machine saying nothing.
/// </remarks>
public sealed record ExpectAnswerStep(SourceLocation Location, byte Command, byte[] Data) : ScriptStep(Location)
{
    public override string Describe() =>
        Data.Length == 0 ? $"expect an answer to lp {Command:X2}" : $"expect an answer to lp {Command:X2} data {Convert.ToHexString(Data)}";
}
