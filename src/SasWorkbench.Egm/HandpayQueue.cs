namespace SasWorkbench.Egm;

/// <summary>Where one queued hand pay has got to with the host (HANDPAY_ITEM_STATE, libsas_db.h:314).</summary>
internal enum HandpayItemState
{
    None,
    Registered,
    Ack,
    InfoSent,
    InfoAck,
    Reset,
    ResetAck,
}

/// <summary>One queued hand pay as long poll 1B reports it (sHandpayItem), its amount already in the unit it is reported in.</summary>
internal sealed record HandpayItem(byte Group, byte Level, long Amount, HandpayItemState State)
{
    /// <summary>The partial pay, in accounting units; zero unless a case declares one (D58).</summary>
    public long PartialPay { get; init; }

    /// <summary>The reset methods reported: 00 standard only, 01 onto the credit meter too (QSP SasNetwork_Conditions.hpp:784).</summary>
    public byte ResetId { get; init; }
}

/// <summary>What a step of the queue asks to be written to the channel's exception queue.</summary>
[Flags]
internal enum HandpayWrite
{
    None = 0,

    /// <summary>Hand pay pending, $51.</summary>
    Pending = 1,

    /// <summary>Hand pay was reset, $52.</summary>
    WasReset = 2,

    /// <summary>Every queued $51 taken back out (internalDequeueSpecificException).</summary>
    WithdrawPending = 4,

    /// <summary>The player cancelled the hand pay request, $55.</summary>
    Cancelled = 8,
}

/// <summary>
/// One channel's hand pay queue, mirrored from the machine library check for check: register, the $51 and
/// $52 acknowledgements, long poll 1B, the dequeue its acknowledgement runs and the attendant's reset.
/// </summary>
/// <remarks>Not thread-safe; the machine guards every call with its own lock.</remarks>
internal sealed class HandpayQueue
{
    /// <summary>The library's queue length (HANDPAY_BUFFER_LENGTH, libsas_db.h:72).</summary>
    public const int Length = 16;

    private readonly List<HandpayItem> _items = new(Length);

    /// <summary>Resets the attendant has made that $52 has not yet reported (sHandpay.resetQueued).</summary>
    public int ResetQueued { get; private set; }

    /// <summary>How many hand pays the queue holds (itemsCounter).</summary>
    public int Count => _items.Count;

    /// <summary>The queued hand pays, oldest first.</summary>
    public IReadOnlyList<HandpayItem> Items => _items;

    private HandpayItemState Front => _items.Count > 0 ? _items[0].State : HandpayItemState.None;

    private void SetFront(HandpayItemState state) => _items[0] = _items[0] with { State = state };

    /// <summary>Appends a hand pay, the oldest overwritten when the queue is full (libsas_internal.cpp:4855).</summary>
    public void Register(HandpayItem item)
    {
        if (_items.Count == Length)
            _items.RemoveAt(0);

        _items.Add(item with { State = HandpayItemState.Registered });
    }

    /// <summary>The newest entry changed in place, for a win the platform adds to the hand pay it is still locked up for.</summary>
    public void AmendNewest(byte group, byte level, long amount)
    {
        if (_items.Count > 0)
            _items[^1] = _items[^1] with { Group = group, Level = level, Amount = amount };
    }

    /// <summary>The reset ID of the entry long poll A8 looks at, the first not yet reset (lp.cpp:9651).</summary>
    public byte CurrentResetId() =>
        _items.Find(item => item.State is HandpayItemState.Registered or HandpayItemState.Ack
            or HandpayItemState.InfoSent or HandpayItemState.InfoAck)?.ResetId ?? 0;

    /// <summary>internalQueueException drops a $52 while the front entry still waits behind others (libsas_internal.cpp:3993).</summary>
    public bool Accepts(byte code) => code != 0x52 || !(_items.Count > 1 && Front == HandpayItemState.Registered);

    /// <summary>
    /// internalIsHandpayAwaitingLp1b: the front entry has been announced and not yet read and acknowledged,
    /// the only state in which $51 is issued again (libsas_internal.cpp:5048).
    /// </summary>
    public bool AwaitingRead => _items.Count > 0 && Front is HandpayItemState.Ack or HandpayItemState.InfoSent;

    /// <summary>
    /// internalUpdateHandpayTransaction for an acknowledged $51 (libsas_internal.cpp:4981).
    /// </summary>
    /// <returns>True when the fifteen-second timer is to be armed.</returns>
    public bool PendingAcknowledged(bool synced)
    {
        if (_items.Count > 0 && Front is HandpayItemState.Registered or HandpayItemState.ResetAck)
        {
            SetFront(HandpayItemState.Ack);
            return true;
        }

        for (int j = 0; j < _items.Count; j++)
        {
            if (synced && _items[j].State is HandpayItemState.Registered or HandpayItemState.ResetAck)
                _items[j] = _items[j] with { State = HandpayItemState.Ack };
        }

        return false;
    }

    /// <summary>internalUpdateHandpayTransaction for an acknowledged $52, which dequeues the entry it reported (libsas_internal.cpp:5014).</summary>
    public HandpayWrite ResetAcknowledged(bool synced)
    {
        if (_items.Count == 0 || Front != HandpayItemState.Reset)
            return HandpayWrite.None;

        SetFront(HandpayItemState.ResetAck);
        return Dequeue(synced);
    }

    /// <summary>
    /// internalDequeueHandpay, run when a 1B reply or a $52 is acknowledged (libsas_internal.cpp:5068).
    /// </summary>
    public HandpayWrite Dequeue(bool synced)
    {
        if (_items.Count == 0)
            return HandpayWrite.None;

        while (true)
        {
            if (Front == HandpayItemState.InfoSent && synced)
            {
                if (ResetQueued > 0)
                {
                    SetFront(HandpayItemState.Reset);
                    return HandpayWrite.WithdrawPending | HandpayWrite.WasReset;
                }

                SetFront(HandpayItemState.InfoAck);
                return HandpayWrite.None;
            }

            if (Front == HandpayItemState.ResetAck)
            {
                ResetQueued--;
                _items.RemoveAt(0);
                if (_items.Count > 0)
                    continue;

                return HandpayWrite.None;
            }

            if (Front == HandpayItemState.Registered && synced)
            {
                // A second hand pay is announced only once the first one's $52 has been sent and acknowledged.
                SetFront(HandpayItemState.ResetAck);
                return HandpayWrite.Pending;
            }

            // Any other front state is left as it is, as the library logs and leaves it.
            return HandpayWrite.None;
        }
    }

    /// <summary>
    /// lp1B_SendHandpayInformation: the first entry the host is still owed, or zeros, and the state the reading
    /// moves it to (lp.cpp:1022).
    /// </summary>
    /// <param name="legacy">Legacy reporting, in which an acknowledged reading still reports the hand pay (SAS 6.02 7.8.2).</param>
    /// <param name="synced">The channel is synchronised with its host.</param>
    /// <param name="dequeueOnAcknowledgement">True when the reply's acknowledgement is to run <see cref="Dequeue"/>.</param>
    /// <returns>The entry reported, or null for a reply of zeros.</returns>
    public HandpayItem? Read(bool legacy, bool synced, out bool dequeueOnAcknowledgement)
    {
        HandpayItem? reported = null;
        int index = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            HandpayItemState state = _items[i].State;
            if (state is HandpayItemState.Ack or HandpayItemState.Registered or HandpayItemState.InfoSent
                || (state == HandpayItemState.InfoAck && legacy))
            {
                reported = _items[i];
                index = i;
                break;
            }

            if (state is HandpayItemState.Reset or HandpayItemState.InfoAck)
            {
                index = i;
                break;
            }
        }

        if (_items.Count > 0)
        {
            HandpayItemState state = _items[index].State;
            if (state is HandpayItemState.Ack or HandpayItemState.Registered)
            {
                if (synced)
                    _items[index] = _items[index] with { State = HandpayItemState.InfoSent };
            }
            else if (state == HandpayItemState.InfoSent && !synced)
            {
                _items[index] = _items[index] with { State = HandpayItemState.Ack };
            }
        }

        dequeueOnAcknowledgement = _items.Count > 0 && _items[index].State != HandpayItemState.Reset;
        return reported;
    }

    /// <summary>
    /// internalHandpayReset for this channel: the reset is counted, and $52 is written at once when the reading
    /// has been acknowledged, or in legacy reporting whenever the front entry is still owed (libsas_internal.cpp:5276).
    /// </summary>
    /// <returns>False when the reset matches no registered entry, which the library refuses.</returns>
    public bool Reset(bool legacy, out HandpayWrite write)
    {
        write = HandpayWrite.None;
        if (ResetQueued == Length)
            ResetQueued--;

        if (ResetQueued >= _items.Count)
            return false;

        ResetQueued++;

        bool legacyResetOfPendingEntry = legacy
            && Front is HandpayItemState.Registered or HandpayItemState.Ack or HandpayItemState.InfoSent;
        if (Front == HandpayItemState.InfoAck || legacyResetOfPendingEntry)
        {
            SetFront(HandpayItemState.Reset);
            write = HandpayWrite.WasReset;
        }

        return true;
    }

    /// <summary>
    /// internalHandpayRefuse for this channel: behind another entry the newest is dropped silently; otherwise the
    /// cancellation is counted like a reset and $55 is written while the front entry is not yet reset
    /// (libsas_internal.cpp:5401, :5448, :5477).
    /// </summary>
    /// <param name="linkUp">The channel's link is neither down nor broken.</param>
    /// <returns>False when the cancellation matches no registered entry, which the library refuses.</returns>
    public bool Refuse(bool linkUp, out HandpayWrite write)
    {
        write = HandpayWrite.None;
        if (_items.Count > 1 && Front >= HandpayItemState.Registered)
        {
            _items.RemoveAt(_items.Count - 1);
            return true;
        }

        if (ResetQueued >= _items.Count)
            return false;

        if (linkUp)
            ResetQueued++;
        else
            _items.RemoveAt(_items.Count - 1);

        if (_items.Count > 0 && Front <= HandpayItemState.InfoAck)
        {
            SetFront(HandpayItemState.Reset);
            write = HandpayWrite.Cancelled;
        }

        return true;
    }

    /// <summary>
    /// What the library does at power up: an entry still awaiting its reading is registered again and $51 is
    /// written, whichever reporting mode is in force (libsas_internal.cpp:887).
    /// </summary>
    public HandpayWrite Restart()
    {
        if (!AwaitingRead)
            return HandpayWrite.None;

        SetFront(HandpayItemState.Registered);
        return HandpayWrite.Pending;
    }

    /// <summary>A memory clear zeroes the library's image, the queue with it.</summary>
    public void Clear()
    {
        _items.Clear();
        ResetQueued = 0;
    }
}
