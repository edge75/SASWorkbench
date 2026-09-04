using Avalonia.Media;

namespace SasWorkbench.Gui.Controls;

/// <summary>
/// The bracket conventions that keep the three status families apart, with an ASCII fallback.
/// </summary>
/// <remarks>
/// Verdicts are chips, data states are angle-bracketed and provenance stamps are double-bracketed,
/// so the three can never be read as each other. The angle brackets are the only characters here
/// that a minimal font set might not have, so they are probed once and swapped for ASCII rather
/// than left to render as a blank box.
/// </remarks>
public static class Typography
{
    private static bool? _ascii;

    /// <summary>True when the resolved fonts cannot draw the angle brackets.</summary>
    public static bool AsciiOnly => _ascii ??= !CanRender('⟨');

    /// <summary>A data state: <c>⟨known, undecoded⟩</c>, or <c>&lt;known, undecoded&gt;</c>.</summary>
    public static string DataState(string text) =>
        AsciiOnly ? $"<{text}>" : $"⟨{text}⟩";

    /// <summary>A provenance stamp, never abbreviated.</summary>
    public static string Stamp(string word) => $"[[ {word.ToUpperInvariant()} ]]";

    private static bool CanRender(char c)
    {
        try
        {
            return FontManager.Current.TryGetGlyphTypeface(Typeface.Default, out IGlyphTypeface? face)
                   && face.TryGetGlyph(c, out ushort glyph)
                   && glyph != 0;
        }
        catch (Exception)
        {
            // A font stack we cannot probe is a font stack we should not gamble on.
            return false;
        }
    }
}
