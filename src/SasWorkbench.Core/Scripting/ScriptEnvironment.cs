using System.Net;
using SasWorkbench.Core.Transport;

namespace SasWorkbench.Core.Scripting;

/// <summary>
/// Everything a test needs in order to run: a link to the machine, and something that can act
/// on it.
/// </summary>
/// <param name="HostTransport">The host end of the link.</param>
/// <param name="Stimulus">What drives the machine.</param>
/// <param name="ExtraCapabilities">What this environment adds beyond the baseline.</param>
/// <param name="Owned">
/// Anything the environment created and must dispose afterwards, such as a peer it started.
/// </param>
public sealed record ScriptEnvironment(
    ISasTransport HostTransport,
    IStimulusBackend Stimulus,
    IReadOnlyDictionary<string, string?> ExtraCapabilities,
    IDisposable? Owned = null) : IDisposable
{
    public void Dispose()
    {
        HostTransport.Dispose();
        Owned?.Dispose();
    }
}

/// <summary>Builds the environment a connect step asks for.</summary>
/// <remarks>
/// The declaration is handed over here rather than checked afterwards because a software machine
/// has to be built in the configuration a case assumes, and the link opens before the first
/// requirement is looked at. An environment that cannot honour it ignores it: the value is read
/// back off the wire either way, so a case running against a machine it cannot configure is
/// skipped rather than run against the wrong unit.
/// </remarks>
public delegate ScriptEnvironment EnvironmentFactory(
    ConnectStep step, ScriptRunnerOptions options, MachineDeclaration? declared);

/// <summary>
/// The environments this assembly can build on its own: a real link to something already
/// running, with nothing driving it.
/// </summary>
/// <remarks>
/// A software gaming machine peer lives in its own assembly, and wiring one in is the caller's
/// choice. That keeps the runner honest about what it is talking to: a loopback link built here
/// has no machine on the other end at all, and says so, rather than quietly substituting a
/// fixture for the thing under test.
/// </remarks>
public static class DefaultEnvironment
{
    /// <summary>Opens the link a connect step names, with no stimulus backend.</summary>
    /// <remarks>
    /// The declaration is not acted on: there is no machine here this build made, so there is
    /// nothing to configure. It is read back off the wire by the runner like any other fact about
    /// the machine.
    /// </remarks>
    public static ScriptEnvironment Create(
        ConnectStep step, ScriptRunnerOptions options, MachineDeclaration? declared = null)
    {
        IStimulusBackend stimulus = options.Stimulus switch
        {
            StimulusBackend.Manual when options.AskPerson is not null =>
                new ManualStimulusBackend(options.AskPerson, options.PromptDeadline),

            // Asking for the software machine on a link this environment did not build is a
            // contradiction, and saying so beats handing back a backend that does nothing: the
            // assertions after each stimulus step would then be checking the machine's earlier
            // state and passing for the wrong reason.
            StimulusBackend.Software => new RefusingStimulusBackend(
                "the software gaming machine is reached over a loopback link, so it cannot drive "
                + $"a machine on {step.Transport.ToString().ToLowerInvariant()}"),

            _ => new NoStimulusBackend(),
        };

        ISasTransport transport = step.Transport switch
        {
            ScriptTransport.Tcp => BuildTcp(step, options),
            ScriptTransport.Serial => new SerialTransport(new SerialTransportOptions(step.Target)),
            ScriptTransport.Loopback => throw new TransportRefusedException(
                "A loopback link needs a peer on the other end, and this environment provides "
                + "none. Run against a machine over TCP, or supply an environment that starts one."),
            _ => throw new TransportRefusedException(
                $"'{step.Transport}' is not a link this build can open."),
        };

        return new ScriptEnvironment(transport, stimulus, stimulus.Capabilities);
    }

    private static ISasTransport BuildTcp(ConnectStep step, ScriptRunnerOptions options)
    {
        if (!TcpTransportOptions.TryParseEndpoint(step.Target, out IPEndPoint endpoint))
            throw new TransportRefusedException($"'{step.Target}' is not an endpoint.");

        return new TcpTransport(new TcpTransportOptions(endpoint, options.TcpRole));
    }
}
