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
    /// <summary>
    /// The machines each channel answers at, in order, where the case named a round rather than
    /// one address.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A channel is a port, and a port is a line: the k-th machine of channel one's round and the
    /// k-th of channel two's are the same machine's two ports, which is why they may carry the
    /// same address — an address is unique on a line, not on a floor. Without that a hundred
    /// machines on two channels would need 200 poll addresses and SAS has 127.
    /// </para>
    /// <para>
    /// A channel declared with one address is a round of one, so the two notations are the same
    /// thing and one rule covers both.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<SasChannel, IReadOnlyList<SasAddress>> Rounds { get; init; } =
        new Dictionary<SasChannel, IReadOnlyList<SasAddress>>();

    /// <summary>The round a channel polls: what it declared, or its one address.</summary>
    public IReadOnlyList<SasAddress> RoundOf(SasChannel channel) =>
        Rounds.TryGetValue(channel, out IReadOnlyList<SasAddress>? round)
            ? round
            : Addresses.Count > channel.ToIndex() && Addresses[channel.ToIndex()] is { } one
                ? [one]
                : [];

    /// <summary>
    /// How many machines this arrangement describes, or a reason no floor could hold it.
    /// </summary>
    /// <remarks>
    /// Every channel with an address describes the same machines, so the rounds are the same
    /// length. Rounds of different lengths are refused with both lengths named rather than zipped
    /// to the shorter: zipping decides on the author's behalf which machines are not on the floor.
    /// </remarks>
    public bool TryCountMachines(out int machines, out string? refusal)
    {
        machines = 1;
        refusal = null;

        (SasChannel Channel, int Count)? first = null;
        foreach (SasChannel channel in SasChannels.All)
        {
            IReadOnlyList<SasAddress> round = RoundOf(channel);
            if (round.Count == 0)
                continue;

            if (first is not { } already)
            {
                first = (channel, round.Count);
                continue;
            }

            if (round.Count != already.Count)
            {
                refusal =
                    $"channel {(int)already.Channel} names {already.Count} machine(s) and channel "
                    + $"{(int)channel} names {round.Count}. The k-th machine of each channel's "
                    + "round is the same machine's k-th port, so the rounds are the same length.";
                return false;
            }
        }

        machines = first?.Count ?? 1;
        return true;
    }
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

    /// <summary>
    /// The arrangement of one machine of the floor this declares: its own address on each channel,
    /// and the ownership every machine on the floor shares.
    /// </summary>
    /// <param name="index">Which machine of the round, from zero.</param>
    public bool TryResolveFor(int index, out SasChannelPlan plan, out string? refusal)
    {
        plan = SasChannelPlan.Single;
        refusal = null;

        if (!TryCountMachines(out int machines, out refusal))
            return false;

        if (index < 0 || index >= machines)
        {
            refusal = $"this arrangement describes {machines} machine(s), so there is no machine "
                      + $"{index + 1} in it.";
            return false;
        }

        var addresses = new SasAddress?[SasChannels.Count];
        foreach (SasChannel channel in SasChannels.All)
        {
            IReadOnlyList<SasAddress> round = RoundOf(channel);
            if (round.Count > index)
                addresses[channel.ToIndex()] = round[index];
        }

        return SasChannelPlan.TryCreate(addresses, Owners, out plan, out refusal);
    }

    public override string ToString()
    {
        IEnumerable<string> channels = SasChannels.All
            .Where(channel => RoundOf(channel).Count > 0)
            .Select(channel => $"{(int)channel}@{SasAddressRound.Describe(RoundOf(channel))}");

        IEnumerable<string> owned = Owners
            .OrderBy(pair => pair.Key)
            .Select(pair => $"{SasCategories.NameOf(pair.Key)}={(int)pair.Value}");

        return $"channels {string.Join(' ', channels)} · {string.Join(' ', owned)}";
    }
}
