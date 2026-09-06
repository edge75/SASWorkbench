using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Tickets;

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

    [Fact]
    public void ATicketCanBeWrittenIntoTheStepItself()
    {
        ScriptTest test = Parse(
            "  egm ticket insert amount=2000 validation=1234567890123456 type=cashable_from_cashout");

        InsertTicketStep step = test.Steps.OfType<InsertTicketStep>().Single();
        Assert.Equal(2000, step.Ticket.AmountCents);
        Assert.Equal(1234567890123456ul, step.Ticket.ValidationNumber);
        Assert.Equal(TicketValidationType.CashableFromCashout, step.Ticket.ValidationType);
        Assert.Null(step.File);
    }

    // Both matter and neither has a sensible default: a ticket with no number cannot be redeemed,
    // and a ticket worth nothing is refused, so a script missing either would be testing a
    // refusal it did not mean to write.
    [Theory]
    [InlineData("  egm ticket insert amount=2000")]
    [InlineData("  egm ticket insert validation=1234")]
    public void AnInlineTicketNeedsBothAnAmountAndANumber(string line)
    {
        ScriptParseException problem = Assert.Throws<ScriptParseException>(() => Parse(line));

        Assert.Contains("validation", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATicketTypeNothingRecognisesIsAParseError()
    {
        ScriptParseException problem = Assert.Throws<ScriptParseException>(
            () => Parse("  egm ticket insert amount=1 validation=2 type=promotional"));

        Assert.Contains("promotional", problem.Message, StringComparison.Ordinal);
        Assert.Contains("cashable_from_cashout", problem.Message, StringComparison.Ordinal);
    }

    // Read while the script is read, so a path that is wrong is a mistake in the script rather
    // than a run that gets most of the way through and then cannot find a file.
    [Fact]
    public void ATicketNamedByFileIsReadDuringTheParse()
    {
        string path = Path.Combine(Path.GetTempPath(), "saswb-insert-" + Guid.NewGuid().ToString("N") + ".json");
        TicketFile.Write(path, new Ticket { ValidationNumber = 4242, AmountCents = 500 });

        try
        {
            ScriptTest test = Parse($"  egm ticket insert {path}");

            InsertTicketStep step = test.Steps.OfType<InsertTicketStep>().Single();
            Assert.Equal(4242ul, step.Ticket.ValidationNumber);
            Assert.Equal(path, step.File);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ATicketFileThatIsNotThereIsAParseError()
    {
        ScriptParseException problem = Assert.Throws<ScriptParseException>(
            () => Parse("  egm ticket insert nowhere/at/all.json"));

        Assert.Contains("nowhere/at/all.json", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InsertingATicketNeedsSomethingToDriveTheMachine()
    {
        ScriptTest test = Parse("  egm ticket insert amount=1 validation=2");

        Assert.Equal("stimulus.ticket",
            test.Steps.OfType<InsertTicketStep>().Single().RequiredCapability);
    }

    /// <summary>
    /// A reply compared against the paper. The machine numbers its own tickets, so this is the
    /// only form of the comparison that can say the host was told about the ticket that was
    /// printed rather than about a number that merely looks right.
    /// </summary>
    [Fact]
    public void AWireFieldCanBeComparedAgainstThePrintedTicket()
    {
        ScriptTest test = Parse("""
              host send lp 3D
              host expect field validation_number of lp 3D == ticket.validation_number
            """);

        ExpectFieldStep step = test.Steps.OfType<ExpectFieldStep>().Single();
        Assert.Equal("validation_number", step.ExpectedTicketField);
        Assert.Null(step.ExpectedNumber);
        Assert.Null(step.ExpectedText);
        Assert.Contains("ticket.validation_number", step.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void AReferenceToATicketFieldThatDoesNotExistIsAParseError()
    {
        ScriptParseException problem = Assert.Throws<ScriptParseException>(() => Parse(
            "  host expect field ticket_amount of lp 3D == ticket.amount_in_pennies"));

        Assert.Contains("amount_in_pennies", problem.Message, StringComparison.Ordinal);
        Assert.Contains("amount_cents", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Refused rather than compared as text. A printed date and a BCD reply are not the same kind
    /// of thing, and a comparison that always failed would look like a machine defect.
    /// </summary>
    [Fact]
    public void AReferenceToATicketFieldThatIsNotANumberIsAParseError()
    {
        ScriptParseException problem = Assert.Throws<ScriptParseException>(() => Parse(
            "  host expect field ticket_amount of lp 3D == ticket.expiration"));

        Assert.Contains("ticket.expiration", problem.Message, StringComparison.Ordinal);
    }
}
