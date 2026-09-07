namespace SasWorkbench.Core.Diagnostics;

/// <summary>
/// Why the host itself put a message on the wire, where no step of a script asked for it.
/// </summary>
/// <remarks>
/// <para>
/// Written onto the record at the moment the message is sent, never worked out afterwards. A
/// runner sends polls of its own: it asks a machine which validation style it performs before a
/// case that requires one, and it reads a meter to establish what a relative assertion is
/// measured against. A reader of the log is entitled to know which packets those were, and the
/// alternative — inferring it from where a packet sits relative to the steps — would be a guess
/// dressed as an explanation.
/// </para>
/// <para>
/// The reasons are a closed set, so a reader matches rather than parses: a label carrying
/// anything else is a label from another build and is reported as it stands.
/// </para>
/// </remarks>
public static class WirePurpose
{
    /// <summary>What separates a message's own label from the reason it was sent.</summary>
    public const string Separator = " · ";

    /// <summary>The enabled-features poll a case needing a machine-answered capability requires.</summary>
    public const string CapabilityProbe = "capability probe";

    /// <summary>A meter read to establish what a relative assertion is measured against.</summary>
    public const string MeterBaseline = "meter baseline";

    /// <summary>Every reason this build writes, so a reader can match one rather than guess.</summary>
    public static IReadOnlyList<string> All { get; } = [CapabilityProbe, MeterBaseline];

    /// <summary>Puts the reason on a message's label, or leaves the label alone.</summary>
    public static string? Label(string? note, string? purpose) =>
        string.IsNullOrEmpty(purpose)
            ? note
            : string.IsNullOrEmpty(note) ? purpose : note + Separator + purpose;

    /// <summary>Reads the reason back off a label, when it carries one this build wrote.</summary>
    public static bool TryRead(string? note, out string purpose)
    {
        purpose = string.Empty;
        if (string.IsNullOrEmpty(note))
            return false;

        foreach (string candidate in All)
        {
            if (note.EndsWith(Separator + candidate, StringComparison.Ordinal)
                || string.Equals(note, candidate, StringComparison.Ordinal))
            {
                purpose = candidate;
                return true;
            }
        }

        return false;
    }
}
