using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SasWorkbench.Egm;

/// <summary>What a transfer request's receipt data carried, field by field, as the library reads it.</summary>
/// <param name="SourceDestination">Field $00, where the money came from or went.</param>
/// <param name="DateAndTime">Field $01, seven bytes of BCD, or null when the host sent none.</param>
/// <param name="PatronName">Field $10.</param>
/// <param name="PatronAccount">Field $11, which an in-house receipt cannot do without.</param>
/// <param name="AccountBalance">Field $13, in cents, or null.</param>
/// <param name="DebitCardNumber">Field $41, the last four digits; zero when absent, as the library tests it.</param>
/// <param name="TransactionFee">Field $42, in cents, or null.</param>
/// <param name="TotalDebit">Field $43, in cents, or null.</param>
internal sealed record AftReceiptData(
    string SourceDestination,
    byte[]? DateAndTime,
    string PatronName,
    string PatronAccount,
    long? AccountBalance,
    long DebitCardNumber,
    long? TransactionFee,
    long? TotalDebit)
{
    public static AftReceiptData None { get; } = new(string.Empty, null, string.Empty, string.Empty, null, 0, null, null);

    /// <summary>True when the account's first byte is not zero, which is the library's test (lp.cpp:5407).</summary>
    public bool HasPatronAccount => PatronAccount.Length > 0 && PatronAccount[0] != '\0';

    /// <summary>Reads receipt fields already checked well formed by the request parser.</summary>
    public static AftReceiptData Read(ReadOnlySpan<byte> data)
    {
        AftReceiptData read = None;
        for (int i = 0; i + 1 < data.Length;)
        {
            byte field = data[i];
            int length = data[i + 1];
            ReadOnlySpan<byte> value = data.Slice(i + 2, Math.Min(length, data.Length - i - 2));
            read = field switch
            {
                0x00 => read with { SourceDestination = Encoding.ASCII.GetString(value) },
                0x01 => read with { DateAndTime = value.ToArray() },
                0x10 => read with { PatronName = Encoding.ASCII.GetString(value) },
                0x11 => read with { PatronAccount = Encoding.ASCII.GetString(value) },
                0x13 => read with { AccountBalance = Bcd(value) },
                0x41 => read with { DebitCardNumber = Bcd(value) },
                0x42 => read with { TransactionFee = Bcd(value) },
                0x43 => read with { TotalDebit = Bcd(value) },
                _ => read,
            };
            i += length + 2;
        }

        return read;
    }

    private static long Bcd(ReadOnlySpan<byte> bytes)
    {
        long value = 0;
        foreach (byte b in bytes)
            value = (value * 100) + ((b >> 4) * 10) + (b & 0x0F);
        return value;
    }
}

/// <summary>
/// The transaction receipt a completed transfer prints, and the receipt status it reports.
/// </summary>
/// <remarks>
/// The receipt status is the platform's to report (QSP's <c>ResolveAftTransactionReceipt</c>): $20
/// while it prints, then $00 through the library's <c>internalUpdateAftReceiptStatus</c>
/// (libsas_internal.cpp:3654), which reopens a transfer the host has already read and raises $69 for
/// it again. The printed text is this build's own, a plain list of what the receipt carries.
/// </remarks>
public sealed partial class FakeEgm
{
    private const byte ReceiptPrinted = 0x00;
    private const byte ReceiptPrinting = 0x20;

    private long _receiptDue;
    private int _receiptsPrinted;

    /// <summary>What the completed transfer reports about its receipt, starting the print when there is one.</summary>
    private byte ResolveReceipt(AftRecord record, byte status)
    {
        bool requested = (record.RequestFlags & FlagReceipt) != 0;
        bool moved = record.Cashable + record.Restricted + record.Nonrestricted > 0;
        bool allowed = record.TransferType is not (0x10 or 0x11);
        if (!requested || status is not (0x00 or 0x01) || !_options.AftReceipts || !moved || !allowed)
            return NoReceipt;

        _receiptDue = Stopwatch.GetTimestamp() + Ticks(_options.AftReceiptPrintDelay);
        return ReceiptPrinting;
    }

    /// <summary>Finishes a print that is due and settles its status, as settleAftReceiptStatusLocked does.</summary>
    private List<Action> TickReceipt(long now)
    {
        if (_receiptDue == 0 || now < _receiptDue)
            return [];

        _receiptDue = 0;
        static bool Awaiting(byte status) => status is ReceiptPrinting or Pending;

        int slot = _zeroAmount != AftZeroAmount.NonZero ? 0
            : Awaiting(_history[_aftIndex].ReceiptStatus) ? _aftIndex
            : PreviousIndex();
        AftRecord record = _history[slot];
        if (!Awaiting(record.ReceiptStatus))
            return [];

        record.ReceiptStatus = ReceiptPrinted;
        if (slot != 0 && Awaiting(_history[0].ReceiptStatus))
            _history[0].ReceiptStatus = ReceiptPrinted;

        // A host that has already read the completion is owed $69 for the receipt's (libsas_internal.cpp:3604).
        if (_aftCycle == AftCycle.Completed && _zeroAmount == AftZeroAmount.NonZero)
        {
            _aftIndex = slot;
            _aftCycle = AftCycle.Updated;
            _history[0] = record.Copy();
            _completionReissueDue = Stopwatch.GetTimestamp() + Ticks(_options.AftCompletionReissue);
            QueueRanked((byte)Core.Protocol.SasExceptionCode.AFTTransferComplete);
        }

        AftRecord printed = record.Copy();
        return [() => FileReceipt(printed)];
    }

    /// <summary>Files the receipt as text, when the machine has somewhere to file it.</summary>
    private void FileReceipt(AftRecord record)
    {
        if (_options.ReceiptDirectory is not { } directory)
            return;

        AftReceiptData data = record.Receipt;
        var text = new StringBuilder();
        text.AppendLine("TRANSFER RECEIPT");
        text.AppendLine(CultureInfo.InvariantCulture, $"type {record.TransferType:X2}");
        if (data.SourceDestination.Length > 0)
            text.AppendLine(CultureInfo.InvariantCulture, $"source {data.SourceDestination}");
        text.AppendLine(CultureInfo.InvariantCulture, $"date {record.CompletedAt:yyyy-MM-dd HH:mm:ss}");
        if (data.PatronName.Length > 0)
            text.AppendLine(CultureInfo.InvariantCulture, $"patron {data.PatronName}");
        if (data.HasPatronAccount)
            text.AppendLine(CultureInfo.InvariantCulture, $"account {data.PatronAccount}");
        text.AppendLine(CultureInfo.InvariantCulture, $"cashable {record.Cashable}");
        text.AppendLine(CultureInfo.InvariantCulture, $"nonrestricted {record.Nonrestricted}");
        if (data.AccountBalance is { } balance)
            text.AppendLine(CultureInfo.InvariantCulture, $"balance {balance}");
        text.AppendLine(CultureInfo.InvariantCulture, $"asset {AssetNumber}");
        text.AppendLine(CultureInfo.InvariantCulture, $"transaction {Encoding.ASCII.GetString(record.TransactionId)}");

        Directory.CreateDirectory(directory);
        int number = Interlocked.Increment(ref _receiptsPrinted);
        File.WriteAllText(Path.Combine(directory, $"receipt-{number:D6}-{Guid.NewGuid():N}.txt"), text.ToString());
    }
}
