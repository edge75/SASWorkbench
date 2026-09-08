namespace SasWorkbench.Core.Overlays;

/// <summary>Which description of the protocol a fact came from.</summary>
public enum OverlaySource
{
    /// <summary>This build's own catalogue, generated from the gaming machine library's headers.</summary>
    Catalogue,

    /// <summary>The legacy tester's configuration file.</summary>
    LegacyTester,

    /// <summary>The conformance toolkit's message templates.</summary>
    ConformanceToolkit,
}

/// <summary>
/// How a source describes one field, reduced to the vocabulary all three have in common.
/// </summary>
/// <remarks>
/// The reduction is the point. Each source has its own element names, and comparing those names
/// would only say that three tools chose three spellings. What is worth comparing is what a
/// field's bytes are and how many there are, so a kind here is a claim about the wire and not
/// about a schema.
/// </remarks>
public enum OverlayFieldKind
{
    /// <summary>The source names the field but says nothing this build can map onto the wire.</summary>
    Unclassified,

    /// <summary>An unsigned integer.</summary>
    Binary,

    /// <summary>Two decimal digits per byte.</summary>
    Bcd,

    /// <summary>Characters.</summary>
    Ascii,

    /// <summary>A single flag.</summary>
    Flag,

    /// <summary>One of a named set of values.</summary>
    Choice,

    /// <summary>Named bits inside one or more bytes.</summary>
    Bitmap,

    /// <summary>Bytes with no stated reading.</summary>
    Raw,

    /// <summary>A run whose length is not fixed by the description.</summary>
    Variable,

    /// <summary>A group that repeats.</summary>
    Repeating,
}

/// <summary>One field of a message as some source describes it.</summary>
/// <param name="Name">The source's own name for it, held for comparison and never shipped.</param>
/// <param name="Kind">What the bytes are.</param>
/// <param name="Length">How many bytes, or null when the source does not fix a length.</param>
/// <param name="Offset">Where the source puts it, counting the address as byte one, when it says.</param>
/// <param name="Native">The source's own word for the field's type, for the expressiveness diff.</param>
public sealed record OverlayField(
    string Name,
    OverlayFieldKind Kind,
    int? Length,
    int? Offset = null,
    string? Native = null);

/// <summary>One long poll as some source describes it.</summary>
/// <remarks>
/// <para>
/// <see cref="Name"/> is the source's own wording. It is read into memory so that two sources can
/// be told apart when they disagree, and it goes nowhere else: no overlay word is written into
/// the catalogue, into a layout, or into the record a run produces. That is decision D4 applied to
/// a diff — their content decides what we model, and every word this product ships is our own.
/// </para>
/// </remarks>
public sealed record OverlayCommand
{
    public required byte Code { get; init; }

    /// <summary>The source's own name for the command.</summary>
    public required string Name { get; init; }

    /// <summary>What the host sends after the address and command bytes, as the source has it.</summary>
    public IReadOnlyList<OverlayField> Request { get; init; } = [];

    /// <summary>What comes back, as the source has it.</summary>
    public IReadOnlyList<OverlayField> Response { get; init; } = [];

    /// <summary>Whole bytes the host sends, address to check sequence, when the source fixes it.</summary>
    public int? RequestLength { get; init; }

    /// <summary>Whole bytes the machine answers with, when the source fixes it.</summary>
    public int? ResponseLength { get; init; }

    /// <summary>True when the source says the message carries a check sequence.</summary>
    public bool? HasCheckSequence { get; init; }

    /// <summary>True when the source says the reply carries no data, only an acknowledgement.</summary>
    public bool? AcknowledgementOnly { get; init; }

    /// <summary>Where in the source this came from, so a claim can be checked in one step.</summary>
    public required string Origin { get; init; }

    /// <summary>True when the source describes a reply for this command at all.</summary>
    public bool DescribesReply => Response.Count > 0 || ResponseLength is not null;
}

/// <summary>One exception as some source describes it.</summary>
public sealed record OverlayException
{
    public required byte Code { get; init; }

    /// <summary>The source's own name for it.</summary>
    public required string Name { get; init; }

    /// <summary>Fields the source says accompany it, when it says any.</summary>
    public IReadOnlyList<OverlayField> Fields { get; init; } = [];

    public required string Origin { get; init; }
}

/// <summary>
/// One description of the SAS protocol, read from a file this project did not write.
/// </summary>
/// <remarks>
/// <see cref="Unreadable"/> carries everything a reader met and could not turn into a fact.
/// A reader that dropped those would make a partial overlay look like a whole one, and the diff
/// taken over it would report a disagreement that is really a hole in the reading.
/// </remarks>
public sealed record ProtocolOverlay
{
    public required OverlaySource Source { get; init; }

    /// <summary>What was read, in our words.</summary>
    public required string Description { get; init; }

    /// <summary>The files it was read from.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    public IReadOnlyDictionary<byte, OverlayCommand> Commands { get; init; } =
        new Dictionary<byte, OverlayCommand>();

    public IReadOnlyDictionary<byte, OverlayException> Exceptions { get; init; } =
        new Dictionary<byte, OverlayException>();

    /// <summary>
    /// Codes the source lists with a placeholder rather than a meaning, kept apart from both
    /// "named" and "absent" because they are neither.
    /// </summary>
    public IReadOnlyList<byte> PlaceholderExceptions { get; init; } = [];

    /// <summary>
    /// Codes the source has no command-list entry for and can nonetheless send by another route,
    /// with how many ways it offers. Kept apart from both "named" and "absent" because a tool that
    /// can send a command only by assembling it from a stored structure has it, and does not have
    /// it in the list a diff would otherwise read.
    /// </summary>
    public IReadOnlyDictionary<byte, int> SentAnotherWay { get; init; } =
        new Dictionary<byte, int>();

    /// <summary>Anything in the files this reader could not turn into a fact, named rather than dropped.</summary>
    public IReadOnlyList<string> Unreadable { get; init; } = [];

    /// <summary>
    /// Constructs the shape a source uses that this reduction cannot carry, so the diff can say
    /// that a comparison is coarser than the source it came from.
    /// </summary>
    public IReadOnlyList<string> NotCarried { get; init; } = [];
}
