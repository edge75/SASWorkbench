using System.Net;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;
using SasWorkbench.Egm;

namespace SasWorkbench.Cli;

/// <summary>
/// Runs the software gaming machine as a peer, so a host — this one or another — can be pointed
/// at something that answers.
/// </summary>
/// <remarks>
/// Worth being blunt about what this is for: it makes the host testable without a build of the
/// platform. It is not the machine under test, and a run against it proves that our host and our
/// machine agree with each other.
/// </remarks>
internal static class EgmCommand
{
    public static int Execute(CommandLine line)
    {
        string endpointText = line.Value("tcp") ?? $"127.0.0.1:{TcpTransportOptions.DefaultPort}";
        if (!TcpTransportOptions.TryParseEndpoint(endpointText, out IPEndPoint endpoint))
        {
            Console.Error.WriteLine(
                $"error: '{endpointText}' is not an endpoint. Expected something like 127.0.0.1:7200.");
            return 2;
        }

        string addressText = line.Value("address") ?? "01";
        if (!SasAddress.TryParse(addressText, out SasAddress address) || address.IsSync)
        {
            Console.Error.WriteLine(
                $"error: '{addressText}' is not a machine address. Expected 01 to 7F.");
            return 2;
        }

        TcpRole role = line.Flag("dial") ? TcpRole.Connect : TcpRole.Listen;
        bool verbose = line.Flag("verbose");

        var transport = new TcpTransport(new TcpTransportOptions(endpoint, role));
        transport.PeerAttached += peer => Console.WriteLine($"  host attached from {peer}");
        transport.PeerDetached += () => Console.WriteLine("  host detached");

        using var machine = new FakeEgm(transport, new FakeEgmOptions { Address = address });

        if (verbose)
        {
            machine.LongPollReceived += command =>
            {
                string name = SasCatalogue.LongPolls.TryGetValue(command, out SasLongPollInfo? info)
                    ? info.Description
                    : "not in the catalogue";
                Console.WriteLine($"  lp ${command:X2}  {name}");
            };
        }

        machine.Start();

        Console.WriteLine($"Software gaming machine at address {address}, "
                          + $"{(role == TcpRole.Listen ? "listening on" : "dialling")} {endpoint}.");
        Console.WriteLine("This is a fixture. A pass against it says our host and our machine agree.");
        Console.WriteLine("Press Ctrl+C to stop.");
        Console.WriteLine();

        using var stopping = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopping.Set();
        };

        stopping.Wait();

        Console.WriteLine();
        Console.WriteLine($"  {machine.GeneralPolls} general poll(s), {machine.LongPolls} long poll(s), "
                          + $"{machine.Rejections} refused");
        return 0;
    }
}
