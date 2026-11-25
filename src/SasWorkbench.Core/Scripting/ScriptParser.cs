using System.Globalization;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Tickets;
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
public static partial class ScriptParser
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

            case "machine":
                ReadMachine(test, words, location);
                return;

            default:
                throw new ScriptParseException(location,
                    $"'{words[0]}' is not a statement. Expected one of: requires, machine, tag, "
                    + "note, host, egm, test");
        }
    }

    /// <summary>
    /// Reads what a case assumes the machine is already in, rather than what it does to it.
    /// </summary>
    /// <remarks>
    /// Deliberately a different word from <c>egm</c>: <c>machine</c> describes, <c>egm</c> acts.
    /// The distinction is the whole of D36 — a declaration builds the software peer before the
    /// link opens and is a precondition check on any other machine, while a setter is a stimulus
    /// that a machine may refuse.
    /// </remarks>
    private static void ReadMachine(TestBuilder test, string[] words, SourceLocation location)
    {
        if (words.Length < 3)
            throw new ScriptParseException(location, MachineUsage);

        bool accounting = words[1].Equals("accounting", StringComparison.OrdinalIgnoreCase)
                          && words[2].Equals("denomination", StringComparison.OrdinalIgnoreCase);
        bool player = words[1].Equals("player", StringComparison.OrdinalIgnoreCase)
                      && words[2].Equals("denominations", StringComparison.OrdinalIgnoreCase);

        if (words[1].Equals("channel", StringComparison.OrdinalIgnoreCase))
        {
            ReadMachineChannel(test, words, location);
            return;
        }

        if (words[1].Equals("validation", StringComparison.OrdinalIgnoreCase))
        {
            ReadMachineValidation(test, words, location);
            return;
        }

        if (words[1].Equals("component", StringComparison.OrdinalIgnoreCase))
        {
            ReadMachineAuthentication(test, words, location);
            return;
        }

        if (words[1].Equals("multiplied", StringComparison.OrdinalIgnoreCase))
        {
            ReadMachineMultipliedJackpot(test, words, location);
            return;
        }

        if (words[1].Equals("progressive", StringComparison.OrdinalIgnoreCase))
        {
            test.Progressive = ReadMachineProgressive(test.Progressive, words, location);
            return;
        }

        if (words[1].Equals("handpay", StringComparison.OrdinalIgnoreCase))
        {
            test.Handpay = ReadMachineHandpay(test.Handpay, words, location);
            return;
        }

        if (words[1].Equals("games", StringComparison.OrdinalIgnoreCase))
        {
            ReadMachineGames(test, words, location);
            return;
        }

        if (!accounting && !player)
            throw new ScriptParseException(location, MachineUsage);

        if (accounting)
        {
            if (test.Denomination is not null)
                throw new ScriptParseException(location,
                    "this case already declares an accounting denomination; a machine is in one "
                    + "unit at a time");

            (byte code, int cents) = ReadDenomination(words[3..], location);
            test.Denomination = (code, cents);
            test.DeclaredAt ??= location;
            return;
        }

        if (words.Length < 4)
            throw new ScriptParseException(location,
                "expected: machine player denominations <cents>c [<cents>c ...]");

        foreach (string word in words[3..])
        {
            if (!TryReadCents(word, out int cents))
                throw new ScriptParseException(location,
                    $"'{word}' is not a player denomination. Expected a number of cents such as "
                    + "'10c'");
            test.PlayerDenominations.Add(cents);
        }

        test.DeclaredAt ??= location;
    }

    /// <summary>Reads 'machine validation &lt;style&gt;', the style the machine is assumed to perform.</summary>
    private static void ReadMachineValidation(TestBuilder test, string[] words, SourceLocation location)
    {
        if (words.Length != 3 || !MachineValidationDeclaration.TryParse(words[2], out SasCashoutValidation style))
            throw new ScriptParseException(location,
                "expected: machine validation standard|secure_enhanced|system. A machine with no "
                + "host validation is a standard one on the wire, so there is no fourth style");

        if (test.Validation is not null)
            throw new ScriptParseException(location,
                "this case already declares a validation style; a machine performs one at a time");

        test.Validation = new MachineValidationDeclaration(location, style);
    }

    /// <summary>Reads 'machine component authentication on', that the machine takes part in component authentication.</summary>
    private static void ReadMachineAuthentication(TestBuilder test, string[] words, SourceLocation location)
    {
        if (words.Length != 4 || !words[2].Equals("authentication", StringComparison.OrdinalIgnoreCase)
            || !words[3].Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            throw new ScriptParseException(location,
                "expected: machine component authentication on. A machine taking no part is the default, so there is no 'off'");
        }

        if (test.Authentication is not null)
            throw new ScriptParseException(location, "this case already declares component authentication");

        test.Authentication = new MachineAuthenticationDeclaration(location);
    }

    /// <summary>Reads 'machine multiplied jackpot on', that the machine multiplies jackpots.</summary>
    private static void ReadMachineMultipliedJackpot(TestBuilder test, string[] words, SourceLocation location)
    {
        if (words.Length != 4 || !words[2].Equals("jackpot", StringComparison.OrdinalIgnoreCase)
            || !words[3].Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            throw new ScriptParseException(location,
                "expected: machine multiplied jackpot on. A machine without multiplied jackpots is the default, so there is no 'off'");
        }

        if (test.MultipliedJackpot is not null)
            throw new ScriptParseException(location, "this case already declares multiplied jackpots");

        test.MultipliedJackpot = new MachineMultipliedJackpotDeclaration(location);
    }

    /// <summary>Reads 'machine handpay legacy' or 'machine handpay validation on', each at most once.</summary>
    private static MachineHandpayDeclaration ReadMachineHandpay(
        MachineHandpayDeclaration? earlier, string[] words, SourceLocation location)
    {
        bool legacy = words.Length == 3 && words[2].Equals("legacy", StringComparison.OrdinalIgnoreCase);
        bool validation = words.Length == 4 && words[2].Equals("validation", StringComparison.OrdinalIgnoreCase)
                          && words[3].Equals("on", StringComparison.OrdinalIgnoreCase);
        if (!legacy && !validation)
        {
            throw new ScriptParseException(location,
                "expected: machine handpay legacy, or machine handpay validation on. The hand pay queue without "
                + "validation is the default, so neither has an 'off'");
        }

        if ((legacy && earlier is { Legacy: true }) || (validation && earlier is { Validation: true }))
            throw new ScriptParseException(location, "this case already declares that");

        earlier ??= new MachineHandpayDeclaration(location, false, false);
        return earlier with { Legacy = earlier.Legacy || legacy, Validation = earlier.Validation || validation };
    }

    private const string MachineUsage =
        "expected: machine accounting denomination <cents>c|code <hex>, or "
        + "machine player denominations <cents>c [<cents>c ...], or "
        + "machine validation standard|secure_enhanced|system, "
        + "machine progressive sas group <g> levels <list>|non_sas levels <list>, "
        + "machine component authentication on, machine multiplied jackpot on, "
        + "machine handpay legacy|validation on, machine games <cents>c [<cents>c ...], or "
        + "machine channel <1-4> address <hh>|addresses <round> [owns <category> ...]";

    /// <summary>
    /// Reads one channel of the machine's arrangement: its poll address, and what it owns.
    /// </summary>
    /// <remarks>
    /// One line per channel, which is the shape the machine's own menu has: a poll address per
    /// port, and each category pointed at one of them. An address of <c>00</c> says the channel
    /// has none, because that is how the machine stores it — so a script can write the one
    /// arrangement that must be refused, and see it refused, rather than being unable to say it.
    /// </remarks>
    private static void ReadMachineChannel(TestBuilder test, string[] words, SourceLocation location)
    {
        if (!SasChannels.TryParse(words[2], out SasChannel channel))
            throw new ScriptParseException(location,
                $"'{words[2]}' is not a channel. Expected 1 to {SasChannels.Count}");

        if (test.ChannelAddresses.ContainsKey(channel))
            throw new ScriptParseException(location,
                $"this case already says what channel {(int)channel} is; a channel has one poll "
                + "address");

        int cursor = 3;
        bool addressGiven = false;
        var owns = new List<SasCategory>();

        while (cursor < words.Length)
        {
            string keyword = words[cursor].ToLowerInvariant();
            if (cursor + 1 >= words.Length)
                throw new ScriptParseException(location, $"'{keyword}' needs a value after it");

            switch (keyword)
            {
                case "address":
                    if (!SasAddress.TryParse(words[cursor + 1], out SasAddress address))
                        throw new ScriptParseException(location,
                            $"'{words[cursor + 1]}' is not a poll address. Expected 00 to 7F");

                    // Zero is not an address a host polls: the machine stores it to mean the
                    // channel is not opened at all, and this keeps that meaning rather than
                    // reading it as the synchronisation address.
                    test.ChannelAddresses[channel] = address.IsSync ? null : address;
                    addressGiven = true;
                    cursor += 2;
                    break;

                //  A round of machines on this channel's line. A channel is a port and a port is a
                //  line, so this is the floor that line carries — and the k-th machine of every
                //  channel's round is the same machine.
                case "addresses":
                    if (!SasAddressRound.TryParse(words[cursor + 1],
                            out IReadOnlyList<SasAddress> round, out string? bad))
                    {
                        throw new ScriptParseException(location, bad!);
                    }

                    test.ChannelRounds[channel] = round;
                    test.ChannelAddresses[channel] = round[0];
                    addressGiven = true;
                    cursor += 2;
                    break;

                case "owns":
                    cursor++;
                    while (cursor < words.Length && !IsChannelKeyword(words[cursor]))
                    {
                        if (!SasCategories.TryParse(words[cursor], out SasCategory category))
                            throw new ScriptParseException(location,
                                $"'{words[cursor]}' is not a category. Known: "
                                + $"{SasCategories.Names}");

                        owns.Add(category);
                        cursor++;
                    }

                    if (owns.Count == 0)
                        throw new ScriptParseException(location,
                            $"'owns' needs at least one category. Known: {SasCategories.Names}");
                    break;

                default:
                    throw new ScriptParseException(location,
                        $"'{keyword}' is not a channel option. Expected address, addresses or owns");
            }
        }

        if (!addressGiven)
            throw new ScriptParseException(location,
                $"machine channel {(int)channel} needs 'address <hh>' or 'addresses <round>': "
                + "write 'address 00' to say the channel has none");

        foreach (SasCategory category in owns)
        {
            if (test.ChannelOwners.TryGetValue(category, out SasChannel already) && already != channel)
                throw new ScriptParseException(location,
                    $"{SasCategories.NameOf(category)} is already on channel {(int)already}. The "
                    + "machine holds one channel number per category, so it cannot be on two");

            test.ChannelOwners[category] = channel;
        }

        test.ChannelsDeclaredAt ??= location;
    }

    private static bool IsChannelKeyword(string word) =>
        word.Equals("address", StringComparison.OrdinalIgnoreCase)
        || word.Equals("addresses", StringComparison.OrdinalIgnoreCase)
        || word.Equals("owns", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a denomination written either as a number of cents or as a Table C-4 code.
    /// </summary>
    /// <remarks>
    /// A bare number is refused rather than guessed at. '3' is ten cents as a code and three cents
    /// as an amount, and the two readings differ by more than a factor of three on every credit
    /// meter in the machine — so the script says which it means.
    /// </remarks>
    private static (byte Code, int Cents) ReadDenomination(string[] words, SourceLocation location)
    {
        if (words.Length == 2 && words[0].Equals("code", StringComparison.OrdinalIgnoreCase))
        {
            if (!SasNames.TryParseCode(words[1], out byte code))
                throw new ScriptParseException(location,
                    $"'{words[1]}' is not a Table C-4 denomination code");

            int? cents = SasDenomination.CentsOf(code);
            if (cents is null)
                throw new ScriptParseException(location,
                    $"${code:X2} is not a denomination the protocol can express as an accounting "
                    + "unit, so no machine can be in force at it");

            return (code, cents.Value);
        }

        if (words.Length == 1 && TryReadCents(words[0], out int amount))
        {
            if (!MachineDeclaration.TryFromCents(amount, out byte code))
                throw new ScriptParseException(location,
                    $"{amount} cents is not in Table C-4, so no machine can be in force at it");

            return (code, amount);
        }

        throw new ScriptParseException(location, MachineUsage);
    }

    /// <summary>Reads '10c' as ten cents. The suffix is what keeps it from being read as a code.</summary>
    private static bool TryReadCents(string word, out int cents)
    {
        cents = 0;
        string trimmed = word.TrimEnd(',');
        if (trimmed.Length < 2
            || (trimmed[^1] != 'c' && trimmed[^1] != 'C'))
        {
            return false;
        }

        return int.TryParse(trimmed[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                   out cents)
               && cents > 0;
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
                "expected: host connect|poll|send|wait|forget|expect ...");

        switch (words[1].ToLowerInvariant())
        {
            case "connect":
                test.Steps.Add(ReadConnect(words, location));
                return;

            case "poll" when words.Length >= 3 && words[2].Equals("address", StringComparison.OrdinalIgnoreCase):
                test.Steps.Add(ReadFollowAddress(words, location));
                return;

            case "poll":
                if (words.Length == 3 && words[2].Equals("sequence", StringComparison.OrdinalIgnoreCase))
                {
                    test.Steps.Add(new PollInSequenceStep(location));
                    return;
                }

                if (words.Length != 4 || !words[2].Equals("every", StringComparison.OrdinalIgnoreCase))
                    throw new ScriptParseException(location,
                        "expected: host poll every <duration>, or host poll sequence");
                test.Steps.Add(new PollEveryStep(location, ParseDuration(words[3], location)));
                return;

            case "broadcast":
                test.Steps.Add(ReadBroadcast(words, location));
                return;

            case "wait":
                if (words.Length != 3)
                    throw new ScriptParseException(location, "expected: host wait <duration>");
                test.Steps.Add(new WaitStep(location, ParseDuration(words[2], location)));
                return;

            case "warm":
                if (words.Length != 4 || !words[2].Equals("up", StringComparison.OrdinalIgnoreCase))
                    throw new ScriptParseException(location, "expected: host warm up <duration>");
                test.Steps.Add(new WarmUpStep(location, ParseDuration(words[3], location)));
                return;

            case "send":
                if (words.Length > 2 && words[2].Equals("gp", StringComparison.OrdinalIgnoreCase))
                {
                    test.Steps.Add(ReadSendGeneralPoll(words, location));
                    return;
                }

                if (words.Length == 3 && words[2].Equals("sync", StringComparison.OrdinalIgnoreCase))
                {
                    test.Steps.Add(new SendSyncPollStep(location));
                    return;
                }

                test.Steps.Add(EndsWithoutWakeup(words) ? ReadSendWithoutWakeup(words, location) : ReadSend(words, location));
                return;

            case "channel":
                if (words.Length != 3 || !SasChannels.TryParse(words[2], out SasChannel moved))
                    throw new ScriptParseException(location, "expected: host channel <1-4>");

                test.Steps.Add(new UseChannelStep(location, moved));
                return;

            case "machine":
                if (words.Length != 3 || !SasAddress.TryParse(words[2], out SasAddress onto)
                    || onto.IsSync)
                {
                    throw new ScriptParseException(location,
                        "expected: host machine <address>, naming one of the machines this host "
                        + "polls. Address 00 is the synchronisation address and is not a machine.");
                }

                test.Steps.Add(new UseMachineStep(location, onto));
                return;

            case "forget":
                if (words.Length < 3 || !words[2].Equals("exceptions",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ScriptParseException(location,
                        "expected: host forget exceptions [within <duration>]");
                }

                test.Steps.Add(new ForgetExceptionsStep(
                    location, ReadWithin(words, 3, location)));
                return;

            case "expect":
                test.Steps.Add(ReadExpectLine(words, location) ?? ReadExpectRomSignature(words, location)
                    ?? ReadExpect(test, words, location));
                return;

            case "remember":
                test.Steps.Add(ReadRemember(test, words, location));
                return;

            default:
                throw new ScriptParseException(location,
                    $"'host {words[1]}' is not a host action. Expected connect, channel, machine, "
                    + "poll, wait, warm up, send, broadcast, forget, remember or expect");
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
        IReadOnlyList<SasAddress> round = [];
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

                case "addresses":
                    // A round, written as ranges and lists rather than as one line per machine.
                    if (!SasAddressRound.TryParse(value, out round, out string? why))
                        throw new ScriptParseException(location, why!);

                    address = round[0];
                    addressGiven = true;
                    break;

                case "channel":
                    if (!SasChannels.TryParse(value, out channel))
                        throw new ScriptParseException(location,
                            $"'{value}' is not a channel. Expected 1 to {SasChannels.Count}");
                    break;

                default:
                    throw new ScriptParseException(location,
                        $"'{keyword}' is not a connect option. Expected address, addresses or "
                        + "channel");
            }
        }

        if (!addressGiven)
            throw new ScriptParseException(location,
                "host connect needs 'address <address>' or 'addresses <round>': a host that does "
                + "not know the machine's address gets silence, and silence is indistinguishable "
                + "from a fault");

        if (transport != ScriptTransport.Loopback && target.Length == 0)
            throw new ScriptParseException(location,
                $"host connect {words[2]} needs a target, such as an endpoint or a port name");

        if (transport == ScriptTransport.Tcp
            && !TcpTransportOptions.TryParseEndpoint(target, out _))
            throw new ScriptParseException(location,
                $"'{target}' is not an endpoint. Expected something like 127.0.0.1:7200");

        return new ConnectStep(location, transport, target, address, channel) { Round = round };
    }

    /// <summary>
    /// Whether a layout has a field at this path. A path may reach inside a repeating group, as
    /// <c>meters[0].value</c> does, because a reply whose shape is a walk rather than a table has
    /// nothing else a test could name.
    /// </summary>
    private static bool HasField(IReadOnlyList<SasField> fields, string path)
    {
        IReadOnlyList<SasField> level = fields;

        foreach (string step in path.Split('.'))
        {
            string name = TrimIndex(step);
            SasField? match = level.FirstOrDefault(f =>
                string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return false;

            level = match.Fields;
        }

        return true;
    }

    /// <summary>Every path a test could name, groups written as the member paths inside them.</summary>
    private static IEnumerable<string> NamesOf(IReadOnlyList<SasField> fields, string? prefix = null)
    {
        foreach (SasField field in fields)
        {
            string path = prefix is null ? field.Name : $"{prefix}.{field.Name}";
            if (field.IsGroup)
            {
                foreach (string inner in NamesOf(field.Fields, $"{path}[n]"))
                    yield return inner;
            }
            else
            {
                yield return path;
            }
        }
    }

    /// <summary>Strips a <c>[3]</c> subscript, leaving the field name.</summary>
    private static string TrimIndex(string step)
    {
        int bracket = step.IndexOf('[', StringComparison.Ordinal);
        return bracket < 0 ? step : step[..bracket];
    }

    /// <summary>
    /// Reads <c>host send gp [draws &lt;name&gt;|nothing|silence] [to &lt;address&gt;]</c>.
    /// </summary>
    /// <remarks>
    /// The expectation is part of the step rather than a separate assertion because what a general
    /// poll drew is a property of that one poll. Asserted from the exception stream instead it
    /// would say only that a code arrived at some point, which is the very thing these cases are
    /// written to distinguish.
    /// </remarks>
    private static SendGeneralPollStep ReadSendGeneralPoll(string[] words, SourceLocation location)
    {
        words = TakeTarget(words, location, out SasAddress? to);
        var step = new SendGeneralPollStep(location) { To = to };

        if (words.Length == 3)
            return step;

        if (words.Length != 5 || !words[3].Equals("draws", StringComparison.OrdinalIgnoreCase))
        {
            throw new ScriptParseException(location,
                "expected: host send gp [draws <exception>|draws nothing|draws silence] "
                + "[to <address>]");
        }

        string what = words[4];
        if (what.Equals("nothing", StringComparison.OrdinalIgnoreCase))
            return step with { Draws = DrawExpectation.NoActivity };

        if (what.Equals("silence", StringComparison.OrdinalIgnoreCase))
            return step with { Draws = DrawExpectation.Silence };

        if (!SasNames.TryResolveException(what, out byte code))
        {
            throw new ScriptParseException(location,
                $"'{what}' is not an exception this build knows. Write its name, such as "
                + "slot_door_opened, or its code, such as $11 - or 'nothing' for a machine "
                + "reporting no activity, or 'silence' for no answer at all");
        }

        return step with
        {
            Draws = DrawExpectation.Exception,
            Code = code,
            CodeName = SasNames.ExceptionName(code),
        };
    }

    private static SendLongPollStep ReadSend(string[] words, SourceLocation location)
    {
        // host send lp 1F [data 0102]
        if (words.Length < 4 || !words[2].Equals("lp", StringComparison.OrdinalIgnoreCase))
            throw new ScriptParseException(location, "expected: host send lp <code> [data <hex>]");

        if (!SasNames.TryParseCode(words[3], out byte command))
            throw new ScriptParseException(location,
                $"'{words[3]}' is not a long poll code. Expected two hex digits, such as 1F");

        if (!SasCatalogue.LongPolls.ContainsKey(command))
            throw new ScriptParseException(location,
                $"long poll ${command:X2} is not one the gaming machine library implements");

        // 'when <condition>' and then 'to <address>' may close any form of the step, so they are
        // taken off the end first and the rest is read as it always was.
        words = TakeCondition(words, location, out Requirement? when);
        words = TakeTarget(words, location, out SasAddress? to,
            broadcast: SasLayouts.For(command)?.Broadcast == true ? command : null);

        if (words.Length > 4 && words[4].Equals("with", StringComparison.OrdinalIgnoreCase))
            return ReadSendWithFields(words, location, command) with { To = to, When = when };

        byte[] data = [];
        if (words.Length > 4)
        {
            if (words.Length != 6 || !words[4].Equals("data", StringComparison.OrdinalIgnoreCase))
                throw new ScriptParseException(location,
                    "expected: host send lp <code> [data <hex> | with <field>=<value> ...] "
                    + "[to <address>] [when <capability> == <value>]");
            data = ParseHex(words[5], location);
        }

        return new SendLongPollStep(location, command, data) { To = to, When = when };
    }

    /// <summary>
    /// Takes a closing <c>when &lt;capability&gt; == &lt;value&gt;</c> off a send.
    /// </summary>
    /// <remarks>Only the compared form; a bare capability belongs on a 'requires' line.</remarks>
    private static string[] TakeCondition(
        string[] words, SourceLocation location, out Requirement? when)
    {
        when = null;
        int at = Array.FindLastIndex(words, w => w.Equals("when", StringComparison.OrdinalIgnoreCase));
        if (at < 0)
            return words;

        if (words.Length - at != 4 || words[at + 2] != "==")
        {
            throw new ScriptParseException(location,
                $"'{string.Join(' ', words[at..])}' is not a condition. Expected 'when "
                + "<capability> == <value>' at the end of the step, after any 'to <address>', such "
                + "as 'when egm.validation == secure_enhanced'");
        }

        when = new Requirement(location, words[at + 1], words[at + 3]);
        return words[..at];
    }

    /// <summary>
    /// Takes a closing <c>to &lt;address&gt;</c> off a send, leaving the rest of the step as it was.
    /// </summary>
    /// <remarks>
    /// At the end rather than after the code, so a step reads as a sentence — send this, with
    /// these fields, to that machine — and so every form of the step gains it without any of them
    /// being reordered.
    /// </remarks>
    /// <param name="broadcast">
    /// The command when its layout may be broadcast, so <c>to 00</c> sends it to every machine and
    /// expects none to answer; null when 00 is refused.
    /// </param>
    private static string[] TakeTarget(
        string[] words, SourceLocation location, out SasAddress? to, byte? broadcast = null)
    {
        to = null;
        if (words.Length < 2 || !words[^2].Equals("to", StringComparison.OrdinalIgnoreCase))
            return words;

        if (broadcast is not null && SasAddress.TryParse(words[^1], out SasAddress all) && all.IsSync)
        {
            to = all;
            return words[..^2];
        }

        if (!SasAddress.TryParse(words[^1], out SasAddress named) || named.IsSync)
        {
            throw new ScriptParseException(location,
                $"'{words[^1]}' is not a machine this host could send to. Expected 01 to 7F; "
                + "address 00 is the synchronisation address and no machine answers to it.");
        }

        to = named;
        return words[..^2];
    }

    /// <summary>
    /// Reads <c>host send lp 71 with transfer_code=ttc_valid_cashable_ticket ...</c>.
    /// </summary>
    /// <remarks>
    /// The fields are turned into bytes here rather than at run time, for the same reason every
    /// other name in a script is resolved while it is read: a field this build cannot write is a
    /// mistake in the script, and a mistake in the script should stop before a machine is touched.
    /// A length byte the layout begins with is filled in from what follows unless it is given.
    /// </remarks>
    private static SendLongPollStep ReadSendWithFields(
        string[] words, SourceLocation location, byte command)
    {
        SasMessageLayout? layout = SasLayouts.For(command);
        if (layout is null)
        {
            throw new ScriptParseException(location,
                $"this build has no layout for long poll ${command:X2}, so its fields cannot be "
                + "named. Write 'data <hex>' instead, and know that nothing will check it");
        }

        if (layout.Request.Count == 0)
            throw new ScriptParseException(location,
                $"long poll ${command:X2} carries no fields, so there is nothing to name");

        if (words.Length == 5)
            throw new ScriptParseException(location,
                "expected: host send lp <code> with <field>=<value> [<field>=<value>...]");

        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (string word in words[5..])
        {
            string[] pair = word.Split('=', 2);
            if (pair.Length != 2)
                throw new ScriptParseException(location,
                    $"'{word}' is not a field setting. Expected <field>=<value>");

            string field = pair[0].Trim();
            if (!HasField(layout.Request, field))
            {
                throw new ScriptParseException(location,
                    $"long poll ${command:X2} has no field '{field}'. It carries: "
                    + string.Join(", ", NamesOf(layout.Request)));
            }

            if (!SasNames.TryResolveFieldValue(pair[1], out long value))
            {
                throw new ScriptParseException(location,
                    $"'{pair[1]}' is not a value for '{field}'. Expected a number, a hex code such "
                    + "as $80, or one of the library's names, such as ttc_valid_cashable_ticket");
            }

            values[field] = value;
        }

        var buffer = new byte[HostMessage.MaxSymbols];
        int length;
        try
        {
            length = SasMessageEncoder.WriteRequestData(layout, values, buffer);
        }
        catch (Exception problem) when (problem is InvalidOperationException
                                            or ArgumentOutOfRangeException)
        {
            throw new ScriptParseException(location,
                $"long poll ${command:X2} cannot be built from those fields: {problem.Message}");
        }

        return new SendLongPollStep(location, command, buffer[..length])
        {
            NamedData = string.Join(' ', words[5..]),
        };
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
            "answer" => ReadExpectAnswer(words, location),
            "meter" => ReadExpectMeter(words, location),
            "field" => ReadExpectField(test, words, location),
            "link" => ReadExpectLink(words, location),
            _ => throw new ScriptParseException(location,
                $"'{words[2]}' cannot be expected. Expected exception, no exception, no reply, "
                + "meter, field or link"),
        };
    }

    private static ScriptStep ReadExpectNo(string[] words, SourceLocation location)
    {
        if (words.Length >= 4 && words[3].Equals("reply", StringComparison.OrdinalIgnoreCase))
            return ReadExpectNoReply(words, location);

        if (words.Length < 4 || !words[3].Equals("exception", StringComparison.OrdinalIgnoreCase))
            throw new ScriptParseException(location,
                "expected: host expect no exception <name> [within <duration>], or "
                + "host expect no reply to lp <hh>");

        return ReadExpectException(words[..2].Concat(words[3..]).ToArray(), location, negated: true);
    }

    /// <summary>
    /// Reads <c>host expect no reply to lp 70</c>: the machine is expected to say nothing at all.
    /// </summary>
    private static ExpectNoReplyStep ReadExpectNoReply(string[] words, SourceLocation location)
    {
        // host expect no reply to lp 70 [data <hex>]
        if (words.Length < 7
            || !words[4].Equals("to", StringComparison.OrdinalIgnoreCase)
            || !words[5].Equals("lp", StringComparison.OrdinalIgnoreCase))
        {
            throw new ScriptParseException(location,
                "expected: host expect no reply to lp <hh> [data <hex>]");
        }

        if (!SasNames.TryParseCode(words[6], out byte command))
            throw new ScriptParseException(location, $"'{words[6]}' is not a long poll code");

        byte[] data = [];
        if (words.Length > 7)
        {
            if (words.Length != 9 || !words[7].Equals("data", StringComparison.OrdinalIgnoreCase))
                throw new ScriptParseException(location,
                    "expected: host expect no reply to lp <hh> [data <hex>]");

            data = ParseHex(words[8], location);
        }

        return new ExpectNoReplyStep(location, command, data);
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

    // egm expect ticket printed within 10s
    // egm expect ticket amount_cents == 2000
    //
    // An observation of the machine rather than of the wire, so it is an 'egm' line: the ticket is
    // read from the wallet the printer files into, not decoded from a reply.
    private static ScriptStep ReadEgmExpect(string[] words, SourceLocation location)
    {
        if (words.Length < 4 || !words[2].Equals("ticket", StringComparison.OrdinalIgnoreCase))
            throw new ScriptParseException(location,
                "expected: egm expect ticket printed within <duration>, or "
                + "egm expect ticket <field> <comparison> <value>");

        if (words[3].Equals("printed", StringComparison.OrdinalIgnoreCase))
        {
            if (words.Length != 6 || !words[4].Equals("within", StringComparison.OrdinalIgnoreCase))
                throw new ScriptParseException(location,
                    "expected: egm expect ticket printed within <duration>");

            return new ExpectTicketStep(location, ParseDuration(words[5], location));
        }

        string fieldName = words[3];

        // A field a ticket does not have is a mistake in the script, so it stops the parse rather
        // than failing halfway through a run. The names are the ones written in a ticket file, so
        // the script and the file a person edits by hand say the same words.
        TicketField? field = TicketFields.Find(fieldName);
        if (field is null)
        {
            throw new ScriptParseException(location,
                $"a ticket has no field '{fieldName}'. Its fields are: "
                + string.Join(", ", TicketFields.All.Select(f => f.Name)));
        }

        if (words.Length < 6)
            throw new ScriptParseException(location, $"'{fieldName}' needs a comparison and a value");

        SasComparison comparison = words[4] switch
        {
            "==" => SasComparison.Equal,
            "!=" => SasComparison.NotEqual,
            ">=" => SasComparison.AtLeast,
            "<=" => SasComparison.AtMost,
            ">" => SasComparison.GreaterThan,
            "<" => SasComparison.LessThan,
            _ => throw new ScriptParseException(location,
                $"'{words[4]}' is not a comparison. Expected ==, !=, >=, <=, > or <"),
        };

        string expected = string.Join(' ', words[5..]);

        // As with a wire field, what decides text from number is the field, not whether the
        // literal happens to look numeric. A validation number is sixteen digits and is compared
        // as a number; an expiration date is 2026-09-06 and is not.
        bool numeric = field.Kind is TicketFieldKind.Number or TicketFieldKind.Digits;
        if (numeric
            && long.TryParse(expected, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number))
        {
            return new ExpectTicketFieldStep(location, field.Name, comparison, number, null);
        }

        if (comparison is not (SasComparison.Equal or SasComparison.NotEqual))
            throw new ScriptParseException(location,
                $"'{expected}' can only be compared with == or !=");

        return new ExpectTicketFieldStep(location, field.Name, comparison, null, expected);
    }

    private static ScriptStep ReadExpectField(
        TestBuilder test, string[] words, SourceLocation location)
    {
        // host expect field base_percentage == 9210
        // host expect field serial_number of lp 54 == QX12345
        // host expect field credits_wagered of event game_started == 5 within 3s
        if (words.Length < 4)
            throw new ScriptParseException(location,
                "expected: host expect field <name> [of lp <code> | of event <name>] "
                + "<comparison> <value>");

        string fieldName = words[3];
        int cursor = 4;
        byte command;

        if (cursor + 2 < words.Length
            && words[cursor].Equals("of", StringComparison.OrdinalIgnoreCase)
            && words[cursor + 1].Equals("event", StringComparison.OrdinalIgnoreCase))
        {
            return ReadExpectEventField(words, location, fieldName, cursor + 2);
        }

        (command, SasMessageLayout? layout, cursor) =
            ReadReplyField(test, words, cursor, fieldName, location);

        if (cursor >= words.Length)
            throw new ScriptParseException(location, $"'{fieldName}' needs a comparison after it");

        SasComparison comparison = ReadComparison(words[cursor], location);

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

        // A reply compared against values remembered earlier in the case, resolved now so a name
        // nothing remembered is a mistake in the script rather than a surprise on a board.
        if (expected.Contains("saved.", StringComparison.OrdinalIgnoreCase))
        {
            SavedSum sum = ReadSavedSum(test, expected, location);
            if (textual && (sum.OnlyName is null
                    || comparison is not (SasComparison.Equal or SasComparison.NotEqual)))
            {
                throw new ScriptParseException(location,
                    $"'{fieldName}' holds text, so it can only be compared with == or != against "
                    + "one remembered value");
            }

            return new ExpectFieldStep(location, command, fieldName, comparison, null, null)
            {
                ExpectedSum = sum,
            };
        }

        // A reply compared against the ticket the machine printed rather than against a literal.
        // Resolved now, so a ticket field that does not exist is a mistake in the script; and
        // restricted to the numeric ticket fields, because comparing a printed date against a
        // BCD reply is a question nobody meant to ask.
        if (expected.StartsWith("ticket.", StringComparison.OrdinalIgnoreCase))
        {
            string ticketFieldName = expected["ticket.".Length..];
            TicketField? ticketField = TicketFields.Find(ticketFieldName);
            if (ticketField is null)
            {
                throw new ScriptParseException(location,
                    $"a ticket has no field '{ticketFieldName}'. Its fields are: "
                    + string.Join(", ", TicketFields.All.Select(f => f.Name)));
            }

            if (ticketField.Kind is not (TicketFieldKind.Number or TicketFieldKind.Digits))
            {
                throw new ScriptParseException(location,
                    $"'ticket.{ticketField.Name}' is not a number, so a long poll field cannot be "
                    + "compared against it");
            }

            if (textual)
            {
                throw new ScriptParseException(location,
                    $"'{fieldName}' holds text, so it cannot be compared against "
                    + $"'ticket.{ticketField.Name}', which is a number");
            }

            return new ExpectFieldStep(location, command, fieldName, comparison, null, null)
            {
                ExpectedTicketField = ticketField.Name,
            };
        }

        // The same rule as a command's fields: a plain number is decimal, a hex code is written
        // with a dollar sign, and the library's own names for a code are accepted. Which matters
        // most for a status byte — 'machine_status == 40' reads as decimal forty and is not the
        // $40 anybody meant, so the ambiguity is refused rather than guessed at.
        if (!textual && SasNames.TryResolveFieldValue(expected, out long number))
            return new ExpectFieldStep(location, command, fieldName, comparison, number, null);

        if (comparison is not (SasComparison.Equal or SasComparison.NotEqual))
            throw new ScriptParseException(location,
                $"'{fieldName}' holds text, which can only be compared with == or !=");

        return new ExpectFieldStep(location, command, fieldName, comparison, null, expected);
    }

    /// <summary>Reads <c>host remember field &lt;name&gt; [of lp &lt;code&gt;] as &lt;saved name&gt;</c>.</summary>
    private static RememberFieldStep ReadRemember(
        TestBuilder test, string[] words, SourceLocation location)
    {
        const string Usage = "expected: host remember field <name> [of lp <code>] as <saved name>";
        if (words.Length < 6 || !words[2].Equals("field", StringComparison.OrdinalIgnoreCase))
            throw new ScriptParseException(location, Usage);

        string fieldName = words[3];
        (byte command, _, int cursor) = ReadReplyField(test, words, 4, fieldName, location);

        if (cursor + 2 != words.Length
            || !words[cursor].Equals("as", StringComparison.OrdinalIgnoreCase))
            throw new ScriptParseException(location, Usage);

        string name = words[cursor + 1];
        if (!SavedName.IsMatch(name))
        {
            throw new ScriptParseException(location,
                $"'{name}' is not a name to remember a value by. Use letters, digits and "
                + "underscores, starting with a letter");
        }

        test.Saved.Add(name);
        return new RememberFieldStep(location, command, fieldName, name);
    }

    private static readonly System.Text.RegularExpressions.Regex SavedName =
        new("^[A-Za-z][A-Za-z0-9_]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads remembered values and numbers joined by + and -, such as
    /// <c>saved.after - saved.before + 10</c>.
    /// </summary>
    private static SavedSum ReadSavedSum(TestBuilder test, string expression, SourceLocation location)
    {
        string[] tokens = expression.Replace("+", " + ").Replace("-", " - ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var terms = new List<SavedTerm>();
        int sign = +1;
        bool wantOperand = true;
        foreach (string token in tokens)
        {
            if (token is "+" or "-")
            {
                if (wantOperand && terms.Count > 0)
                    throw new ScriptParseException(location,
                        $"'{expression}' has two operators in a row");

                sign = token == "-" ? -sign : sign;
                wantOperand = true;
                continue;
            }

            if (!wantOperand)
                throw new ScriptParseException(location,
                    $"'{expression}' needs + or - between '{terms[^1].Operand}' and '{token}'");

            terms.Add(ReadSavedTerm(test, token, sign, expression, location));
            sign = +1;
            wantOperand = false;
        }

        if (wantOperand)
            throw new ScriptParseException(location,
                $"'{expression}' ends without a value after its last operator");

        return new SavedSum(terms);
    }

    private static SavedTerm ReadSavedTerm(
        TestBuilder test, string token, int sign, string expression, SourceLocation location)
    {
        if (token.StartsWith("saved.", StringComparison.OrdinalIgnoreCase))
        {
            string name = token["saved.".Length..];
            if (!test.Saved.Contains(name))
            {
                throw new ScriptParseException(location,
                    $"'saved.{name}' was not remembered earlier in this case. Remember it first "
                    + "with: host remember field <name> as " + name);
            }

            return new SavedTerm(sign, null, name);
        }

        if (SasNames.TryResolveFieldValue(token, out long number))
            return new SavedTerm(sign, number, null);

        throw new ScriptParseException(location,
            $"'{token}' in '{expression}' is neither a remembered value (saved.<name>) nor a "
            + "number; only + and - join them");
    }

    /// <summary>
    /// Resolves which reply a field comes from - <c>of lp &lt;code&gt;</c>, or the most recent send -
    /// and refuses a field that reply's layout does not have.
    /// </summary>
    private static (byte Command, SasMessageLayout? Layout, int Cursor) ReadReplyField(
        TestBuilder test, string[] words, int cursor, string fieldName, SourceLocation location)
    {
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

        if (layout is not null && !HasField(layout.Response, fieldName)
            && !PreambleBaseHasField(test, command, fieldName))
        {
            throw new ScriptParseException(location,
                $"long poll ${command:X2} has no field '{fieldName}'. Its fields are: "
                + string.Join(", ", NamesOf(layout.Response)));
        }

        return (command, layout, cursor);
    }

    /// <summary>
    /// Reads an assertion about one field of a real-time event's payload.
    /// </summary>
    /// <remarks>
    /// The event is named rather than coded wherever this build has established a payload for it,
    /// and an exception with no established payload is refused at parse time — there is no field
    /// to name, so a script asking for one is a mistake in the script rather than a capability
    /// this environment happens to lack.
    /// </remarks>
    private static ExpectEventFieldStep ReadExpectEventField(
        string[] words, SourceLocation location, string fieldName, int cursor)
    {
        string eventName = words[cursor];
        if (!SasNames.TryResolveException(eventName, out byte code))
        {
            throw new ScriptParseException(location,
                $"'{eventName}' is not an exception this build knows. Write its name, such as "
                + "game_started, or its code, such as $7E");
        }

        SasEventLayout? layout = SasEventLayouts.For(code);
        if (layout is null)
        {
            throw new ScriptParseException(location,
                $"exception ${code:X2} carries no payload this build has established, so it has "
                + "no fields. The events with one are: "
                + string.Join(", ", SasEventLayouts.All.Values.Select(e => e.Name).Order()));
        }

        if (!HasField(layout.Payload, fieldName))
        {
            throw new ScriptParseException(location,
                $"event {layout.Name} has no field '{fieldName}'. Its fields are: "
                + string.Join(", ", NamesOf(layout.Payload)));
        }

        cursor++;
        if (cursor >= words.Length)
            throw new ScriptParseException(location, $"'{fieldName}' needs a comparison after it");

        SasComparison comparison = ReadComparison(words[cursor], location);
        cursor++;
        if (cursor >= words.Length)
            throw new ScriptParseException(location, "the comparison needs a value after it");

        // 'within' ends the value, the way it does on an exception expectation.
        int end = words.Length;
        TimeSpan within = DefaultWithin;
        for (int i = cursor + 1; i < words.Length - 1; i++)
        {
            if (!words[i].Equals("within", StringComparison.OrdinalIgnoreCase))
                continue;

            within = ReadWithin(words, i, location);
            end = i;
            break;
        }

        string expected = string.Join(' ', words[cursor..end]);
        SasField? declared = layout.Payload.FirstOrDefault(f =>
            string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));
        bool textual = declared?.Encoding is SasFieldEncoding.Ascii or SasFieldEncoding.Raw;

        if (!textual && SasNames.TryResolveFieldValue(expected, out long number))
        {
            return new ExpectEventFieldStep(
                location, code, layout.Name, fieldName, comparison, number, null, within);
        }

        if (comparison is not (SasComparison.Equal or SasComparison.NotEqual))
        {
            throw new ScriptParseException(location,
                $"'{fieldName}' holds text, which can only be compared with == or !=");
        }

        return new ExpectEventFieldStep(
            location, code, layout.Name, fieldName, comparison, null, expected, within);
    }

    private static SasComparison ReadComparison(string word, SourceLocation location) => word switch
    {
        "==" => SasComparison.Equal,
        "!=" => SasComparison.NotEqual,
        ">=" => SasComparison.AtLeast,
        "<=" => SasComparison.AtMost,
        ">" => SasComparison.GreaterThan,
        "<" => SasComparison.LessThan,
        _ => throw new ScriptParseException(location,
            $"'{word}' is not a comparison. Expected ==, !=, >=, <=, > or <"),
    };

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
            throw new ScriptParseException(location,
                "expected: egm bill|ticket|play|press|door|handpay|printer|note|set|select|aft|component|settle|expect ...");

        switch (words[1].ToLowerInvariant())
        {
            case "expect":
                test.Steps.Add(ReadEgmExpect(words, location));
                return;

            case "settle":
                if (words.Length != 2)
                    throw new ScriptParseException(location, "expected: egm settle");
                test.Steps.Add(new SettleStep(location));
                return;

            case "bill":
                if (words.Length != 4 || !words[2].Equals("insert", StringComparison.OrdinalIgnoreCase))
                    throw new ScriptParseException(location, "expected: egm bill insert <cents>");
                if (!int.TryParse(words[3], out int cents) || cents <= 0)
                    throw new ScriptParseException(location,
                        $"'{words[3]}' is not a note value in cents");
                test.Steps.Add(new InsertBillStep(location, cents));
                return;

            case "ticket":
                test.Steps.Add(ReadTicketInsert(words, location));
                return;

            case "play":
                test.Steps.Add(ReadPlay(words, location));
                return;

            case "progressive":
                test.Steps.Add(ReadProgressiveWin(words, location));
                return;

            case "game":
                test.Steps.Add(ReadGame(words, location));
                return;

            case "select":
                test.Steps.Add(ReadSelect(words, location));
                return;

            case "set" when words.Length >= 3
                             && words[2].Equals("address", StringComparison.OrdinalIgnoreCase):
                test.Steps.Add(ReadSetPollAddress(words, location));
                return;

            case "set" when words.Length >= 3
                             && words[2].Equals("game", StringComparison.OrdinalIgnoreCase):
                test.Steps.Add(ReadSetGame(words, location));
                return;

            case "menu":
                test.Steps.Add(ReadMenu(words, location));
                return;

            case "component":
                if (words.Length != 3 || !words[2].Equals("change", StringComparison.OrdinalIgnoreCase))
                    throw new ScriptParseException(location, "expected: egm component change");
                test.Steps.Add(new ComponentChangeStep(location));
                return;

            case "press" when words.Length == 5 && words[3].Equals("lasting", StringComparison.OrdinalIgnoreCase):
                test.Steps.Add(ReadSlowCashout(words, location));
                return;

            case "press":
                if (words.Length != 3)
                    throw new ScriptParseException(location, "expected: egm press <button>");
                test.Steps.Add(new PressButtonStep(location, words[2].ToLowerInvariant()));
                return;

            case "set" when words.Length >= 3
                             && words[2].Equals("asset", StringComparison.OrdinalIgnoreCase):
                if (words.Length != 5
                    || !words[3].Equals("number", StringComparison.OrdinalIgnoreCase)
                    || !uint.TryParse(words[4], System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out uint asset))
                {
                    throw new ScriptParseException(location, "expected: egm set asset number <decimal>");
                }

                test.Steps.Add(new SetAssetNumberStep(location, asset));
                return;

            case "set" when words.Length == 4
                             && words[2].Equals("aft", StringComparison.OrdinalIgnoreCase)
                             && words[3].ToLowerInvariant() is "on" or "off":
                test.Steps.Add(new SetAftStep(location, words[3].Equals("on", StringComparison.OrdinalIgnoreCase)));
                return;

            case "set" when words.Length >= 3
                             && words[2].Equals("jackpot", StringComparison.OrdinalIgnoreCase):
                test.Steps.Add(ReadJackpotLimit(words, location));
                return;

            case "set" when words.Length >= 3
                             && words[2].Equals("credit", StringComparison.OrdinalIgnoreCase):
                if (words.Length != 5
                    || !words[3].Equals("limit", StringComparison.OrdinalIgnoreCase)
                    || !long.TryParse(words[4], System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out long limit))
                {
                    throw new ScriptParseException(location, "expected: egm set credit limit <cents>");
                }

                test.Steps.Add(new SetCreditLimitStep(location, limit));
                return;

            case "set" when words.Length >= 3
                             && words[2].Equals("aft", StringComparison.OrdinalIgnoreCase):
                if (words.Length != 5
                    || !words[3].Equals("bonus", StringComparison.OrdinalIgnoreCase)
                    || words[4].ToLowerInvariant() is not ("on" or "off"))
                {
                    throw new ScriptParseException(location, "expected: egm set aft on|off, or egm set aft bonus on|off");
                }

                test.Steps.Add(new SetAftBonusStep(location, words[4].Equals("on", StringComparison.OrdinalIgnoreCase)));
                return;

            case "set" when words.Length >= 3
                             && words[2].Equals("legacy", StringComparison.OrdinalIgnoreCase):
                if (words.Length != 5
                    || !words[3].Equals("bonus", StringComparison.OrdinalIgnoreCase)
                    || words[4].ToLowerInvariant() is not ("on" or "off"))
                {
                    throw new ScriptParseException(location, "expected: egm set legacy bonus on|off");
                }

                test.Steps.Add(new SetLegacyBonusStep(location, words[4].Equals("on", StringComparison.OrdinalIgnoreCase)));
                return;

            case "aft":
                if (words.Length != 3 || !words[2].Equals("register", StringComparison.OrdinalIgnoreCase))
                    throw new ScriptParseException(location, "expected: egm aft register");
                test.Steps.Add(new AftRegisterStep(location));
                return;

            case "set":
                if (words.Length < 5
                    || !words[2].Equals("accounting", StringComparison.OrdinalIgnoreCase)
                    || !words[3].Equals("denomination", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ScriptParseException(location,
                        "expected: egm set accounting denomination <cents>c|code <hex>, egm set asset number <decimal>, egm set credit limit <cents>, egm set jackpot limit <cents>, egm set aft on|off, egm set aft bonus on|off, or egm set legacy bonus on|off");
                }

                (byte code, int unit) = ReadDenomination(words[4..], location);
                test.Steps.Add(new SetDenominationStep(location, code, unit));
                return;

            case "handpay":
                test.Steps.Add(ReadHandpay(words, location));
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

            case "power":
                if (words.Length != 3 || !words[2].Equals("cycle", StringComparison.OrdinalIgnoreCase))
                    throw new ScriptParseException(location, "expected: egm power cycle");
                test.Steps.Add(new PowerCycleStep(location));
                return;

            case "memory":
                test.Steps.Add(ReadMemoryFault(words, location));
                return;

            case "nvram":
                if (words.Length != 3 || !words[2].Equals("clear", StringComparison.OrdinalIgnoreCase))
                    throw new ScriptParseException(location, "expected: egm nvram clear");
                test.Steps.Add(new NvramClearStep(location));
                return;

            case "printer":
                test.Steps.Add(ReadPeripheralFault(PeripheralDevice.Printer, words, location));
                return;

            case "note":
                test.Steps.Add(ReadPeripheralFault(PeripheralDevice.Note, words, location));
                return;

            default:
                throw new ScriptParseException(location,
                    $"'egm {words[1]}' is not a machine action. Expected bill, ticket, play, "
                    + "press, door, handpay, power, memory, nvram, printer, note, progressive, set or settle");
        }
    }

    /// <summary>Reads <c>egm printer fault &lt;kind&gt;</c> or <c>egm note fault &lt;kind&gt;</c>.</summary>
    private static PeripheralFaultStep ReadPeripheralFault(
        PeripheralDevice device, string[] words, SourceLocation location)
    {
        IReadOnlyList<string> faults = PeripheralFaultStep.Faults[device];
        string expected = $"expected: egm {PeripheralFaultStep.WordFor(device)} fault <kind>, where the kind is one of "
                          + string.Join(", ", faults);

        if (words.Length != 4 || !words[2].Equals("fault", StringComparison.OrdinalIgnoreCase))
            throw new ScriptParseException(location, expected);

        string fault = words[3].ToLowerInvariant();
        if (!faults.Contains(fault))
            throw new ScriptParseException(location, $"'{words[3]}' is not a {PeripheralFaultStep.WordFor(device)} fault. {expected}");

        return new PeripheralFaultStep(location, device, fault);
    }

    /// <summary>Reads <c>egm memory fault &lt;kind&gt;</c>.</summary>
    private static MemoryFaultStep ReadMemoryFault(string[] words, SourceLocation location)
    {
        string expected = "expected: egm memory fault <kind>, where the kind is one of "
                          + string.Join(", ", MemoryFaultStep.Words.Keys);

        if (words.Length != 4 || !words[2].Equals("fault", StringComparison.OrdinalIgnoreCase))
            throw new ScriptParseException(location, expected);

        if (!MemoryFaultStep.Words.TryGetValue(words[3], out MemoryFaultKind kind))
            throw new ScriptParseException(location, $"'{words[3]}' is not a memory fault. {expected}");

        return new MemoryFaultStep(location, kind);
    }

    /// <summary>Reads <c>egm handpay &lt;cents&gt;</c>,<c>egm handpay jackpot &lt;cents&gt;</c> or <c>egm handpay reset</c>.</summary>
    private static StimulusStep ReadHandpay(string[] words, SourceLocation location)
    {
        const string Expected = "expected: egm handpay <cents>, egm handpay jackpot <cents> or egm handpay reset";

        if (words.Length == 3 && words[2].Equals("reset", StringComparison.OrdinalIgnoreCase))
            return new HandpayResetStep(location);

        bool jackpot = words.Length == 4 && words[2].Equals("jackpot", StringComparison.OrdinalIgnoreCase);
        if (!jackpot && words.Length != 3)
            throw new ScriptParseException(location, Expected);

        string amount = words[^1];
        if (!long.TryParse(amount, NumberStyles.None, CultureInfo.InvariantCulture, out long cents) || cents <= 0)
            throw new ScriptParseException(location, $"'{amount}' is not an amount in cents. {Expected}");

        return new HandpayStep(location, jackpot ? HandpayKind.Jackpot : HandpayKind.CancelledCredit, cents);
    }

    /// <summary>
    /// Reads <c>egm ticket insert &lt;file&gt;</c> or <c>egm ticket insert amount=… validation=…</c>.
    /// </summary>
    /// <remarks>
    /// A ticket named by file is read now rather than when the step runs, so a path that is wrong
    /// is a mistake in the script and is reported as one. A ticket written inline exists so the
    /// cases nothing has printed — a number no machine issued, an amount that does not match —
    /// can be written without a file per case.
    /// </remarks>
    private static InsertTicketStep ReadTicketInsert(string[] words, SourceLocation location)
    {
        if (words.Length < 4 || !words[2].Equals("insert", StringComparison.OrdinalIgnoreCase))
        {
            throw new ScriptParseException(location,
                "expected: egm ticket insert <file>, or "
                + "egm ticket insert amount=<cents> validation=<number> "
                + "[system=<0-99>] [type=<name>]");
        }

        if (words.Length == 4 && !words[3].Contains('=', StringComparison.Ordinal))
        {
            string path = words[3];
            try
            {
                return new InsertTicketStep(location, TicketFile.Read(path), path);
            }
            catch (Exception problem) when (problem is TicketFormatException or IOException
                                                or ArgumentException)
            {
                throw new ScriptParseException(location,
                    $"'{path}' cannot be read as a ticket: {problem.Message}");
            }
        }

        long amount = 0;
        ulong validation = 0;
        byte system = 0;
        var type = TicketValidationType.CashableFromCashout;
        bool amountGiven = false;
        bool validationGiven = false;

        foreach (string word in words[3..])
        {
            string[] pair = word.Split('=', 2);
            if (pair.Length != 2)
                throw new ScriptParseException(location,
                    $"'{word}' is not a setting. Expected amount=<cents>, validation=<number>, "
                    + "system=<0-99> or type=<name>");

            switch (pair[0].ToLowerInvariant())
            {
                case "amount":
                    if (!long.TryParse(pair[1], out amount) || amount <= 0)
                        throw new ScriptParseException(location,
                            $"'{pair[1]}' is not an amount in cents");
                    amountGiven = true;
                    break;

                case "validation":
                    if (!ulong.TryParse(pair[1], out validation) || pair[1].Length > 16)
                        throw new ScriptParseException(location,
                            $"'{pair[1]}' is not a validation number. Expected up to sixteen "
                            + "digits, which is what the barcode leaves after the system identifier");
                    validationGiven = true;
                    break;

                case "system":
                    if (!byte.TryParse(pair[1], out system) || system > 99)
                        throw new ScriptParseException(location,
                            $"'{pair[1]}' is not a validation system identifier. Expected 0 to 99, "
                            + "which is the two digits the barcode gives it");
                    break;

                case "type":
                    if (!Enum.TryParse(pair[1].Replace("_", string.Empty, StringComparison.Ordinal),
                            ignoreCase: true, out type))
                    {
                        throw new ScriptParseException(location,
                            $"'{pair[1]}' is not a ticket type. Expected one of: "
                            + string.Join(", ",
                                Enum.GetNames<TicketValidationType>().Select(SasNames.Underscore)));
                    }

                    break;

                default:
                    throw new ScriptParseException(location,
                        $"'{pair[0]}' is not a ticket setting. Expected amount, validation, system "
                        + "or type");
            }
        }

        // Both are required, and neither has a sensible default. A ticket with no number is one
        // the host cannot redeem, and a ticket worth nothing is refused by the machine, so a
        // script that forgot either would be testing a refusal it did not mean to write.
        if (!amountGiven || !validationGiven)
            throw new ScriptParseException(location,
                "egm ticket insert needs both amount=<cents> and validation=<number>");

        return new InsertTicketStep(
            location,
            new Ticket
            {
                ValidationNumber = validation,
                ValidationSystemId = system,
                ValidationType = type,
                AmountCents = amount,
                Note = "written by a test",
            },
            File: null);
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
        || word.Equals("addresses", StringComparison.OrdinalIgnoreCase)
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

        /// <summary>The names remembered so far, so a comparison with one nobody remembered is refused.</summary>
        public HashSet<string> Saved { get; } = new(StringComparer.Ordinal);

        /// <summary>The accounting denomination declared, before it is joined to the player list.</summary>
        public (byte Code, int Cents)? Denomination { get; set; }

        public List<int> PlayerDenominations { get; } = [];

        public SourceLocation? DeclaredAt { get; set; }

        public Dictionary<SasChannel, SasAddress?> ChannelAddresses { get; } = [];

        public Dictionary<SasChannel, IReadOnlyList<SasAddress>> ChannelRounds { get; } = [];

        public Dictionary<SasCategory, SasChannel> ChannelOwners { get; } = [];

        public SourceLocation? ChannelsDeclaredAt { get; set; }

        public MachineValidationDeclaration? Validation { get; set; }

        public MachineProgressiveDeclaration? Progressive { get; set; }

        public MachineAuthenticationDeclaration? Authentication { get; set; }

        public MachineMultipliedJackpotDeclaration? MultipliedJackpot { get; set; }

        public MachineHandpayDeclaration? Handpay { get; set; }

        public MachineGamesDeclaration? Games { get; set; }

        public bool HasContent =>
            Steps.Count > 0 || Requirements.Count > 0 || DeclaredAt is not null
            || ChannelsDeclaredAt is not null || Validation is not null || Progressive is not null
            || Authentication is not null || MultipliedJackpot is not null || Handpay is not null
            || Games is not null;

        public ScriptTest Build() => new()
        {
            Name = name,
            Location = Location,
            Requirements = Requirements,
            Machine = BuildDeclaration(),
            Validation = Validation,
            Progressive = Progressive,
            Authentication = Authentication,
            MultipliedJackpot = MultipliedJackpot,
            Handpay = Handpay,
            Games = Games,
            Channels = BuildChannels(),
            Tags = Tags,
            Steps = Steps,
        };

        /// <summary>
        /// The channel arrangement, or null where the case does not say and one channel owning
        /// everything is what it means.
        /// </summary>
        private MachineChannelDeclaration? BuildChannels()
        {
            if (ChannelsDeclaredAt is not { } where)
                return null;

            var addresses = new SasAddress?[SasChannels.Count];
            foreach ((SasChannel channel, SasAddress? address) in ChannelAddresses)
                addresses[channel.ToIndex()] = address;

            return new MachineChannelDeclaration(where, addresses, ChannelOwners)
            {
                Rounds = ChannelRounds,
            };
        }

        /// <summary>
        /// Player denominations without an accounting denomination declare nothing a machine can
        /// be built or checked against, so they are refused rather than kept and ignored.
        /// </summary>
        private MachineDeclaration? BuildDeclaration()
        {
            if (Denomination is not { } denomination)
            {
                return PlayerDenominations.Count == 0
                    ? null
                    : throw new ScriptParseException(DeclaredAt!,
                        "player denominations say nothing on their own; declare the accounting "
                        + "denomination they are meant to divide by");
            }

            return new MachineDeclaration(
                DeclaredAt!, denomination.Code, denomination.Cents, PlayerDenominations);
        }
    }
}
