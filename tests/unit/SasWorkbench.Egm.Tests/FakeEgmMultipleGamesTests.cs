using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The software machine built with several games, against the library's lp09, lp51, lp55, lp56, lp6F, lpB0, lpB1,
/// lpB2 and lpB5 handlers and the platform's configuration of its games. The host runs in a sequence, so nothing
/// reaches the line the test did not send.
/// </summary>
public sealed class FakeEgmMultipleGamesTests : IDisposable
{
    private readonly LoopbackBus _bus = new();
    private FakeEgm? _machine;
    private PollEngine? _host;

    private FakeEgm Machine => _machine!;

    private PollEngine Host => _host!;

    private void Start(params int[] games)
    {
        _machine = new FakeEgm(_bus.Egm, new FakeEgmOptions { GameDenominationsInCents = games, AccountingDenomination = 0x01 });
        _machine.Start();
        _host = new PollEngine(_bus.Host, new PollEngineOptions
        {
            Address = SasAddress.Of(1),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(1),
            QuietGap = TimeSpan.FromMilliseconds(15),
            LinkDownAfter = TimeSpan.FromSeconds(30),
        });
        _host.Start();
        Assert.True(_host.BeginSequence(), "the round did not hand the line over");
    }

    public void Dispose()
    {
        _host?.Dispose();
        _machine?.Dispose();
        _bus.Dispose();
    }

    private SasDecoded Ask(byte command, params byte[] data)
    {
        LongPollResult result = Host.Execute(command, data);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(command)!, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return decoded;
    }

    private void Play(int bet)
    {
        Machine.InsertBill(2000);
        Machine.PlayGames(1, bet);
    }

    [Fact]
    public void The_machine_counts_its_games_and_starts_in_the_first()
    {
        Start(1, 2, 5, 10);

        Assert.Equal(4, Ask(0x51).Number("games"));
        Assert.Equal(1, Ask(0x55).Number("selected_game"));
        Assert.Equal(0x01, Ask(0xB1).Number("denomination"));
    }

    [Fact]
    public void The_enabled_games_are_those_at_the_selected_denomination()
    {
        Start(1, 5, 1);

        SasDecoded games = Ask(0x56);
        Assert.Equal(2, games.Number("count"));
        Assert.Equal(1, games.Number("games[0].game_number"));
        Assert.Equal(3, games.Number("games[1].game_number"));

        Assert.Null(Machine.SelectGame(0));
        Assert.Equal(3, Ask(0x56).Number("count"));
    }

    [Fact]
    public void The_player_denominations_are_every_game_s_once()
    {
        Start(1, 5, 1, 10);

        SasDecoded denominations = Ask(0xB2);
        Assert.Equal(3, denominations.Number("count"));
        Assert.Equal(0x01, denominations.Number("denominations[0].denomination"));
        Assert.Equal(0x02, denominations.Number("denominations[1].denomination"));
        Assert.Equal(0x03, denominations.Number("denominations[2].denomination"));
    }

    [Fact]
    public void A_game_withdrawn_by_the_operator_leaves_the_lists_and_cannot_be_chosen()
    {
        Start(1, 5);

        Assert.Null(Machine.SetGameEnabled(2, enabled: false));

        Assert.Equal(1, Ask(0xB2).Number("count"));
        Assert.NotNull(Machine.SelectGame(2));
        Assert.NotNull(Machine.SelectDenomination(5));
    }

    [Fact]
    public void A_wager_is_charged_to_the_selected_game_and_to_its_denomination()
    {
        Start(1, 5);

        Play(bet: 2);
        Assert.Null(Machine.SelectDenomination(5));
        Play(bet: 1);

        // Coin in: two cents at one cent, five at five cents, seven for the machine, all in one-cent units.
        Assert.Equal(7, Ask(0x6F, 0x04, 0x00, 0x00, 0x00, 0x00).Number("meters[0].value"));
        Assert.Equal(2, Ask(0x6F, 0x04, 0x00, 0x01, 0x00, 0x00).Number("meters[0].value"));
        Assert.Equal(5, Ask(0x52, 0x00, 0x02).Number("total_coin_in"));
        Assert.Equal(5, Ask(0xB0, 0x07, 0x02, 0x6F, 0x04, 0x00, 0x00, 0x00, 0x00).Number("meters[0].value"));
    }

    [Fact]
    public void A_per_game_meter_poll_leaves_out_a_meter_the_library_keeps_for_the_machine_alone()
    {
        Start(1, 5);

        // Meter 000C, current credits, is not a multi-game meter.
        SasDecoded reply = Ask(0x6F, 0x06, 0x00, 0x01, 0x00, 0x00, 0x0C, 0x00);
        Assert.Equal(1, reply.Number("meters.count"));
    }

    [Fact]
    public void A_per_game_poll_past_the_last_game_is_silent()
    {
        Start(1, 5);

        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x6F, [0x04, 0x00, 0x03, 0x00, 0x00]).Outcome);
        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x53, [0x00, 0x03]).Outcome);
    }

    [Fact]
    public void Long_poll_09_refuses_game_zero_and_a_game_past_the_last()
    {
        Start(1, 5);

        Assert.Equal(LongPollOutcome.Rejected, Host.Execute(0x09, [0x00, 0x00, 0x00]).Outcome);
        Assert.Equal(LongPollOutcome.Rejected, Host.Execute(0x09, [0x00, 0x03, 0x00]).Outcome);
    }

    [Fact]
    public void Long_poll_09_takes_a_game_off_the_enabled_list_and_puts_it_back()
    {
        Start(1, 1);

        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x09, [0x00, 0x02, 0x00]).Outcome);
        Assert.Equal(1, Ask(0x56).Number("count"));
        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x09, [0x00, 0x02, 0x01]).Outcome);
        Assert.Equal(2, Ask(0x56).Number("count"));
    }

    [Theory]
    [InlineData((byte)0xFE, 0x01)]
    [InlineData((byte)0x1F, 0x03)]
    public void The_preamble_names_why_it_does_not_answer(byte baseCommand, int error)
    {
        Start(1, 5);

        SasDecoded reply = Ask(0xB0, 0x02, 0x01, baseCommand);
        Assert.Equal(0, reply.Number("base_command"));
        Assert.Equal(error, reply.Number("error_code"));
    }

    [Fact]
    public void The_preamble_refuses_a_meter_poll_for_a_game_or_at_a_denomination_the_machine_lacks()
    {
        Start(1, 5);

        Assert.Equal(0x04, Ask(0xB0, 0x07, 0x01, 0x6F, 0x04, 0x00, 0x01, 0x00, 0x00).Number("error_code"));
        Assert.Equal(0x04, Ask(0xB0, 0x07, 0x03, 0x6F, 0x04, 0x00, 0x00, 0x00, 0x00).Number("error_code"));
        Assert.Equal(0x05, Ask(0xB0, 0x04, 0x03, 0xB5, 0x00, 0x00).Number("error_code"));
    }

    [Fact]
    public void The_preamble_wraps_the_extended_information_of_game_zero_at_a_denomination()
    {
        Start(1, 5, 5);

        SasDecoded reply = Ask(0xB0, 0x04, 0x02, 0xB5, 0x00, 0x00);
        Assert.Equal(0x02, reply.Number("denomination"));
        Assert.Equal(0xB5, reply.Number("base_command"));
        Assert.Equal(0, reply.Number("game_number"));
        Assert.Equal(500, reply.Number("max_bet"));
    }

    [Fact]
    public void The_preamble_switches_a_game_off_with_09_and_answers_its_address()
    {
        Start(1, 5);

        SasDecoded reply = Ask(0xB0, 0x05, 0x02, 0x09, 0x00, 0x02, 0x00);
        Assert.Equal(0x09, reply.Number("base_command"));
        Assert.Equal(1, Ask(0xB2).Number("count"));
    }

    [Fact]
    public void The_identification_reply_summarises_the_games_a_player_can_reach()
    {
        Start(1, 5, 10);

        // Paybacks 92.00, 92.50 and 92.00: the average of the reachable ones, divided as the library divides.
        Assert.Equal(9216, Ask(0x1F).Number("base_percentage"));
        Assert.Null(Machine.SetGameEnabled(3, enabled: false));
        Assert.Equal(9225, Ask(0x1F).Number("base_percentage"));
        Assert.Equal(0xFF, Ask(0x1F).Number("max_bet"));
    }

    [Fact]
    public void A_changed_selection_is_reported_once_with_its_number()
    {
        Start(1, 5);

        Assert.Null(Machine.SelectGame(2));
        Assert.Null(Machine.SelectGame(2));
        Host.Synchronise();
        Assert.Equal(0x8C, Host.PollGenerally().Code);
        Assert.Equal(2, Ask(0x55).Number("selected_game"));
        Assert.Equal(0x00, Host.PollGenerally().Code);
    }

    [Fact]
    public void The_machine_claims_the_multi_denomination_extensions()
    {
        Start(1, 5);

        Assert.Equal(0x80, Ask(0xA0, 0x00, 0x00).Number("feature_codes_2") & 0x80);
    }

    [Fact]
    public void No_game_is_played_while_the_game_menu_is_open()
    {
        Start(1, 5);
        Machine.InsertBill(2000);

        Assert.Null(Machine.SelectGame(0));
        Assert.Throws<InvalidOperationException>(() => Machine.PlayGames(1, 1));
        Assert.Equal(0x01, Ask(0xB1).Number("denomination"));
    }
}
