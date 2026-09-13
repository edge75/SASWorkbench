using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Diagnostics;

/// <summary>
/// A fixed-size ring of recent messages, written from the poll thread and read from anywhere.
/// </summary>
/// <remarks>
/// Storage is allocated once at construction and reused, so recording the wire adds no
/// allocation to the poll loop. When the ring wraps, the oldest messages are lost and
/// <see cref="Dropped"/> says how many — a report should say so rather than present a log with
/// a silent hole in it.
/// </remarks>
public sealed class WireLog
{
    private readonly Lock _gate = new();
    private readonly Slot[] _slots;
    private long _sequence;
    private int _next;
    private int _filled;

    public WireLog(int capacity = 4096, int maxSymbolsPerMessage = 260)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _slots = new Slot[capacity];
        for (int i = 0; i < _slots.Length; i++)
            _slots[i] = new Slot(maxSymbolsPerMessage);
    }

    /// <summary>How many messages the log can hold before it starts overwriting.</summary>
    public int Capacity => _slots.Length;

    /// <summary>How many messages have been recorded since the log was created.</summary>
    public long Recorded
    {
        get
        {
            lock (_gate)
                return _sequence;
        }
    }

    /// <summary>How many messages were overwritten before anyone read them.</summary>
    public long Dropped { get; private set; }

    /// <summary>Records one message. Called from the poll thread; allocates nothing.</summary>
    public void Record(
        WireDirection direction,
        SasChannel channel,
        SasAddress? address,
        ReadOnlySpan<SasSymbol> symbols,
        TimeSpan elapsed,
        string? note)
    {
        lock (_gate)
        {
            Slot slot = _slots[_next];
            if (_filled == _slots.Length)
                Dropped++;

            slot.Sequence = _sequence++;
            slot.Direction = direction;
            slot.Channel = channel;
            slot.Address = address;
            slot.Elapsed = elapsed;
            slot.Note = note;
            slot.Length = Math.Min(symbols.Length, slot.Symbols.Length);
            slot.Truncated = symbols.Length > slot.Symbols.Length;
            symbols[..slot.Length].CopyTo(slot.Symbols);

            _next = (_next + 1) % _slots.Length;
            if (_filled < _slots.Length)
                _filled++;
        }
    }

    /// <summary>Copies out everything currently held, oldest first.</summary>
    public IReadOnlyList<WireRecord> Snapshot()
    {
        lock (_gate)
        {
            var records = new List<WireRecord>(_filled);
            int start = (_next - _filled + _slots.Length) % _slots.Length;
            for (int i = 0; i < _filled; i++)
            {
                Slot slot = _slots[(start + i) % _slots.Length];
                records.Add(new WireRecord(
                    slot.Sequence,
                    slot.Direction,
                    slot.Channel,
                    slot.Elapsed,
                    slot.Symbols[..slot.Length].ToArray(),
                    slot.Truncated ? $"{slot.Note} (truncated)".TrimStart() : slot.Note)
                {
                    Address = slot.Address,
                });
            }

            return records;
        }
    }

    /// <summary>Forgets everything held, without resetting the sequence numbering.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _filled = 0;
            _next = 0;
        }
    }

    private sealed class Slot(int maxSymbols)
    {
        public readonly SasSymbol[] Symbols = new SasSymbol[maxSymbols];
        public long Sequence;
        public WireDirection Direction;
        public SasChannel Channel;
        public SasAddress? Address;
        public TimeSpan Elapsed;
        public int Length;
        public bool Truncated;
        public string? Note;
    }
}
