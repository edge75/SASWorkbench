using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// A game played on a chosen game (D59): the recall screen the platform reports with $8A, the declared bet-one button
/// and the wait for the player, each game's own legacy bonus and progressive meters, the total won worked out from
/// coin out and jackpot, and the ticket meters of each kind. Every case runs with the host in a sequence.
/// </summary>
public sealed class FakeEgmPlayedGameTests : IDisposable
{
    private readonly LoopbackBus _bus = new();
    private FakeEgm? _machine;
    private PollEngine? _host;

    private FakeEgm Machine => _machine!;

    private PollEngine Host => _host!;

    private void Start(FakeEgmOptions options)
    {
        _machine = new FakeEgm(_bus.Egm, options);
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

    private static FakeEgmOptions TwoGames(bool betButton = false) => new()
    {
        GameDenominationsInCents = [1, 1],
        AccountingDenomination = 0x01,
        BetButton = betButton,
    };

    public void Dispose()
    {
        _host?.Dispose();
        _machine?.Dispose();
        _bus.Dispose();
    }

    private GeneralPollResult Poll()
    {
        Host.Synchronise();
        GeneralPollResult result = Host.PollGenerally();
        Assert.True(result.Answered, $"the general poll went unanswered: {result.Outcome}");
        return result;
    }

    private List<GeneralPollResult> Drain()
    {
        var drawn = new List<GeneralPollResult>();
        for (GeneralPollResult result = Poll(); result.Outcome == GeneralPollOutcome.Exception && drawn.Count < 40; result = Poll())
            drawn.Add(result);
        return drawn;
    }

    private void ReportInEventFormat() =>
        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x0E, [0x01]).Outcome);

    private SasDecoded Ask(byte command, params byte[] data)
    {
        LongPollResult result = Host.Execute(command, data);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(command)!, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return decoded;
    }

    private long GameMeter(byte game, ushort meter) =>
        Ask(0x6F, 0x04, 0x00, game, (byte)(meter & 0xFF), (byte)(meter >> 8)).Number("meters[0].value") ?? -1;

    [Fact]
    public void The_recall_screen_reports_each_past_game_with_its_number_and_index()
    {
        Start(TwoGames());
        Machine.Meters.SetCurrentCredits(100);
        Assert.NotNull(Machine.RecallGame(0));

        Assert.Null(Machine.SelectGame(1));
        Machine.PlayGames(1, 1);
        Assert.Null(Machine.SelectGame(2));
        Machine.PlayGames(1, 1);
        Drain();

        Assert.Null(Machine.RecallGame(0));
        Assert.Null(Machine.RecallGame(1));
        Assert.NotNull(Machine.RecallGame(2));

        List<GeneralPollResult> drawn = Drain();
        Assert.Equal([0x8A, 0x8A], drawn.Select(d => d.Code));
    }

    [Fact]
    public void A_recalled_game_carries_its_number_and_index_in_the_event_format()
    {
        Start(TwoGames());
        Machine.Meters.SetCurrentCredits(100);
        Assert.Null(Machine.SelectGame(2));
        Machine.PlayGames(1, 1);
        ReportInEventFormat();
        Drain();

        Assert.Null(Machine.RecallGame(0));
        GeneralPollResult recalled = Poll();

        Assert.True(recalled.IsRealTimeEvent);
        Assert.Equal(0x8A, recalled.Code);
        Assert.Equal([0x00, 0x02, 0x00, 0x00], recalled.Data);
    }

    [Fact]
    public void Without_the_declaration_there_is_no_bet_button()
    {
        Start(TwoGames());

        Assert.NotNull(Machine.PressBet());
    }

    [Fact]
    public void Each_press_of_bet_one_is_reported_up_to_the_maximum_and_the_machine_then_waits_for_the_player()
    {
        Start(TwoGames(betButton: true) with { GameDenominationsInCents = [] });
        Machine.Meters.SetCurrentCredits(1000);
        ReportInEventFormat();
        Drain();

        for (int press = 0; press < FakeEgm.BetButtonMaxBet + 1; press++)
            Assert.Null(Machine.PressBet());

        List<GeneralPollResult> drawn = Drain();
        Assert.Equal(FakeEgm.BetButtonMaxBet, drawn.Count(d => d.Code == 0x89));
        Assert.All(drawn.Where(d => d.Code == 0x89), d => Assert.Empty(d.Data!));
        Assert.Equal(0x1F, drawn[^1].Code);

        // Played, the wager is used and the machine is idle again.
        Machine.PlayGames(1, FakeEgm.BetButtonMaxBet);
        Drain();
        Assert.Equal(GeneralPollOutcome.NoActivity, Poll().Outcome);
    }

    [Fact]
    public void With_reporting_off_bet_one_is_never_queued()
    {
        Start(TwoGames(betButton: true) with { GameDenominationsInCents = [] });
        Machine.Meters.SetCurrentCredits(1000);
        Drain();

        Assert.Null(Machine.PressBet());

        // The wait for the player answers, and no $89 is behind it.
        GeneralPollResult waiting = Poll();
        Assert.Equal(0x1F, waiting.Code);
        Assert.False(waiting.IsRealTimeEvent);
        Assert.DoesNotContain(Drain(), d => d.Code == 0x89);
    }

    [Fact]
    public void A_legacy_bonus_moves_the_machine_legacy_meter_and_no_game_one()
    {
        Start(TwoGames() with { LegacyBonus = true });
        Assert.Null(Machine.SelectGame(2));

        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x8A, [0x00, 0x00, 0x00, 0x05, 0x00]).Outcome);

        Assert.Equal(5, Ask(0x9A, 0x00, 0x00).Number("deductible"));
        Assert.Equal(0, Ask(0x9A, 0x00, 0x02).Number("deductible"));
        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x9A, [0x00, 0x03]).Outcome);
    }

    [Fact]
    public void Each_game_answers_its_own_progressive_wins_and_total_won_is_coin_out_and_jackpot()
    {
        Start(TwoGames());
        Machine.Meters.SetCurrentCredits(100);
        Assert.Null(Machine.SelectGame(1));
        Machine.PlayGames(1, 1, 7);

        Assert.Equal(7, GameMeter(0x01, 0x0022));
        Assert.Equal(0, GameMeter(0x02, 0x0022));
        Assert.Equal(Machine.Meters.Read(SasMeterCode.TotalCoinOutCredits) + Machine.Meters.Read(SasMeterCode.TotalJackpotCredits),
            Machine.Meters.Read(SasMeterCode.TotalWonCredits));
        Assert.Equal(0, Ask(0x83, 0x00, 0x02).Number("cumulative_progressive_win"));
    }

    [Fact]
    public void A_printed_cashable_ticket_moves_the_cashable_ticket_out_totals()
    {
        string wallet = Path.Combine(Path.GetTempPath(), "sasworkbench-played-" + Guid.NewGuid().ToString("N"));
        try
        {
            Start(new FakeEgmOptions { AccountingDenomination = 0x01 });
            Machine.Meters.SetCurrentCredits(250);

            Assert.NotNull(Machine.Cashout(wallet));

            Assert.Equal(250, Machine.Meters.Read(SasMeterCode.TotalCashableTicketOut_Crd));
            Assert.Equal(1, Machine.Meters.Read(SasMeterCode.TotalCashableTicketOut_Qty));
        }
        finally
        {
            if (Directory.Exists(wallet))
                Directory.Delete(wallet, recursive: true);
        }
    }
}
