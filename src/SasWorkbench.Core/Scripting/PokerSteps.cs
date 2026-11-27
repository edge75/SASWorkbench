namespace SasWorkbench.Core.Scripting;

/// <summary>What the player does to a dealt poker hand.</summary>
public enum PokerAction
{
    /// <summary>Holds one card.</summary>
    Hold,

    /// <summary>Releases one card held.</summary>
    Release,

    /// <summary>Asks for the cards not held to be replaced.</summary>
    Draw,
}

/// <summary>
/// The player holds or releases a card of the dealt hand, or draws, written <c>egm poker hold|release &lt;1-5&gt;</c>
/// and <c>egm poker draw</c> (D61).
/// </summary>
/// <param name="Location">Where it was written.</param>
/// <param name="Action">What the player does.</param>
/// <param name="Position">The card, 1 to 5 from the left; zero for a draw.</param>
public sealed record PokerStep(SourceLocation Location, PokerAction Action, int Position) : StimulusStep(Location)
{
    /// <summary>The capability of a machine whose poker game a script can play.</summary>
    public const string Capability = "stimulus.poker";

    public override string RequiredCapability => Capability;

    public override string Describe() => Action switch
    {
        PokerAction.Draw => "poker draw",
        PokerAction.Hold => $"poker hold {Position}",
        _ => $"poker release {Position}",
    };

    public override string? Guidance => Action switch
    {
        PokerAction.Draw => "Press draw on the dealt hand.",
        PokerAction.Hold => $"Hold card {Position}, counting from the left.",
        _ => $"Release card {Position}, counting from the left.",
    };
}
