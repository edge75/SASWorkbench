using System.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Egm;

/// <summary>
/// A power cycle, a memory fault and a memory clear, mirrored from the library's non-volatile image
/// and from what the platform keeps, restores and raises around it.
/// </summary>
/// <remarks>
/// <para>
/// The library keeps one image across a restart (sNvramData, libsas_db.h:855): the exception queue,
/// the validation buffer and identifier, the transfer history, lock and registration, the hand pay
/// and the legacy bonus award. Everything else - pending replies, real-time reporting, the link -
/// starts again. The platform keeps its meters, credits, conditions and configuration, removes the
/// transfer lock and ends a game delay that was running.
/// </para>
/// <para>
/// The library raises no power or memory exception of its own; every one of those is the platform's
/// (QSP SasNetwork.cpp:159 and SasNetwork_Conditions.hpp:477 for $18 and $17).
/// </para>
/// </remarks>
public sealed partial class FakeEgm
{
    // True while the machine is off; every link discards what it hears and no timer runs.
    private volatile bool _poweredOff;

    // The platform's memory error and program media changed conditions, both kept until a memory clear.
    private bool _memoryError;
    private bool _programChanged;

    /// <summary>True while a memory error or a changed program has the machine out of play until its memory is cleared.</summary>
    public bool MemoryTiltActive
    {
        get
        {
            lock (_gate)
                return _memoryError || _programChanged;
        }
    }

    /// <summary>Cuts the power, leaves the machine dead to the line a moment, and starts it again.</summary>
    public void PowerCycle()
    {
        _poweredOff = true;
        Thread.Sleep(_options.PowerOffTime);
        StartUp();
    }

    /// <summary>
    /// Puts the machine into a memory fault, or clears its memory; null when done, otherwise why the platform cannot.
    /// </summary>
    public string? MemoryFault(MemoryFaultKind kind)
    {
        switch (kind)
        {
            case MemoryFaultKind.CmosLost:
                RaiseMemoryError();
                return null;

            // QSP AttachableDeviceHandler.cpp:436 finds the program changed while the machine starts.
            case MemoryFaultKind.EpromChanged:
                lock (_gate)
                    _programChanged = true;
                PowerCycle();
                return null;

            case MemoryFaultKind.Clear:
                ClearMemory();
                return null;

            // QSP BankCache.hpp:231 recovers a bank from its mirror and raises nothing.
            case MemoryFaultKind.CmosRecovered:
                return "the platform recovers a damaged memory bank from its mirror and reports nothing, so it never raises $31";

            default:
                return $"the platform has no report for {kind}: it raises $32, $36 and $3A among the memory codes and never $33, $34, $35, $37, $38 or $39";
        }
    }

    /// <summary>
    /// The memory error the platform finds while running: $32, and the machine out of play until a memory clear.
    /// </summary>
    /// <remarks>
    /// The running machine's path (QSP NvramHandler.cpp:47, SecurityHandler.cpp:504), the one on which the link
    /// stays up. Memory found invalid at power-up starts no link at all (QSP SasNetwork.cpp:309), so the host hears nothing.
    /// </remarks>
    private void RaiseMemoryError()
    {
        lock (_gate)
        {
            // An active condition raised again is a duplicate and reports nothing (QSP ConditionHandler.cpp:263).
            if (_memoryError)
                return;
            _memoryError = true;
        }

        QueueOrdinary((byte)SasExceptionCode.CMOSRAMErrorNoDataRecovered);
        WithdrawLockIfUnplayable();
    }

    /// <summary>
    /// The operator's memory clear: everything the library and the platform keep is emptied, the
    /// configuration goes back to the machine's own, and the library starts again (QSP PostNvramReset,
    /// SasNetwork.cpp:1197).
    /// </summary>
    /// <remarks>
    /// The attendant sets the poll address again at once, so the machine answers where it did; the
    /// platform would otherwise come back at address 1 (SasNetwork.cpp:1200) and the asset number does.
    /// </remarks>
    public void ClearMemory()
    {
        _poweredOff = true;
        Thread.Sleep(_options.PowerOffTime);

        lock (_gate)
        {
            foreach (MachineLink link in _links)
            {
                link.Exceptions.Clear();
                RestartLink(link);
            }

            ClearMachineLocked();
        }

        Meters.ClearAll();
        Interlocked.Exchange(ref _billValueInDollars, 0);
        Interlocked.Exchange(ref _ticketsPrinted, 0);

        // What was owed before is dropped, and the reset is reported, then the configuration completing (SasNetwork.cpp:1219, SasNetwork_Conditions.hpp:63).
        QueueOrdinary((byte)SasExceptionCode.MemoryErrorReset);
        QueueOrdinary((byte)SasExceptionCode.GamingMachineSoftMetersResetToZero);
        QueueOrdinary((byte)SasExceptionCode.OperatorChangedOptions);

        StartValidationIdReporting();
        PowerOn();
    }

    /// <summary>The restart after a power cut: what is kept stays, what the library and the platform restore is reported again.</summary>
    private void StartUp()
    {
        bool reissueCompletion, recoverValidation, handpayAwaitingRead;
        long now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            foreach (MachineLink link in _links)
            {
                // The queue is kept with every entry marked unsent, so one sent and not acknowledged goes again (libsas_internal.cpp:622).
                link.Exceptions.Refuse();
                RestartLink(link);
            }

            // QSP removes the transfer lock at power-up (SasNetwork_Conditions.hpp:547).
            UnlockGame();

            // A cash-out to the host in progress is cancelled (QSP SasNetwork_Conditions.hpp:519).
            EndHostCashout();

            // The configured game delay is kept, but the countdown is held in memory only (QSP SasNetwork.h:376).
            _gameDelayEnds = 0;

            // libsas_internal.cpp:3044 raises $69 again for a transfer still pending or unread.
            reissueCompletion = _aftCycle is AftCycle.Pending or AftCycle.PendingPartial or AftCycle.Updated;
            if (reissueCompletion)
                _completionReissueDue = now + Ticks(_options.AftCompletionReissue);

            // libsas_internal.cpp:681 announces an unread record again, and :855 counts its ten seconds as spent.
            recoverValidation = CashoutValidation != SasCashoutValidation.Standard && _pendingRecords > 0;
            _validationRecordsUnread = false;
            if (recoverValidation)
            {
                _unreadSince = now - Ticks(UnreadLimit);
                _printedReissueDue = now + Ticks(PrintedReissue);
            }

            // libsas_internal.cpp:887 raises $51 again for a hand pay still awaiting its reading.
            handpayAwaitingRead = _handpay is { ReadAcknowledged: false };
            if (handpayAwaitingRead)
                _handpayIssuedAt = now;
        }

        // A count kept in memory only, so it starts again (QSP ConditionHandler.cpp:81).
        Meters.Restart(SasMeterCode.GamesSinceLastReset);

        if (reissueCompletion)
            QueueRanked((byte)SasExceptionCode.AFTTransferComplete);
        if (recoverValidation)
            QueueRanked((byte)SasExceptionCode.CashoutTicketPrinted);
        if (handpayAwaitingRead)
            QueueRanked((byte)SasExceptionCode.HandpayIsPending);

        RaisePowerUpExceptions();
        StartValidationIdReporting();
        PowerOn();
    }

    /// <summary>
    /// What the platform raises on every start, in its order: $18, the conditions it restores, $17, then the
    /// device faults it finds again (QSP SasNetwork.cpp:159, ConditionHandler.cpp:741, SasNetwork_Conditions.hpp:477).
    /// </summary>
    private void RaisePowerUpExceptions()
    {
        QueueOrdinary((byte)SasExceptionCode.ACPowerLost);

        (List<SasExceptionCode> restored, List<SasExceptionCode> rederived) = RestoredConditionCodes();
        foreach (SasExceptionCode code in restored)
            QueueOrdinary((byte)code);

        bool memoryError, programChanged;
        lock (_gate)
        {
            memoryError = _memoryError;
            programChanged = _programChanged;
        }

        if (memoryError)
            QueueOrdinary((byte)SasExceptionCode.CMOSRAMErrorNoDataRecovered);
        if (programChanged)
            QueueOrdinary((byte)SasExceptionCode.EEPROMErrorVersionChanged);

        QueueOrdinary((byte)SasExceptionCode.ACPowerApplied);

        foreach (SasExceptionCode code in rederived)
            QueueOrdinary((byte)code);
    }

    /// <summary>Everything a link knew about the host is gone: it waits for a synchronisation poll, with reporting off (libsas_internal.cpp:305, :313).</summary>
    private void RestartLink(MachineLink link)
    {
        link.AwaitingSync = true;
        link.LastWasOwnGeneralPoll = false;
        link.LastHeard = 0;
        link.UnacknowledgedHandpayPoll = null;
        _unacknowledgedLegacyReads.Remove(link.Channel);
        _unacknowledgedValidationReads.Remove(link.Channel);
        _rteEnabled = false;
    }

    /// <summary>What a memory clear leaves: the library's image zeroed to its defaults and the platform's records gone.</summary>
    private void ClearMachineLocked()
    {
        _memoryError = false;
        _programChanged = false;

        // Every SAS condition is removed, the host's shutdown among them (QSP SasNetwork.cpp:1208).
        _enabled = true;
        _enabledNotes = uint.MaxValue;
        (_denominationCode, _accountingCents, _) = ResolveDenomination(_options);
        _lastBillCountryCode = 0;
        _lastBillDenominationCode = 0;
        _lastValidationNumber = 0;
        _lastTicketAmountCents = 0;

        ClearTransfersLocked();
        ClearValidationLocked();

        _handpay = null;
        Array.Clear(_legacyEscrow);
        Array.Clear(_legacyMeters);
        _lastLegacyAward = default;
        _legacyBonus = _options.LegacyBonus;
        _gameDelayTenths = 0;
        _gameDelayEnds = 0;
    }

    /// <summary>The transfer half of the library's defaults (libsas_internal.cpp:773 to :821) and the platform's asset number 1.</summary>
    private void ClearTransfersLocked()
    {
        for (int slot = 0; slot < _history.Length; slot++)
            _history[slot] = new AftRecord();

        _aftIndex = 1;
        _aftOverflow = false;
        _aftCycle = AftCycle.None;
        _zeroAmount = AftZeroAmount.NonZero;
        _completionDue = 0;
        _completionReissueDue = 0;
        _cancelRequested = false;
        _payAfterTransfer = null;
        _nonrestrictedCents = 0;
        _lockStatus = NotLocked;
        _lockRemainingHundredths = 0;
        _lockDue = 0;
        _hostCashoutStatus = HostControllable;
        EndHostCashout();

        _assetNumber = 1;
        _registrationStatus = NotRegistered;
        _registrationKey = new byte[20];
        _posId = 0;
        _receiptsDue.Clear();

        _aftOn = _options.Aft;
        _aftBonus = _options.AftBonus;
        _creditLimitCents = _options.CreditLimitCents;
        _restrictedCents = 0;
        _restrictedPool = 0;
        _restrictedExpiration = new byte[4];
    }

    /// <summary>The validation half of the library's defaults: no records, no identifier, the default status words and expirations.</summary>
    private void ClearValidationLocked()
    {
        Array.Clear(_buffer);
        _bufferIndex = 0;
        _pendingRecords = 0;
        _unreadSince = 0;
        _printedReissueDue = 0;
        _validationRecordsUnread = false;
        _validationBufferFull = false;
        _secureValidationId = 0;
        _secureSequenceNumber = 0;
        _seIsSet = false;
        _seIncremented = false;
        _seBaseSequence = 0;
        _validationIdDue = 0;
        _hostValidationStatus = 0x803F;
        _platformValidationStatus = 0x0039;
        _cashableExpirationDays = DefaultExpirationDays;
        _restrictedExpirationDays = DefaultExpirationDays;
        _cashoutState = CashoutValidationState.None;
        _awaitingValidationNumber = false;
        _awaitingPrint = null;
    }

    /// <summary>
    /// Restores the power and returns only once every link has turned past the read it was in while off,
    /// so nothing the host sends next is discarded as heard by a machine with no power.
    /// </summary>
    private void PowerOn()
    {
        _poweredOff = false;
        Thread.Sleep(TimeSpan.FromMilliseconds(50));
    }

    /// <summary>A memory error or a changed program takes the machine out of play (QSP ConditionDefaults.h:56, :59).</summary>
    private bool MemoryTilt() => _memoryError || _programChanged;
}
