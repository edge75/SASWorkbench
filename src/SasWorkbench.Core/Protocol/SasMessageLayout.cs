namespace SasWorkbench.Core.Protocol;

/// <summary>What kind of answer a long poll produces.</summary>
public enum SasResponseShape
{
    /// <summary>A reply carrying data, ending in a check sequence.</summary>
    Data,

    /// <summary>The machine answers with its address alone, or rejects with the address and bit 7.</summary>
    Acknowledgement,

    /// <summary>Nothing comes back at all, by design.</summary>
    None,
}

/// <summary>
/// The layout of one long poll: what the host sends, what comes back, and where the layout
/// itself was established.
/// </summary>
/// <remarks>
/// <see cref="DerivedFrom"/> is not a comment. Every layout here was read out of the gaming
/// machine library's own handler for that command, and naming the handler means a reviewer can
/// check the claim in one step instead of taking it on trust. A layout with no provenance is a
/// guess, and a guess that decodes plausibly is worse than one that fails.
/// </remarks>
public sealed record SasMessageLayout
{
    /// <summary>The command byte.</summary>
    public required byte Command { get; init; }

    /// <summary>A short name a test can use instead of the code.</summary>
    public required string Name { get; init; }

    /// <summary>One sentence, in our own words, on what the command asks for.</summary>
    public required string Description { get; init; }

    /// <summary>What the host sends after the address and command bytes.</summary>
    public IReadOnlyList<SasField> Request { get; init; } = [];

    /// <summary>What comes back after the echoed address and command bytes.</summary>
    public IReadOnlyList<SasField> Response { get; init; } = [];

    /// <summary>What shape the answer takes.</summary>
    public SasResponseShape Shape { get; init; } = SasResponseShape.Data;

    /// <summary>
    /// The library handler this layout was read from, so the claim can be checked.
    /// </summary>
    public required string DerivedFrom { get; init; }

    /// <summary>
    /// True when this command may be sent to the broadcast address rather than to one machine.
    /// </summary>
    public bool Broadcast { get; init; }

    /// <summary>
    /// True when the reply has been checked against bytes captured from a real machine, rather
    /// than only against the library source.
    /// </summary>
    public bool ConfirmedByCapture { get; init; }

    /// <summary>Total bytes of a fixed-length reply, address and check sequence included.</summary>
    public int? FixedResponseLength
    {
        get
        {
            if (Shape != SasResponseShape.Data)
                return null;

            int total = 2;
            foreach (SasField part in Response)
            {
                if (part.Length == SasField.Remaining || part.IsGroup || part.PresentWhen is not null)
                    return null;
                total += part.Length;
            }

            return total + 2;
        }
    }

    public override string ToString() => $"${Command:X2} {Name}";
}
