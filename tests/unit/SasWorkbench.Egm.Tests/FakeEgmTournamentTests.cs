using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// Host-controlled tournaments, which a case declares (D60): the feature bit, $8C acknowledged or ignored, and the
/// four meters $95 to $99 read. A session with credits plays on its own and leaves the accounting meters still; one
/// without plays on the player's; one asked for while the machine is busy is entered once it is free.
/// </summary>
public sealed class FakeEgmTournamentTests : IDisposable
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
    }

    public void Dispose()
    {
        _host?.Dispose();
        _machine?.Dispose();
        _bus.Dispose();
    }

    /// <summary>A machine with tournaments, one game credit to the accounting unit as the cases assume.</summary>
    private static FakeEgmOptions Declared() => new() { Tournament = true, CentsPerGameCredit = 1, AccountingDenomination = 0x01 };

    private LongPollResult Session(string hex) => Host.Execute(0x8C, Convert.FromHexString(hex));

    private void StartSession(string hex) => Assert.Equal(LongPollOutcome.Acknowledged, Session(hex).Outcome);

    private void EndSession() => StartSession("000000000000000000");

    /// <summary>Games played, games won, credits wagered and credits won of a game, read with $99.</summary>
    private long[] Meters(byte game = 0)
    {
        LongPollResult result = Host.Execute(0x99, [0x00, game]);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x99)!, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return [.. new[] { "games_played", "games_won", "credits_wagered", "credits_won" }.Select(f => decoded.Number(f)!.Value)];
    }

    private long Single(byte command, string field)
    {
        LongPollResult result = Host.Execute(command, [0x00, 0x00]);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        return SasMessageDecoder.DecodeResponse(SasLayouts.For(command)!, result.Frame).Number(field)!.Value;
    }

    private long FeatureCodes1() =>
        SasMessageDecoder.DecodeResponse(SasLayouts.For(0xA0)!, Host.Execute(0xA0, [0x00, 0x00]).Frame)
            .Number("feature_codes_1")!.Value;

    [Fact]
    public void A_machine_without_tournaments_clears_the_bit_and_ignores_every_tournament_poll()
    {
        Start(new FakeEgmOptions());

        Assert.Equal(0, FeatureCodes1() & 0x08);
        Assert.Equal(LongPollOutcome.Silent, Session("000005000000010000").Outcome);
        foreach (byte command in new byte[] { 0x95, 0x96, 0x97, 0x98, 0x99 })
            Assert.Equal(LongPollOutcome.Silent, Host.Execute(command, [0x00, 0x00]).Outcome);
    }

    [Fact]
    public void A_declared_machine_sets_the_bit_acknowledges_a_session_and_answers_each_meter_separately()
    {
        Start(Declared());

        Assert.Equal(0x08, FeatureCodes1() & 0x08);
        StartSession("000005000000010001");
        Assert.Equal(0, Single(0x95, "games_played"));
        Assert.Equal(0, Single(0x96, "games_won"));
        Assert.Equal(0, Single(0x97, "credits_wagered"));
        Assert.Equal(0, Single(0x98, "credits_won"));

        // A game past the last is ignored (lp.cpp:239).
        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x95, [0x00, 0x09]).Outcome);
        Assert.Equal(LongPollOutcome.Silent, Session("000905000000010000").Outcome);
    }

    [Fact]
    public void A_session_with_credits_plays_on_them_moves_no_accounting_meter_and_ends_once_they_are_spent()
    {
        Start(Declared());
        StartSession("000010000000010000");

        // No credits of the player's: the session's hundred pay.
        Machine.PlayGames(1, 10, 10);
        Machine.PlayGames(10, 10, 0);
        Assert.Equal([11, 1, 110, 10], Meters());
        Assert.Equal(0, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.Equal(0, Machine.Meters.Read(SasMeterCode.TotalCoinInCredits));
        Assert.Equal(0, Machine.Meters.Read(SasMeterCode.GamesPlayed));

        // Spent: the next game is the player's, and counted nowhere in the session.
        Assert.Throws<InvalidOperationException>(() => Machine.PlayGames(1, 5, 0));
        Machine.Meters.SetCurrentCredits(100);
        Machine.PlayGames(2, 5, 0);
        Assert.Equal([11, 1, 110, 10], Meters());
        Assert.Equal(90, Machine.Meters.Read(SasMeterCode.CurrentCredits));
    }

    [Fact]
    public void A_bet_larger_than_what_is_left_ends_the_session()
    {
        Start(Declared());
        StartSession("000000000000000500");
        Machine.Meters.SetCurrentCredits(100);

        Machine.PlayGames(1, 3, 0);
        Machine.PlayGames(1, 3, 0);
        Assert.Equal([1, 0, 3, 0], Meters());
        Assert.Equal(97, Machine.Meters.Read(SasMeterCode.CurrentCredits));
    }

    [Fact]
    public void A_time_only_session_counts_on_the_players_credits_from_its_first_game_until_the_time_is_up()
    {
        Start(Declared());
        Machine.Meters.SetCurrentCredits(100);

        // One second, and no credits: the timer has not started while nobody plays.
        StartSession("000000010000000000");
        Thread.Sleep(1200);
        Machine.PlayGames(1, 2, 4);
        Assert.Equal([1, 1, 2, 4], Meters());
        Assert.Equal(102, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.Equal(2, Machine.Meters.Read(SasMeterCode.TotalCoinInCredits));

        Thread.Sleep(1200);
        Machine.PlayGames(1, 2, 4);
        Assert.Equal([1, 1, 2, 4], Meters());
    }

    [Fact]
    public void Ending_a_session_keeps_its_meters_and_a_new_one_starts_them_from_zero()
    {
        Start(Declared());
        Machine.Meters.SetCurrentCredits(100);
        StartSession("000005000000000000");
        Machine.PlayGames(1, 1, 0);
        EndSession();
        Machine.PlayGames(1, 1, 0);
        Assert.Equal([1, 0, 1, 0], Meters());

        StartSession("000005000000000000");
        Assert.Equal([0, 0, 0, 0], Meters());
    }

    [Fact]
    public void A_session_asked_for_while_the_machine_is_busy_starts_once_it_is_free()
    {
        Start(Declared());
        Machine.Meters.SetCurrentCredits(100);

        // A game running as it is asked for is not counted, nor is a game played with the door open.
        Machine.StartGame(1);
        StartSession("000005000000000000");
        Machine.EndGame(0);
        Assert.True(Machine.SetDoor("main", open: true));
        Machine.PlayGames(1, 1, 0);
        Assert.Equal([0, 0, 0, 0], Meters());

        Assert.True(Machine.SetDoor("main", open: false));
        Machine.PlayGames(1, 1, 0);
        Assert.Equal([1, 0, 1, 0], Meters());
    }

    [Fact]
    public void A_session_on_one_game_counts_only_that_game()
    {
        Start(new FakeEgmOptions { Tournament = true, GameDenominationsInCents = [1, 1], AccountingDenomination = 0x01 });
        Machine.Meters.SetCurrentCredits(100);
        Assert.Null(Machine.SelectGame(2));
        StartSession("000210000000000000");

        Machine.PlayGames(1, 1, 0);
        Assert.Null(Machine.SelectGame(1));
        Machine.PlayGames(1, 1, 0);
        Assert.Equal([1, 0, 1, 0], Meters(2));
        Assert.Equal([0, 0, 0, 0], Meters(1));
    }

    [Fact]
    public void The_declaration_parses_once_and_names_the_capability()
    {
        ScriptTest test = ScriptParser.Parse("""
            machine tournament on
            host connect loopback address 01
            """, "tournament.sas").Tests[0];
        Assert.True(test.Devices!.Tournament);
        Assert.Contains(test.Devices.AsRequirements(), r => r.Key == MachineDevicesDeclaration.TournamentCapability);

        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse("""
            machine tournament on
            machine tournament on
            """, "tournament.sas"));
    }
}
