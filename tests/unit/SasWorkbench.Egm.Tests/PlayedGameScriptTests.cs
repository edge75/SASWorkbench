using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// A game played on a chosen game end to end through the script runner (D59): the busy reply, silence on a poll that
/// carries its own length, the bet-one button a case declares, the recall screen, and the ticket meters read by code.
/// </summary>
public class PlayedGameScriptTests
{
    private static TestOutcome Run(string script, StimulusBackend stimulus = StimulusBackend.Software)
    {
        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = stimulus,
            Environment = SoftwareMachineEnvironment.For(),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(5),
            QuietGap = TimeSpan.FromMilliseconds(15),
        });
        return runner.Run(ScriptParser.Parse(script, "played.sas").Tests[0]);
    }

    private static string Explain(TestOutcome result) =>
        string.Join('\n', result.Steps.Select(s => s.ToString()));

    [Fact]
    public void A_bonus_sent_with_the_door_open_is_answered_busy_and_one_sent_with_it_shut_is_not()
    {
        TestOutcome busy = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            egm door main open
            host expect busy reply to lp 8A data 0000000500
            """);
        Assert.True(busy.Verdict == Verdict.Pass, Explain(busy));

        TestOutcome served = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host expect busy reply to lp 8A data 0000000500
            """);
        Assert.Equal(Verdict.Fail, served.Verdict);
    }

    [Fact]
    public void Silence_is_asserted_on_a_poll_that_carries_its_own_length_only_when_the_length_matches()
    {
        TestOutcome whole = Run("""
            machine games 1c 1c
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host expect no reply to lp 6F data 0400030000
            """);
        Assert.True(whole.Verdict == Verdict.Pass, Explain(whole));

        // A length of six with four bytes behind it is dropped by the receiver, so its silence proves nothing.
        TestOutcome shortened = Run("""
            machine games 1c 1c
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host expect no reply to lp 6F data 0600030000
            """);
        Assert.Equal(Verdict.Fail, shortened.Verdict);
    }

    [Fact]
    public void Bet_one_and_the_recall_screen_parse_and_the_button_needs_its_declaration()
    {
        ScriptTest test = ScriptParser.Parse("""
            machine bet button on
            host connect loopback address 01
            egm press bet
            egm game recall 1
            host expect busy reply to lp 8A data 0000000500
            """, "played.sas").Tests[0];

        Assert.True(test.Devices!.BetButton);
        Assert.Equal(["machine.bet_button"], test.Devices.AsRequirements().Select(r => r.Key));
        Assert.IsType<BetButtonStep>(test.Steps[1]);
        Assert.Equal(1, Assert.IsType<GameRecallStep>(test.Steps[2]).Index);
        Assert.Equal(0x8A, Assert.IsType<ExpectBusyReplyStep>(test.Steps[3]).Command);

        TestOutcome undeclared = Run("""
            requires stimulus.bet_button
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            egm press bet
            """);
        Assert.Equal(Verdict.Skip, undeclared.Verdict);
    }

    [Fact]
    public void A_redeemed_non_restricted_promotional_ticket_joins_the_cashable_total_and_not_the_regular_one()
    {
        TestOutcome result = Run("""
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            host forget exceptions within 1s
            host send lp 6F data 06000028002900
            host remember field meters[0].value as cashable
            host remember field meters[1].value as regular
            egm ticket insert amount=1000 validation=0000000000000777
            host expect exception ticket_inserted within 5s
            host send lp 70
            host send lp 71 with transfer_code=ttc_valid_nonrestricted_promotional_ticket transfer_amount=1000 parsing_code=0 validation_data=0000000000000777
            host expect exception ticket_transfer_complete within 5s
            host forget exceptions within 1s
            host send lp 6F data 06000028002900
            host expect field meters[0].value > saved.cashable
            host expect field meters[1].value == saved.regular
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }
}
