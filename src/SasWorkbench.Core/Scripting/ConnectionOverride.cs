using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Scripting;

/// <summary>
/// Points a suite at a different machine without editing it.
/// </summary>
/// <remarks>
/// The script says which link a test opens, and that is usually right. But the same corpus has to
/// run against the software machine, against a board over TCP and against a serial adapter, and
/// editing every connect step to move between them would make the three runs different tests.
/// Both the command line and the window apply this, so an override means the same thing in each.
/// </remarks>
/// <param name="Transport">The link to use instead, or null to leave each test's own.</param>
/// <param name="Target">The endpoint or port name, for a link that needs one.</param>
/// <param name="Address">The poll address to use instead, or null to leave each test's own.</param>
/// <param name="Channel">The channel to use instead, or null to leave each test's own.</param>
public sealed record ConnectionOverride(
    ScriptTransport? Transport = null,
    string? Target = null,
    SasAddress? Address = null,
    SasChannel? Channel = null)
{
    /// <summary>Changes nothing: every test opens the link it names.</summary>
    public static ConnectionOverride None { get; } = new();

    /// <summary>True when this would change nothing, so the documents can be passed through.</summary>
    public bool IsEmpty => Transport is null && Address is null && Channel is null;

    /// <summary>
    /// Checks the override makes sense before anything runs, and says what is wrong when it does not.
    /// </summary>
    public bool TryValidate(out string? error)
    {
        error = null;

        switch (Transport)
        {
            case ScriptTransport.Tcp
                when !TcpTransportOptions.TryParseEndpoint(Target ?? string.Empty, out _):
                error = $"'{Target}' is not an endpoint. Expected something like 127.0.0.1:7200.";
                return false;

            case ScriptTransport.Serial when string.IsNullOrWhiteSpace(Target):
                error = "a serial link needs a port name.";
                return false;

            default:
                return true;
        }
    }

    /// <summary>Rewrites every connect step. The documents are copied; the originals are untouched.</summary>
    public IReadOnlyList<ScriptDocument> ApplyTo(IReadOnlyList<ScriptDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (IsEmpty)
            return documents;

        return documents.Select(document => document with
        {
            Tests = document.Tests.Select(test => test with
            {
                Steps = test.Steps.Select(step => step is ConnectStep connect
                    ? connect with
                    {
                        Transport = Transport ?? connect.Transport,
                        Target = Transport is null ? connect.Target : Target ?? string.Empty,
                        Address = Address ?? connect.Address,
                        Channel = Channel ?? connect.Channel,
                    }
                    : step).ToList(),
            }).ToList(),
        }).ToList();
    }
}
