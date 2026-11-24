using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// A case chooses the validation style with 'machine validation': the software machine is built
/// performing it, and any machine the build did not make is asked and skips when it performs another.
/// </summary>
public class ValidationStyleScriptTests
{
    private static TestOutcome Run(string script, EnvironmentFactory? environment = null)
    {
        var runner = new ScriptRunner(new ScriptRunnerOptions
        {
            Stimulus = StimulusBackend.Software,
            Environment = environment ?? SoftwareMachineEnvironment.For(),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(5),
            QuietGap = TimeSpan.FromMilliseconds(15),
        });
        return runner.Run(ScriptParser.Parse(script, "validation.sas").Tests[0]);
    }

    private static string Explain(TestOutcome result) =>
        string.Join('\n', result.Steps.Select(s => s.ToString()));

    [Theory]
    [InlineData("standard", 0)]
    [InlineData("system", 1)]
    [InlineData("secure_enhanced", 2)]
    public void The_software_machine_is_built_performing_the_declared_style(string style, int bits)
    {
        TestOutcome result = Run($$"""
            test "style" {
              machine validation {{style}}
              host connect loopback address 01
              host poll every 20ms
              host expect link up within 2s
              host send lp A0 data 0000
              host expect field validation_style of lp A0 == {{bits}}
            }
            """);

        Assert.True(result.Verdict == Verdict.Pass, Explain(result));
    }

    [Fact]
    public void A_machine_the_build_did_not_configure_is_asked_and_skips_on_another_style()
    {
        // A standard machine that ignores the declaration, as every machine this build did not make does.
        EnvironmentFactory unconfigurable = (step, options, declared, channels, _, _, _) =>
            SoftwareMachineEnvironment.For()(step, options, declared, channels);

        TestOutcome result = Run("""
            test "style" {
              machine validation secure_enhanced
              host connect loopback address 01
              host poll every 20ms
              host expect link up within 2s
            }
            """, unconfigurable);

        Assert.Equal(Verdict.Skip, result.Verdict);
        Assert.Contains("'egm.validation' is 'standard', not 'secure_enhanced'", result.SkipReason!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("machine validation none")]
    [InlineData("machine validation")]
    [InlineData("machine validation system\n  machine validation standard")]
    public void A_style_the_wire_has_no_name_for_or_a_second_style_is_refused(string line)
    {
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse($$"""
            test "style" {
              {{line}}
            }
            """, "validation.sas"));
    }
}
