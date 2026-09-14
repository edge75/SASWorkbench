using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>
/// Several software gaming machines, each on one multi-drop line per channel it opens, with one
/// host end per line for a poll engine to drive.
/// </summary>
/// <remarks>
/// <para>
/// The second axis made concrete. Each machine is the same fixture the two-channel cases use and
/// answers the protocol properly; what is new is that they share a line, so they are told apart by
/// address alone and a host reaches all of them through one transport.
/// </para>
/// <para>
/// Both axes at once is the third thing, and it is a line per channel rather than a second host on
/// one line. A channel is a port in the machine — the platform opens channel one on its first
/// network serial port and each further channel on the next — so two hosts on one line is not an
/// arrangement any machine here has, and on a real pair it is two hosts transmitting at once with
/// no arbitration to separate them. A line each means the two rounds interleave instead.
/// </para>
/// <para>
/// It also settles the arithmetic: a hundred machines on two channels wants two hundred poll
/// addresses and SAS has 127. An address is unique on a line and not on a floor, so both lines can
/// carry <c>01-64</c> and the machine at $25 on one is the same machine as $25 on the other.
/// </para>
/// <para>
/// It is still not a floor of gaming machines. Every machine here is one this build wrote, so a
/// clean round proves that our host can walk a round and that our machines answer only their own
/// polls — not anything about a hundred real machines on a real line.
/// </para>
/// </remarks>
public sealed class SoftwareFloor : IDisposable
{
    private readonly Dictionary<SasChannel, MultidropLoopbackBus> _lines = [];
    private readonly List<FakeEgm> _machines = [];

    private SoftwareFloor(SasChannel first) => First = first;

    /// <summary>Brings up a floor of machines at consecutive addresses, all on channel one.</summary>
    /// <param name="machines">How many machines. SAS addresses run 1-127, so that is the ceiling.</param>
    /// <param name="first">The address of the first machine. Later ones follow it.</param>
    /// <param name="template">What every machine is configured like.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The count and the first address together would need an address above 127, which SAS has no
    /// room for. Refused rather than wrapped: a floor that quietly gave two machines one address
    /// would produce a round that looks right and is not.
    /// </exception>
    public static SoftwareFloor Of(int machines, SasAddress? first = null, FakeEgmOptions? template = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(machines, 1);

        byte start = (first ?? SasAddress.Of(1)).Value;
        if (start == 0)
            throw new ArgumentOutOfRangeException(nameof(first), "Address zero is the synchronisation address, which no machine answers to.");

        int last = start + machines - 1;
        if (last > SasAddress.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(machines), machines,
                $"{machines} machines from ${start:X2} would need address ${last:X2}, and SAS "
                + $"addresses stop at ${SasAddress.MaxValue:X2}.");
        }

        // Each machine settles its own arrangement at its own address. Sharing one plan across the
        // floor would put every machine at the first one's address, which is the mistake that
        // makes a round look clean while eight machines answer as one.
        return Of(
            [.. Enumerable.Range(0, machines).Select(i =>
                SasChannelPlan.Only(SasChannel.One, SasAddress.Of((byte)(start + i))))],
            template);
    }

    /// <summary>
    /// Brings up a floor from one arrangement per machine, opening a line for every channel any
    /// of them answers on.
    /// </summary>
    /// <param name="machines">
    /// The arrangement of each machine, in the order the round visits them. Machine k's address on
    /// a channel is the k-th machine on that channel's line.
    /// </param>
    /// <param name="template">What every machine is configured like beyond its arrangement.</param>
    public static SoftwareFloor Of(
        IReadOnlyList<SasChannelPlan> machines, FakeEgmOptions? template = null)
    {
        ArgumentNullException.ThrowIfNull(machines);
        ArgumentOutOfRangeException.ThrowIfLessThan(machines.Count, 1);

        // Every channel any machine opens gets a line, and every machine gets an end on it.
        SasChannel[] channels =
            [.. SasChannels.All.Where(c => machines.Any(plan => plan.AddressOf(c) is not null))];

        if (channels.Length == 0)
        {
            throw new ArgumentException(
                "no machine on this floor has a poll address on any channel, so there is nothing "
                + "for a host to poll.", nameof(machines));
        }

        //  Every machine on a floor opens the same channels, and a floor whose machines differ is
        //  refused rather than built. The host end of a line writes to every machine end on it, so
        //  a machine with no address on that channel would hold an end nothing ever reads — the
        //  line would fill and start losing symbols, and that loss would be read as the line
        //  dropping traffic rather than as a floor that was never coherent. A mixed floor is a
        //  real thing and it is not this; it would need a machine count per line.
        foreach (SasChannelPlan plan in machines)
        {
            if (plan.Open.SequenceEqual(channels))
                continue;

            throw new ArgumentException(
                "every machine on a floor opens the same channels, and this one opens "
                + string.Join(", ", plan.Open.Select(c => $"channel {(int)c}"))
                + " where the floor carries "
                + string.Join(", ", channels.Select(c => $"channel {(int)c}"))
                + ". A floor of machines divided differently would need a line with only some of "
                + "the machines on it, which is not what this builds.", nameof(machines));
        }

        var floor = new SoftwareFloor(channels[0]);
        try
        {
            foreach (SasChannel channel in channels)
                floor._lines[channel] = new MultidropLoopbackBus(machines.Count);

            for (int i = 0; i < machines.Count; i++)
            {
                SasChannelPlan plan = machines[i];
                SasChannel primary = plan.Open[0];

                FakeEgmOptions settings = (template ?? new FakeEgmOptions()) with
                {
                    Address = plan.AddressOf(primary)!.Value,
                    Channel = primary,
                    Channels = plan,
                };

                var machine = new FakeEgm(floor._lines[primary].Machines[i], settings);
                foreach (SasChannel channel in plan.Open.Skip(1))
                    machine.Attach(floor._lines[channel].Machines[i], channel);

                machine.Start();
                floor._machines.Add(machine);
            }
        }
        catch
        {
            floor.Dispose();
            throw;
        }

        return floor;
    }

    /// <summary>The first channel this floor opens, which is the one a host reaches by default.</summary>
    public SasChannel First { get; }

    /// <summary>The one end a poll engine drives, on the first channel.</summary>
    public ISasTransport Host => _lines[First].Host;

    /// <summary>The host end of one channel's line, or null where this floor opens no such line.</summary>
    public ISasTransport? HostFor(SasChannel channel) =>
        _lines.TryGetValue(channel, out MultidropLoopbackBus? line) ? line.Host : null;

    /// <summary>Every channel with a line on this floor, in order.</summary>
    public IReadOnlyList<SasChannel> Channels => [.. _lines.Keys.Order()];

    /// <summary>Every machine's address on the first channel, in order, which is the round a host walks.</summary>
    public IReadOnlyList<SasAddress> Addresses => RoundOf(First);

    /// <summary>The machines a channel's line carries, in the order the round visits them.</summary>
    public IReadOnlyList<SasAddress> RoundOf(SasChannel channel) =>
        [.. _machines.Select(machine => machine.Channels.AddressOf(channel))
            .OfType<SasAddress>()];

    /// <summary>The machines themselves, in address order.</summary>
    public IReadOnlyList<FakeEgm> Machines => _machines;

    /// <summary>Symbols lost to an end that fell behind, over every line.</summary>
    public long Dropped => _lines.Values.Sum(line => line.Dropped);

    public void Dispose()
    {
        foreach (FakeEgm machine in _machines)
            machine.Dispose();

        foreach (MultidropLoopbackBus line in _lines.Values)
            line.Dispose();
    }
}
