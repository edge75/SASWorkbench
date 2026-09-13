using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Scripting;

/// <summary>
/// How a case says the machine is divided between hosts: an address per channel, and which
/// channel owns each category.
/// </summary>
/// <remarks>
/// <para>
/// A declaration and not a stimulus, for the same reason the accounting denomination is one: the
/// arrangement is set in the machine's own configuration menu and no long poll changes it. Against
/// the software machine it builds a machine in that arrangement; against any other machine it
/// configures nothing, and the case's own assertions are what establish whether the machine in
/// front of it agrees.
/// </para>
/// <para>
/// An address of <c>00</c> means the channel has none, which is how the machine itself stores it —
/// a channel whose poll address is zero is one the machine never opens. Writing it is the only way
/// a script can express the configuration that has to be refused, so it is allowed and refused
/// rather than rejected by the parser as a typo.
/// </para>
/// </remarks>
/// <param name="Location">Where it was declared.</param>
/// <param name="Addresses">
/// The poll address of each channel, in order, null where the channel has none.
/// </param>
/// <param name="Owners">Which channel owns each category. One left out stays on channel one.</param>
public sealed record MachineChannelDeclaration(
    SourceLocation Location,
    IReadOnlyList<SasAddress?> Addresses,
    IReadOnlyDictionary<SasCategory, SasChannel> Owners)
{
    /// <summary>What a channel this build has opened is called as a capability.</summary>
    /// <remarks>
    /// Published by an environment that built the machine and therefore knows. It is deliberately
    /// <em>not</em> what decides whether a two-channel case runs: no long poll reports how a
    /// machine is divided, so against somebody else's machine nothing can answer that before the
    /// link opens. Opening the second channel is the measurement, and a machine that has only one
    /// makes that step a skip naming the channel — which is what GSA 10.1.4 asks for in words.
    /// </remarks>
    public static string CapabilityFor(SasChannel channel) => $"channel.{(int)channel}";

    /// <summary>How many channels this arrangement gives an address.</summary>
    public int Declared => Addresses.Count(address => address is not null);

    /// <summary>
    /// Turns the declaration into an arrangement, or says why no machine could hold it.
    /// </summary>
    public bool TryResolve(out SasChannelPlan plan, out string? refusal) =>
        SasChannelPlan.TryCreate(Addresses, Owners, out plan, out refusal);

    public override string ToString()
    {
        IEnumerable<string> channels = SasChannels.All
            .Where(channel => Addresses.Count > channel.ToIndex()
                              && Addresses[channel.ToIndex()] is not null)
            .Select(channel => $"{(int)channel}@{Addresses[channel.ToIndex()]}");

        IEnumerable<string> owned = Owners
            .OrderBy(pair => pair.Key)
            .Select(pair => $"{SasCategories.NameOf(pair.Key)}={(int)pair.Value}");

        return $"channels {string.Join(' ', channels)} · {string.Join(' ', owned)}";
    }
}
