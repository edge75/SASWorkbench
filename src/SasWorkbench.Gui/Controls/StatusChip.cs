using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace SasWorkbench.Gui.Controls;

/// <summary>The states a chip can report. Distinct from a verdict: not every chip is a verdict.</summary>
public enum ChipStatus
{
    Pass,
    Fail,
    Skip,
    Error,
    Undecoded,
    Unknown,
    Warning,
}

/// <summary>
/// A status chip: the word, a fill treatment, and a vector glyph.
/// </summary>
/// <remarks>
/// All three cues are required. Pass and fail cannot be separated by colour under deuteranopia in
/// any palette we measured, so the word, the failure-only solid fill and the glyph are the whole
/// mitigation — making a chip icon-only, or outlining failure to match the others, silently breaks
/// the accessibility contract. Glyphs are geometry rather than font characters so a minimal font
/// set cannot turn one into a blank box.
/// </remarks>
public sealed class StatusChip : TemplatedControl
{
    public static readonly StyledProperty<ChipStatus> StatusProperty =
        AvaloniaProperty.Register<StatusChip, ChipStatus>(nameof(Status));

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<StatusChip, string?>(nameof(Label));

    public static readonly StyledProperty<string?> ReasonProperty =
        AvaloniaProperty.Register<StatusChip, string?>(nameof(Reason));

    public static readonly DirectProperty<StatusChip, Geometry?> GlyphProperty =
        AvaloniaProperty.RegisterDirect<StatusChip, Geometry?>(nameof(Glyph), o => o.Glyph);

    public static readonly DirectProperty<StatusChip, string> WordProperty =
        AvaloniaProperty.RegisterDirect<StatusChip, string>(nameof(Word), o => o.Word);

    private Geometry? _glyph;
    private string _word = string.Empty;

    public StatusChip() => Refresh();

    /// <summary>Which state this chip reports.</summary>
    public ChipStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <summary>Overrides the word, when a caller has a better one than the state's own.</summary>
    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Why, for a screen reader. A skip or a failure without one is a chip that hides.</summary>
    public string? Reason
    {
        get => GetValue(ReasonProperty);
        set => SetValue(ReasonProperty, value);
    }

    /// <summary>The glyph drawn beside the word.</summary>
    public Geometry? Glyph
    {
        get => _glyph;
        private set => SetAndRaise(GlyphProperty, ref _glyph, value);
    }

    /// <summary>The word actually shown.</summary>
    public string Word
    {
        get => _word;
        private set => SetAndRaise(WordProperty, ref _word, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StatusProperty
            || change.Property == LabelProperty
            || change.Property == ReasonProperty)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        Word = Label ?? Status switch
        {
            ChipStatus.Pass => "PASS",
            ChipStatus.Fail => "FAIL",
            ChipStatus.Skip => "SKIP",
            ChipStatus.Error => "ERROR",
            ChipStatus.Undecoded => "UNDECODED",
            ChipStatus.Unknown => "UNKNOWN",
            ChipStatus.Warning => "WARNING",
            _ => Status.ToString().ToUpperInvariant(),
        };

        Glyph = ChipGlyphs.For(Status);

        foreach (ChipStatus state in Enum.GetValues<ChipStatus>())
            PseudoClasses.Set($":{state.ToString().ToLowerInvariant()}", state == Status);

        AutomationProperties.SetName(this, Reason is null ? Word : $"{Word} — {Reason}");
    }
}

/// <summary>The vector glyphs, drawn once and shared.</summary>
internal static class ChipGlyphs
{
    private static readonly Geometry Check = Parse("M1,5.4 L3.8,8.2 L9,1.8");
    private static readonly Geometry Cross = Parse("M1.6,1.6 L8.4,8.4 M8.4,1.6 L1.6,8.4");
    private static readonly Geometry Slashed = Parse(
        "M5,0.8 A4.2,4.2 0 1 1 4.96,0.8 M2.05,7.95 L7.95,2.05");
    private static readonly Geometry Question = Parse(
        "M2.4,3.1 A2.6,2.6 0 1 1 5,5.9 L5,6.9 M5,8.6 L5,8.8");
    private static readonly Geometry Dash = Parse("M1,5 L9,5");
    private static readonly Geometry Bang = Parse("M5,1.2 L5,6.1 M5,8.4 L5,8.6");

    public static Geometry For(ChipStatus status) => status switch
    {
        ChipStatus.Pass => Check,
        ChipStatus.Fail => Cross,
        ChipStatus.Skip => Slashed,
        ChipStatus.Error => Bang,
        ChipStatus.Undecoded => Question,
        ChipStatus.Unknown => Dash,
        ChipStatus.Warning => Bang,
        _ => Dash,
    };

    private static Geometry Parse(string path) => StreamGeometry.Parse(path);
}
