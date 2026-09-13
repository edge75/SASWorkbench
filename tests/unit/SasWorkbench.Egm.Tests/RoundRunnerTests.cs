using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// A script that names a round of machines, run end to end against a floor of software ones.
/// </summary>
/// <remarks>
/// The two refusals are here rather than in the committed suite because, expressed as cases, they
/// would be skips — and everything under tests/regression runs green with no skips at all, so two
/// deliberate ones would hide the accidental one.
/// </remarks>
public class RoundRunnerTests
{
    private static ScriptRunner Runner() => new(new ScriptRunnerOptions
    {
        Stimulus = StimulusBackend.Software,
        Environment = SoftwareMachineEnvironment.For(),
        PollInterval = TimeSpan.Zero,
        ResponseTimeout = TimeSpan.FromSeconds(5),
        QuietGap = TimeSpan.FromMilliseconds(15),
    });

    private static TestOutcome Run(string body)
    {
        ScriptDocument document = ScriptParser.Parse(
            $"test \"a round\" {{\n{body}\n}}\n", "round.sas");
        return Runner().Run(document.Tests[0]);
    }

    private static string Explain(TestOutcome result) =>
        string.Join('\n', result.Steps.Select(s => s.ToString()));

    /// <summary>
    /// A send that names no machine on a round is refused, and the round is named in the reason.
    /// </summary>
    /// <remarks>
    /// The refusal the whole notation exists to make possible. A command sent to whichever machine
    /// the cycle had reached would be answered correctly by that machine, so the case would pass
    /// having addressed one it never named — the worst outcome available, because nothing in the
    /// report would look wrong.
    /// </remarks>
    [Fact]
    public void A_send_that_names_no_machine_on_a_round_is_refused()
    {
        TestOutcome result = Run("""
              host connect loopback addresses 01-04
              host expect link up within 5s
              host send lp 1F
            """);

        Assert.Equal(Verdict.Skip, result.Verdict);

        StepResult refused = result.Steps.Single(s => s.Verdict == Verdict.Skip);
        Assert.Contains("has not said which one", refused.Reason!, StringComparison.Ordinal);
        Assert.Contains("$01, $02, $03, $04", refused.Reason!, StringComparison.Ordinal);
    }

    /// <summary>Moving to a machine this host does not poll is refused, naming the round.</summary>
    [Fact]
    public void Moving_to_a_machine_outside_the_round_is_refused()
    {
        TestOutcome result = Run("""
              host connect loopback addresses 01-04
              host expect link up within 5s
              host machine 09
            """);

        Assert.Equal(Verdict.Skip, result.Verdict);
        Assert.Contains("does not poll $09", Explain(result), StringComparison.Ordinal);
    }

    /// <summary>A host on one machine still needs no machine named, as every case before this.</summary>
    [Fact]
    public void A_host_on_one_machine_needs_no_machine_named()
    {
        TestOutcome result = Run("""
              host connect loopback address 01
              host expect link up within 5s
              host send lp 1F
            """);

        Assert.Equal(Verdict.Pass, result.Verdict);
    }

    /// <summary>
    /// Every message a round produced says which machine it belongs to, and the synchronisation
    /// poll says none.
    /// </summary>
    /// <remarks>
    /// The half that makes a saved floor run readable. A reply to a general poll is a bare
    /// exception byte with no address in it, so this field is the only thing that attributes it.
    /// The synchronisation poll is addressed to nobody and must stay that way: filling it in would
    /// put a poll no machine owns under some machine's name.
    /// </remarks>
    [Fact]
    public void Every_message_on_a_round_says_which_machine_it_belongs_to()
    {
        TestOutcome result = Run("""
              host connect loopback addresses 01-04
              host expect link up within 5s
              host send lp 1F to 03
            """);

        Assert.Equal(Verdict.Pass, result.Verdict);

        IReadOnlyList<WireRecord> wire = result.Wire;
        Assert.NotEmpty(wire);

        // Everything the host attributed belongs to a machine in the round, and more than one
        // machine was reached. How many of the four a short case gets through is a question of
        // timing and is asserted deterministically in FloorRoundTests instead; what is asserted
        // here is that nothing was attributed to a machine this host does not poll.
        var polled = wire
            .Where(r => r.Direction == WireDirection.Transmit && r.Address is not null)
            .Select(r => r.Address!.Value.Value)
            .ToHashSet();

        Assert.Subset(new HashSet<byte> { 1, 2, 3, 4 }, polled);
        Assert.True(polled.Count >= 2, $"only machine(s) {string.Join(", ", polled)} were polled");

        // A synchronisation poll belongs to nobody. It is the one-symbol transmission of $80.
        Assert.All(
            wire.Where(r => r.Direction == WireDirection.Transmit
                            && r.Symbols.Length == 1
                            && r.Symbols[0].Data == 0x80),
            r => Assert.Null(r.Address));

        // The long poll to $03 and its reply both belong to $03.
        WireRecord request = wire.Single(
            r => r.Direction == WireDirection.Transmit
                 && r.Symbols.Length > 1
                 && r.Symbols[1].Data == 0x1F);

        Assert.Equal(SasAddress.Of(3), request.Address);

        WireRecord reply = wire.First(
            r => r.Direction == WireDirection.Receive && r.Sequence > request.Sequence);

        Assert.Equal(SasAddress.Of(3), reply.Address);
    }
}
