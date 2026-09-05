namespace SasWorkbench.Core.Tickets;

/// <summary>
/// Reading and writing the one ticket model in either of its two encodings.
/// </summary>
/// <remarks>
/// The encoding is chosen by the file's extension when reading and named outright when writing.
/// An extension this build does not recognise is refused rather than guessed at: a ticket read as
/// the wrong encoding would either fail confusingly or, worse, half-succeed. The overload that
/// takes an encoding is there for the caller who genuinely knows better than the file name.
/// </remarks>
public static class TicketFile
{
    /// <summary>The extension a ticket written in each encoding is given by default.</summary>
    public static string ExtensionFor(TicketEncoding encoding) =>
        encoding == TicketEncoding.Json ? ".json" : ".ticket";

    /// <summary>
    /// Which encoding a path names, or a refusal listing the extensions this build knows.
    /// </summary>
    /// <exception cref="TicketFormatException">The extension is not one of the recognised ones.</exception>
    public static TicketEncoding EncodingOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".json" => TicketEncoding.Json,
            ".ticket" or ".ini" or ".txt" => TicketEncoding.KeyValue,
            _ => throw new TicketFormatException(
                $"'{Path.GetFileName(path)}' does not say which encoding it is in. A ticket is read from "
                + ".json as JSON, and from .ticket, .ini or .txt as key/value text."),
        };
    }

    /// <summary>Renders a ticket in the named encoding.</summary>
    public static string Render(Ticket ticket, TicketEncoding encoding) =>
        encoding == TicketEncoding.Json ? TicketJsonCodec.Write(ticket) : TicketKeyValueCodec.Write(ticket);

    /// <summary>Reads a ticket from text in the named encoding.</summary>
    /// <exception cref="TicketFormatException">The text is not a ticket in that encoding.</exception>
    public static Ticket Parse(string text, TicketEncoding encoding) =>
        encoding == TicketEncoding.Json ? TicketJsonCodec.Read(text) : TicketKeyValueCodec.Read(text);

    /// <summary>Reads a ticket, choosing the encoding from the file's extension.</summary>
    /// <exception cref="TicketFormatException">The extension is unknown, or the file is not a ticket.</exception>
    public static Ticket Read(string path) => Read(path, EncodingOf(path));

    /// <summary>Reads a ticket in an encoding the caller names, whatever the file is called.</summary>
    /// <exception cref="TicketFormatException">The file is not a ticket in that encoding.</exception>
    public static Ticket Read(string path, TicketEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(path);

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TicketFormatException($"'{path}' could not be read: {ex.Message}");
        }

        try
        {
            return Parse(text, encoding);
        }
        catch (TicketFormatException ex)
        {
            throw new TicketFormatException($"{Path.GetFileName(path)}: {ex.Reason}");
        }
    }

    /// <summary>Writes a ticket in the named encoding.</summary>
    public static void Write(string path, Ticket ticket, TicketEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(ticket);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, Render(ticket, encoding));
    }

    /// <summary>Writes a ticket in whichever encoding the file's extension names.</summary>
    /// <exception cref="TicketFormatException">The extension is not one of the recognised ones.</exception>
    public static void Write(string path, Ticket ticket) => Write(path, ticket, EncodingOf(path));
}
