using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>
/// Several software gaming machines on one multi-drop line, addressed 1 upwards, with one host
/// end for a poll engine to drive.
/// </summary>
/// <remarks>
/// <para>
/// The second axis made concrete. Each machine is the same fixture the two-channel cases use and
/// answers the protocol properly; what is new is that they share a line, so they are told apart by
/// address alone and a host reaches all of them through one transport.
/// </para>
/// <para>
/// It is still not a floor of gaming machines. Every machine here is one this build wrote, so a
/// clean round proves that our host can walk a round and that our machines answer only their own
/// polls — not anything about a hundred real machines on a real line.
/// </para>
/// </remarks>
public sealed class SoftwareFloor : IDisposable
{
    private readonly MultidropLoopbackBus _bus;
    private readonly List<FakeEgm> _machines = [];

    private SoftwareFloor(MultidropLoopbackBus bus, IReadOnlyList<SasAddress> addresses)
    {
        _bus = bus;
        Addresses = addresses;
    }

    /// <summary>
    /// Brings up a floor of machines at consecutive addresses.
    /// </summary>
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

        var bus = new MultidropLoopbackBus(machines);
        var addresses = new List<SasAddress>(machines);
        var floor = new SoftwareFloor(bus, addresses);

        for (int i = 0; i < machines; i++)
        {
            SasAddress address = SasAddress.Of((byte)(start + i));
            addresses.Add(address);

            FakeEgmOptions settings = (template ?? new FakeEgmOptions()) with
            {
                Address = address,
                Channel = SasChannel.One,

                // Each machine settles its own arrangement at its own address. Sharing one plan
                // across the floor would put every machine at the first one's address, which is
                // the mistake that makes a round look clean while eight machines answer as one.
                Channels = null,
            };

            var machine = new FakeEgm(bus.Machines[i], settings);
            machine.Start();
            floor._machines.Add(machine);
        }

        return floor;
    }

    /// <summary>The one end a poll engine drives.</summary>
    public ISasTransport Host => _bus.Host;

    /// <summary>Every machine's address, in order, which is the round a host walks.</summary>
    public IReadOnlyList<SasAddress> Addresses { get; }

    /// <summary>The machines themselves, in address order.</summary>
    public IReadOnlyList<FakeEgm> Machines => _machines;

    /// <summary>Symbols lost to an end that fell behind, over the whole line.</summary>
    public long Dropped => _bus.Dropped;

    public void Dispose()
    {
        foreach (FakeEgm machine in _machines)
            machine.Dispose();

        _bus.Dispose();
    }
}
