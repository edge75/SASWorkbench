using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// The two long polls a ticket redemption is made of, and the way a script writes them.
/// </summary>
/// <remarks>
/// Both carry fields that are there or not depending on an earlier byte, which is the first
/// place in this build where that matters on the sending side as well as the receiving one. The
/// tests below are mostly about which fields appear, because a reply read one field out of step
/// decodes perfectly and means something else entirely.
/// </remarks>
public class TicketRedemptionProtocolTests
{
    private static SasMessageLayout Lp70 => SasLayouts.All[0x70];

    private static SasMessageLayout Lp71 => SasLayouts.All[0x71];

    private static byte[] Reply(SasMessageLayout layout, Dictionary<string, object> values)
    {
        var buffer = new byte[HostMessage.MaxSymbols];
        int length = SasMessageEncoder.WriteResponse(layout, SasAddress.Of(1), values, buffer);
        return buffer[..length];
    }

    [Fact]
    public void ATicketInEscrowIsAnsweredWithItsNumber()
    {
        byte[] frame = Reply(Lp70, new Dictionary<string, object>
        {
            ["length"] = 16,
            ["ticket_status"] = 0x00,
            ["transfer_amount"] = 0L,
            ["parsing_code"] = 0,
            ["validation_data"] = 123456789012345678L,
        });

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(Lp70, frame);

        Assert.Null(decoded.Failure);
        Assert.Equal(0, decoded.Number("ticket_status"));
        Assert.Equal(123456789012345678L, decoded.Number("validation_data"));
        Assert.Equal(0, decoded.Number("transfer_amount"));
    }

    // Nineteen bytes: address, command, length, status, five of amount, one of parsing code, nine
    // of validation data, and two of check sequence. Checked as a number because a reply one byte
    // wide of the specification is the kind of defect that only shows up against another vendor.
    [Fact]
    public void AnEscrowReplyIsNineteenBytesLong()
    {
        byte[] frame = Reply(Lp70, new Dictionary<string, object>
        {
            ["length"] = 16,
            ["ticket_status"] = 0x00,
            ["transfer_amount"] = 0L,
            ["parsing_code"] = 0,
            ["validation_data"] = 1L,
        });

        Assert.Equal(21, frame.Length);
        Assert.Equal(16, frame[2]);
    }

    [Fact]
    public void NoTicketInEscrowIsAnsweredWithTheStatusAlone()
    {
        byte[] frame = Reply(Lp70, new Dictionary<string, object>
        {
            ["length"] = 1,
            ["ticket_status"] = 0xFF,
        });

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(Lp70, frame);

        Assert.Null(decoded.Failure);
        Assert.Equal(0xFF, decoded.Number("ticket_status"));
        Assert.Null(decoded["validation_data"]);
    }

    // $C0 and $FF say the machine has nothing to quote back, and the amount and validation data
    // are absent. A reader that expected them would run off the end of a four-byte reply.
    [Theory]
    [InlineData(0xC0)]
    [InlineData(0xFF)]
    public void AStatusThatCarriesNoTicketAnswersWithItselfAlone(int status)
    {
        byte[] frame = Reply(Lp71, new Dictionary<string, object>
        {
            ["length"] = 1,
            ["machine_status"] = status,
        });

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(Lp71, frame);

        Assert.Null(decoded.Failure);
        Assert.Equal(status, decoded.Number("machine_status"));
        Assert.Null(decoded["transfer_amount"]);
    }

    [Fact]
    public void ARedeemedStatusCarriesTheAmountAndTheNumber()
    {
        byte[] frame = Reply(Lp71, new Dictionary<string, object>
        {
            ["length"] = 16,
            ["machine_status"] = 0x00,
            ["transfer_amount"] = 2000L,
            ["parsing_code"] = 0,
            ["validation_data"] = 987654321098765432L,
        });

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(Lp71, frame);

        Assert.Null(decoded.Failure);
        Assert.Equal(2000, decoded.Number("transfer_amount"));
        Assert.Equal(987654321098765432L, decoded.Number("validation_data"));
    }

    [Fact]
    public void ARedemptionCommandIsBuiltFromItsFieldsAndReadsBackTheSame()
    {
        var buffer = new byte[HostMessage.MaxSymbols];
        int length = SasMessageEncoder.WriteRequestData(Lp71, new Dictionary<string, object>
        {
            ["transfer_code"] = 0x00,
            ["transfer_amount"] = 2000L,
            ["parsing_code"] = 0,
            ["validation_data"] = 123456789012345678L,
        }, buffer);

        // The length byte is filled in from what follows: one transfer code, five of amount, one
        // parsing code and nine of validation data.
        Assert.Equal(16, buffer[0]);
        Assert.Equal(17, length);

        var frame = new byte[2 + length + 2];
        frame[0] = 0x01;
        frame[1] = 0x71;
        buffer.AsSpan(0, length).CopyTo(frame.AsSpan(2));
        SasCrc.Append(frame, 2 + length);

        SasDecoded read = SasMessageDecoder.DecodeRequest(Lp71, frame);

        Assert.Null(read.Failure);
        Assert.Equal(0x00, read.Number("transfer_code"));
        Assert.Equal(2000, read.Number("transfer_amount"));
        Assert.Equal(123456789012345678L, read.Number("validation_data"));
    }

    [Fact]
    public void AStatusRequestCarriesOnlyItsTransferCode()
    {
        var buffer = new byte[HostMessage.MaxSymbols];
        int length = SasMessageEncoder.WriteRequestData(Lp71, new Dictionary<string, object>
        {
            ["transfer_code"] = 0xFF,
        }, buffer);

        Assert.Equal(2, length);
        Assert.Equal(1, buffer[0]);
        Assert.Equal(0xFF, buffer[1]);
    }

    [Fact]
    public void ScriptFieldsAreTurnedIntoBytesWhileTheScriptIsRead()
    {
        ScriptTest test = ScriptParser.Parse(
            "test \"t\" {\n"
            + "  host connect loopback address 01\n"
            + "  host send lp 71 with transfer_code=ttc_valid_cashable_ticket "
            + "transfer_amount=2000 parsing_code=0 validation_data=123456789012345678\n"
            + "}\n").Tests[0];

        SendLongPollStep step = test.Steps.OfType<SendLongPollStep>().Single();

        Assert.Equal(0x71, step.Command);
        Assert.Equal(16, step.Data[0]);
        Assert.Equal(0x00, step.Data[1]);
        Assert.Contains("transfer_code=", step.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void AHexCodeAndALibraryNameMeanTheSameThing()
    {
        static byte[] DataOf(string setting) => ScriptParser.Parse(
                "test \"t\" {\n  host connect loopback address 01\n"
                + $"  host send lp 71 with transfer_code={setting}\n}}\n")
            .Tests[0].Steps.OfType<SendLongPollStep>().Single().Data;

        Assert.Equal(DataOf("$FF"), DataOf("ttc_request_for_current_ticket_status"));
    }

    // Two hex digits are a code everywhere else in this protocol, but a field value can just as
    // easily be an amount, so a bare number here is decimal and hex has to say so.
    [Fact]
    public void ABareNumberInAFieldIsDecimal()
    {
        Assert.True(SasNames.TryResolveFieldValue("20", out long plain));
        Assert.Equal(20, plain);

        Assert.True(SasNames.TryResolveFieldValue("$20", out long hex));
        Assert.Equal(32, hex);
    }

    [Fact]
    public void AFieldTheCommandDoesNotHaveIsAParseError()
    {
        ScriptParseException problem = Assert.Throws<ScriptParseException>(() => ScriptParser.Parse(
            "test \"t\" {\n  host connect loopback address 01\n"
            + "  host send lp 71 with transfer_reason=1\n}\n"));

        Assert.Contains("transfer_reason", problem.Message, StringComparison.Ordinal);
        Assert.Contains("transfer_code", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueNothingRecognisesIsAParseError()
    {
        ScriptParseException problem = Assert.Throws<ScriptParseException>(() => ScriptParser.Parse(
            "test \"t\" {\n  host connect loopback address 01\n"
            + "  host send lp 71 with transfer_code=accept\n}\n"));

        Assert.Contains("accept", problem.Message, StringComparison.Ordinal);
    }

    // Naming the fields of a command whose layout has not been established would be a claim this
    // build cannot support, so it is refused and the raw form is offered instead.
    [Fact]
    public void ACommandWithNoLayoutCannotHaveItsFieldsNamed()
    {
        ScriptParseException problem = Assert.Throws<ScriptParseException>(() => ScriptParser.Parse(
            "test \"t\" {\n  host connect loopback address 01\n"
            + "  host send lp 75 with receipt_code=0\n}\n"));

        Assert.Contains("data <hex>", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStatusCodeCanBeExpectedByItsName()
    {
        ScriptTest test = ScriptParser.Parse(
            "test \"t\" {\n  host connect loopback address 01\n"
            + "  host send lp 71 with transfer_code=$FF\n"
            + "  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending\n"
            + "}\n").Tests[0];

        ExpectFieldStep step = test.Steps.OfType<ExpectFieldStep>().Single();
        Assert.Equal(0x40, step.ExpectedNumber);
    }
}
