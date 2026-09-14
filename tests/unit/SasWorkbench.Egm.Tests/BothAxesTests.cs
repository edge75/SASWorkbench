using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// A case that is both axes at once: a round of machines, each divided between hosts.
/// </summary>
/// <remarks>
/// <para>
/// The refusals live here rather than in the committed suite because, expressed as cases, they
/// would be skips — and everything under tests/regression runs green with no skips at all, so a
/// deliberate one would hide the accidental one. The properties that can be asserted as passes
/// are in <c>tests/regression/floor/</c>.
/// </para>
/// <para>
/// The arrangement of a floor is a line per channel, which is read out of the machine rather than
/// chosen: the platform opens channel one on its first network serial port and each further
/// channel on the next. Two hosts on one line is not something any machine here has.
/// </para>
/// </remarks>
public class BothAxesTests
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
            $"test \"both axes\" {{\n{body}\n}}\n", "floor.sas");
        return Runner().Run(document.Tests[0]);
    }

    private static string Reasons(TestOutcome result) =>
        string.Join('\n', result.Steps.Select(s => s.Reason).Where(r => r is not null));

    // ---- the declaration ------------------------------------------------------------------

    /// <summary>
    /// The k-th machine of a floor has the k-th address of every channel's round.
    /// </summary>
    /// <remarks>
    /// Both rounds may name the same addresses, because an address is unique on a line and not on
    /// a floor. This is what makes a hundred machines on two channels expressible at all: two
    /// hundred ports, and SAS has 127 addresses.
    /// </remarks>
    [Fact]
    public void Each_machine_of_a_floor_takes_its_own_address_on_every_channel()
    {
        MachineChannelDeclaration declared = Declare("""
            machine channel 1 addresses 01-04
            machine channel 2 addresses 01-04 owns validation
            """);

        Assert.True(declared.TryCountMachines(out int machines, out string? refusal), refusal);
        Assert.Equal(4, machines);

        Assert.True(declared.TryResolveFor(2, out SasChannelPlan third, out refusal), refusal);
        Assert.Equal(SasAddress.Of(3), third.AddressOf(SasChannel.One));
        Assert.Equal(SasAddress.Of(3), third.AddressOf(SasChannel.Two));
        Assert.Equal(SasChannel.Two, third.OwnerOf(SasCategory.Validation));
        Assert.Equal(SasChannel.One, third.OwnerOf(SasCategory.Aft));
    }

    /// <summary>
    /// Rounds of different lengths are refused with both lengths named.
    /// </summary>
    /// <remarks>
    /// Zipping them to the shorter would decide, on the author's behalf, which machines are not on
    /// the floor — and the ones left out would simply never answer, which reads as a floor with
    /// holes in it rather than as a script that says two different things.
    /// </remarks>
    [Fact]
    public void Rounds_of_different_lengths_are_refused()
    {
        MachineChannelDeclaration declared = Declare("""
            machine channel 1 addresses 01-64
            machine channel 2 addresses 01-08 owns validation
            """);

        Assert.False(declared.TryCountMachines(out _, out string? refusal));
        Assert.Contains("100", refusal!, StringComparison.Ordinal);
        Assert.Contains("8", refusal!, StringComparison.Ordinal);
        Assert.Contains("same machine", refusal!, StringComparison.Ordinal);
    }

    /// <summary>A single address is a round of one, so one rule covers both notations.</summary>
    [Fact]
    public void A_single_address_beside_a_round_is_refused_as_a_length_mismatch()
    {
        MachineChannelDeclaration declared = Declare("""
            machine channel 1 addresses 01-04
            machine channel 2 address 02 owns validation
            """);

        Assert.False(declared.TryCountMachines(out _, out string? refusal));
        Assert.Contains("4", refusal!, StringComparison.Ordinal);
    }

    // ---- what a case is refused ------------------------------------------------------------

    /// <summary>
    /// A host polling machines its line does not carry is refused rather than left to hear silence.
    /// </summary>
    [Fact]
    public void A_host_whose_round_is_not_its_lines_round_is_refused()
    {
        TestOutcome result = Run("""
              machine channel 1 addresses 01-04
              machine channel 2 addresses 01-04 owns validation
              host connect loopback addresses 01-08 channel 1
            """);

        Assert.Equal(Verdict.Skip, result.Verdict);
        Assert.Contains("arrangement describes", result.SkipReason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A second host on a channel no machine opens is told so, and the channels there are named.
    /// </summary>
    [Fact]
    public void A_second_host_on_a_channel_the_floor_does_not_carry_is_refused()
    {
        TestOutcome result = Run("""
              machine channel 1 addresses 01-04
              machine channel 2 addresses 01-04 owns validation
              host connect loopback addresses 01-04 channel 1
              host expect link up within 5s
              host connect loopback addresses 01-04 channel 3
            """);

        Assert.Equal(Verdict.Skip, result.Verdict);
        Assert.Contains("channel 3", Reasons(result), StringComparison.Ordinal);
    }

    /// <summary>
    /// A stimulus step on a floor that has not named a machine is skipped, naming the round.
    /// </summary>
    /// <remarks>
    /// Never applied to the head of the round. That machine would take the note and every
    /// assertion after it would hold, so the case would pass having driven a machine it never
    /// named — and nothing in the report would look wrong.
    /// </remarks>
    [Fact]
    public void A_stimulus_with_no_machine_named_is_skipped_rather_than_driving_the_first()
    {
        TestOutcome result = Run("""
              machine channel 1 addresses 01-04
              machine channel 2 addresses 01-04 owns validation
              host connect loopback addresses 01-04 channel 1
              host expect link up within 5s
              egm bill insert 2000
            """);

        Assert.Equal(Verdict.Skip, result.Verdict);

        string reasons = Reasons(result);
        Assert.Contains("has not said which one", reasons, StringComparison.Ordinal);
        Assert.Contains("01-04", reasons, StringComparison.Ordinal);
    }

    /// <summary>
    /// An exception assertion on a floor with no machine named is refused for the same reason.
    /// </summary>
    /// <remarks>
    /// A general poll is answered with a bare exception code and no address in it, so without a
    /// named machine the assertion would be satisfied by any of a hundred machines raising that
    /// code — which is not what the case asked.
    /// </remarks>
    [Fact]
    public void An_exception_assertion_with_no_machine_named_is_refused()
    {
        TestOutcome result = Run("""
              machine channel 1 addresses 01-04
              machine channel 2 addresses 01-04 owns validation
              host connect loopback addresses 01-04 channel 1
              host expect link up within 5s
              host expect exception bill_accepted within 1s
            """);

        Assert.Equal(Verdict.Skip, result.Verdict);
        Assert.Contains("which one's exceptions", Reasons(result), StringComparison.Ordinal);
    }

    // ---- attribution ------------------------------------------------------------------------

    /// <summary>
    /// An exception raised by one machine does not answer an assertion about another.
    /// </summary>
    /// <remarks>
    /// The property the whole wave is for, and the one a floor can get wrong invisibly: the bill
    /// goes into $02, and the case asks $03 for the same exception. Before the machine was carried
    /// on the report this passed, because both machines are on one channel and the code matched.
    /// </remarks>
    [Fact]
    public void An_exception_from_one_machine_does_not_answer_an_assertion_about_another()
    {
        TestOutcome result = Run("""
              machine channel 1 addresses 01-04
              machine channel 2 addresses 01-04 owns validation
              host connect loopback addresses 01-04 channel 1
              host expect link up within 5s

              host machine 02
              egm bill insert 2000
              host expect exception $4A within 10s

              host machine 03
              host expect no exception $4A within 1s
            """);

        Assert.Equal(Verdict.Pass, result.Verdict);
    }

    /// <summary>
    /// Every exception the engine reports on a round says which machine raised it.
    /// </summary>
    [Fact]
    public void Every_exception_on_a_round_carries_its_machine()
    {
        using SoftwareFloor floor = SoftwareFloor.Of(4);
        var reported = new List<Core.Polling.ExceptionReport>();

        using var engine = new Core.Polling.PollEngine(floor.Host, new Core.Polling.PollEngineOptions
        {
            Address = floor.Addresses[0],
            Addresses = floor.Addresses,
            PollInterval = TimeSpan.Zero,
            QuietGap = TimeSpan.FromMilliseconds(15),
        });

        engine.ExceptionReported += report =>
        {
            lock (reported)
                reported.Add(report);
        };

        engine.Start();
        floor.Machines[2].InsertBill(2000);

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            lock (reported)
            {
                if (reported.Count > 0)
                    break;
            }

            Thread.Sleep(10);
        }

        engine.Stop();

        lock (reported)
        {
            Assert.NotEmpty(reported);
            Assert.All(reported, report => Assert.NotNull(report.Address));
            Assert.Contains(reported, report => report.Address == floor.Addresses[2]);
        }
    }

    private static MachineChannelDeclaration Declare(string body)
    {
        ScriptDocument document = ScriptParser.Parse(
            $"test \"declaration\" {{\n{body}\nhost connect loopback address 01\n}}\n", "floor.sas");
        return document.Tests[0].Channels!;
    }
}
