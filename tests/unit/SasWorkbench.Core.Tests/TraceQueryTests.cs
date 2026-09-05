using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// Finding one message among thousands. What a message is gets worked out from its own bytes
/// rather than from whatever note happened to be recorded beside it, so a search works on a
/// recording made by anything.
/// </summary>
public class TraceQueryTests
{
    private static readonly SasSymbol[] SyncPoll = [SasSymbol.Address(0x80)];
    private static readonly SasSymbol[] GeneralPoll = [SasSymbol.Address(0x81)];
    private static readonly SasSymbol[] LongPoll1F =
        [SasSymbol.Address(0x01), SasSymbol.Data8(0x1F)];
    private static readonly SasSymbol[] ReplyTo1F =
        [SasSymbol.Data8(0x01), SasSymbol.Data8(0x1F), SasSymbol.Data8(0x51)];
    private static readonly SasSymbol[] Exception4A = [SasSymbol.Data8(0x4A)];

    [Fact]
    public void A_one_symbol_poll_of_address_zero_is_a_synchronisation_poll()
    {
        TraceSubject subject = TraceQuery.Classify(WireDirection.Transmit, SyncPoll);

        Assert.Equal(HostMessageKind.SyncPoll, subject.Kind);
    }

    [Fact]
    public void A_one_symbol_poll_of_a_machine_is_a_general_poll()
    {
        TraceSubject subject = TraceQuery.Classify(WireDirection.Transmit, GeneralPoll);

        Assert.Equal(HostMessageKind.GeneralPoll, subject.Kind);
    }

    [Fact]
    public void A_long_poll_carries_its_command_in_the_second_symbol()
    {
        TraceSubject subject = TraceQuery.Classify(WireDirection.Transmit, LongPoll1F);

        Assert.Equal(HostMessageKind.LongPoll, subject.Kind);
        Assert.Equal<byte?>(0x1F, subject.Command);
    }

    /// <summary>
    /// A search for a long poll finds the command and the reply to it. Finding only the command
    /// would leave somebody looking at what was asked and not at what came back.
    /// </summary>
    [Fact]
    public void Searching_for_a_long_poll_finds_the_command_and_its_reply()
    {
        var query = new TraceQuery { LongPoll = 0x1F };

        Assert.True(query.Matches(WireDirection.Transmit, LongPoll1F, null));
        Assert.True(query.Matches(WireDirection.Receive, ReplyTo1F, null));
        Assert.False(query.Matches(WireDirection.Transmit, GeneralPoll, null));
    }

    [Fact]
    public void A_search_can_be_narrowed_to_one_end_of_the_link()
    {
        var host = new TraceQuery { LongPoll = 0x1F, Side = TraceSide.Host };
        var machine = new TraceQuery { LongPoll = 0x1F, Side = TraceSide.Machine };

        Assert.True(host.Matches(WireDirection.Transmit, LongPoll1F, null));
        Assert.False(host.Matches(WireDirection.Receive, ReplyTo1F, null));
        Assert.True(machine.Matches(WireDirection.Receive, ReplyTo1F, null));
        Assert.False(machine.Matches(WireDirection.Transmit, LongPoll1F, null));
    }

    [Fact]
    public void An_exception_is_a_single_byte_the_machine_answered_with()
    {
        var query = new TraceQuery { Exception = 0x4A };

        Assert.True(query.Matches(WireDirection.Receive, Exception4A, null));
        Assert.False(query.Matches(WireDirection.Receive, ReplyTo1F, null));
    }

    /// <summary>
    /// The one genuine ambiguity on the wire. A one-byte reply of $4A is exception 4A from any
    /// machine except the one at address 4A, where it is an acknowledgement instead - and the
    /// only thing that tells them apart is knowing the address.
    /// </summary>
    [Fact]
    public void An_acknowledgement_is_not_an_exception_when_the_address_is_known()
    {
        var blind = new TraceQuery { Exception = 0x4A };
        var informed = new TraceQuery { Exception = 0x4A, Address = SasAddress.Of(0x4A) };

        Assert.True(blind.Matches(WireDirection.Receive, Exception4A, null));
        Assert.False(informed.Matches(WireDirection.Receive, Exception4A, null));
    }

    /// <summary>Two terms both have to hold, so a search narrows rather than widens.</summary>
    [Fact]
    public void Two_terms_are_both_required()
    {
        var query = new TraceQuery { LongPoll = 0x1F, Side = TraceSide.Host };

        Assert.True(query.Matches(WireDirection.Transmit, LongPoll1F, null));
        Assert.False(query.Matches(WireDirection.Receive, ReplyTo1F, null));
    }

    [Fact]
    public void Text_matches_the_symbols_or_the_note()
    {
        var symbols = new TraceQuery { Text = "0.1F" };
        var note = new TraceQuery { Text = "answered" };

        Assert.True(symbols.Matches(WireDirection.Transmit, LongPoll1F, null));
        Assert.True(note.Matches(WireDirection.Receive, ReplyTo1F, "Answered"));
        Assert.False(note.Matches(WireDirection.Receive, ReplyTo1F, "Silent"));
    }

    [Fact]
    public void An_empty_search_matches_everything()
    {
        var query = new TraceQuery();

        Assert.True(query.IsEmpty);
        Assert.True(query.Matches(WireDirection.Transmit, SyncPoll, null));
        Assert.True(query.Matches(WireDirection.Receive, Exception4A, null));
    }

    [Fact]
    public void A_message_reads_back_out_of_the_form_it_was_written_in()
    {
        string written = string.Join(' ', LongPoll1F.Select(s => s.ToString()));

        Assert.True(SasSymbol.TryParseMessage(written, out SasSymbol[] read));
        Assert.Equal<SasSymbol[]>(LongPoll1F, read);
        Assert.False(SasSymbol.TryParseMessage("nonsense", out _));
    }

    [Fact]
    public void A_search_says_in_words_what_it_looked_for()
    {
        var query = new TraceQuery { LongPoll = 0x2F, Side = TraceSide.Machine };

        Assert.Equal("long poll $2F from the machine", query.ToString());
    }
}
