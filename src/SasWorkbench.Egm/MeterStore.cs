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

        lock (_gate)
        {
            long updated = _values.GetValueOrDefault((ushort)meter) + amount;
            _values[(ushort)meter] = updated;
            return updated;
        }
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
