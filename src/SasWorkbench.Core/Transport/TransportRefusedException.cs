namespace SasWorkbench.Core.Transport;

/// <summary>
/// Thrown when a link cannot carry SAS faithfully and the only honest thing to do is stop.
/// </summary>
/// <remarks>
/// Degrading quietly is worse than failing: a run that could not send the wakeup bit but
/// reported green would be actively misleading. This exception exists so the runner can turn
/// the reason into a skip, with the reason attached.
/// </remarks>
public sealed class TransportRefusedException : Exception
{
    public TransportRefusedException(string message) : base(message)
    {
    }

    public TransportRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
