using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Transport;

/// <summary>
/// A bounded one-way symbol queue with a blocking read, used to join two in-process endpoints.
/// </summary>
/// <remarks>
/// A fixed ring rather than a growing queue: the hot path must not allocate, and a link that
/// has stopped being drained should behave like a real one and lose symbols rather than
/// consume memory until something falls over.
/// </remarks>
internal sealed class SymbolPipe
{
    private readonly ushort[] _ring;
    // A plain object, not a Lock: this pipe waits and pulses, which Monitor provides.
    private readonly object _gate = new();
    private int _head;
    private int _count;
    private bool _closed;

    public SymbolPipe(int capacity = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        _ring = new ushort[capacity];
    }

    /// <summary>How many symbols were dropped because the reader was not keeping up.</summary>
    public long Dropped { get; private set; }

    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            Monitor.PulseAll(_gate);
        }
    }

    public void Write(ReadOnlySpan<SasSymbol> symbols)
    {
        lock (_gate)
        {
            if (_closed)
                return;

            foreach (SasSymbol symbol in symbols)
            {
                if (_count == _ring.Length)
                {
                    // Drop the oldest, the way a UART overrun does.
                    _head = (_head + 1) % _ring.Length;
                    _count--;
                    Dropped++;
                }

                _ring[(_head + _count) % _ring.Length] = symbol.Raw;
                _count++;
            }

            Monitor.PulseAll(_gate);
        }
    }

    public int Read(Span<SasSymbol> destination, TimeSpan timeout)
    {
        if (destination.IsEmpty)
            return 0;

        long deadline = Environment.TickCount64 + (long)Math.Max(0, timeout.TotalMilliseconds);

        lock (_gate)
        {
            while (_count == 0 && !_closed)
            {
                long remaining = deadline - Environment.TickCount64;
                if (remaining <= 0)
                    return 0;
                Monitor.Wait(_gate, (int)Math.Min(remaining, int.MaxValue));
            }

            int taken = Math.Min(destination.Length, _count);
            for (int i = 0; i < taken; i++)
            {
                destination[i] = SasSymbol.FromRawTruncating(_ring[_head]);
                _head = (_head + 1) % _ring.Length;
            }

            _count -= taken;
            return taken;
        }
    }
}
