namespace SasWorkbench.Core.Tickets;

/// <summary>Which of the two renderings of a ticket a file carries.</summary>
public enum TicketEncoding
{
    /// <summary>A key/value text file in the shape of a classic .ini.</summary>
    KeyValue,

    /// <summary>A JSON object.</summary>
    Json,
}

/// <summary>Thrown when a file cannot be read as a ticket, and why.</summary>
public sealed class TicketFormatException(string reason) : Exception(reason)
{
    /// <summary>The reason, phrased for the line of a report that explains a refusal.</summary>
    public string Reason => Message;
}
