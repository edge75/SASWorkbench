using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// Long poll $72's reply, in the three shapes the machine library's handler sends it: the two-byte
/// refusal, the three-byte answer to an interrogation with no history, and the whole record. The
/// byte strings are assembled by hand from lp72_TransferFunds's own offsets, not by this build's
/// encoder, so a layout that drifted from the handler would fail here.
/// </summary>
public class TransferFundsLayoutTests
{
    private static readonly SasMessageLayout Layout = SasLayouts.For(0x72)!;

    /// <summary>Address, command, payload and a two-byte check sequence the decoder ignores.</summary>
    private static byte[] Reply(string payloadHex) =>
        [0x01, 0x72, .. Convert.FromHexString(payloadHex), 0x00, 0x00];

    [Fact]
    public void An_unsupported_transfer_code_is_answered_with_position_and_status_alone()
    {
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(Layout, Reply("0200C1"));

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(2, decoded.Number("length"));
        Assert.Equal(0, decoded.Number("transaction_buffer_position"));
        Assert.Equal(0xC1, decoded.Number("transfer_status"));
        Assert.Null(decoded["receipt_status"]);
        Assert.Null(decoded["transfer_type"]);
    }

    [Fact]
    public void An_interrogation_with_no_history_carries_no_record()
    {
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(Layout, Reply("0300FFFF"));

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0xFF, decoded.Number("transfer_status"));
        Assert.Equal(0xFF, decoded.Number("receipt_status"));
        Assert.Null(decoded["cashable_amount"]);
    }

    /// <summary>
    /// A completed five-dollar transfer in, identifier "2", with a five-byte cumulative cashable
    /// meter and four-byte restricted and non-restricted ones.
    /// </summary>
    [Fact]
    public void A_completed_transfer_reads_field_by_field()
    {
        string payload =
            "37"            // length
            + "01" + "00" + "FF" + "00"        // position, status, receipt status, type
            + "0000000500" + "0000000000" + "0000000000"
            + "01"          // flags
            + "39300000"    // asset number 12345, least significant first
            + "01" + "32"   // identifier "2"
            + "11202026" + "153000" // date and time
            + "00000000" + "0000"   // expiration, pool
            + "05" + "0000001500"
            + "04" + "00000000"
            + "04" + "00000000";

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(Layout, Reply(payload));

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Null(decoded["unmodelled_trailer"]);
        Assert.Equal(1, decoded.Number("transaction_buffer_position"));
        Assert.Equal(500, decoded.Number("cashable_amount"));
        Assert.Equal(12345, decoded.Number("asset_number"));
        Assert.Equal("2", decoded["transaction_id"]!.Text);
        Assert.Equal(11202026, decoded.Number("transaction_date"));
        Assert.Equal(153000, decoded.Number("transaction_time"));
        Assert.Equal(5, decoded.Number("cashable_meter_size"));
        Assert.Equal(1500, decoded.Number("cashable_meter"));
        Assert.Equal(4, decoded.Number("nonrestricted_meter_size"));
    }

    /// <summary>A pending reply's meters carry a size of zero and no digits.</summary>
    [Fact]
    public void A_pending_transfer_reports_meters_of_no_width()
    {
        string payload =
            "2A" + "00" + "40" + "FF" + "00"
            + "0000000500" + "0000000000" + "0000000000"
            + "00" + "39300000" + "01" + "32"
            + "00000000" + "000000" + "00000000" + "0000"
            + "00" + "00" + "00";

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(Layout, Reply(payload));

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0x2A, decoded.Number("length"));
        Assert.Equal(0x40, decoded.Number("transfer_status"));
        Assert.Equal(0, decoded.Number("transaction_date"));
        Assert.Equal(0, decoded.Number("cashable_meter_size"));
        Assert.Null(decoded["unmodelled_trailer"]);
    }

    [Fact]
    public void A_transfer_request_decodes_up_to_its_lock_timeout()
    {
        byte[] frame =
        [
            0x01, 0x72,
            .. Convert.FromHexString(
                "36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000132000000000000000000"),
            0x00, 0x00,
        ];

        SasDecoded decoded = SasMessageDecoder.DecodeRequest(Layout, frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Null(decoded["unmodelled_trailer"]);
        Assert.Equal(500, decoded.Number("cashable_amount"));
        Assert.Equal(12345, decoded.Number("asset_number"));
        Assert.Equal("2", decoded["transaction_id"]!.Text);
        Assert.Equal(0, decoded.Number("lock_timeout"));
    }

    [Fact]
    public void An_interrogation_request_carries_no_transfer()
    {
        byte[] frame = [0x01, 0x72, 0x02, 0xFF, 0x00, 0x00, 0x00];

        SasDecoded decoded = SasMessageDecoder.DecodeRequest(Layout, frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0xFF, decoded.Number("transfer_code"));
        Assert.Equal(0, decoded.Number("transaction_index"));
        Assert.Null(decoded["transfer_type"]);
    }
}
