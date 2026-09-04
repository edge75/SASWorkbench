namespace SasWorkbench.Core.Protocol;

/// <summary>One decoded field.</summary>
/// <param name="Path">
/// How a test names it. Members of a repeating group carry an index, as in
/// <c>meters[2].value</c>.
/// </param>
/// <param name="Field">The field this came from.</param>
/// <param name="Offset">Where it started in the frame, so a failure can point at a byte.</param>
/// <param name="Bytes">The bytes themselves, always kept, so a test can assert on the wire.</param>
/// <param name="Number">The value as a number, when the encoding produces one.</param>
/// <param name="Text">The value as text, when the encoding produces one.</param>
/// <param name="Violation">
/// Why the value breaks the field's constraint, when it does. A violation never stops decoding:
/// the rest of the message is still worth seeing, and a test that asserts on another field
/// should still get a useful answer.
/// </param>
public sealed record SasValue(
    string Path,
    SasField Field,
    int Offset,
    byte[] Bytes,
    long? Number = null,
    string? Text = null,
    string? Violation = null)
{
    /// <summary>True when the value satisfies its constraint.</summary>
    public bool IsValid => Violation is null;

    /// <summary>The bytes as hex, for a report.</summary>
    public string ToHex() => Convert.ToHexString(Bytes);

    public override string ToString()
    {
        string rendered = Text ?? Number?.ToString() ?? ToHex();
        string unit = Field.Unit == SasUnit.None ? string.Empty : $" {Field.Unit}";
        string violation = Violation is null ? string.Empty : $"  ** {Violation}";
        return $"{Path} = {rendered}{unit}{violation}";
    }
}

/// <summary>The outcome of decoding one reply against a layout.</summary>
/// <param name="Command">The command the reply answers.</param>
/// <param name="Layout">The layout that was applied.</param>
/// <param name="Values">Every field that was decoded, in order.</param>
/// <param name="Failure">
/// Why decoding could not finish, when it could not. A short reply, a field running past the
/// end, or bytes that are not valid where the layout says they must be.
/// </param>
public sealed record SasDecoded(
    byte Command,
    SasMessageLayout? Layout,
    IReadOnlyList<SasValue> Values,
    string? Failure = null)
{
    /// <summary>True when every field was decoded, whether or not all of them are valid.</summary>
    public bool Succeeded => Failure is null;

    /// <summary>Every field whose value breaks its constraint.</summary>
    public IEnumerable<SasValue> Violations => Values.Where(v => !v.IsValid);

    /// <summary>Looks a field up by path. Case-insensitive, because scripts are written by hand.</summary>
    public SasValue? this[string path] =>
        Values.FirstOrDefault(v => string.Equals(v.Path, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads a field as a number, or null when it is absent or not numeric.</summary>
    public long? Number(string path) => this[path]?.Number;

    /// <summary>Reads a field as text, or null when it is absent or not textual.</summary>
    public string? Text(string path) => this[path]?.Text;

    /// <summary>Every path that was decoded, for an error message that lists what was available.</summary>
    public IEnumerable<string> Paths => Values.Select(v => v.Path);
}
