namespace SasWorkbench.Core.Protocol;

/// <summary>How the bytes of a field are to be read.</summary>
public enum SasFieldEncoding
{
    /// <summary>Plain unsigned integer.</summary>
    Binary,

    /// <summary>Binary coded decimal — two decimal digits per byte. Every meter uses it.</summary>
    Bcd,

    /// <summary>Text, as characters.</summary>
    Ascii,

    /// <summary>Decimal digits held as text, to be read as a number.</summary>
    AsciiNumber,

    /// <summary>A bit field, asserted on by name and mask rather than as a number.</summary>
    Bits,

    /// <summary>Bytes with no interpretation, compared as bytes.</summary>
    Raw,
}

/// <summary>Which end of a multi-byte field comes first.</summary>
/// <remarks>
/// Not decoration. Most SAS fields are big-endian, but not all: the game options in the
/// gaming machine identification reply are little-endian, and reading them the other way
/// gives a plausible wrong answer rather than an obvious one. A model with no byte-order
/// attribute cannot express the difference, which is why this one has it.
/// </remarks>
public enum SasByteOrder
{
    /// <summary>A single byte, or text.</summary>
    NotApplicable,

    /// <summary>Most significant byte first.</summary>
    BigEndian,

    /// <summary>Least significant byte first.</summary>
    LittleEndian,
}

/// <summary>What a field's number means, so a report and an assertion can agree.</summary>
public enum SasUnit
{
    None,

    /// <summary>Credits, at the machine's accounting denomination.</summary>
    Credits,

    /// <summary>Cents, or the smallest unit of the machine's currency.</summary>
    Cents,

    /// <summary>A plain count of events.</summary>
    Count,

    /// <summary>Hundredths of a percent, the form a base payback percentage takes.</summary>
    HundredthsOfPercent,

    Milliseconds,

    Seconds,

    /// <summary>An index into a table, such as a game number.</summary>
    Index,

    /// <summary>A code whose meaning comes from the catalogue rather than its value.</summary>
    Code,
}

/// <summary>How a value may be compared with another.</summary>
public enum SasComparison
{
    Equal,
    NotEqual,
    LessThan,
    AtMost,
    GreaterThan,
    AtLeast,

    /// <summary>Every bit of the operand is set in the value.</summary>
    BitsSet,

    /// <summary>No bit of the operand is set in the value.</summary>
    BitsClear,
}

/// <summary>A condition on an earlier field, deciding whether a later one is present at all.</summary>
/// <remarks>
/// The gap this closes is real: several replies carry fields only when a status byte says so.
/// A model without conditions has to describe those messages as several unrelated messages,
/// or bespoke element types — which is exactly what happens to models that cannot express it.
/// </remarks>
/// <param name="Field">Name of an earlier field in the same message.</param>
/// <param name="Comparison">How to compare it.</param>
/// <param name="Value">What to compare it against.</param>
public sealed record SasCondition(string Field, SasComparison Comparison, long Value)
{
    public override string ToString() => $"{Field} {Comparison} {Value}";
}

/// <summary>How many times a group repeats.</summary>
/// <param name="FixedCount">A count known in advance.</param>
/// <param name="CountFromField">Name of an earlier field holding the count.</param>
/// <param name="UntilEndOfMessage">Repeat until the check sequence is reached.</param>
public sealed record SasRepeat(
    int? FixedCount = null,
    string? CountFromField = null,
    bool UntilEndOfMessage = false)
{
    /// <summary>A group that repeats as many times as the remaining bytes allow.</summary>
    public static SasRepeat ToEndOfMessage { get; } = new(UntilEndOfMessage: true);

    /// <summary>A group repeated a fixed number of times.</summary>
    public static SasRepeat Times(int count) => new(FixedCount: count);

    /// <summary>A group repeated as many times as an earlier field says.</summary>
    public static SasRepeat CountedBy(string field) => new(CountFromField: field);
}

/// <summary>
/// What a field's value is allowed to be. Violations are findings, not decode failures: the
/// bytes are still reported, alongside the reason they are wrong.
/// </summary>
/// <param name="Minimum">Smallest permitted value.</param>
/// <param name="Maximum">Largest permitted value.</param>
/// <param name="OneOf">The only permitted values.</param>
/// <param name="Length">An exact byte length, for a variable-length field.</param>
public sealed record SasConstraint(
    long? Minimum = null,
    long? Maximum = null,
    IReadOnlyList<long>? OneOf = null,
    int? Length = null)
{
    public static SasConstraint Between(long minimum, long maximum) => new(minimum, maximum);

    public static SasConstraint AnyOf(params long[] values) => new(OneOf: values);
}

/// <summary>One field of a SAS message.</summary>
public sealed record SasField
{
    /// <summary>Use as <see cref="Length"/> to mean everything up to the check sequence.</summary>
    public const int Remaining = -1;

    /// <summary>The name a test uses to refer to this field.</summary>
    public required string Name { get; init; }

    /// <summary>How many bytes it occupies, or <see cref="Remaining"/>.</summary>
    public required int Length { get; init; }

    public SasFieldEncoding Encoding { get; init; } = SasFieldEncoding.Binary;

    public SasByteOrder ByteOrder { get; init; } = SasByteOrder.BigEndian;

    public SasUnit Unit { get; init; } = SasUnit.None;

    /// <summary>One sentence, in our own words, describing what the field carries.</summary>
    public string? Description { get; init; }

    /// <summary>What the value is allowed to be.</summary>
    public SasConstraint? Constraint { get; init; }

    /// <summary>Set to make this field a repeating group described by <see cref="Fields"/>.</summary>
    public SasRepeat? Repeat { get; init; }

    /// <summary>The members of a repeating group.</summary>
    public IReadOnlyList<SasField> Fields { get; init; } = [];

    /// <summary>When set, the field is present only if this holds.</summary>
    public SasCondition? PresentWhen { get; init; }

    /// <summary>
    /// True when this field is an accumulating meter a test may read by name.
    /// </summary>
    /// <remarks>
    /// Stated rather than inferred. A unit of credits does not make a field a meter: the amount
    /// owed on a pending handpay is measured in cents and is a status, not an accumulator, and
    /// treating it as a meter offers a test a reading that only exists while a handpay is
    /// outstanding.
    /// </remarks>
    public bool IsMeter { get; init; }

    /// <summary>True when this field is a repeating group rather than a scalar.</summary>
    public bool IsGroup => Fields.Count > 0;

    public override string ToString() => IsGroup
        ? $"{Name}[{Repeat}]"
        : $"{Name}:{Length}:{Encoding}";
}
