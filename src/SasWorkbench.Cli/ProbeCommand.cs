using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;

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

            capabilities = capabilities.With(CapabilityProbe.ForStimulus(backend));
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
        foreach (string name in capabilities.Names)
        {
            string? value = capabilities.Value(name);
            Console.WriteLine(value is null ? $"  {name}" : $"  {name} = {value}");
        }

        Console.WriteLine();
        Console.WriteLine("Not available yet, and why");
        Console.WriteLine("  printer            the ticket printer arrives with the ticket work");
        Console.WriteLine("  stimulus.qpm       the peripheral simulator is not driven yet");
        Console.WriteLine("  egm.validation     nothing here reads the machine's validation setting");
        Console.WriteLine("  channel.2-4        carried through the model, but no second link is wired");

        return 0;
    }
}
