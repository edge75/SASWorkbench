namespace SasWorkbench.Egm;

/// <summary>One queued exception and whatever it carries.</summary>
internal readonly record struct PendingEvent(byte Code, byte[] Data);

/// <summary>
/// One channel's exception queue: sixty-four deep, oldest lost on overflow, and the exception last
/// sent on a general poll held until the host's next message acknowledges or refuses it.
/// </summary>
/// <remarks>
/// Not thread-safe; the machine guards every call with its own lock.
/// </remarks>
internal sealed class ExceptionQueue
{
    /// <summary>The gaming machine library's queue depth.</summary>
    public const int Depth = 64;

    /// <summary>Exception buffer overflow, a priority exception that is never itself queued.</summary>
    public const byte Overflow = 0x70;

    private readonly LinkedList<PendingEvent> _queue = new();
    private bool _overflowPending;
    private PendingEvent? _unacknowledged;
    private int _repeats;

    /// <summary>Called with an exception's code once the host has acknowledged it, as the library's dequeue does.</summary>
    public Action<byte>? Acknowledged { get; set; }

    /// <summary>Exceptions still to be sent, the overflow included and the one awaiting acknowledgement not.</summary>
    public int Count => _queue.Count + (_overflowPending ? 1 : 0);

    /// <summary>Queues an exception, losing the oldest and owing an overflow when the queue is full.</summary>
    public void Add(PendingEvent pending)
    {
        if (_queue.Count >= Depth)
        {
            _queue.RemoveFirst();
            _overflowPending = true;
        }

        _queue.AddLast(pending);
    }

    /// <summary>Queues a priority exception ahead of every ordinary one, as the library sends them first.</summary>
    public void AddPriority(PendingEvent pending) => _queue.AddFirst(pending);

    /// <summary>
    /// Where the library ranks a priority exception, one going first; an ordinary exception ranks last
    /// (internalGetExceptionPriority, libsas_internal.cpp).
    /// </summary>
    public static int RankOf(byte code) => code switch
    {
        0x57 => 1,
        0x67 => 2,
        0x68 => 3,
        0x3F => 4,
        0x6A => 5,
        0x6B => 6,
        0x6F => 7,
        0x56 => 8,
        0x3D => 9,
        0x3E => 10,
        0x69 => 11,
        0x6C => 12,
        0x6D => 13,
        0x51 => 14,
        0x52 => 15,
        0x8F => 16,
        Overflow => 17,
        _ => int.MaxValue,
    };

    /// <summary>
    /// Queues a priority exception the way the library's queue does: never twice, and ahead of the
    /// first one queued that ranks below it (internalQueueException, libsas_internal.cpp).
    /// </summary>
    public void AddRanked(PendingEvent pending)
    {
        if (_unacknowledged?.Code == pending.Code || _queue.Any(queued => queued.Code == pending.Code))
            return;

        if (_queue.Count >= Depth)
            DropOldestOrdinary();

        int rank = RankOf(pending.Code);
        for (LinkedListNode<PendingEvent>? node = _queue.First; node is not null; node = node.Next)
        {
            if (RankOf(node.Value.Code) <= rank)
                continue;

            _queue.AddBefore(node, pending);
            return;
        }

        Add(pending);
    }

    /// <summary>Takes every queued copy of one exception back out, as the library does when what it reported has ended.</summary>
    public void Remove(byte code)
    {
        for (LinkedListNode<PendingEvent>? node = _queue.First; node is not null;)
        {
            LinkedListNode<PendingEvent>? next = node.Next;
            if (node.Value.Code == code)
                _queue.Remove(node);
            node = next;
        }
    }

    private void DropOldestOrdinary()
    {
        for (LinkedListNode<PendingEvent>? node = _queue.First; node is not null; node = node.Next)
        {
            if (RankOf(node.Value.Code) != int.MaxValue)
                continue;

            _queue.Remove(node);
            _overflowPending = true;
            return;
        }
    }

    /// <summary>Takes the next exception outright, with no acknowledgement to wait for.</summary>
    public PendingEvent? Take()
    {
        if (_overflowPending)
        {
            _overflowPending = false;
            return new PendingEvent(Overflow, []);
        }

        if (_queue.First is not { } first)
            return null;

        _queue.RemoveFirst();
        return first.Value;
    }

    /// <summary>
    /// What a general poll addressed to this machine draws, given whether it repeats the one
    /// before it with nothing in between.
    /// </summary>
    /// <remarks>
    /// A repeat is a refusal and draws the same exception again; the third in a row is final, so
    /// it is answered once more and the exception goes back to the head of the queue.
    /// </remarks>
    public PendingEvent? NextForGeneralPoll(bool repeat)
    {
        if (repeat && _unacknowledged is { } refused)
        {
            _repeats++;
            if (_repeats >= 2)
                Refuse();
            return refused;
        }

        Acknowledge();
        _unacknowledged = Take();
        return _unacknowledged;
    }

    /// <summary>The host's next message was not a repeat, so the exception last sent was received.</summary>
    public void Acknowledge()
    {
        PendingEvent? acknowledged = _unacknowledged;
        _unacknowledged = null;
        _repeats = 0;
        if (acknowledged is { } sent)
            Acknowledged?.Invoke(sent.Code);
    }

    /// <summary>Empties the queue outright, as a memory clear zeroes the library's copy of it (libsas_internal.cpp:756).</summary>
    public void Clear()
    {
        _queue.Clear();
        _overflowPending = false;
        _unacknowledged = null;
        _repeats = 0;
    }

    /// <summary>A final refusal: the exception last sent goes back to the head of the queue.</summary>
    public void Refuse()
    {
        if (_unacknowledged is { } refused)
        {
            if (refused.Code == Overflow)
                _overflowPending = true;
            else
                _queue.AddFirst(refused);
        }

        _unacknowledged = null;
        _repeats = 0;
    }
}
