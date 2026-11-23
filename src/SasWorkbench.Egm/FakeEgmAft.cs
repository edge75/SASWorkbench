using System.Diagnostics;
using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Egm;

/// <summary>
/// The lock and status half of funds transfer: long poll $74, the lock it takes, the game locked
/// exception, and the host cashout request the collect button raises when the host has asked for it.
/// </summary>
/// <remarks>
/// <para>
/// The protocol half is the machine library's, read from <c>lp74_LockAndStatusRequest</c>,
/// <c>internalLockGame</c> and the lock timer in <c>libsas_threads.cpp</c>. What the library asks
/// of the application - which transfers are available, whether the machine can lock now - is
/// answered as the platform above it answers (QSP's <c>AftAvailability.h</c>), reduced to what this
/// machine has: doors, a hand pay, the host's disable, and no game ever left running.
/// </para>
/// <para>
/// Times are taken on the polling thread, so a deadline is honoured within one turn of the loop.
/// </para>
/// </remarks>
public sealed partial class FakeEgm
{
    private const byte NotLocked = 0xFF;
    private const byte Locked = 0x00;

    // The library's lock timer re-raises $6F every five seconds of a lock, in hundredths.
    private const int LockStepHundredths = 500;

    // Transfer to the machine and from it, as QSP configures; no transfer to a ticket, which the platform does not perform.
    private const byte TransfersInAndOut = 0x03;
    private const byte BonusAwardTransfer = 0x10;

    // In-house transfers, transfers to the host of less than everything, and AFT on, as QSP configures.
    private const byte InHouseAftStatus = 0x92;
    private const byte BonusAftStatus = 0x20;
    private const byte ReceiptsAftStatus = 0x01;
    private const byte RegisteredAftStatus = 0x08;

    // The host may decide whether a cash-out goes to it; enabled and hard start clear.
    private const byte HostControllable = 0x01;
    private const byte HostCashoutEnabled = 0x02;

    private static readonly TimeSpan HostCashoutStep = TimeSpan.FromMilliseconds(400);

    private byte _lockStatus = NotLocked;
    private long _lockRemainingHundredths;
    private long _lockDue;
    private byte _hostCashoutStatus = HostControllable;
    private uint? _assetNumber;
    private bool _aftBonus;

    // The 400 ms ticks a host cashout request has been outstanding, as the library counts them.
    private int _hostCashoutTicks;
    private long _hostCashoutDue;
    private string? _hostCashoutWallet;

    /// <summary>Whether the machine is locked for a transfer: $00 locked or $FF not; it locks at once, so never $40 pending.</summary>
    public byte AftLockStatus
    {
        get
        {
            lock (_gate)
                return _lockStatus;
        }
    }

    /// <summary>The number this machine is registered under for funds transfer.</summary>
    public uint AssetNumber
    {
        get
        {
            lock (_gate)
                return _assetNumber ?? _options.AssetNumber;
        }
    }

    /// <summary>
    /// Gives the machine another asset number, as an attendant would in its menu.
    /// </summary>
    /// <remarks>Exists so a case can state the asset number its transfers are addressed to.</remarks>
    public void SetAssetNumber(uint asset)
    {
        lock (_gate)
        {
            // A registered machine given another number is no longer registered (libsas.cpp:2186).
            if (asset != AssetNumber && _registrationStatus == Registered)
                CancelRegistration();
            _assetNumber = asset;
        }
    }

    /// <summary>True while the machine takes bonus awards over funds transfer.</summary>
    public bool AftBonusOn
    {
        get
        {
            lock (_gate)
                return _aftBonus;
        }
    }

    /// <summary>The transfer types the machine is configured for, before its state reduces them.</summary>
    private byte ConfiguredTransfers => (byte)(TransfersInAndOut | (_aftBonus ? BonusAwardTransfer : 0));

    /// <summary>The AFT status the machine is configured with, before its state reduces it.</summary>
    private byte ConfiguredAftStatus => (byte)(InHouseAftStatus
                                               | (_aftBonus ? BonusAftStatus : 0)
                                               | (_options.AftReceipts ? ReceiptsAftStatus : 0));

    /// <summary>True while the host has asked for cash-outs to come to it and has not turned that off.</summary>
    public bool HostCashoutRequested
    {
        get
        {
            lock (_gate)
                return _hostCashoutDue != 0;
        }
    }

    /// <summary>The platform's test of whether the machine can be played and can accept a transfer.</summary>
    private bool IsPlayable()
    {
        bool doorOpen;
        lock (_openDoors)
            doorOpen = _openDoors.Count > 0;

        return !doorOpen && _enabled && _handpay is not { Reset: false } && !PeripheralTilt() && !MemoryTilt() && !_validationBufferFull && !ProgressiveWaitLocked();
    }

    /// <summary>What the platform publishes as available now: everything configured, nothing while unplayable.</summary>
    /// <remarks>A host cashout in progress leaves only the transfer it asked for (QSP AftAvailableTransfersNow).</remarks>
    private byte AvailableTransfers()
    {
        if (!_aftOn)
            return 0;
        if (_hostCashoutDue != 0)
            return (byte)(ConfiguredTransfers & 0x02);
        return IsPlayable() ? ConfiguredTransfers : (byte)0;
    }

    private byte AftStatus()
    {
        if (!_aftOn)
            return 0;
        return IsPlayable() || _hostCashoutDue != 0 ? ConfiguredAftStatus : (byte)0;
    }

    /// <summary>What long poll $74 answers; a machine with transfers off answers nothing, as the library does.</summary>
    private Dictionary<string, object>? LockAndStatus(ReadOnlySpan<byte> request)
    {
        if (!_aftOn)
            return NoAnswer;

        if (request.Length < 6)
            return null;

        byte lockCode = request[2];
        byte condition = request[3];
        long timeout = BcdValue(request.Slice(4, 2));
        List<Action> after = [];
        byte status;

        lock (_gate)
        {
            // A $74 during a host cashout request holds back its 800 ms reissue, as the library's reset does.
            if (_hostCashoutDue != 0)
                _hostCashoutTicks = Math.Max(0, _hostCashoutTicks - (_hostCashoutTicks % 2 == 0 ? 2 : 1));

            if (lockCode == 0xFF)
            {
                status = _lockStatus;
            }
            else if (lockCode == 0x00)
            {
                byte available = AvailableTransfers();
                bool offered = (condition & ((available & 0x07) | ((available >> 1) & 0x08))) != 0;
                if (offered && _aftCycle != AftCycle.Pending)
                {
                    _lockRemainingHundredths = timeout;

                    // QSP's lock provider locks at once whenever the machine is playable, and no game here is ever left running.
                    if (_lockStatus == NotLocked && IsPlayable())
                        LockGame();
                    else if (_lockStatus == Locked)
                        LockGame();

                    status = _lockStatus;
                }
                else
                {
                    // Section 8.2: a lock the machine cannot offer is refused and any standing lock is withdrawn (lp.cpp:6588).
                    UnlockGame();
                    status = NotLocked;
                }
            }
            else
            {
                UnlockGame();
                status = NotLocked;
            }
        }

        foreach (Action action in after)
            action();

        return new Dictionary<string, object>
        {
            ["length"] = 0x23,
            ["asset_number"] = (long)AssetNumber,
            ["game_lock_status"] = status,
            ["available_transfers"] = (byte)(AvailableTransfersLocked() & 0x7F),
            ["host_cashout_status"] = HostCashoutStatusLocked(),
            ["aft_status"] = (byte)(AftStatusLocked() | RegisteredBit()),
            ["max_buffer_index"] = (byte)(HistoryDepth - 1),
            ["current_cashable_amount"] = CurrentCashableCents(),
            ["current_restricted_amount"] = CurrentRestrictedCents(),
            ["current_nonrestricted_amount"] = CurrentNonrestrictedCents(),
            ["gaming_machine_transfer_limit"] = TransferLimitCents(),

            // The expiration and pool only while restricted credit is held (lp.cpp:6638).
            ["restricted_expiration"] = RestrictedExpirationValue(),
            ["restricted_pool_id"] = (long)RestrictedPool(),
        };
    }

    private byte AvailableTransfersLocked()
    {
        lock (_gate)
            return AvailableTransfers();
    }

    private byte AftStatusLocked()
    {
        lock (_gate)
            return AftStatus();
    }

    /// <summary>The library adds the registered bit to every $74 status from the live registration (lp.cpp:6630).</summary>
    private byte RegisteredBit()
    {
        lock (_gate)
            return _registrationStatus == Registered ? RegisteredAftStatus : (byte)0;
    }

    private byte HostCashoutStatusLocked()
    {
        lock (_gate)
            return _hostCashoutStatus;
    }

    /// <summary>
    /// Locks, or refreshes a lock, from the timeout last stored, re-raising $6F only on the way in
    /// (internalLockGame, libsas_internal.cpp:2261).
    /// </summary>
    private void LockGame()
    {
        byte previous = _lockStatus;
        _lockStatus = Locked;
        ArmLockStep(Stopwatch.GetTimestamp());

        if (previous != Locked)
            QueueRanked((byte)SasExceptionCode.GameLocked);
    }

    /// <summary>One step of the lock timer from <paramref name="from"/>: five seconds while more than that is left, otherwise what is left.</summary>
    /// <remarks>Each step runs from when the last one was due, so the loop's latency does not add up over a long lock.</remarks>
    private void ArmLockStep(long from)
    {
        long step;
        if (_lockRemainingHundredths > LockStepHundredths)
        {
            _lockRemainingHundredths -= LockStepHundredths;
            step = LockStepHundredths;
        }
        else
        {
            step = _lockRemainingHundredths;
            _lockRemainingHundredths = 0;
        }

        // A zero step fires at once, which is the Windows timer's reading of a zero due time.
        _lockDue = from + (step * Stopwatch.Frequency / 100);
    }

    /// <summary>Gives up a lock or a pending lock and takes back its $6F (internalUnlockGame and the lock timer).</summary>
    private void UnlockGame()
    {
        _lockRemainingHundredths = 0;
        _lockDue = 0;
        if (_lockStatus == NotLocked)
            return;

        _lockStatus = NotLocked;
        Withdraw((byte)SasExceptionCode.GameLocked);
    }

    /// <summary>The platform withdraws a lock the moment the machine stops being playable (QSP ShouldWithdrawAftLock).</summary>
    private void WithdrawLockIfUnplayable()
    {
        lock (_gate)
        {
            if (_lockStatus != NotLocked && !IsPlayable())
                UnlockGame();
        }
    }

    /// <summary>
    /// The channel that owns funds transfer going quiet past the loop break ends any lock, as soon as it
    /// does (libsas_threads.cpp:2542); silence on any other channel leaves it.
    /// </summary>
    private void WithdrawLockOnLinkLoss(MachineLink link)
    {
        if (_options.LinkLossAfter <= TimeSpan.Zero || link.LastHeard == 0
            || !Channels.Answers(link.Channel, (byte)SasLongPollCode.LP_AFT_LOCKANDSTATUS)
            || Stopwatch.GetElapsedTime(link.LastHeard) <= _options.LinkLossAfter)
        {
            return;
        }

        lock (_gate)
            UnlockGame();
        CancelUnfinishedRegistration();
    }

    /// <summary>Runs every timer funds transfer keeps: the lock, the platform finishing a transfer, $69's reissue and the host cashout request.</summary>
    private void TickAft(MachineLink link)
    {
        if (!_aftOn)
            return;

        WithdrawLockOnLinkLoss(link);

        long now = Stopwatch.GetTimestamp();
        List<Action> after = [];

        lock (_gate)
        {
            if (_lockDue != 0 && now >= _lockDue)
            {
                long due = _lockDue;
                _lockDue = 0;
                if (_lockRemainingHundredths == 0)
                {
                    UnlockGame();
                }
                else
                {
                    ArmLockStep(due);
                    QueueRanked((byte)SasExceptionCode.GameLocked);
                }
            }

            after.AddRange(TickTransfer(now));
            after.AddRange(TickHostCashout(now));
        }

        foreach (Action action in after)
            action();
    }

    /// <summary>
    /// The collect button with cash-out to the host turned on: $66, then $6A every 800 ms for eight
    /// seconds while the host decides (sasAftGmToHost and the GM-to-host timer, libsas_threads.cpp:2634).
    /// </summary>
    /// <returns>True when the host has been asked, so the machine does not pay out yet.</returns>
    private bool RequestHostCashout(string walletDirectory)
    {
        lock (_gate)
        {
            if (!_aftOn || (_hostCashoutStatus & HostCashoutEnabled) == 0)
                return false;
        }

        QueueOrdinary((byte)SasExceptionCode.CashoutButtonPressed);

        lock (_gate)
        {
            if (!IsPlayable() || _hostCashoutDue != 0)
                return false;

            _hostCashoutTicks = 0;
            _hostCashoutWallet = walletDirectory;
            _hostCashoutDue = Stopwatch.GetTimestamp() + Ticks(HostCashoutStep);
        }

        QueueRanked((byte)SasExceptionCode.AFTRequestForHostCashout);
        return true;
    }

    private List<Action> TickHostCashout(long now)
    {
        if (_hostCashoutDue == 0 || now < _hostCashoutDue)
            return [];

        _hostCashoutTicks++;
        bool disabled = (_hostCashoutStatus & HostControllable) != 0 && (_hostCashoutStatus & HostCashoutEnabled) == 0;
        if (disabled || _hostCashoutTicks >= 20)
        {
            // Eight seconds unanswered, or the host turned it off: the platform pays the usual way.
            string wallet = _hostCashoutWallet!;
            EndHostCashout();
            Withdraw((byte)SasExceptionCode.AFTRequestForHostCashout);
            return [() => PayOut(wallet)];
        }

        _hostCashoutDue += Ticks(HostCashoutStep);
        if (_hostCashoutTicks % 2 == 0)
            QueueRanked((byte)SasExceptionCode.AFTRequestForHostCashout);
        return [];
    }

    /// <summary>Stops asking the host, because it answered with a transfer from the machine or the request ran out.</summary>
    private void EndHostCashout()
    {
        _hostCashoutDue = 0;
        _hostCashoutTicks = 0;
        _hostCashoutWallet = null;
    }

    private static long Ticks(TimeSpan span) => (long)(span.TotalSeconds * Stopwatch.Frequency);

    /// <summary>Queues a priority exception on every channel that hears it, ranked as the library ranks it.</summary>
    private void QueueRanked(byte code)
    {
        lock (_gate)
        {
            foreach (MachineLink link in _links)
            {
                if (Channels.Hears(link.Channel, code))
                    link.Exceptions.AddRanked(new PendingEvent(code, []));
            }
        }
    }

    private void QueueOrdinary(byte code) => Queue(code, [], priority: false);

    /// <summary>Takes an exception back off every channel, as internalDequeueSpecificException does.</summary>
    private void Withdraw(byte code)
    {
        lock (_gate)
        {
            foreach (MachineLink link in _links)
                link.Exceptions.Remove(code);
        }
    }

    private static long BcdValue(ReadOnlySpan<byte> bytes)
    {
        long value = 0;
        foreach (byte b in bytes)
            value = (value * 100) + ((b >> 4) * 10) + (b & 0x0F);
        return value;
    }
}
