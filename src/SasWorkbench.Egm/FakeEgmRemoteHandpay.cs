using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>
/// The host clearing a hand pay ($94) and choosing how a jackpot hand pay is cleared ($A8): the library's tests and
/// replies, and the platform's choice of the standard hand pay or the credit meter.
/// </summary>
/// <remarks>
/// Both are the platform's: the library has remote reset on unless the application turns it off, which QSP never does
/// (libsas_internal.cpp:329), and QSP pays a jackpot onto the credit meter when the host asked for it and the win fits
/// under the credit limit (QSP HandpayResetRules.h). The machine has no upper limit for that reset, as the operator
/// cannot set one on the platform.
/// </remarks>
public sealed partial class FakeEgm
{
    private const byte ResetMethodStandard = 0x00;
    private const byte ResetMethodCreditMeter = 0x01;
    private const byte ResetMethodCelebration = 0x80;
    private const byte ResetIdCreditMeter = 0x01;
    private const byte ResetCodeDone = 0x00;
    private const byte ResetCodeUnable = 0x01;
    private const byte ResetCodeNoHandpay = 0x02;

    // QSP's fHandpayResetMethod: how the next $94 pays, standard unless $A8 asked and the machine agreed.
    private byte _hostResetMethod = ResetMethodStandard;

    /// <summary>Answers $94 and $A8, or returns false for any other command.</summary>
    private bool AnswerHandpayReset(MachineLink link, byte command, ReadOnlySpan<byte> frame)
    {
        if (command is not ((byte)SasLongPollCode.LP_REMOTEHANDPAYRESET or (byte)SasLongPollCode.LP_ENABLEJACKPOTHANDPAYRESET))
            return false;

        // Both handlers test the address first and stay silent for another (lp.cpp:9066, :9597).
        if (frame[0] != link.Address.Value)
            return true;

        if (command == (byte)SasLongPollCode.LP_REMOTEHANDPAYRESET)
            AnswerRemoteHandpayReset(link);
        else
            AnswerResetMethod(link, frame);
        return true;
    }

    /// <summary>
    /// $94: no hand pay registered answers 02; otherwise the library resets it, answering 00, or 01 if it refuses
    /// (lp.cpp:9097). The library's busy test for $94 never fires, as QSP never sets it (QSP SasNetwork_AftTransfer.hpp:1525).
    /// </summary>
    private void AnswerRemoteHandpayReset(MachineLink link)
    {
        byte code;
        HandpayOwed? paid = null;
        byte method = ResetMethodStandard;
        lock (_gate)
        {
            if (!_handpayRegistered)
            {
                code = ResetCodeNoHandpay;
            }
            else if (!ResetWithLibraryLocked())
            {
                code = ResetCodeUnable;
            }
            else
            {
                code = ResetCodeDone;
                paid = _handpay;
                _handpay = null;
                method = _hostResetMethod;

                // The platform pays by the method chosen and then forgets it (QSP SasNetwork_Callbacks.hpp:1029).
                _hostResetMethod = ResetMethodStandard;
            }
        }

        SendCode(link, (byte)SasLongPollCode.LP_REMOTEHANDPAYRESET, "reset_code", code);
        if (paid is null)
            return;

        if (method == ResetMethodStandard)
        {
            // As if the attendant had paid it (QSP AsyncLpRemoteHandpayReset, SasNetwork_Callbacks.hpp:864).
            MeterHandpay(paid);
            ValidateHandpay(paid);
        }
        else if (paid.Kind == HandpayKind.Jackpot)
        {
            CreditHandpay(paid);
        }
    }

    /// <summary>
    /// $A8: a method other than 00, 01 or 80 is refused; no hand pay answers 02; the credit meter needs the pending
    /// entry's reset ID to allow it, else 01; anything else is 00 (lp.cpp:9625 to :9682).
    /// </summary>
    private void AnswerResetMethod(MachineLink link, ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 5)
        {
            Reject(link);
            return;
        }

        byte method = frame[2];
        if (method is not (ResetMethodStandard or ResetMethodCreditMeter or ResetMethodCelebration))
        {
            Reject(link);
            return;
        }

        byte ack;
        lock (_gate)
        {
            if (!_handpayRegistered)
                ack = ResetCodeNoHandpay;
            else if (method == ResetMethodCreditMeter)
                ack = link.Handpays.CurrentResetId() is ResetIdCreditMeter or 0x80 ? ResetCodeDone : ResetCodeUnable;
            else
                ack = ResetCodeDone;

            // The platform acts only for a jackpot, and only on what it agreed to and still allows (QSP SasNetwork_Callbacks.hpp:820).
            if (_handpay is { Kind: HandpayKind.Jackpot } owed)
            {
                _hostResetMethod = method != ResetMethodStandard && ack == ResetCodeDone && MayResetToCreditMeterLocked(owed)
                    ? method
                    : ResetMethodStandard;
            }
        }

        SendCode(link, (byte)SasLongPollCode.LP_ENABLEJACKPOTHANDPAYRESET, "ack_code", ack);
    }

    /// <summary>
    /// Whether a jackpot may be paid onto the credit meter: it must fit under the credit limit, which zero leaves
    /// unset (QSP ResetToCreditMeterAllowed). A cancelled-credit hand pay never may. Called under the gate.
    /// </summary>
    private bool MayResetToCreditMeterLocked(HandpayOwed owed)
    {
        if (owed.Kind != HandpayKind.Jackpot)
            return false;
        if (_creditLimitCents == 0)
            return true;

        long held = Meters.Read(SasMeterCode.CurrentCredits) * AccountingCents;
        return held + owed.AmountCents <= _creditLimitCents;
    }

    /// <summary>
    /// The jackpot paid onto the credit meter as a machine-paid win: credits, coin out and the machine-paid meter of
    /// its kind move, the jackpot meter does not (QSP SasNetwork_Callbacks.hpp:925 to :1018).
    /// </summary>
    private void CreditHandpay(HandpayOwed paid)
    {
        long credits = paid.AmountCents / AccountingCents;
        long progressive = paid.ProgressiveCents / AccountingCents;

        Meters.SetCurrentCredits(Meters.Read(SasMeterCode.CurrentCredits) + credits);
        Meters.Increment(SasMeterCode.TotalCoinOutCredits, credits);
        if (paid.Bonus)
        {
            Meters.Increment(SasMeterCode.TotalMachinePaidExternalBonusWin_Crd, credits);
        }
        else
        {
            if (progressive > 0)
                Meters.Increment(SasMeterCode.TotalMachinePaidProgressiveWin_Crd, progressive);
            if (credits - progressive > 0)
                Meters.Increment(SasMeterCode.TotalMachinePaidPaytableWin_Crd, credits - progressive);
        }

        PublishRestricted();
    }

    /// <summary>A reply of one code byte after the address and command, with its check sequence.</summary>
    private void SendCode(MachineLink link, byte command, string field, byte code)
    {
        SasMessageLayout layout = SasLayouts.For(command)!;
        int length = SasMessageEncoder.WriteResponse(
            layout, link.Address, new Dictionary<string, object> { [field] = code }, link.Frame);
        for (int i = 0; i < length; i++)
            link.Outbound[i] = SasSymbol.Data8(link.Frame[i]);
        Send(link, link.Outbound.AsSpan(0, length));
    }
}
