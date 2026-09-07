using System.Globalization;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Captures;

/// <summary>
/// Reads the legacy tester's serial log.
/// </summary>
/// <remarks>
/// <para>
/// Plain text, three kinds of line and nothing else — no header, no footer, no length, no sequence
/// number. A record is a time, then one line of what was sent, then at most one line of what came
/// back; a record with no reply is a transaction that was never answered.
/// </para>
/// <code>
/// Tue Feb 25 09:44:31 2025
/// TX&gt;= 01 1F
/// RX&lt;= 01 1F 51 58 ...
/// </code>
/// <para>
/// <b>Two things this format does not hold.</b> The ninth bit is not stored, so a message read
/// back from it cannot say where the wakeup bit was. And its clock is whole seconds, so every
/// message inside one second shares a time: the intervals a poll loop is judged by are not in this
/// file at all, and are not invented here.
/// </para>
/// </remarks>
public static class IgtSerialLogReader
{
    private const string Sent = "TX>= ";
    private const string Received = "RX<= ";

    /// <summary>The shape of the timestamp line: the C library's own, with no zone.</summary>
    private static readonly string[] TimeFormats =
        ["ddd MMM d HH:mm:ss yyyy", "ddd MMM dd HH:mm:ss yyyy"];

    /// <param name="limit">
    /// Stop after this many messages. Given, it is recorded on the capture so a partial import
    /// cannot be mistaken for a whole one.
    /// </param>
    public static Capture Read(string path, int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Parse(File.ReadLines(path), path, limit);
    }

    public static Capture Parse(IEnumerable<string> lines, string source, int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var messages = new List<CapturedMessage>();
        var unreadable = new List<string>();
        DateTimeOffset? startedAt = null;
        DateTimeOffset? current = null;
        int number = 0;
        bool stopped = false;

        foreach (string raw in lines)
        {
            number++;
            string line = raw.TrimEnd();
            if (line.Length == 0)
                continue;

            if (limit is { } cap && messages.Count >= cap)
            {
                stopped = true;
                break;
            }

            WireDirection direction;
            int prefix;
            if (line.StartsWith(Sent, StringComparison.Ordinal))
                (direction, prefix) = (WireDirection.Transmit, Sent.Length);
            else if (line.StartsWith(Received, StringComparison.Ordinal))
                (direction, prefix) = (WireDirection.Receive, Received.Length);
            else
            {
                if (TryReadTime(line, out DateTimeOffset when))
                {
                    current = when;
                    startedAt ??= when;
                }
                else
                {
                    unreadable.Add($"{source}:{number}: '{Shorten(line)}' is neither a time nor a "
                                   + "line of bytes, so this build does not know what it records");
                }

                continue;
            }

            if (!TryReadSymbols(line[prefix..], out SasSymbol[] symbols))
            {
                unreadable.Add($"{source}:{number}: '{Shorten(line)}' carries something that is not "
                               + "a pair of hexadecimal digits");
                continue;
            }

            if (symbols.Length == 0)
                continue; // A direction marker with nothing after it records no message.

            if (current is null)
            {
                unreadable.Add($"{source}:{number}: a message before any time, so it cannot be "
                               + "placed in the capture");
                continue;
            }

            messages.Add(new CapturedMessage(direction, symbols, current.Value - startedAt!.Value));
        }

        string caveat = "The log's clock is whole seconds, so every message inside one second "
                        + "carries the same time and no interval between two messages is recorded. "
                        + "Nothing here fills that in.";
        if (stopped)
            caveat += $" Reading stopped at {messages.Count} message(s) because a limit was asked "
                      + "for, so this is part of the file and not all of it.";

        return new Capture
        {
            Source = source,
            Format = CaptureFormat.IgtSerialLog,
            WakeupBitCaptured = false,
            StartedAt = startedAt,
            Messages = messages,
            Unreadable = unreadable,
            TimingCaveat = caveat,
        };
    }

    private static bool TryReadTime(string line, out DateTimeOffset when)
    {
        if (DateTime.TryParseExact(line, TimeFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out DateTime parsed))
        {
            when = new DateTimeOffset(parsed, TimeZoneInfo.Local.GetUtcOffset(parsed));
            return true;
        }

        when = default;
        return false;
    }

    private static bool TryReadSymbols(string text, out SasSymbol[] symbols)
    {
        string[] tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var read = new List<SasSymbol>(tokens.Length);

        foreach (string token in tokens)
        {
            if (token.Length != 2
                || !byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                    out byte value))
            {
                symbols = [];
                return false;
            }

            read.Add(SasSymbol.Data8(value));
        }

        symbols = [.. read];
        return true;
    }

    private static string Shorten(string line) =>
        line.Length <= 60 ? line : line[..57] + "...";
}
