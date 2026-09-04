using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace SasWorkbench.Gui.Controls;

/// <summary>
/// The stamp that says how much a link is worth: <c>[[ PROVEN ]]</c> or <c>[[ UNVERIFIED ]]</c>.
/// </summary>
/// <remarks>
/// Unverified is hatched and italic as well as differently coloured, because the whole point of
/// the stamp is that it survives a greyscale screenshot of somebody else's run.
/// </remarks>
public sealed class ProvenanceSeal : TemplatedControl
{
    public static readonly StyledProperty<bool> IsProvenProperty =
        AvaloniaProperty.Register<ProvenanceSeal, bool>(nameof(IsProven), defaultValue: true);

    public static readonly StyledProperty<string?> CaveatProperty =
        AvaloniaProperty.Register<ProvenanceSeal, string?>(nameof(Caveat));

    public static readonly DirectProperty<ProvenanceSeal, string> WordProperty =
        AvaloniaProperty.RegisterDirect<ProvenanceSeal, string>(nameof(Word), o => o.Word);

    private string _word = string.Empty;

    public ProvenanceSeal() => Refresh();

    /// <summary>Whether this link has been exercised end to end, or only implemented.</summary>
    public bool IsProven
    {
        get => GetValue(IsProvenProperty);
        set => SetValue(IsProvenProperty, value);
    }

    /// <summary>The sentence a reader needs in order not to over-read the result.</summary>
    public string? Caveat
    {
        get => GetValue(CaveatProperty);
        set => SetValue(CaveatProperty, value);
    }

    /// <summary>The stamp as drawn.</summary>
    public string Word
    {
        get => _word;
        private set => SetAndRaise(WordProperty, ref _word, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsProvenProperty || change.Property == CaveatProperty)
            Refresh();
    }

    private void Refresh()
    {
        Word = Typography.Stamp(IsProven ? "proven" : "unverified");
        PseudoClasses.Set(":proven", IsProven);
        PseudoClasses.Set(":unverified", !IsProven);
        AutomationProperties.SetName(this, Caveat is null ? Word : $"{Word} — {Caveat}");
    }
}
