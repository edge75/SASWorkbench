using System.Globalization;

namespace SasWorkbench.Qpm;

/// <summary>One kind of game's draws for an outcome, as the scripted build's queue takes them.</summary>
/// <param name="Game">The kind of game, which names the alternative in the queue and the log.</param>
/// <param name="Draws">Each draw's highest value and the value it returns, in the order the game draws.</param>
/// <param name="PaytableCreditsPerMultiplier">What the outcome pays from the game's paytable, per bet multiplier.</param>
public sealed record OutcomeAlternative(string Game, IReadOnlyList<(uint Max, uint Value)> Draws, long PaytableCreditsPerMultiplier)
{
    public string ToQueueText() =>
        Game + "=" + string.Join(',', Draws.Select(d => string.Create(CultureInfo.InvariantCulture, $"{d.Max}:{d.Value}")));
}

/// <summary>
/// The outcomes the measured demo (QSP_DemoOpenGL v2.0.4.37) can be given, as values of its own draws (D64).
/// </summary>
/// <remarks>
/// The slot draws one stop per reel over strips of 16, 17, 16, 15 and 14 symbols (<c>Game_01/Math_01.cpp:17-21</c>,
/// <c>:151</c>); a reel shows its stop in the middle row (<c>:215</c>). The wheel draws one of 20 segments
/// (<c>Game_02/WheelGame.cpp:425</c>, <c>WheelMath.h:20</c>). Each outcome was checked against the demo's way and
/// scatter rules (<c>Math_01.cpp:231-376</c>) and its jackpot rule (<c>:383-403</c>); the unit tests check it again.
/// </remarks>
public static class DemoOutcomes
{
    /// <summary>The highest value of each of the slot's five reel draws.</summary>
    public static readonly uint[] SlotReelMax = [15, 16, 15, 14, 13];

    /// <summary>The highest value of the wheel's one draw.</summary>
    public const uint WheelMax = 19;

    /// <summary>The credits five jackpot symbols pay from the slot's paytable, per multiplier (<c>Math_01.cpp:41</c>).</summary>
    public const long SlotJackpotLineCredits = 1000;

    /// <summary>A game that wins nothing on either kind of game.</summary>
    public static IReadOnlyList<OutcomeAlternative> Lose { get; } =
    [
        // Reels show no symbol on reel 2 that reel 1 starts, no wild and no scatter.
        Slot([0, 2, 0, 0, 0], 0),

        // Segment 0 pays nothing (WheelMath.cpp:12).
        new("wheel", [(WheelMax, 0)], 0),
    ];

    /// <summary>The alternatives that win exactly these progressive levels, or none when no game of the demo can.</summary>
    /// <remarks>
    /// Level 1 is Grand and level 2 Major (<c>Math_01.h:21</c>). The slot pays Grand for five jackpot symbols on a way,
    /// Grand and Major for five on the middle row, Major alone for five on the bottom row; the five pay their line too.
    /// The wheel's draw reaches Major alone, on segment 3, which pays nothing more; its Grand comes only from the
    /// demo's own cheat, which no draw sets.
    /// </remarks>
    public static IReadOnlyList<OutcomeAlternative> ForLevels(IReadOnlyCollection<int> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        var wanted = new SortedSet<int>(levels);

        if (wanted.SetEquals([1]))
            return [Slot([12, 13, 13, 13, 12], SlotJackpotLineCredits)];       // jackpots on the top row

        if (wanted.SetEquals([1, 2]))
            return [Slot([11, 12, 12, 12, 11], SlotJackpotLineCredits)];       // jackpots on the middle row

        if (wanted.SetEquals([2]))
        {
            return
            [
                Slot([10, 11, 11, 11, 10], SlotJackpotLineCredits),            // jackpots on the bottom row
                new("wheel", [(WheelMax, 3)], 0),
            ];
        }

        return [];
    }

    private static OutcomeAlternative Slot(uint[] stops, long paytable) =>
        new("slot", [.. stops.Select((stop, reel) => (SlotReelMax[reel], stop))], paytable);
}
