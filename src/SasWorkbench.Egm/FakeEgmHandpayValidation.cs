using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Tickets;

namespace SasWorkbench.Egm;

/// <summary>
/// Hand pay validation and receipts, each on only when a case declares it, as the platform performs neither (QSP
/// SasNetwork.cpp:617): the attendant's reset prints a receipt while the host has bit 1 on, and writes a validation
/// record through the library's buffer while it has bit 2 on, announced with $3E, or $3D when a receipt was printed.
/// </summary>
public sealed partial class FakeEgm
{
    /// <summary>Long poll 7B bit 2, validate hand pays and hand pay receipts.</summary>
    private const ushort ValidateHandpays = 0x0004;

    /// <summary>Long poll 7B bit 1, use the printer as a hand pay receipt device.</summary>
    private const ushort HandpayReceipts = 0x0002;

    /// <summary>A validated hand pay printed a receipt: cancelled credit $10, jackpot $20 (LP_VALIDATION_TYPE, libsas.h:1780).</summary>
    private const byte CancelledCreditHandpayReceipt = 0x10;
    private const byte JackpotHandpayReceipt = 0x20;

    /// <summary>A validated hand pay printed no receipt: cancelled credit $40, jackpot $60 (LP_VALIDATION_TYPE, libsas.h:1782).</summary>
    private const byte CancelledCreditHandpayNoReceipt = 0x40;
    private const byte JackpotHandpayNoReceipt = 0x60;

    /// <summary>What the machine can do, as long poll 7B reports it: hand pay validation added when it was built with it.</summary>
    private ushort GameValidationStatusNow =>
        (ushort)(GameValidationStatus
                 | (_options.HandpayValidation ? ValidateHandpays : 0)
                 | (_options.HandpayReceipts ? HandpayReceipts : 0)
                 | RestrictedTicketStatus);

    /// <summary>
    /// Validates a hand pay as it is reset, while the host has bit 2 on: sasStartCashout with the receipt or no-receipt
    /// type, its meters, then the completion that announces it (libsas_internal.cpp:5928); and prints its receipt while
    /// the host has bit 1 on (GSA 10.3.4).
    /// </summary>
    /// <remarks>Secure enhanced only: a system-validated hand pay would wait for $58, and the document has a system machine print no receipt.</remarks>
    private void ValidateHandpay(HandpayOwed paid)
    {
        if (CashoutValidation != SasCashoutValidation.SecureEnhanced)
            return;

        bool cancelled = paid.Kind == HandpayKind.CancelledCredit;
        bool receipt;
        bool validated;
        ulong number = 0;
        lock (_gate)
        {
            ushort granted = (ushort)(_hostValidationStatus & GameValidationStatusNow);
            receipt = (granted & HandpayReceipts) != 0;
            byte type = (cancelled, receipt) switch
            {
                (true, true) => CancelledCreditHandpayReceipt,
                (false, true) => JackpotHandpayReceipt,
                (true, false) => CancelledCreditHandpayNoReceipt,
                _ => JackpotHandpayNoReceipt,
            };
            validated = (granted & ValidateHandpays) != 0 && TryStartCashout(paid.AmountCents, 0, out number, type);
        }

        if (receipt && _options.WalletDirectory is { } wallet)
            PrintHandpayReceipt(wallet, paid, cancelled, number);

        if (!validated)
            return;

        Meters.Increment(
            (cancelled, receipt) switch
            {
                (true, true) => SasMeterCode.ValidatedCancelledCreditHandpayReceiptPrinted_Cents,
                (false, true) => SasMeterCode.ValidatedJackpotHandpayReceiptPrinted_Cents,
                (true, false) => SasMeterCode.ValidatedCancelledCreditHandpayNoReceipt_Cents,
                _ => SasMeterCode.ValidatedJackpotHandpayNoReceipt_Cents,
            },
            paid.AmountCents);
        Meters.Increment(
            (cancelled, receipt) switch
            {
                (true, true) => SasMeterCode.ValidatedCancelledCreditHandpayReceiptPrinted_Qty,
                (false, true) => SasMeterCode.ValidatedJackpotHandpayReceiptPrinted_Qty,
                (true, false) => SasMeterCode.ValidatedCancelledCreditHandpayNoReceipt_Qty,
                _ => SasMeterCode.ValidatedJackpotHandpayNoReceipt_Qty,
            },
            1);
        CompleteCashout();
    }

    /// <summary>The receipt the attendant hands over, filed in the wallet with the validation number it carries, zero when unvalidated.</summary>
    private void PrintHandpayReceipt(string wallet, HandpayOwed paid, bool cancelled, ulong number)
    {
        uint printed = (uint)Interlocked.Increment(ref _ticketsPrinted);
        FileTicket(wallet, new Ticket
        {
            ValidationNumber = number,
            ValidationType = cancelled
                ? TicketValidationType.CancelledCreditHandpayReceiptPrinted
                : TicketValidationType.JackpotHandpayReceiptPrinted,
            AmountCents = paid.AmountCents,
            TicketNumber = printed,
            PrintedAt = DateTime.Now,
        });
    }

    /// <summary>
    /// The exception the oldest unread record is announced with: $3E for a validated hand pay with no receipt on a
    /// secure enhanced machine, $3D otherwise (libsas_internal.cpp:690). Called under the gate.
    /// </summary>
    private byte UnreadAnnouncementLocked()
    {
        if (_pendingRecords > 0 && CashoutValidation == SasCashoutValidation.SecureEnhanced)
        {
            int oldest = _bufferIndex - (_pendingRecords - 1);
            while (oldest < BufferStart)
                oldest += BufferItems - BufferStart;

            if (_buffer[oldest]?.Type is CancelledCreditHandpayNoReceipt or JackpotHandpayNoReceipt)
                return (byte)SasExceptionCode.HandpayValidated;
        }

        return (byte)SasExceptionCode.CashoutTicketPrinted;
    }
}
