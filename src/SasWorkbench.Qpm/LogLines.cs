namespace SasWorkbench.Qpm;

/// <summary>The lines of a log another process is still appending to.</summary>
public static class LogLines
{
    /// <summary>
    /// Only the lines already ended by a newline, without it, so a count taken now indexes the next line written.
    /// </summary>
    /// <remarks>
    /// Splitting on the newline leaves one more piece after the last one, empty or a line still being written; counting
    /// that piece made a reader skip the first line that came after the count.
    /// </remarks>
    public static string[] Complete(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] pieces = text.Split('\n');
        string[] lines = pieces[..^1];
        for (int i = 0; i < lines.Length; i++)
            lines[i] = lines[i].TrimEnd('\r');
        return lines;
    }
}
