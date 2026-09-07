using System.Globalization;
using System.Text.RegularExpressions;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Captures;

/// <summary>
/// Reads the conformance toolkit's textual trace.
/// </summary>
/// <remarks>
/// <para>
/// Verified against the thirty-two traces the toolkit ships. The shape is a header, then one line
/// per message and one line of the toolkit's own prose under it:
/// </para>
/// <code>
/// Trace started at 07/20/26 10:01:32
///
/// Timestamp is relative to end of previous poll
///
/// HOST     160.0   [01] 54
///                  Send SAS version ID and EGM serial number:
/// EGM       29.4   01 54 09 36 30 32 31 30 30 30 30 31 5B C9
/// ----------
/// </code>
/// <para>
/// <b>Square brackets are the ninth bit</b>, and this is the one capture format of the three that
/// records it. Counted over the thirty-two traces the toolkit ships: a bracket never appears
/// anywhere but the first token of a line, it appears on all 2,005 host lines, and on 13 of 945
/// machine lines. A marker for the start of a printed line would be on every line of both, so it
/// is not that; the ninth bit on every host address byte and on a hundredth of a machine's bytes
/// is exactly what the protocol says and what a receiver misreading parity would add.
/// </para>
/// <para>
/// Which also explains why a machine's reply is sometimes printed across two lines: the tracer
/// starts a new line at every byte carrying the bit, so one fourteen-byte reply with two spurious
/// bits in it comes out as three fragments. They are read as the tracer wrote them. Repairing them
/// here would hide a fault on the line and put this build's guess about the message in its place —
/// and the fragments arrive with the spurious bit reported beside them, which is the finding.
/// </para>
/// <para>
/// A timestamp of dashes means the message opens a transaction and has no interval to report; any
/// other timestamp is milliseconds since the end of the previous poll, so a message's time in the
/// capture is the running total of them.
/// </para>
/// </remarks>
public static partial class GsaTraceReader
{
    [GeneratedRegex(@"^(HOST|EGM)\s+(-+|\d+(?:\.\d+)?)\s+(.*?)\s*$")]
    private static partial Regex EventLine();

    [GeneratedRegex(@"^\[?([0-9A-Fa-f]{2})\]?$")]
    private static partial Regex SymbolToken();

    public static Capture Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Parse(File.ReadAllLines(path), path);
    }

    public static Capture Parse(IReadOnlyList<string> lines, string source)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var messages = new List<CapturedMessage>();
        var unreadable = new List<string>();
        DateTimeOffset? startedAt = null;
        var elapsed = TimeSpan.Zero;
        int pauses = 0;

        for (int number = 1; number <= lines.Count; number++)
        {
            string line = lines[number - 1];
            if (line.Trim().Length == 0)
                continue;

            if (startedAt is null && line.StartsWith("Trace started at ", StringComparison.Ordinal))
            {
                startedAt = ReadStart(line["Trace started at ".Length..].Trim());
                if (startedAt is null)
                    unreadable.Add($"{source}:{number}: the trace's own start time could not be read, "
                                   + "so nothing in it can be put on a clock");
                continue;
            }

            if (line.StartsWith("***", StringComparison.Ordinal))
            {
                // The recording stopped and started again. Times either side of it are still the
                // trace's own, but the gap between them is not an interval anything measured.
                pauses++;
                continue;
            }

            Match match = EventLine().Match(line);
            if (!match.Success)
                continue; // A separator, the header, or the toolkit's own prose.

            if (!TryReadSymbols(match.Groups[3].Value, out SasSymbol[] symbols))
            {
                unreadable.Add($"{source}:{number}: '{line.Trim()}' is a message line whose symbols "
                               + "are not pairs of hexadecimal digits");
                continue;
            }

            if (symbols.Length == 0)
            {
                unreadable.Add($"{source}:{number}: a message line with no symbols on it");
                continue;
            }

            string interval = match.Groups[2].Value;
            if (interval[0] != '-')
            {
                if (!double.TryParse(interval, CultureInfo.InvariantCulture, out double milliseconds))
                {
                    unreadable.Add($"{source}:{number}: '{interval}' is not an interval this build "
                                   + "can read, so the messages after it are not placed in time");
                    continue;
                }

                elapsed += TimeSpan.FromMilliseconds(milliseconds);
            }

            WireDirection direction = match.Groups[1].Value == "HOST"
                ? WireDirection.Transmit
                : WireDirection.Receive;

            messages.Add(new CapturedMessage(direction, symbols, elapsed));
        }

        return new Capture
        {
            Source = source,
            Format = CaptureFormat.GsaTrace,
            WakeupBitCaptured = true,
            StartedAt = startedAt,
            Messages = messages,
            Unreadable = unreadable,
            TimingCaveat = "Times are the running total of the trace's own intervals, each measured "
                           + "from the end of the previous poll rather than from the start of the "
                           + "capture, so they accumulate that tool's rounding."
                           + (pauses == 0
                               ? string.Empty
                               : $" The recording was interrupted {pauses} time(s); the gap across "
                                 + "an interruption is not an interval anything measured."),
        };
    }

    private static bool TryReadSymbols(string text, out SasSymbol[] symbols)
    {
        string[] tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var read = new List<SasSymbol>(tokens.Length);

        foreach (string token in tokens)
        {
            Match match = SymbolToken().Match(token);
            if (!match.Success)
            {
                symbols = [];
                return false;
            }

            byte value = byte.Parse(match.Groups[1].Value, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);

            // The brackets are the ninth bit, read as written rather than as it ought to be.
            read.Add(token[0] == '[' ? SasSymbol.Address(value) : SasSymbol.Data8(value));
        }

        symbols = [.. read];
        return true;
    }

    /// <summary>
    /// The trace's own start line, which is month first and a two-digit year, and carries no zone.
    /// It is read as local time because that is what the machine it was taken beside was showing.
    /// </summary>
    private static DateTimeOffset? ReadStart(string text) =>
        DateTime.TryParseExact(text, "MM/dd/yy HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime when)
            ? new DateTimeOffset(when, TimeZoneInfo.Local.GetUtcOffset(when))
            : null;
}
