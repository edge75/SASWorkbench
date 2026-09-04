using System.Globalization;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Scripting;

/// <summary>
/// Reads the test language: one statement per line, no build step, no manual required.
/// </summary>
/// <remarks>
/// <para>
/// Two rules shape this parser. Anything it does not recognise is an error naming the line and
/// what was expected there — a step silently ignored is a test that passes without testing
/// anything, which is the worst failure a test runner has. And every name a script uses is
/// resolved at parse time against the generated catalogue, so a misspelled exception or a meter
/// this build cannot read is caught before a link is even opened.
/// </para>
/// <para>
/// A file may hold statements directly, in which case the whole file is one test named after it,
/// or wrap them in <c>test "name" { ... }</c> blocks.
/// </para>
/// </remarks>
public static class ScriptParser
{
    private static readonly TimeSpan DefaultWithin = TimeSpan.FromSeconds(5);

    /// <summary>Reads a script from disk.</summary>
    public static ScriptDocument ParseFile(string path)
    {
        string text = File.ReadAllText(path);
        return Parse(text, path);
    }

    /// <summary>Reads a script from text.</summary>
    /// <exception cref="ScriptParseException">The script could not be read.</exception>
    public static ScriptDocument Parse(string text, string path = "<text>")
    {
        var tests = new List<ScriptTest>();
        var loose = new TestBuilder(Path.GetFileNameWithoutExtension(path), new SourceLocation(path, 1));
        TestBuilder? open = null;

        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            var location = new SourceLocation(path, index + 1);
            string line = StripComment(lines[index]).Trim();
            if (line.Length == 0)
                continue;

            if (line == "}")
            {
                if (open is null)
                    throw new ScriptParseException(location,
                        "a closing brace with no open 'test' block");

                tests.Add(open.Build());
                open = null;
                continue;
            }

            string[] words = Tokenise(line, location);

            if (words[0].Equals("test", StringComparison.OrdinalIgnoreCase))
            {
                if (open is not null)
                    throw new ScriptParseException(location,
                        $"'test' inside the block opened at {open.Location}; close it with '}}' first");
                if (words.Length < 3 || words[^1] != "{")
                    throw new ScriptParseException(location,
                        "expected: test \"<name>\" {");

                open = new TestBuilder(words[1], location);
                continue;
            }

            TestBuilder target = open ?? loose;
            ReadStatement(target, words, location);
        }

        if (open is not null)
            throw new ScriptParseException(open.Location,
                "this 'test' block is never closed with '}'");

        if (loose.HasContent)
            tests.Insert(0, loose.Build());

        if (tests.Count == 0)
            throw new ScriptParseException(new SourceLocation(path, 1),
                "the script holds no steps");

        return new ScriptDocument { Path = path, Tests = tests };
    }

    private static void ReadStatement(TestBuilder test, string[] words, SourceLocation location)
    {
        switch (words[0].ToLowerInvariant())
        {
            case "requires":
                ReadRequires(test, words, location);
                return;

            case "tag":
            case "tags":
                ReadTags(test, words, location);
                return;

            case "note":
                if (words.Length < 2)
                    throw new ScriptParseException(location, "expected: note \"<text>\"");
                test.Steps.Add(new NoteStep(location, string.Join(' ', words[1..])));
                return;

            case "host":
                ReadHost(test, words, location);
                return;

            case "egm":
                ReadEgm(test, words, location);
                return;

            default:
                throw new ScriptParseException(location,
                    $"'{words[0]}' is not a statement. Expected one of: requires, tag, note, "
                    + "host, egm, test");
        }
    }

    private static void ReadRequires(TestBuilder test, string[] words, SourceLocation location)
    {
        if (words.Length < 2)
            throw new ScriptParseException(location,
                "expected: requires <capability>[, <capability>...]");

        // Commas are optional separators, so both 'requires a, b' and 'requires a b' read the same.
        string joined = string.Join(' ', words[1..]);
        foreach (string clause in joined.Split(',', StringSplitOptions.RemoveEmptyEntries
                                                    | StringSplitOptions.TrimEntries))
        {
            string[] parts = clause.Split("==", StringSplitOptions.TrimEntries);
            if (parts.Length == 1)
            {
                foreach (string key in parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    test.Requirements.Add(new Requirement(location, key));
            }
            else if (parts.Length == 2)
            {
                test.Requirements.Add(new Requirement(location, parts[0], parts[1]));
            }
            else
            {
                throw new ScriptParseException(location,
                    $"'{clause}' is not a requirement. Expected '<capability>' or "
                    + "'<capability> == <value>'");
            }
        }
    }

    private static void ReadTags(TestBuilder test, string[] words, SourceLocation location)
    {
        if (words.Length < 2)
            throw new ScriptParseException(location, "expected: tag @<name> [@<name>...]");

        foreach (string word in words[1..])
        {
            string tag = word.TrimStart('@').Trim(',');
            if (tag.Length == 0)
                throw new ScriptParseException(location, $"'{word}' is not a tag name");
            test.Tags.Add(tag);
        }
    }

    private static void ReadHost(TestBuilder test, string[] words, SourceLocation location)
    {
        if (words.Length < 2)
            throw new ScriptParseException(location,
                "expected: host connect|poll|send|wait|expect ...");

        switch (words[1].ToLowerInvariant())
        {
            case "connect":
                test.Steps.Add(ReadConnect(words, location));
                return;

            case "poll":
                if (words.Length != 4 || !words[2].Equals("every", StringComparison.OrdinalIgnoreCase))
                    throw new ScriptParseException(location, "expected: host poll every <duration>");
                test.Steps.Add(new PollEveryStep(location, ParseDuration(words[3], location)));
                return;

            case "wait":
                if (words.Length != 3)
                    throw new ScriptParseException(location, "expected: host wait <duration>");
                test.Steps.Add(new WaitStep(location, ParseDuration(words[2], location)));
                return;

            case "send":
                test.Steps.Add(ReadSend(words, location));
                return;

            case "expect":
                test.Steps.Add(ReadExpect(test, words, location));
                return;

            default:
                throw new ScriptParseException(location,
                    $"'host {words[1]}' is not a host action. Expected connect, poll, wait, send "
                    + "or expect");
        }
    }

    private static ConnectStep ReadConnect(string[] words, SourceLocation location)
    {
        // host connect tcp 127.0.0.1:7200 address 01 [channel 1]
        // host connect loopback address 01
        if (words.Length < 3)
            throw new ScriptParseException(location,
                "expected: host connect tcp|serial|loopback [<target>] address <address> "
                + "[channel <1-4>]");

        if (!Enum.TryParse(words[2], ignoreCase: true, out ScriptTransport transport))
            throw new ScriptParseException(location,
                $"'{words[2]}' is not a transport. Expected tcp, serial or loopback");

        int cursor = 3;
        string target = string.Empty;
        if (cursor < words.Length && !IsKeyword(words[cursor]))
            target = words[cursor++];

        SasAddress address = SasAddress.Of(1);
        SasChannel channel = SasChannel.One;
        bool addressGiven = false;

        while (cursor < words.Length)
        {
            string keyword = words[cursor].ToLowerInvariant();
            if (cursor + 1 >= words.Length)
                throw new ScriptParseException(location, $"'{keyword}' needs a value after it");

            string value = words[cursor + 1];
            cursor += 2;

            switch (keyword)
            {
                case "address":
                    if (!SasAddress.TryParse(value, out address))
                        throw new ScriptParseException(location,
                            $"'{value}' is not a poll address. Expected 00 to 7F");
                    addressGiven = true;
                    break;

                case "channel":
                    if (!SasChannels.TryParse(value, out channel))
                        throw new ScriptParseException(location,
                            $"'{value}' is not a channel. Expected 1 to {SasChannels.Count}");
                    break;

                default:
                    throw new ScriptParseException(location,
                        $"'{keyword}' is not a connect option. Expected address or channel");
            }
        }

        if (!addressGiven)
            throw new ScriptParseException(location,
                "host connect needs 'address <address>': a host that does not know the machine's "
                + "address gets silence, and silence is indistinguishable from a fault");

        if (transport != ScriptTransport.Loopback && target.Length == 0)
            throw new ScriptParseException(location,
                $"host connect {words[2]} needs a target, such as an endpoint or a port name");

        if (transport == ScriptTransport.Tcp
            && !TcpTransportOptions.TryParseEndpoint(target, out _))
            throw new ScriptParseException(location,
                $"'{target}' is not an endpoint. Expected something like 127.0.0.1:7200");

        return new ConnectStep(location, transport, target, address, channel);
    }

    private static SendLongPollStep ReadSend(string[] words, SourceLocation location)
    {
        // host send lp 1F [data 0102]
        if (words.Length < 4 || !words[2].Equals("lp", StringComparison.OrdinalIgnoreCase))
            throw new ScriptParseException(location, "expected: host send lp <code> [data <hex>]");

        if (!SasNames.TryParseCode(words[3], out byte command))
            throw new ScriptParseException(location,
                $"'{words[3]}' is not a long poll code. Expected two hex digits, such as 1F");

        byte[] data = [];
        if (words.Length > 4)
        {
            if (words.Length != 6 || !words[4].Equals("data", StringComparison.OrdinalIgnoreCase))
                throw new ScriptParseException(location,
                    "expected: host send lp <code> data <hex>");
            data = ParseHex(words[5], location);
        }

        if (!SasCatalogue.LongPolls.ContainsKey(command))
            throw new ScriptParseException(location,
                $"long poll ${command:X2} is not one the gaming machine library implements");

        return new SendLongPollStep(location, command, data);
    }

    private static ScriptStep ReadExpect(TestBuilder test, string[] words, SourceLocation location)
    {
        if (words.Length < 3)
            throw new ScriptParseException(location,
                "expected: host expect exception|meter|field|link ...");

        return words[2].ToLowerInvariant() switch
        {
            "exception" => ReadExpectException(words, location, negated: false),
            "no" => ReadExpectNo(words, location),
            "meter" => ReadExpectMeter(words, location),
            "field" => ReadExpectField(test, words, location),
            "link" => ReadExpectLink(words, location),
            _ => throw new ScriptParseException(location,
                $"'{words[2]}' cannot be expected. Expected exception, no exception, meter, "
                + "field or link"),
        };
    }

    private static ScriptStep ReadExpectNo(string[] words, SourceLocation location)
    {
        if (words.Length < 4 || !words[3].Equals("exception", StringComparison.OrdinalIgnoreCase))
            throw new ScriptParseException(location,
                "expected: host expect no exception <name> [within <duration>]");

        return ReadExpectException(words[..2].Concat(words[3..]).ToArray(), location, negated: true);
    }

    private static ExpectExceptionStep ReadExpectException(
        string[] words, SourceLocation location, bool negated)
    {
        if (words.Length < 4)
            throw new ScriptParseException(location,
                "expected: host expect exception <name> [within <duration>]");

        string name = words[3];
        if (!SasNames.TryResolveException(name, out byte code))
            throw new ScriptParseException(location,
                $"'{name}' is not an exception this build knows. Write its name, such as "
                + "slot_door_opened, or its code, such as $11");

        TimeSpan within = ReadWithin(words, 4, location);
        return new ExpectExceptionStep(location, code, SasNames.ExceptionName(code), within, negated);
    }

    private static ExpectMeterStep ReadExpectMeter(string[] words, SourceLocation location)
    {
        // host expect meter total_bill_in += 2000
        // host expect meter games_played unchanged
        if (words.Length < 5)
            throw new ScriptParseException(location,
                "expected: host expect meter <name> +=|+>=|==|>=|<=|unchanged <number>");

        string meterName = words[3];
        if (!MeterIndex.TryResolve(meterName, out MeterSource _))
            throw new ScriptParseException(location,
                $"'{meterName}' is not a meter this build can read. Readable meters are: "
                + string.Join(", ", MeterIndex.KnownNames()));

        string op = words[4];
        if (op.Equals("unchanged", StringComparison.OrdinalIgnoreCase))
            return new ExpectMeterStep(location, meterName, MeterComparison.Unchanged, 0,
                ReadWithin(words, 5, location));

        if (words.Length < 6)
            throw new ScriptParseException(location, $"'{op}' needs a number after it");

        MeterComparison comparison = op switch
        {
            "+=" => MeterComparison.IncreasedBy,
            "+>=" => MeterComparison.IncreasedByAtLeast,
            "==" => MeterComparison.Equals,
            ">=" => MeterComparison.AtLeast,
            "<=" => MeterComparison.AtMost,
            _ => throw new ScriptParseException(location,
                $"'{op}' is not a meter comparison. Expected +=, +>=, ==, >=, <= or unchanged"),
        };

        if (!long.TryParse(words[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            throw new ScriptParseException(location, $"'{words[5]}' is not a number");

        return new ExpectMeterStep(location, meterName, comparison, value, ReadWithin(words, 6, location));
    }

    private static ExpectFieldStep ReadExpectField(
        TestBuilder test, string[] words, SourceLocation location)
    {
        // host expect field base_percentage == 9210
        // host expect field serial_number of lp 54 == QX12345
        if (words.Length < 4)
            throw new ScriptParseException(location,
                "expected: host expect field <name> [of lp <code>] <comparison> <value>");

        string fieldName = words[3];
        int cursor = 4;
        byte command;

        if (cursor + 2 < words.Length
            && words[cursor].Equals("of", StringComparison.OrdinalIgnoreCase)
            && words[cursor + 1].Equals("lp", StringComparison.OrdinalIgnoreCase))
        {
            if (!SasNames.TryParseCode(words[cursor + 2], out command))
                throw new ScriptParseException(location,
                    $"'{words[cursor + 2]}' is not a long poll code");
            cursor += 3;
        }
        else
        {
            // No command given, so the field must belong to the most recent 'send lp'. Resolving
            // it here rather than at run time means a script with no preceding send fails to
            // parse instead of failing mysteriously halfway through a run.
            SendLongPollStep? last = test.Steps.OfType<SendLongPollStep>().LastOrDefault();
            if (last is null)
                throw new ScriptParseException(location,
                    $"'{fieldName}' has no reply to come from. Either send a long poll first, or "
                    + "write 'of lp <code>'");
            command = last.Command;
        }

        // Two different problems, deliberately handled differently. A field name that a known
        // layout does not have is a mistake in the script, so it stops the parse. A command whose
        // reply this build cannot decode at all is a missing capability, so the step is accepted
        // and the runner skips it with that reason — which is what lets a test be written against
        // a command before its layout has been established, and reported honestly meanwhile.
        SasMessageLayout? layout = SasLayouts.For(command);
        if (layout is null && !SasCatalogue.LongPolls.ContainsKey(command))
        {
            throw new ScriptParseException(location,
                $"long poll ${command:X2} is not one the gaming machine library implements");
        }

        if (layout is not null && !layout.Response.Any(f =>
                string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ScriptParseException(location,
                $"long poll ${command:X2} has no field '{fieldName}'. Its fields are: "
                + string.Join(", ", layout.Response.Select(f => f.Name)));
        }

        if (cursor >= words.Length)
            throw new ScriptParseException(location, $"'{fieldName}' needs a comparison after it");

        SasComparison comparison = words[cursor] switch
        {
            "==" => SasComparison.Equal,
            "!=" => SasComparison.NotEqual,
            ">=" => SasComparison.AtLeast,
            "<=" => SasComparison.AtMost,
            ">" => SasComparison.GreaterThan,
            "<" => SasComparison.LessThan,
            _ => throw new ScriptParseException(location,
                $"'{words[cursor]}' is not a comparison. Expected ==, !=, >=, <=, > or <"),
        };

        cursor++;
        if (cursor >= words.Length)
            throw new ScriptParseException(location, "the comparison needs a value after it");

        string expected = string.Join(' ', words[cursor..]);

        // Whether a value is text or a number is decided by the field, not by whether the literal
        // happens to look numeric. A protocol revision written as three characters is text even
        // though '602' parses as a number, and comparing it as one would fail for a reason that
        // has nothing to do with the machine.
        SasField? declared = layout?.Response.FirstOrDefault(f =>
            string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));
        bool textual = declared?.Encoding is SasFieldEncoding.Ascii or SasFieldEncoding.Raw;

        if (!textual
            && long.TryParse(expected, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number))
        {
            return new ExpectFieldStep(location, command, fieldName, comparison, number, null);
        }

        if (comparison is not (SasComparison.Equal or SasComparison.NotEqual))
            throw new ScriptParseException(location,
                $"'{fieldName}' holds text, which can only be compared with == or !=");

        return new ExpectFieldStep(location, command, fieldName, comparison, null, expected);
    }

    private static ExpectLinkStep ReadExpectLink(string[] words, SourceLocation location)
    {
        if (words.Length < 4)
            throw new ScriptParseException(location,
                "expected: host expect link up|down|synchronising [within <duration>]");

        LinkState expected = words[3].ToLowerInvariant() switch
        {
            "up" => LinkState.Up,
            "down" => LinkState.Down,
            "synchronising" or "synchronizing" => LinkState.Synchronising,
            _ => throw new ScriptParseException(location,
                $"'{words[3]}' is not a link state. Expected up, down or synchronising"),
        };

        return new ExpectLinkStep(location, expected, ReadWithin(words, 4, location));
    }

    private static void ReadEgm(TestBuilder test, string[] words, SourceLocation location)
    {
        if (words.Length < 2)
            throw new ScriptParseException(location, "expected: egm bill|play|press|door ...");

        switch (words[1].ToLowerInvariant())
        {
            case "bill":
                if (words.Length != 4 || !words[2].Equals("insert", StringComparison.OrdinalIgnoreCase))
                    throw new ScriptParseException(location, "expected: egm bill insert <cents>");
                if (!int.TryParse(words[3], out int cents) || cents <= 0)
                    throw new ScriptParseException(location,
                        $"'{words[3]}' is not a note value in cents");
                test.Steps.Add(new InsertBillStep(location, cents));
                return;

            case "play":
                test.Steps.Add(ReadPlay(words, location));
                return;

            case "press":
                if (words.Length != 3)
                    throw new ScriptParseException(location, "expected: egm press <button>");
                test.Steps.Add(new PressButtonStep(location, words[2].ToLowerInvariant()));
                return;

            case "door":
                if (words.Length != 4)
                    throw new ScriptParseException(location, "expected: egm door <name> open|close");
                bool open = words[3].Equals("open", StringComparison.OrdinalIgnoreCase);
                if (!open && !words[3].Equals("close", StringComparison.OrdinalIgnoreCase))
                    throw new ScriptParseException(location,
                        $"'{words[3]}' is not a door action. Expected open or close");
                test.Steps.Add(new DoorStep(location, words[2].ToLowerInvariant(), open));
                return;

            default:
                throw new ScriptParseException(location,
                    $"'egm {words[1]}' is not a machine action. Expected bill, play, press or door");
        }
    }

    private static PlayGamesStep ReadPlay(string[] words, SourceLocation location)
    {
        // egm play games=2 bet=100 [win=50]
        int games = 0;
        long bet = 0;
        long win = 0;

        foreach (string word in words[2..])
        {
            string[] pair = word.Split('=', 2);
            if (pair.Length != 2 || !long.TryParse(pair[1], out long value) || value < 0)
                throw new ScriptParseException(location,
                    $"'{word}' is not a setting. Expected games=<n>, bet=<credits> or win=<credits>");

            switch (pair[0].ToLowerInvariant())
            {
                case "games":
                    games = (int)value;
                    break;
                case "bet":
                    bet = value;
                    break;
                case "win":
                    win = value;
                    break;
                default:
                    throw new ScriptParseException(location,
                        $"'{pair[0]}' is not a play setting. Expected games, bet or win");
            }
        }

        if (games <= 0)
            throw new ScriptParseException(location, "egm play needs games=<n> with n at least one");

        return new PlayGamesStep(location, games, bet, win);
    }

    private static TimeSpan ReadWithin(string[] words, int cursor, SourceLocation location)
    {
        if (cursor >= words.Length)
            return DefaultWithin;

        if (!words[cursor].Equals("within", StringComparison.OrdinalIgnoreCase))
            throw new ScriptParseException(location,
                $"'{words[cursor]}' is unexpected here. Expected 'within <duration>' or the end "
                + "of the line");

        if (cursor + 1 >= words.Length)
            throw new ScriptParseException(location, "'within' needs a duration after it");
        if (cursor + 2 < words.Length)
            throw new ScriptParseException(location,
                $"'{words[cursor + 2]}' is unexpected after the duration");

        return ParseDuration(words[cursor + 1], location);
    }

    /// <summary>Parses <c>200ms</c>, <c>5s</c>, <c>2m</c> or a bare number of milliseconds.</summary>
    public static TimeSpan ParseDuration(string text, SourceLocation location)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
            throw new ScriptParseException(location, "expected a duration, such as 200ms or 5s");

        (string suffix, double multiplier) = trimmed switch
        {
            _ when trimmed.EndsWith("ms", StringComparison.OrdinalIgnoreCase) => ("ms", 1.0),
            _ when trimmed.EndsWith('s') || trimmed.EndsWith('S') => ("s", 1000.0),
            _ when trimmed.EndsWith('m') || trimmed.EndsWith('M') => ("m", 60_000.0),
            _ => (string.Empty, 1.0),
        };

        string number = suffix.Length == 0 ? trimmed : trimmed[..^suffix.Length];
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || value < 0)
        {
            throw new ScriptParseException(location,
                $"'{text}' is not a duration. Expected something like 200ms, 5s or 2m");
        }

        return TimeSpan.FromMilliseconds(value * multiplier);
    }

    private static byte[] ParseHex(string text, SourceLocation location)
    {
        string cleaned = text.Replace(" ", string.Empty).Replace("-", string.Empty);
        if (cleaned.Length == 0 || cleaned.Length % 2 != 0)
            throw new ScriptParseException(location,
                $"'{text}' is not hex data. Expected an even number of hex digits");

        try
        {
            return Convert.FromHexString(cleaned);
        }
        catch (FormatException)
        {
            throw new ScriptParseException(location, $"'{text}' is not hex data");
        }
    }

    private static bool IsKeyword(string word) =>
        word.Equals("address", StringComparison.OrdinalIgnoreCase)
        || word.Equals("channel", StringComparison.OrdinalIgnoreCase);

    private static string StripComment(string line)
    {
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
                inQuotes = !inQuotes;
            else if (line[i] == '#' && !inQuotes)
                return line[..i];
        }

        return line;
    }

    private static string[] Tokenise(string line, SourceLocation location)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (inQuotes)
            throw new ScriptParseException(location, "a quoted value is never closed");

        if (current.Length > 0)
            words.Add(current.ToString());

        return words.ToArray();
    }

    private sealed class TestBuilder(string name, SourceLocation location)
    {
        public SourceLocation Location { get; } = location;

        public List<Requirement> Requirements { get; } = [];

        public List<string> Tags { get; } = [];

        public List<ScriptStep> Steps { get; } = [];

        public bool HasContent => Steps.Count > 0 || Requirements.Count > 0;

        public ScriptTest Build() => new()
        {
            Name = name,
            Location = Location,
            Requirements = Requirements,
            Tags = Tags,
            Steps = Steps,
        };
    }
}
