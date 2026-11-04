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
