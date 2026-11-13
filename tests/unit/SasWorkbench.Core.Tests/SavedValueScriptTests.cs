using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// A value one reply carried, kept under a name and compared later with another reply. This is
/// what lets a script say "both polls report the same meter" or "the per-denomination gains add
/// up to the machine's own", which a literal never can: the script's author does not know the
/// numbers, only how they must relate.
/// </summary>
public class SavedValueScriptTests
{
    private static ScriptTest Parse(string body) =>
        ScriptParser.Parse("test \"t\" {\n  host connect loopback address 01\n" + body + "\n}\n").Tests[0];

    [Fact]
    public void AFieldIsRememberedFromTheLastReplyUnderAName()
    {
        ScriptTest test = Parse("""
              host send lp 6F data 0400000500
              host remember field meters[0].value as games_before
            """);

        RememberFieldStep step = test.Steps.OfType<RememberFieldStep>().Single();
        Assert.Equal(0x6F, step.Command);
        Assert.Equal("meters[0].value", step.Field);
        Assert.Equal("games_before", step.Name);
    }

    [Fact]
    public void AFieldCanBeRememberedFromANamedPoll()
    {
        ScriptTest test = Parse("  host remember field game_id of lp 1F as id");

        RememberFieldStep step = test.Steps.OfType<RememberFieldStep>().Single();
        Assert.Equal(0x1F, step.Command);
    }

    [Fact]
    public void AReplyIsComparedWithARememberedValue()
    {
        ScriptTest test = Parse("""
              host send lp 6F data 0400000100
              host remember field meters[0].value as coin_out
              host send lp AF data 0400000100
              host expect field meters[0].value == saved.coin_out
            """);

        ExpectFieldStep step = test.Steps.OfType<ExpectFieldStep>().Single();
        Assert.NotNull(step.ExpectedSum);
        Assert.Null(step.ExpectedNumber);
        Assert.Equal("saved.coin_out", step.ExpectedSum!.ToString());
        Assert.Contains("saved.coin_out", step.Describe(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Sums and differences of remembered values and numbers: a gain is "after minus before", and
    /// a total is the sum of its parts, so these two operators are all the arithmetic the GSA
    /// meter cases ask for.
    /// </summary>
    [Fact]
    public void ARememberedValueCanBeAddedToAndSubtractedFrom()
    {
        ScriptTest test = Parse("""
              host send lp 6F data 0400000500
              host remember field meters[0].value as a
              host remember field meters[0].value as b
              host expect field meters[0].value >= saved.a + saved.b - 3
            """);

        SavedSum sum = test.Steps.OfType<ExpectFieldStep>().Single().ExpectedSum!;
        Assert.Equal(3, sum.Terms.Count);
        Assert.Equal(new SavedTerm(+1, null, "a"), sum.Terms[0]);
        Assert.Equal(new SavedTerm(+1, null, "b"), sum.Terms[1]);
        Assert.Equal(new SavedTerm(-1, 3, null), sum.Terms[2]);
        Assert.Equal("saved.a + saved.b - 3", sum.ToString());
    }

    [Fact]
    public void OperatorsNeedNoSpacesAroundThem()
    {
        ScriptTest test = Parse("""
              host send lp 6F data 0400000500
              host remember field meters[0].value as a
              host expect field meters[0].value == saved.a+10
            """);

        SavedSum sum = test.Steps.OfType<ExpectFieldStep>().Single().ExpectedSum!;
        Assert.Equal(new SavedTerm(+1, 10, null), sum.Terms[1]);
    }

    /// <summary>
    /// A name nothing earlier in the case remembered is a mistake in the script, found when the
    /// script is read rather than halfway through a run on a board.
    /// </summary>
    [Fact]
    public void ANameNothingRememberedIsAParseError()
    {
        ScriptParseException refused = Assert.Throws<ScriptParseException>(() => Parse("""
              host send lp 6F data 0400000500
              host expect field meters[0].value == saved.never
            """));

        Assert.Contains("saved.never", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANameMayBeRememberedAgainAndTheLaterValueWins()
    {
        ScriptTest test = Parse("""
              host send lp 6F data 0400000500
              host remember field meters[0].value as v
              host remember field meters[0].value as v
            """);

        Assert.Equal(2, test.Steps.OfType<RememberFieldStep>().Count());
    }

    [Fact]
    public void AFieldTheReplyDoesNotHaveIsAParseError()
    {
        Assert.Throws<ScriptParseException>(() => Parse("""
              host send lp 1F
              host remember field no_such_field as x
            """));
    }

    [Theory]
    [InlineData("  host remember field game_id of lp 1F")]
    [InlineData("  host remember field game_id of lp 1F as")]
    [InlineData("  host remember field game_id of lp 1F as 9lives")]
    [InlineData("  host remember field game_id of lp 1F as a b")]
    public void ARememberNeedsOneName(string line)
    {
        Assert.Throws<ScriptParseException>(() => Parse(line));
    }

    [Theory]
    [InlineData("saved.a +")]
    [InlineData("saved.a + + 2")]
    [InlineData("saved.a * 2")]
    [InlineData("saved.a + banana")]
    public void AMalformedSumIsAParseError(string expression)
    {
        Assert.Throws<ScriptParseException>(() => Parse($"""
              host send lp 6F data 0400000500
              host remember field meters[0].value as a
              host expect field meters[0].value == {expression}
            """));
    }

    /// <summary>
    /// The new comparisons are claimed by the baseline, so the cases that declared they need them
    /// run instead of skipping.
    /// </summary>
    [Theory]
    [InlineData("host.compare_replies")]
    [InlineData("host.meter_arithmetic")]
    public void TheComparisonsAreAvailableEverywhere(string capability)
    {
        Assert.Equal("yes", CapabilityProbe.Baseline().Value(capability));
    }
}
