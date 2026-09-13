using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Reporting;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// Naming a round in a script, and the machine on the record.
/// </summary>
/// <remarks>
/// Two halves of one problem. A hundred machines cannot be written on a hundred lines, and a run
/// of a hundred machines cannot be read back unless each message says which one it belongs to.
/// Neither half is worth anything alone: a script that can address a floor and a log that cannot
/// attribute it produces a pile of bytes with a good syntax.
/// </remarks>
public class RoundScriptTests
{
    private static ScriptTest Parse(string body) =>
        ScriptParser.Parse(
            $"test \"a round\" {{\n{body}\n}}\n", "round.sas").Tests[0];

    // ---- naming a round ----------------------------------------------------------------------

    /// <summary>A hundred machines, on one line.</summary>
    [Fact]
    public void ARangeNamesEveryMachineBetweenItsEnds()
    {
        ConnectStep connect = Parse(
            "  host connect loopback addresses 01-64").Steps.OfType<ConnectStep>().Single();

        Assert.Equal(100, connect.Round.Count);
        Assert.Equal(SasAddress.Of(1), connect.Round[0]);
        Assert.Equal(SasAddress.Of(100), connect.Round[^1]);

        // The address the host starts at is the first of the round, so everything that already
        // reads Address keeps working without knowing a round exists.
        Assert.Equal(SasAddress.Of(1), connect.Address);
    }

    [Fact]
    public void AListAndARangeCanBeMixed()
    {
        ConnectStep connect = Parse(
            "  host connect loopback addresses 01,05,09-0B").Steps.OfType<ConnectStep>().Single();

        Assert.Equal<byte>([1, 5, 9, 10, 11], connect.Round.Select(a => a.Value));
    }

    /// <summary>
    /// A round written the way the round prints is the round that was written.
    /// </summary>
    /// <remarks>
    /// The same rule the channel arrangement follows: what a report puts on the page can be pasted
    /// back into a script and reproduce the arrangement it describes.
    /// </remarks>
    [Fact]
    public void ARoundSurvivesBeingWrittenOutAndReadBackIn()
    {
        Assert.True(SasAddressRound.TryParse("01,05,09-0B,7F", out IReadOnlyList<SasAddress> round, out _));

        string written = SasAddressRound.Describe(round);
        Assert.Equal("01,05,09-0B,7F", written);

        Assert.True(SasAddressRound.TryParse(written, out IReadOnlyList<SasAddress> again, out _));
        Assert.Equal(round, again);
    }

    [Fact]
    public void AHostGivenOneAddressHasNoRound()
    {
        ConnectStep connect = Parse(
            "  host connect loopback address 03").Steps.OfType<ConnectStep>().Single();

        Assert.Empty(connect.Round);
        Assert.Equal(SasAddress.Of(3), connect.Address);
    }

    // ---- what a round refuses ----------------------------------------------------------------

    /// <summary>
    /// A machine named twice is refused rather than quietly polled once.
    /// </summary>
    /// <remarks>
    /// A round that names one machine twice polls it twice and costs more than its author thinks.
    /// Removing the repeat would be a decision about the arrangement taken on the author's behalf.
    /// </remarks>
    [Fact]
    public void ARoundThatNamesAMachineTwiceIsRefused()
    {
        ScriptParseException refused = Assert.Throws<ScriptParseException>(
            () => Parse("  host connect loopback addresses 01-03,02"));

        Assert.Contains("named twice", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARoundContainingTheSynchronisationAddressIsRefused()
    {
        ScriptParseException refused = Assert.Throws<ScriptParseException>(
            () => Parse("  host connect loopback addresses 00-03"));

        Assert.Contains("synchronisation address", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARangeWrittenBackwardsIsRefused()
    {
        ScriptParseException refused = Assert.Throws<ScriptParseException>(
            () => Parse("  host connect loopback addresses 08-02"));

        Assert.Contains("runs backwards", refused.Message, StringComparison.Ordinal);
    }

    // ---- naming the machine a step talks to ---------------------------------------------------

    [Fact]
    public void AStepCanNameItsMachine()
    {
        SendLongPollStep send = Parse(
            "  host connect loopback addresses 01-04\n  host send lp 1F to 03")
            .Steps.OfType<SendLongPollStep>().Single();

        Assert.Equal(SasAddress.Of(3), send.To);
        Assert.Equal("send lp 1F to 03", send.Describe());
    }

    /// <summary>Naming a machine works on the form that names fields too.</summary>
    [Fact]
    public void AStepWithNamedFieldsCanAlsoNameItsMachine()
    {
        SendLongPollStep send = Parse(
            "  host connect loopback addresses 01-04\n"
            + "  host send lp 0E data 00 to 02")
            .Steps.OfType<SendLongPollStep>().Single();

        Assert.Equal(SasAddress.Of(2), send.To);
        Assert.Equal<byte>([0x00], send.Data);
    }

    [Fact]
    public void TheCaseCanMoveToAMachineInsteadOfNamingItOnEveryStep()
    {
        ScriptTest test = Parse(
            "  host connect loopback addresses 01-04\n  host machine 02\n  host send lp 1F");

        UseMachineStep moved = test.Steps.OfType<UseMachineStep>().Single();

        Assert.Equal(SasAddress.Of(2), moved.Address);
        Assert.Null(test.Steps.OfType<SendLongPollStep>().Single().To);
    }

    [Fact]
    public void MovingToTheSynchronisationAddressIsRefused()
    {
        ScriptParseException refused = Assert.Throws<ScriptParseException>(
            () => Parse("  host connect loopback addresses 01-04\n  host machine 00"));

        Assert.Contains("synchronisation address", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SendingToTheSynchronisationAddressIsRefused()
    {
        ScriptParseException refused = Assert.Throws<ScriptParseException>(
            () => Parse("  host connect loopback addresses 01-04\n  host send lp 1F to 00"));

        Assert.Contains("no machine answers to it", refused.Message, StringComparison.Ordinal);
    }

    // ---- the machine on the record -------------------------------------------------------------

    /// <summary>
    /// Which machine a message belongs to survives into the record and back out.
    /// </summary>
    /// <remarks>
    /// The half without which naming a round buys nothing. A reply to a general poll is a bare
    /// exception byte with no address in it, so across a floor this field is the only thing that
    /// says who spoke.
    /// </remarks>
    [Fact]
    public void WhichMachineAMessageBelongsToSurvivesIntoTheRecordAndBackOut()
    {
        var report = new RunReport
        {
            Tests =
            [
                new TestOutcome
                {
                    Name = "a floor",
                    Location = SourceLocation.None,
                    Verdict = Verdict.Pass,
                    Wire =
                    [
                        new WireRecord(0, WireDirection.Transmit, SasChannel.One,
                            TimeSpan.Zero, [SasSymbol.Address(0x83)], "general poll")
                        {
                            Address = SasAddress.Of(3),
                        },

                        // A general poll's answer: one byte, an exception code, no address at all.
                        new WireRecord(1, WireDirection.Receive, SasChannel.One,
                            TimeSpan.FromMilliseconds(30), [SasSymbol.Data8(0x11)], null)
                        {
                            Address = SasAddress.Of(3),
                        },

                        // The synchronisation poll belongs to no machine, and must stay that way.
                        new WireRecord(2, WireDirection.Transmit, SasChannel.One,
                            TimeSpan.FromMilliseconds(40), [SasSymbol.Address(0x80)],
                            "sync poll - address zero"),
                    ],
                },
            ],
        };

        JsonLinesReader.Result read = JsonLinesReader.Read(
            new JsonLinesReportWriter().Render(report).Split('\n'));

        Assert.Empty(read.Problems);
        IReadOnlyList<WireRecord> wire = read.Report.Tests[0].Wire;

        Assert.Equal(SasAddress.Of(3), wire[0].Address);
        Assert.Equal(SasAddress.Of(3), wire[1].Address);
        Assert.Null(wire[2].Address);
    }

    /// <summary>
    /// A record that names no machine is read as naming none, never as naming the first.
    /// </summary>
    /// <remarks>
    /// An import and a run written by an older build both look like this. Filling the field in
    /// would attribute a whole floor's traffic to whichever machine happened to be first, which is
    /// worse than saying nothing.
    /// </remarks>
    [Fact]
    public void ARecordWithNoMachineIsNotReadAsTheFirstMachine()
    {
        string[] lines =
        [
            """{"kind":"test","name":"t","file":"t.sas","line":1,"verdict":"Pass","durationMs":1}""",
            """{"kind":"wire","test":"t","sequence":0,"direction":"Receive","channel":1,"atMs":1,"symbols":"0.11"}""",
        ];

        WireRecord record = Assert.Single(JsonLinesReader.Read(lines).Report.Tests[0].Wire);

        Assert.Null(record.Address);
    }

    /// <summary>An address SAS does not define is reported, never read as a machine.</summary>
    [Fact]
    public void AnAddressSasDoesNotDefineIsReported()
    {
        string[] lines =
        [
            """{"kind":"test","name":"t","file":"t.sas","line":1,"verdict":"Pass","durationMs":1}""",
            """{"kind":"wire","test":"t","sequence":0,"direction":"Receive","channel":1,"atMs":1,"symbols":"0.11","address":200}""",
        ];

        JsonLinesReader.Result read = JsonLinesReader.Read(lines);

        Assert.Contains(read.Problems, p => p.Contains("which SAS does not", StringComparison.Ordinal));
        Assert.Null(read.Report.Tests[0].Wire[0].Address);
    }
}
