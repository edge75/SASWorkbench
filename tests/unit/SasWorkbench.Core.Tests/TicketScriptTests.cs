using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Core.Tests;

public class TicketScriptTests
{
    private static ScriptTest Parse(string body) =>
        ScriptParser.Parse("test \"t\" {\n  host connect loopback address 01\n" + body + "\n}\n").Tests[0];

    [Fact]
    public void APrintedTicketIsWaitedFor()
    {
        ScriptTest test = Parse("  egm expect ticket printed within 12s");

        ExpectTicketStep step = test.Steps.OfType<ExpectTicketStep>().Single();
        Assert.Equal(TimeSpan.FromSeconds(12), step.Within);
    }

    [Fact]
    public void ATicketFieldIsComparedAsANumberWhenTheFieldIsOne()
    {
        ScriptTest test = Parse("  egm expect ticket amount_cents == 2000");

        ExpectTicketFieldStep step = test.Steps.OfType<ExpectTicketFieldStep>().Single();
        Assert.Equal("amount_cents", step.Field);
        Assert.Equal(SasComparison.Equal, step.Comparison);
        Assert.Equal(2000, step.ExpectedNumber);
        Assert.Null(step.ExpectedText);
    }

    // A validation number is sixteen digits and is still a number; an expiration is a date and is
    // not. What decides is the field, not whether the literal happens to look numeric.
    [Fact]
    public void ADateIsComparedAsText()
    {
        ScriptTest test = Parse("  egm expect ticket expiration == 2026-12-31");

        ExpectTicketFieldStep step = test.Steps.OfType<ExpectTicketFieldStep>().Single();
        Assert.Equal("2026-12-31", step.ExpectedText);
        Assert.Null(step.ExpectedNumber);
    }

    [Fact]
    public void ASixteenDigitValidationNumberStaysANumber()
    {
        ScriptTest test = Parse("  egm expect ticket validation_number == 9876543210987654");

        ExpectTicketFieldStep step = test.Steps.OfType<ExpectTicketFieldStep>().Single();
        Assert.Equal(9876543210987654, step.ExpectedNumber);
    }

    // A field a ticket does not have is a mistake in the script, and it stops the parse rather than
    // failing halfway through a run against a machine.
    [Fact]
    public void AFieldATicketDoesNotHaveIsAParseError()
    {
        ScriptParseException problem = Assert.Throws<ScriptParseException>(
            () => Parse("  egm expect ticket serial_number == 4"));

        Assert.Contains("serial_number", problem.Message, StringComparison.Ordinal);
        Assert.Contains("amount_cents", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADurationIsRequiredWhenWaitingForATicket()
    {
        Assert.Throws<ScriptParseException>(() => Parse("  egm expect ticket printed"));
    }

    [Fact]
    public void AnOrderingComparisonOnTextIsRefused()
    {
        Assert.Throws<ScriptParseException>(() => Parse("  egm expect ticket location > x"));
    }
}
