using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Tickets;

namespace SasWorkbench.Qpm;

/// <summary>Where to find the machine's peripherals and its configuration.</summary>
/// <param name="RegionPath">
/// The peripheral shared region, or null for this platform's usual place.
/// </param>
/// <param name="ResourcesDirectory">
/// The machine's own resource directory, which is where its door and button indices are declared.
/// </param>
/// <param name="Timings">How long to wait for the machine, or null for the usual timings.</param>
public sealed record QpmOptions(
    string? RegionPath = null,
    string? ResourcesDirectory = null,
    QpmTimings? Timings = null);

/// <summary>
/// Builds an environment whose stimulus comes from the peripheral shared region.
/// </summary>
/// <remarks>
/// An environment that cannot reach the machine's peripherals still builds, carrying the reason
/// it could not. That is what turns "the gaming machine is not running" into a skip that names
/// the problem on the tests that needed stimulus, while the tests that only need the wire go on
/// running — rather than an error that stops the suite and says nothing useful about either.
/// </remarks>
public static class QpmEnvironment
{
    /// <summary>An environment factory that drives the machine's peripherals directly.</summary>
    public static EnvironmentFactory For(QpmOptions? options = null)
    {
        QpmOptions settings = options ?? new QpmOptions();

        // The declaration is deliberately dropped: a machine driven through its peripherals is a
        // real machine, its accounting denomination is established by a memory clear, and nothing
        // reachable from here can change it. The runner reads it off the wire and skips a case
        // that assumed another unit, which is the honest outcome.
        return (step, runnerOptions, _, channels, _, _, _, _) =>
        {
            //  The channel arrangement is dropped for the same reason the denomination is: a
            //  machine driven through its peripherals is a real machine, its channels are set in
            //  its own configuration menu, and nothing reachable from here changes them. The link
            //  a second channel needs is opened all the same, so a case can still drive two hosts.
            ScriptEnvironment link = DefaultEnvironment.Create(
                step, runnerOptions with { Stimulus = StimulusBackend.None }, null, channels);

            IStimulusBackend stimulus = Attach(settings, runnerOptions.AcceptorDirectory);

            return link with
            {
                Stimulus = stimulus,
                ExtraCapabilities = stimulus.Capabilities,
                Owned = stimulus as IDisposable,
            };
        };
    }

    /// <summary>
    /// What the peripheral driver would offer, read from the machine's configuration alone.
    /// </summary>
    /// <remarks>
    /// Nothing is attached and nothing is written, so it is safe to ask for a case that never
    /// opens a link, whether or not a machine is running.
    /// </remarks>
    public static IReadOnlyDictionary<string, string?> Offer(
        QpmOptions? options = null, string? acceptorDirectory = null)
    {
        QpmOptions settings = options ?? new QpmOptions();
        return QpmStimulusBackend.Offer(
            EgmHardwareProfile.ReadFrom(settings.ResourcesDirectory),
            acceptorDirectory ?? TicketAcceptor.DefaultDirectory).AsReadOnly();
    }

    /// <summary>
    /// Attaches to the machine's peripherals, or hands back a backend that refuses and says why.
    /// </summary>
    public static IStimulusBackend Attach(QpmOptions? options = null, string? acceptorDirectory = null)
    {
        QpmOptions settings = options ?? new QpmOptions();

        try
        {
            return new QpmStimulusBackend(
                QpmMachine.Attach(settings.RegionPath, settings.ResourcesDirectory, settings.Timings),
                acceptorDirectory);
        }
        catch (QpmUnavailableException unavailable)
        {
            return new RefusingStimulusBackend(unavailable.Reason);
        }
    }
}
