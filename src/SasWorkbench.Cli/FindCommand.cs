using System.Globalization;
using System.Text.Json;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Cli;

/// <summary>
/// Finds messages in a recorded run and says which line of the file each one is on.
/// </summary>
/// <remarks>
/// The line number is the point of it. A run of any length puts more on the wire than anybody
/// reads, and the useful thing is not a filtered copy of it but a way back into the file already
/// open in an editor - so every hit is printed with the line it sits on, and the file is untouched.
/// </remarks>
internal static class FindCommand
{
    public static int Execute(CommandLine line)
    {
        if (line.Arguments.Count == 0)
        {
            Console.Error.WriteLine(
                "error: which file? Expected: sasctl find <report.jsonl> [options]");
            return 2;
        }

        string path = line.Arguments[0];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"error: there is no file at '{path}'.");
            return 2;
        }

        if (!TryBuildQuery(line, out TraceQuery query, out string problem))
        {
            Console.Error.WriteLine($"error: {problem}");
            return 2;
        }

        int lineNumber = 0;
        int examined = 0;
        int matched = 0;

        foreach (string text in File.ReadLines(path))
        {
            lineNumber++;
            if (!TryReadWire(text, out Recorded record))
                continue;

            examined++;
            if (!query.Matches(record.Direction, record.Symbols, record.Note))
                continue;

            matched++;
            Report(lineNumber, record);
        }

        Console.WriteLine();
        if (examined == 0)
        {
            Console.WriteLine(
                $"No recorded messages in {path}. A run writes them with --report jsonl.");
            return 1;
        }

        Console.WriteLine($"{matched} of {examined} message(s) matched {query} in {path}");
        return matched > 0 ? 0 : 1;
    }

    private static void Report(int lineNumber, Recorded record)
    {
        string arrow = record.Direction == WireDirection.Transmit ? "TX" : "RX";
        string hex = string.Join(' ', record.Symbols.Select(s => s.ToString()));
        string meaning = Describe(record);
        string where = record.Test is null ? string.Empty : $"  ({record.Test})";

        Console.WriteLine(
            $"{lineNumber,7}: [{record.AtMs,10:F2}ms] {arrow} {hex,-32} {meaning}{where}");
    }

    /// <summary>What the message is, in words, so a hit reads without decoding it by eye.</summary>
    private static string Describe(Recorded record)
    {
        if (!string.IsNullOrWhiteSpace(record.Note))
            return record.Note;

        TraceSubject subject = TraceQuery.Classify(record.Direction, record.Symbols);
        if (subject.Kind is HostMessageKind.SyncPoll)
            return "sync poll";
        if (subject.Kind is HostMessageKind.GeneralPoll)
            return "general poll";
        if (subject.Command is { } command)
            return $"long poll ${command:X2}";
        if (subject.Exception is { } code and not 0x00)
            return $"exception ${code:X2} {SasNames.ExceptionName(code)}";

        return string.Empty;
    }

    private static bool TryBuildQuery(CommandLine line, out TraceQuery query, out string problem)
    {
        query = new TraceQuery();
        problem = string.Empty;

        if (line.Value("lp") is { } lp)
        {
            if (!SasNames.TryParseCode(lp, out byte command))
            {
                problem = $"'{lp}' is not a long poll code. Write it in hex, such as 2F.";
                return false;
            }

            query = query with { LongPoll = command };
        }

        if (line.Value("exception") is { } ex)
        {
            if (!SasNames.TryParseCode(ex, out byte code))
            {
                problem = $"'{ex}' is not an exception code. Write it in hex, such as 4A.";
                return false;
            }

            query = query with { Exception = code };
        }

        if (line.Value("poll") is { } poll)
        {
            HostMessageKind? kind = poll.ToLowerInvariant() switch
            {
                "sync" or "synchronisation" or "synchronization" => HostMessageKind.SyncPoll,
                "general" => HostMessageKind.GeneralPoll,
                "long" => HostMessageKind.LongPoll,
                _ => null,
            };

            if (kind is null)
            {
                problem = $"'{poll}' is not a kind of poll. Expected sync, general or long.";
                return false;
            }

            query = query with { Poll = kind };
        }

        if (line.Value("side") is { } side)
        {
            TraceSide? which = side.ToLowerInvariant() switch
            {
                "host" => TraceSide.Host,
                "egm" or "machine" => TraceSide.Machine,
                "both" => TraceSide.Both,
                _ => null,
            };

            if (which is null)
            {
                problem = $"'{side}' is not a side of the link. Expected host, egm or both.";
                return false;
            }

            query = query with { Side = which.Value };
        }

        if (line.Value("text") is { } text)
            query = query with { Text = text };

        if (line.Value("address") is { } address)
        {
            if (!byte.TryParse(address, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                    out byte value) || value is 0 or > 127)
            {
                problem = $"'{address}' is not a poll address. Write it in hex, from 01 to 7F.";
                return false;
            }

            query = query with { Address = SasAddress.Of(value) };
        }

        return true;
    }

    private static bool TryReadWire(string text, out Recorded record)
    {
        record = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out JsonElement kind)
                || kind.GetString() != "wire")
            {
                return false;
            }

            if (!root.TryGetProperty("symbols", out JsonElement hex)
                || !SasSymbol.TryParseMessage(hex.GetString(), out SasSymbol[] symbols))
            {
                return false;
            }

            record = new Recorded(
                root.TryGetProperty("direction", out JsonElement d)
                && d.GetString() == nameof(WireDirection.Receive)
                    ? WireDirection.Receive
                    : WireDirection.Transmit,
                symbols,
                root.TryGetProperty("note", out JsonElement n) ? n.GetString() : null,
                root.TryGetProperty("atMs", out JsonElement at) ? at.GetDouble() : 0,
                root.TryGetProperty("test", out JsonElement t) ? t.GetString() : null);
            return true;
        }
        catch (JsonException)
        {
            // A line that is not JSON is not a recording. Skipping it rather than stopping keeps
            // the search usable on a file somebody has been editing.
            return false;
        }
    }

    /// <summary>One message read back out of a recorded run.</summary>
    private readonly record struct Recorded(
        WireDirection Direction,
        SasSymbol[] Symbols,
        string? Note,
        double AtMs,
        string? Test);
}
