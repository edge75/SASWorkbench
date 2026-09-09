using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transactions;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// Splitting a flat list of packets into the exchanges and the transactions inside it.
/// </summary>
/// <remarks>
/// The properties worth holding are the ones that keep the split honest rather than tidy: a reply
/// is placed by what it says and not by where it sits, silence is kept as silence, and every place
/// the split is not in the bytes is reported as such. A splitter that produced a tidy account of a
/// session that never happened would be worse than none.
/// </remarks>
public class TransactionSplitTests
{
    private long _sequence;

    private WireRecord Host(params byte[] bytes)
    {
        SasSymbol[] symbols = bytes.Select((b, i) =>
            i == 0 ? SasSymbol.Address(b) : SasSymbol.Data8(b)).ToArray();
        return new WireRecord(_sequence++, WireDirection.Transmit, SasChannel.One,
            TimeSpan.FromMilliseconds(_sequence * 10), symbols, null);
    }

    private WireRecord Machine(params byte[] bytes) =>
        new(_sequence++, WireDirection.Receive, SasChannel.One,
            TimeSpan.FromMilliseconds(_sequence * 10),
            [.. bytes.Select(SasSymbol.Data8)], null);

    /// <summary>A long poll with a check sequence, built the way the host builds one.</summary>
    private WireRecord LongPoll(byte address, byte command, params byte[] data)
    {
        var symbols = new SasSymbol[HostMessage.MaxSymbols];
        int length = HostMessage.WriteLongPoll(
            symbols, SasAddress.Of(address), command, data, checkSequence: true);
        return new WireRecord(_sequence++, WireDirection.Transmit, SasChannel.One,
            TimeSpan.FromMilliseconds(_sequence * 10), symbols[..length], null);
    }

    private WireRecord Reply(byte address, byte command, params byte[] data)
    {
        byte[] frame = new byte[2 + data.Length + 2];
        frame[0] = address;
        frame[1] = command;
        data.CopyTo(frame, 2);
        SasCrc.Append(frame, 2 + data.Length);
        return Machine(frame);
    }

    [Fact]
    public void A_general_poll_and_its_exception_are_one_exchange()
    {
        TransactionSplit split = TransactionSplitter.Split(
            [Host(0x81), Machine(0x11)]);

        SasExchange exchange = Assert.Single(split.Exchanges);
        Assert.Equal(ExchangeKind.GeneralPoll, exchange.Kind);
        Assert.Equal(ExchangeAnswer.Exception, exchange.Answer);
        Assert.Equal((byte?)0x11, exchange.Exception);
        Assert.Equal(SasAddress.Of(1), exchange.Address);
    }

    [Fact]
    public void A_general_poll_answered_with_zero_is_the_host_waiting()
    {
        TransactionSplit split = TransactionSplitter.Split([Host(0x81), Machine(0x00)]);

        Assert.Equal(ExchangeAnswer.NoException, split.Exchanges[0].Answer);
        Assert.Equal(1, split.IdlePolls);
        Assert.Null(split.Exchanges[0].Exception);
    }

    [Fact]
    public void Nobody_answers_a_synchronisation_poll_and_that_is_not_a_finding()
    {
        TransactionSplit split = TransactionSplitter.Split([Host(0x80)]);

        Assert.Equal(ExchangeKind.SyncPoll, split.Exchanges[0].Kind);
        Assert.Equal(ExchangeAnswer.Silent, split.Exchanges[0].Answer);
        Assert.Empty(split.Ambiguities);
    }

    [Fact]
    public void A_long_poll_and_the_reply_that_echoes_it_are_one_exchange()
    {
        TransactionSplit split = TransactionSplitter.Split(
            [LongPoll(0x01, 0x11), Reply(0x01, 0x11, 0x00, 0x00, 0x00, 0x01)]);

        SasExchange exchange = Assert.Single(split.Exchanges);
        Assert.Equal(ExchangeKind.LongPoll, exchange.Kind);
        Assert.Equal((byte)0x11, exchange.Command);
        Assert.Equal(ExchangeAnswer.Data, exchange.Answer);
        Assert.Null(exchange.Ambiguity);
    }

    /// <summary>
    /// The rule the poll engine itself had to be corrected to use, holding here too: a reply is
    /// placed by the command it echoes. Placing it by position is what reports one command's
    /// answer under another command's name.
    /// </summary>
    [Fact]
    public void A_reply_echoing_another_command_answers_nothing()
    {
        TransactionSplit split = TransactionSplitter.Split(
            [LongPoll(0x01, 0x11), Reply(0x01, 0x12, 0x00, 0x00, 0x00, 0x07)]);

        Assert.Equal(2, split.Exchanges.Count);
        Assert.Equal(ExchangeAnswer.Silent, split.Exchanges[0].Answer);
        Assert.Equal(ExchangeKind.Orphan, split.Exchanges[1].Kind);
        Assert.Contains("echoes long poll $12", split.Exchanges[1].Ambiguity);
        Assert.Single(split.Ambiguities);
    }

    [Fact]
    public void A_repeated_unanswered_long_poll_is_one_exchange_that_was_retried()
    {
        TransactionSplit split = TransactionSplitter.Split(
            [LongPoll(0x01, 0x11), LongPoll(0x01, 0x11), LongPoll(0x01, 0x11)]);

        SasExchange exchange = Assert.Single(split.Exchanges);
        Assert.Equal(2, exchange.Retries);
        Assert.Equal(ExchangeAnswer.Silent, exchange.Answer);
    }

    /// <summary>
    /// A repeated general poll is the poll loop and not a retry of anything, so collapsing it
    /// would turn a session's whole idle stretch into one exchange with a large number on it.
    /// </summary>
    [Fact]
    public void A_repeated_general_poll_is_not_a_retry()
    {
        TransactionSplit split = TransactionSplitter.Split([Host(0x81), Host(0x81), Host(0x81)]);

        Assert.Equal(3, split.Exchanges.Count);
        Assert.All(split.Exchanges, exchange => Assert.Equal(0, exchange.Retries));
    }

    [Fact]
    public void A_machine_message_before_any_poll_is_reported_as_answering_nothing()
    {
        TransactionSplit split = TransactionSplitter.Split(
            [Reply(0x01, 0x11, 0x00, 0x00, 0x00, 0x01), Host(0x81), Machine(0x00)]);

        Assert.Equal(ExchangeKind.Orphan, split.Exchanges[0].Kind);
        Assert.Contains("no host message before it", split.Exchanges[0].Ambiguity);
        Assert.Contains(split.Ambiguities,
            ambiguity => ambiguity.What.Contains("no host message before it"));
    }

    /// <summary>
    /// The one-byte answer with two readings. Where nothing in the record shows which address is
    /// being polled, an acknowledgement and an exception of the same value are the same byte, and
    /// choosing one would be a guess.
    /// </summary>
    [Fact]
    public void A_one_byte_answer_with_no_address_in_the_record_is_ambiguous()
    {
        // A bare two-byte long poll shows no address this build will learn from... except that it
        // does: the first byte of a long poll is the address. So the record is started with a
        // machine message instead, which shows nothing.
        TransactionSplit split = TransactionSplitter.Split([Machine(0x4A)]);

        Assert.Equal(ExchangeAnswer.Ambiguous, split.Exchanges[0].Answer);
        Assert.Null(split.Address);
    }

    [Fact]
    public void An_acknowledgement_is_told_from_an_exception_by_the_address()
    {
        TransactionSplit split = TransactionSplitter.Split(
            [LongPoll(0x4A, 0x0E, 0x01), Machine(0x4A)]);

        Assert.Equal(ExchangeAnswer.Acknowledged, split.Exchanges[0].Answer);
        Assert.Null(split.Exchanges[0].Exception);
    }

    [Fact]
    public void A_refusal_is_the_address_with_the_top_bit_set()
    {
        TransactionSplit split = TransactionSplitter.Split(
            [LongPoll(0x01, 0x0E, 0x01), Machine(0x81)]);

        Assert.Equal(ExchangeAnswer.Refused, split.Exchanges[0].Answer);
    }

    [Fact]
    public void A_run_of_bytes_that_fails_its_own_check_is_not_read_as_a_message()
    {
        TransactionSplit split = TransactionSplitter.Split(
            [LongPoll(0x01, 0x11), Machine(0x01, 0x11, 0x00, 0x00, 0x00, 0x01, 0xFF, 0xFF)]);

        Assert.Equal(ExchangeAnswer.NotAMessage, split.Exchanges[0].Answer);
        Assert.Contains("decided by position", split.Exchanges[0].Ambiguity);
    }

    [Fact]
    public void Every_machine_address_the_host_polled_is_recorded()
    {
        TransactionSplit split = TransactionSplitter.Split(
            [LongPoll(0x7F, 0x54), LongPoll(0x01, 0x54), Reply(0x01, 0x54, 0x01, 0x36)]);

        Assert.Equal([SasAddress.Of(0x7F), SasAddress.Of(0x01)], split.Addresses);
    }

    // ---- episodes -------------------------------------------------------------------------

    [Fact]
    public void A_door_episode_is_keyed_on_the_door_the_machine_named()
    {
        TransactionSplit split = TransactionSplitter.Split(
        [
            Host(0x81), Machine(0x11),   // slot door opened
            Host(0x81), Machine(0x13),   // drop door opened
            Host(0x81), Machine(0x12),   // slot door closed
        ]);

        Assert.Equal(2, split.Episodes.Count);
        SasEpisode slot = split.Episodes.First(e => e.Key == "slot");
        Assert.Equal(EpisodeKind.Door, slot.Rule.Kind);
        Assert.True(slot.Complete);
        Assert.Equal(Ends.Reported, slot.EndReason);
        Assert.Null(slot.Ambiguity);

        SasEpisode drop = split.Episodes.First(e => e.Key == "drop");
        Assert.False(drop.Complete);
        Assert.Equal(Ends.Truncated, drop.EndReason);
    }

    /// <summary>
    /// Two doors open at once are two episodes and neither is a guess, because which door an
    /// exception is about is in the code itself. That is what makes the door episode the one kind
    /// needing no judgement at all.
    /// </summary>
    [Fact]
    public void Two_doors_open_at_once_are_never_ambiguous()
    {
        TransactionSplit split = TransactionSplitter.Split(
        [
            Host(0x81), Machine(0x11),
            Host(0x81), Machine(0x13),
            Host(0x81), Machine(0x14),
            Host(0x81), Machine(0x12),
        ]);

        Assert.Equal(2, split.Episodes.Count);
        Assert.All(split.Episodes, episode => Assert.Null(episode.Ambiguity));
        Assert.All(split.Episodes, episode => Assert.True(episode.Complete));
    }

    [Fact]
    public void A_cashout_ticket_episode_is_keyed_on_the_validation_number_it_reads()
    {
        TransactionSplit split = TransactionSplitter.Split(
        [
            Host(0x81), Machine(0x3D),                                   // ticket printed
            LongPoll(0x01, 0x3D),
            Reply(0x01, 0x3D, 0x12, 0x34, 0x56, 0x78, 0x00, 0x00, 0x01, 0x00, 0x00),
        ]);

        SasEpisode episode = Assert.Single(split.Episodes);
        Assert.Equal(EpisodeKind.TicketOut, episode.Rule.Kind);
        Assert.Equal("12345678", episode.Key);
        Assert.Equal(Ends.Read, episode.EndReason);
        Assert.Null(episode.Ambiguity);
    }

    /// <summary>
    /// The honest limit of the second level, asserted rather than left in a comment. Nothing in a
    /// bill meter's reply names which note it is about, so two notes taken close together cannot
    /// be told apart and the split says so.
    /// </summary>
    [Fact]
    public void Two_notes_at_once_cannot_be_told_apart_and_the_split_says_so()
    {
        TransactionSplit split = TransactionSplitter.Split(
        [
            Host(0x81), Machine(0x4A),   // a twenty accepted
            Host(0x81), Machine(0x4A),   // and another
            LongPoll(0x01, 0x49),
            Reply(0x01, 0x49, 0x00, 0x00, 0x00, 0x02),
        ]);

        Assert.Equal(2, split.Episodes.Count);
        Assert.All(split.Episodes, episode =>
        {
            Assert.Equal(EpisodeKind.NoteIn, episode.Rule.Kind);
            Assert.NotNull(episode.Ambiguity);
            Assert.Contains("nothing in these replies says which", episode.Ambiguity);
        });

        Assert.Contains(split.Ambiguities, a => a.What.Contains("no key in the bytes"));
    }

    [Fact]
    public void A_note_episode_ends_when_the_host_moves_on()
    {
        TransactionSplit split = TransactionSplitter.Split(
        [
            Host(0x81), Machine(0x4A),
            LongPoll(0x01, 0x49),
            Reply(0x01, 0x49, 0x00, 0x00, 0x00, 0x01),
            LongPoll(0x01, 0x11),
            Reply(0x01, 0x11, 0x00, 0x00, 0x00, 0x05),
        ]);

        SasEpisode episode = Assert.Single(split.Episodes);
        Assert.Equal(Ends.MovedOn, episode.EndReason);
        Assert.Equal(2, episode.Exchanges.Count);
    }

    [Fact]
    public void A_ticket_redemption_runs_from_insertion_to_transfer_complete()
    {
        TransactionSplit split = TransactionSplitter.Split(
        [
            Host(0x81), Machine(0x67),   // ticket inserted
            LongPoll(0x01, 0x70, 0x00),
            Reply(0x01, 0x70, 0x10, 0x00,
                0x00, 0x00, 0x00, 0x10, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x12, 0x34, 0x56),
            Host(0x81), Machine(0x68),   // transfer complete
        ]);

        SasEpisode episode = Assert.Single(split.Episodes);
        Assert.Equal(EpisodeKind.TicketIn, episode.Rule.Kind);
        Assert.Equal(Ends.Reported, episode.EndReason);
        Assert.Equal((byte)0x68, episode.ClosedBy);
    }

    /// <summary>
    /// An episode whose opening the recording does not hold. Reported as one that began before the
    /// file did, rather than dropped — a recording that starts mid-transaction is common and is a
    /// property of the capture.
    /// </summary>
    [Fact]
    public void An_episode_closed_with_none_open_says_where_it_began_is_unknown()
    {
        TransactionSplit split = TransactionSplitter.Split([Host(0x81), Machine(0x12)]);

        SasEpisode episode = Assert.Single(split.Episodes);
        Assert.Equal(EpisodeKind.Door, episode.Rule.Kind);
        Assert.Contains("is not in the record", episode.Ambiguity);
        Assert.Contains(split.Ambiguities, a => a.What.Contains("and none was open"));
    }
}

/// <summary>
/// The rules that decide what an episode is, checked against the generated catalogue.
/// </summary>
/// <remarks>
/// The point of these is that a rule cannot go stale in silence. Two of the tables are derived
/// from the gaming machine library's own symbol names and the rest are stated; a rename in the
/// library has to arrive as a failure here rather than as an episode that quietly stopped matching
/// anything.
/// </remarks>
public class EpisodeRuleTests
{
    [Fact]
    public void Every_exception_a_rule_names_is_in_the_catalogue()
    {
        foreach (EpisodeRule rule in EpisodeRules.All)
        {
            foreach (byte code in rule.Opens.Concat(rule.Closes))
                Assert.True(SasCatalogue.Exceptions.ContainsKey(code), $"exception ${code:X2}");
        }
    }

    [Fact]
    public void Every_command_a_rule_names_is_in_the_catalogue()
    {
        foreach (EpisodeRule rule in EpisodeRules.All)
        {
            foreach (byte command in rule.Commands.Concat(rule.Reads))
                Assert.True(SasCatalogue.LongPolls.ContainsKey(command), $"long poll ${command:X2}");
        }
    }

    [Fact]
    public void The_derived_tables_are_not_empty()
    {
        // They were, once: the library writes its symbols in one style and this matched another,
        // so every derived table came out empty and every note and door episode silently stopped
        // existing. Nothing said so, which is why this test is here.
        Assert.NotEmpty(EpisodeRules.NoteExceptions);
        Assert.NotEmpty(EpisodeRules.DoorsOpened);
        Assert.NotEmpty(EpisodeRules.DoorsClosed);
        Assert.Equal(EpisodeRules.DoorsOpened.Count, EpisodeRules.DoorsClosed.Count);
    }

    [Fact]
    public void The_per_denomination_bill_counts_do_not_swallow_the_ticket_poll()
    {
        // $3D sits in the middle of the run of bill-count commands and is the cashout ticket
        // information, so a range put a ticket poll into the note episode.
        EpisodeRule note = EpisodeRules.For(EpisodeKind.NoteIn);
        Assert.DoesNotContain((byte)0x3D, note.Commands);
        Assert.Contains((byte)0x3C, note.Commands);
        Assert.Contains((byte)0x3E, note.Commands);
    }

    [Theory]
    [InlineData(0x11, "slot")]
    [InlineData(0x12, "slot")]
    [InlineData(0x13, "drop")]
    [InlineData(0x1B, "cashbox")]
    [InlineData(0x1C, "cashbox")]
    public void A_door_exception_names_its_door(byte code, string door) =>
        Assert.Equal(door, EpisodeRules.DoorOf(code));

    [Fact]
    public void Every_rule_says_how_it_splits()
    {
        foreach (EpisodeRule rule in EpisodeRules.All)
        {
            Assert.NotEmpty(rule.How);
            Assert.NotEmpty(rule.Name);
        }
    }
}
