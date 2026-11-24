using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm;

/// <summary>
/// Hand pay validation, on only when a case declares it, as the platform performs none (QSP SasNetwork.cpp:617):
/// the attendant's reset writes a validation record through the library's buffer, and $3E announces it.
/// </summary>
public sealed partial class FakeEgm
{
    /// <summary>Long poll 7B bit 2, validate hand pays and hand pay receipts.</summary>
    private const ushort ValidateHandpays = 0x0004;

    /// <summary>A validated hand pay printed no receipt: cancelled credit $40, jackpot $60 (LP_VALIDATION_TYPE, libsas.h:1782).</summary>
    private const byte CancelledCreditHandpayNoReceipt = 0x40;
    private const byte JackpotHandpayNoReceipt = 0x60;

    /// <summary>What the machine can do, as long poll 7B reports it: hand pay validation added when it was built with it.</summary>
    private ushort GameValidationStatusNow =>
        _options.HandpayValidation ? (ushort)(GameValidationStatus | ValidateHandpays) : GameValidationStatus;

    /// <summary>
    /// Validates a hand pay as it is reset, while the host has bit 2 on: sasStartCashout with the no-receipt type,
    /// its meters, then the completion that announces it (libsas_internal.cpp:5928).
    /// </summary>
    /// <remarks>Secure enhanced only: a system-validated hand pay would wait for $58, and no case asks for one.</remarks>
    private void ValidateHandpay(HandpayOwed paid)
    {
        if (!_options.HandpayValidation || CashoutValidation != SasCashoutValidation.SecureEnhanced)
            return;

        byte type = paid.Kind == HandpayKind.CancelledCredit ? CancelledCreditHandpayNoReceipt : JackpotHandpayNoReceipt;
        lock (_gate)
        {
            if ((_hostValidationStatus & GameValidationStatusNow & ValidateHandpays) == 0
                || !TryStartCashout(paid.AmountCents, 0, out _, type))
            {
                return;
            }
        }

        bool cancelled = type == CancelledCreditHandpayNoReceipt;
        Meters.Increment(
            cancelled ? SasMeterCode.ValidatedCancelledCreditHandpayNoReceipt_Cents : SasMeterCode.ValidatedJackpotHandpayNoReceipt_Cents,
            paid.AmountCents);
        Meters.Increment(
            cancelled ? SasMeterCode.ValidatedCancelledCreditHandpayNoReceipt_Qty : SasMeterCode.ValidatedJackpotHandpayNoReceipt_Qty, 1);
        CompleteCashout();
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
