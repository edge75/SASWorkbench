using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Tickets;
using SasWorkbench.Core.Transport;
using SasWorkbench.Qpm;

namespace SasWorkbench.Cli;

/// <summary>
/// Reports what this environment can and cannot do, so a skip in a report can be explained
/// without guessing.
/// </summary>
internal static class ProbeCommand
{
    public static int Execute(CommandLine line)
    {
        CapabilitySet capabilities = CapabilityProbe.Baseline();
        var peripherals = new QpmOptions(line.Value("qpm-shm"), line.Value("egm-resources"));
        string? refusal = null;

        string? stimulus = line.Value("stimulus");
        if (stimulus is not null)
        {
            if (!Enum.TryParse(stimulus, ignoreCase: true, out StimulusBackend backend))
            {
                Console.Error.WriteLine(
                    $"error: '{stimulus}' is not a stimulus backend. Expected none, manual, "
                    + "software or qpm.");
                return 2;
            }

            // The peripheral driver is asked the real machine rather than answered from a table:
            // which doors and buttons exist is something only the machine on the other end knows,
            // and a probe that guessed would be the thing this command exists to prevent.
            if (backend == StimulusBackend.Qpm)
            {
                IStimulusBackend live = QpmEnvironment.Attach(peripherals);
                try
                {
                    capabilities = capabilities.With(live.Capabilities);

                    if (live is QpmStimulusBackend attached)
                        Describe(attached);
                    else
                        refusal = Refused(live);
                }
                finally
                {
                    (live as IDisposable)?.Dispose();
                }
            }
            else
            {
                capabilities = capabilities.With(CapabilityProbe.ForStimulus(backend));
            }
        }

        Console.WriteLine("Protocol this build knows");
        (int longPolls, int exceptions, int meters) = SasCatalogue.Counts;
        Console.WriteLine($"  {longPolls} long polls, {exceptions} exceptions, {meters} meters");
        Console.WriteLine($"  {SasLayouts.All.Count} replies can be decoded field by field");
        Console.WriteLine($"  {MeterIndex.All.Count} meters can be read by name");
        Console.WriteLine();

        Console.WriteLine("Links");
        Console.WriteLine("  loopback   available — in-process, proves our own framing only");
        Console.WriteLine("  tcp        available — nine-bit symbols, one word per symbol");

        string[] ports = SerialTransport.AvailablePorts();
        if (ports.Length == 0)
        {
            Console.WriteLine("  serial     no ports on this machine");
        }
        else
        {
            using var probe = new SerialTransport(new SerialTransportOptions(ports[0]));
            TransportCapabilities caps = probe.Capabilities;
            string state = caps.CarriesWakeupBit ? "available" : "refused";
            Console.WriteLine($"  serial     {state} — {string.Join(", ", ports)}");
            if (caps.Caveat is not null)
                Console.WriteLine($"             {caps.Caveat}");
        }

        Console.WriteLine();
        Console.WriteLine("What a test may require");

        //  The wallet is added by a run rather than by the baseline, because the directory is a
        //  setting of the run. Showing it here means the probe lists what a run would actually
        //  offer rather than a subset of it.
        capabilities = capabilities.With(CapabilityProbe.ForWallet(TicketWallet.DefaultDirectory));
        foreach (string name in capabilities.Names)
        {
            string? value = capabilities.Value(name);
            Console.WriteLine(value is null ? $"  {name}" : $"  {name} = {value}");
        }

        Console.WriteLine();
        Console.WriteLine("Not available yet, and why");
        Console.WriteLine("  printer            whether the machine has one, and whether its protocol "
            + "is set, is a property of the machine and is not readable from here");
        if (refusal is not null)
            Console.WriteLine($"  stimulus.qpm       {refusal}");
        Console.WriteLine("  egm.validation     the software machine publishes what it was built "
            + "with; nothing here reads it off a real machine's wire");
        Console.WriteLine("  channel.2-4        carried through the model, but no second link is wired");

        return 0;
    }

    private static void Describe(QpmStimulusBackend backend)
    {
        EgmHardwareProfile profile = backend.Machine.Profile;

        Console.WriteLine("Peripherals of the machine now running");
        Console.WriteLine($"  shared region      {backend.Machine.RegionPath}");
        Console.WriteLine($"  configuration      {profile.Source}");
        Console.WriteLine($"  doors              {Join(profile.Doors.Select(d => d.Type))}");
        Console.WriteLine($"  buttons            {Join(profile.Buttons.Select(b => b.Label))}");
        Console.WriteLine();
    }

    private static string Join(IEnumerable<string> names)
    {
        string joined = string.Join(", ", names);
        return joined.Length == 0 ? "none declared" : joined;
    }

    private static string Refused(IStimulusBackend backend)
    {
        try
        {
            backend.Apply(new SettleStep(SourceLocation.None));
            return "refused, for a reason it would not state";
        }
        catch (StimulusUnavailableException unavailable)
        {
            return unavailable.Reason;
        }
    }
}
