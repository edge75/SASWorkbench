using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Egm;

/// <summary>
/// The meters a gaming machine keeps. Every meter accumulates and none of them ever goes down.
/// </summary>
/// <remarks>
/// The store refuses to decrement, and that refusal is deliberate. SAS meters only ever
/// accumulate, so a fixture that allowed a meter to fall would let a test pass that should have
/// failed — and relative assertions, which are the discipline this project uses, depend on the
/// direction being guaranteed.
/// </remarks>
public sealed class MeterStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ushort, long> _values = new();

    /// <summary>Told of every amount added to a meter, after the store has taken it; a multi-game machine charges its game here.</summary>
    public Action<ushort, long>? Incremented { get; set; }

    /// <summary>Reads a meter. An unset meter reads as zero, the way a fresh machine does.</summary>
    public long Read(SasMeterCode meter) => Read((ushort)meter);

    /// <summary>Reads a meter by code.</summary>
    public long Read(ushort meter)
    {
        lock (_gate)
            return _values.GetValueOrDefault(meter);
    }

    /// <summary>Adds to a meter.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The amount is negative.</exception>
    public long Increment(SasMeterCode meter, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        long updated;
        lock (_gate)
        {
            updated = _values.GetValueOrDefault((ushort)meter) + amount;
            _values[(ushort)meter] = updated;
        }

        Incremented?.Invoke((ushort)meter, amount);
        return updated;
    }

    /// <summary>
    /// Sets a meter outright. Only for preparing a starting state before a run; a machine has no
    /// such operation.
    /// </summary>
    public void Preset(SasMeterCode meter, long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        lock (_gate)
            _values[(ushort)meter] = value;
    }

    /// <summary>
    /// The one meter that is not an accumulator: credits on the machine go up and down.
    /// </summary>
    public void SetCurrentCredits(long credits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(credits);

        lock (_gate)
            _values[(ushort)SasMeterCode.CurrentCredits] = credits;
    }

    /// <summary>The restricted part of the credit meter, which goes up and down with it.</summary>
    public void SetCurrentRestrictedCredits(long credits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(credits);

        lock (_gate)
            _values[(ushort)SasMeterCode.CurrentRestrictedCredits] = credits;
    }

    /// <summary>
    /// Starts a since-an-event meter again, which is what that event does to it.
    /// </summary>
    /// <remarks>
    /// The library keeps two meters counted since something happened rather than since the
    /// machine was built - games since the last power up, and games since the slot door last
    /// closed - and each starts again when the thing it is counted from happens. That is the
    /// meter behaving correctly, not a fixture cheating, so it is a named operation with two
    /// permitted meters rather than a general way round the no-decrement rule.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The meter is not one of the two.</exception>
    public void Restart(SasMeterCode meter)
    {
        if (meter is not (SasMeterCode.GamesSinceLastReset or SasMeterCode.GamesSinceSlotDoorClosure))
            throw new ArgumentOutOfRangeException(
                nameof(meter), meter, "Only a meter counted since an event starts again.");

        lock (_gate)
            _values[(ushort)meter] = 0;
    }

    /// <summary>A copy of every meter that has been touched, for a baseline or a report.</summary>
    public IReadOnlyDictionary<ushort, long> Snapshot()
    {
        lock (_gate)
            return new Dictionary<ushort, long>(_values);
    }

    /// <summary>
    /// Clears every meter, as a RAM clear would. Only a test that declares it should ask for this.
    /// </summary>
    public void ClearAll()
    {
        lock (_gate)
            _values.Clear();
    }
}
