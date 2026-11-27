using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Scripting;

/// <summary>
/// Listens to a line nobody is polling for the machine's loop-break chirp, or for its absence.
/// </summary>
/// <remarks>
/// A chirp is the machine's own address with the wakeup bit, every 200 ms once it has heard nothing
/// for five seconds. Only a sequence leaves the line quiet enough to hear one.
/// </remarks>
public sealed record ExpectChirpStep(SourceLocation Location, TimeSpan Within, bool Negated)
    : ScriptStep(Location)
{
    /// <summary>How many chirps make the cadence, each gap checked.</summary>
    public const int Count = 3;

    public override string Describe() => Negated
        ? $"expect no chirp for {Within.TotalMilliseconds:F0}ms"
        : $"expect chirp within {Within.TotalMilliseconds:F0}ms";
}

/// <summary>
/// Sends a long poll whose address byte carries no wakeup bit, which the machine must ignore or refuse.
/// </summary>
public sealed record SendWithoutWakeupStep(SourceLocation Location, SendLongPollStep Poll) : ScriptStep(Location)
{
    public override string Describe() => $"{Poll.Describe()} without wakeup";
}

/// <summary>
/// Sends a long poll whose check sequence is broken, which the machine must ignore or refuse (host.bad_crc).
/// </summary>
public sealed record SendBadCrcStep(SourceLocation Location, SendLongPollStep Poll) : ScriptStep(Location)
{
    /// <summary>The capability of a host that can break a check sequence on purpose.</summary>
    public const string Capability = "host.bad_crc";

    public override string Describe() => $"{Poll.Describe()} with bad crc";
}

/// <summary>The host follows the one machine it polls to a new poll address.</summary>
public sealed record FollowAddressStep(SourceLocation Location, SasAddress Address) : ScriptStep(Location)
{
    public override string Describe() => $"poll address {Address}";
}

/// <summary>The operator sets the machine's poll address at the machine.</summary>
public sealed record SetPollAddressStep(SourceLocation Location, SasAddress Address) : StimulusStep(Location)
{
    public override string RequiredCapability => "machine.address_configuration";

    public override string Describe() => $"set address {Address}";

    public override string? Guidance =>
        $"In the operator's network menu, set the machine's first poll address to {Address.Value} (hex {Address}) and save.";
}

/// <summary>
/// Expects an exception to come again a set time after it last came, as a machine repeating it
/// until it is read does.
/// </summary>
/// <remarks>
/// Times are when the host read each one off the wire, so the tolerance is a quarter of the period
/// or two poll intervals, whichever is larger.
/// </remarks>
public sealed record ExpectExceptionRepeatStep(
    SourceLocation Location, byte Code, string Name, TimeSpan Period) : ScriptStep(Location)
{
    public override string Describe() => $"expect exception {Name} every {Period.TotalMilliseconds:F0}ms";
}
