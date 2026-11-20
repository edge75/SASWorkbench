namespace SasWorkbench.Core.Protocol;

public static partial class SasLayouts
{
    // Properties, because field initialisers across partial files run in no promised order.

    // Only a reply this long carries the receipt status: $C0 and $C1 answer with the position and
    // the status alone (lp.cpp, the finalizePacket jump in lp72_TransferFunds).
    private static SasCondition HasReceiptStatus =>
        new("length", SasComparison.GreaterThan, 2);

    // An interrogation with nothing in the history answers position, status and receipt status
    // and omits the rest; every other answer carries the whole record.
    private static SasCondition HasRecord => new("length", SasComparison.GreaterThan, 3);

    // A request of three bytes or fewer is an interrogation or a cancel and carries no transfer.
    private static SasCondition CarriesTransfer =>
        new("length", SasComparison.GreaterThan, 2);

    /// <summary>Long poll $72, which moves money and reads back what a transfer did.</summary>
    /// <remarks>
    /// Read from <c>lp72_TransferFunds</c> and unconfirmed until a real link carries a transfer, with the
    /// field names the chapter 16 cases were written with.
    /// </remarks>
    private static SasMessageLayout TransferFunds() => new()
    {
        Command = 0x72,
        Name = "transfer_funds",
        Description = "Moves money to or from the machine, cancels a transfer, or reads one back.",
        Request =
        [
            Byte("length", SasUnit.Count, "Bytes following, not counting the check sequence."),
            Byte("transfer_code", SasUnit.Code,
                "$00 full, $01 partial allowed, $80 cancel, $FE and $FF read a transaction."),
            Byte("transaction_index", SasUnit.Index,
                "Zero for the current transaction, or a position in the history to read.")
                with { PresentWhen = new SasCondition("length", SasComparison.AtLeast, 2) },
            Byte("transfer_type", SasUnit.Code, "Which way the money moves, and what kind it is.")
                with { PresentWhen = CarriesTransfer },
            Bcd("cashable_amount", 5, SasUnit.Cents, "Cashable amount to move.")
                with { PresentWhen = CarriesTransfer },
            Bcd("restricted_amount", 5, SasUnit.Cents, "Restricted amount to move.")
                with { PresentWhen = CarriesTransfer },
            Bcd("nonrestricted_amount", 5, SasUnit.Cents, "Non-restricted amount to move.")
                with { PresentWhen = CarriesTransfer },
            Byte("transfer_flags", SasUnit.Code,
                "Host cashout control, lock required, receipt wanted and the rest.")
                with { PresentWhen = CarriesTransfer },
            LittleEndianNumber("asset_number", 4, "The machine the host means.")
                with { PresentWhen = CarriesTransfer },
            new()
            {
                Name = "registration_key",
                Length = 20,
                Encoding = SasFieldEncoding.Raw,
                Description = "The key a debit transfer is authorised with; zeros otherwise.",
                PresentWhen = CarriesTransfer,
            },
            Byte("transaction_id_length", SasUnit.Count, "How long the transaction identifier is.")
                with { PresentWhen = CarriesTransfer },
            TextOfLength("transaction_id", "transaction_id_length",
                "The host's identifier for this transfer.") with { PresentWhen = CarriesTransfer },
            Bcd("expiration", 4, SasUnit.None,
                "When restricted credits expire, as MMDDYYYY or a number of days.")
                with { PresentWhen = CarriesTransfer },
            LittleEndianNumber("pool_id", 2, "Which restricted pool the credits belong to.")
                with { PresentWhen = CarriesTransfer },
            Byte("receipt_data_length", SasUnit.Count, "How many bytes of receipt data follow.")
                with { PresentWhen = CarriesTransfer },
            new()
            {
                Name = "receipt_data",
                Length = 0,
                LengthFromField = "receipt_data_length",
                Encoding = SasFieldEncoding.Raw,
                Description = "Receipt fields, each a code, a length and its bytes.",
                PresentWhen = CarriesTransfer,
            },
            Bcd("lock_timeout", 2, SasUnit.None, "How long a lock may last, in hundredths of a second.")
                with { PresentWhen = CarriesTransfer },
        ],
        Response =
        [
            Byte("length", SasUnit.Count, "Bytes following, not counting the check sequence."),
            Byte("transaction_buffer_position", SasUnit.Index,
                "Where the transaction sits in the history; zero while it has no place there."),
            Byte("transfer_status", SasUnit.Code,
                "$00 whole, $01 part, $40 pending, $80 to $9F failed, $C0 and $C1 refused, $FF none."),
            Byte("receipt_status", SasUnit.Code, "Whether a receipt was printed, is pending, or none.")
                with { PresentWhen = HasReceiptStatus },
            Byte("transfer_type", SasUnit.Code, "Which way the money moved, and what kind it was.")
                with { PresentWhen = HasRecord },
            Bcd("cashable_amount", 5, SasUnit.Cents,
                "Asked for while pending, moved once final, zero when it failed.")
                with { PresentWhen = HasRecord },
            Bcd("restricted_amount", 5, SasUnit.Cents, "As the cashable amount, for restricted credits.")
                with { PresentWhen = HasRecord },
            Bcd("nonrestricted_amount", 5, SasUnit.Cents,
                "As the cashable amount, for non-restricted credits.")
                with { PresentWhen = HasRecord },
            Byte("transfer_flags", SasUnit.Code,
                "The request's flags, with the host cashout bits as the machine holds them.")
                with { PresentWhen = HasRecord },
            LittleEndianNumber("asset_number", 4, "The number this machine is registered under.")
                with { PresentWhen = HasRecord },
            Byte("transaction_id_length", SasUnit.Count, "How long the transaction identifier is.")
                with { PresentWhen = HasRecord },
            TextOfLength("transaction_id", "transaction_id_length",
                "The host's identifier, sent back byte for byte.") with { PresentWhen = HasRecord },
            Bcd("transaction_date", 4, SasUnit.None, "MMDDYYYY the transfer completed; zero while pending.")
                with { PresentWhen = HasRecord },
            Bcd("transaction_time", 3, SasUnit.None, "HHMMSS the transfer completed; zero while pending.")
                with { PresentWhen = HasRecord },
            Bcd("expiration", 4, SasUnit.None, "When restricted credits expire.")
                with { PresentWhen = HasRecord },
            LittleEndianNumber("pool_id", 2, "Which restricted pool.") with { PresentWhen = HasRecord },
            Byte("cashable_meter_size", SasUnit.Count, "Bytes in the cashable meter; zero while pending.")
                with { PresentWhen = HasRecord },
            BcdOfLength("cashable_meter", "cashable_meter_size",
                "The machine's cumulative cashable meter for this kind of transfer, in cents."),
            Byte("restricted_meter_size", SasUnit.Count, "Bytes in the restricted meter; zero while pending.")
                with { PresentWhen = HasRecord },
            BcdOfLength("restricted_meter", "restricted_meter_size",
                "The machine's cumulative restricted meter for this kind of transfer, in cents."),
            Byte("nonrestricted_meter_size", SasUnit.Count,
                "Bytes in the non-restricted meter; zero while pending.")
                with { PresentWhen = HasRecord },
            BcdOfLength("nonrestricted_meter", "nonrestricted_meter_size",
                "The machine's cumulative non-restricted meter for this kind of transfer, in cents."),
        ],
        DerivedFrom = "lp72_TransferFunds",
    };

    private static SasField TextOfLength(string name, string lengthField, string description) => new()
    {
        Name = name,
        Length = 0,
        LengthFromField = lengthField,
        Encoding = SasFieldEncoding.Ascii,
        ByteOrder = SasByteOrder.NotApplicable,
        Description = description,
    };

    private static SasField BcdOfLength(string name, string lengthField, string description) =>
        Bcd(name, 0, SasUnit.Cents, description) with
        {
            LengthFromField = lengthField,
            PresentWhen = HasRecord,
        };
}
