using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The coin acceptor, hopper and reels end to end through the script runner (D58): a case that declares them gets a
/// software machine with them, a case that does not skips naming the stimulus, and the declarations parse once each.
/// </summary>
public class DeviceScriptTests
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
        return runner.Run(ScriptParser.Parse(script, "devices.sas").Tests[0]);
    }

    private static string Explain(TestOutcome result) =>
        string.Join('\n', result.Steps.Select(s => s.ToString()));

    [Fact]
    public void A_declared_hopper_pays_a_cash_out_and_raises_hopper_empty_when_it_runs_dry()
    {
        TestOutcome result = Run("""
            machine hopper on
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            egm hopper level 0
            egm bill insert 2000
            egm press collect
            host expect exception hopper_empty within 5s
            host send lp 4F
            host expect field status of lp 4F == 7
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }

    [Fact]
    public void Without_the_declaration_a_coin_case_skips_naming_the_stimulus()
    {
        TestOutcome result = Run("""
            requires stimulus.coin
            host connect loopback address 01
            host poll every 20ms
            host expect link up within 2s
            egm coin insert 1
            """);

        Assert.Equal(Verdict.Skip, result.Verdict);
        Assert.StartsWith("stimulus.coin:", result.SkipReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_partial_pay_brings_its_hopper_and_each_declaration_is_made_once()
    {
        ScriptTest test = ScriptParser.Parse("""
            machine partial pay on
            machine coin acceptor external
            machine reels on
            host connect loopback address 01
            """, "devices.sas").Tests[0];

        Assert.NotNull(test.Devices);
        Assert.True(test.Devices!.Hopper);
        Assert.Equal(CoinAcceptorKind.External, test.Devices.CoinAcceptor);
        Assert.Equal(
            ["machine.coin_acceptor", "machine.external_coin_acceptor", "machine.hopper", "machine.physical_reels", "machine.partial_pay"],
            test.Devices.AsRequirements().Select(r => r.Key));

        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse("""
            machine hopper on
            machine hopper on
            """, "twice.sas"));
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse("""
            machine hopper off
            """, "off.sas"));
    }

    [Fact]
    public void The_device_steps_parse_with_the_capability_each_needs()
    {
        IReadOnlyList<ScriptStep> steps = ScriptParser.Parse("""
            egm coin insert 3
            egm hopper fault jam
            egm reel fault reel_2
            egm hopper level 40
            egm set hopper limit 1000
            egm set win payout hopper
            egm cashbox removed
            egm handpay cancel
            """, "steps.sas").Tests[0].Steps;

        Assert.Equal(
            ["stimulus.coin", "stimulus.hopper_fault", "stimulus.reel", "stimulus.hopper", "stimulus.hopper", "stimulus.hopper",
             "stimulus.cashbox", "stimulus.handpay.cancel"],
            steps.OfType<StimulusStep>().Select(s => s.RequiredCapability));
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse("egm reel fault reel_6", "bad.sas"));
    }
}
