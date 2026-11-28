namespace SasWorkbench.Qpm.Tests;

/// <summary>
/// Each scripted outcome checked against the measured demo's own slot rules, so a constant that does not win what it
/// claims fails here rather than on the cabinet.
/// </summary>
/// <remarks>
/// The evaluator below is a test-only reading of QSP_DemoOpenGL v2.0.4.37 <c>Game_01/Math_01.cpp</c>: the reel strips
/// (lines 17-21), the pay table (29-42), the window (207-224), the way search (231-332), the scatter pay (339-376) and
/// the jackpot levels (383-403). It is deliberately kept out of the product: the host never computes a win, it only
/// queues these fixed stops and the build's log confirms they were drawn.
/// </remarks>
public class DemoOutcomesTests
{
    [Fact]
    public void TheLosingSlotStopsPayNothingAndShowNoScatter()
    {
        uint[] stops = SlotStops(DemoOutcomes.Lose.Single(a => a.Game == "slot"));
        (long won, int[] levels, int scatters) = Slot.Evaluate(stops);

        Assert.Equal(0, won);
        Assert.Empty(levels);
        Assert.True(scatters < 3);
    }

    [Theory]
    [InlineData(new[] { 1 }, new[] { 1 })]
    [InlineData(new[] { 1, 2 }, new[] { 1, 2 })]
    [InlineData(new[] { 2 }, new[] { 2 })]
    public void EachJackpotOutcomeWinsExactlyItsLevelsAndPaysOnlyTheJackpotLine(int[] asked, int[] expected)
    {
        uint[] stops = SlotStops(DemoOutcomes.ForLevels(asked).Single(a => a.Game == "slot"));
        (long won, int[] levels, _) = Slot.Evaluate(stops);

        Assert.Equal(expected, levels);
        Assert.Equal(DemoOutcomes.SlotJackpotLineCredits, won);
    }

    [Fact]
    public void TheWheelLosesOnSegmentZeroAndWinsMajorAloneOnSegmentThree()
    {
        Assert.Equal((DemoOutcomes.WheelMax, 0u), DemoOutcomes.Lose.Single(a => a.Game == "wheel").Draws.Single());
        Assert.Equal((DemoOutcomes.WheelMax, 3u), DemoOutcomes.ForLevels([2]).Single(a => a.Game == "wheel").Draws.Single());
        Assert.DoesNotContain(DemoOutcomes.ForLevels([1]), a => a.Game == "wheel");
    }

    [Fact]
    public void EveryStopIsInsideItsReelAndTheRangesAreTheStrips()
    {
        Assert.Equal([.. Slot.Strips.Select(s => (uint)s.Length - 1)], DemoOutcomes.SlotReelMax);
        foreach (int[] levels in new[] { new[] { 1 }, [1, 2], [2] })
        {
            foreach (OutcomeAlternative alternative in DemoOutcomes.ForLevels(levels).Concat(DemoOutcomes.Lose))
                Assert.All(alternative.Draws, d => Assert.InRange(d.Value, 0u, d.Max));
        }
    }

    [Fact]
    public void NoOtherLevelSetIsOffered()
    {
        Assert.Empty(DemoOutcomes.ForLevels([3]));
        Assert.Empty(DemoOutcomes.ForLevels([1, 3]));
    }

    private static uint[] SlotStops(OutcomeAlternative slot) => [.. slot.Draws.Select(d => d.Value)];

    private static class Slot
    {
        private const int W = 0, P1 = 1, P2 = 2, P3 = 3, P4 = 4, P5 = 5, P6 = 6, P7 = 7, P8 = 8, Sc = 9, Jp = 10;

        public static readonly int[][] Strips =
        [
            [P1, P2, P3, P4, P5, Sc, P8, P6, P5, P7, P6, Jp, P7, P8, P6, P5],
            [W, P8, P7, P6, P5, P4, Sc, P8, P5, P6, P3, P7, Jp, P2, P1, P6, P7],
            [W, P1, P8, P2, P7, Sc, P3, P7, P5, P6, P8, P6, Jp, P4, P5, P8],
            [W, P3, P5, P7, P1, Sc, P4, P8, P6, P5, P7, P6, Jp, P8, P2],
            [P2, P5, P8, P1, P4, P7, Sc, P5, P6, P8, P7, Jp, P3, P6],
        ];

        private static readonly long[][] Pays =
        [
            [0, 0, 0, 0, 0], [0, 10, 100, 500, 1000], [0, 0, 50, 200, 500], [0, 0, 40, 100, 250], [0, 0, 40, 100, 250],
            [0, 0, 20, 60, 100], [0, 0, 20, 60, 100], [0, 0, 10, 40, 60], [0, 0, 10, 40, 60], [0, 0, 2, 10, 50],
            [0, 0, 0, 0, 1000],
        ];

        public static (long Won, int[] Levels, int Scatters) Evaluate(uint[] stops)
        {
            var window = new int[15];
            for (int reel = 0; reel < 5; reel++)
            {
                int size = Strips[reel].Length;
                for (int row = 0; row < 3; row++)
                    window[reel * 3 + row] = Strips[reel][(((int)stops[reel] + row - 1) % size + size) % size];
            }

            var wins = new List<(int Symbol, int Count)>();
            long won = 0;
            for (int row = 0; row < 3; row++)
            {
                if (window[row] != Sc)
                    won += Search(window, window[row], 1, 0, wins);
            }

            int scatters = window.Count(s => s == Sc);
            if (scatters >= 3)
                won += 50 * Pays[Sc][scatters - 1];

            bool Row(int row) => Enumerable.Range(0, 5).All(reel => window[reel * 3 + row] == Jp);
            int[] levels = Row(1) ? [1, 2] : Row(2) ? [2] : wins.Any(w => w.Symbol == Jp && w.Count == 5) ? [1] : [];
            return (won, levels, scatters);
        }

        private static long Search(int[] window, int symbol, int reel, int wild, List<(int, int)> wins)
        {
            long win = 0;
            for (int row = 0; row < 3; row++)
            {
                int next = window[reel * 3 + row];
                if (next == Sc || (next != symbol && next != W && symbol != W))
                    continue;

                if (symbol == W && next != W)
                {
                    if (reel < 4)
                    {
                        win += Search(window, next, reel + 1, reel, wins);
                        continue;
                    }

                    wild = reel;
                }

                if (reel < 4)
                {
                    win += Search(window, symbol, reel + 1, wild, wins);
                }
                else
                {
                    (int paid, int last) = Substitute(symbol, reel, wild);
                    wins.Add((paid, last + 1));
                    win += Pays[paid][last];
                }
            }

            if (win > 0)
                return win;

            (int s, int r) = Substitute(symbol, reel, wild);
            if (Pays[s][r - 1] > 0)
            {
                wins.Add((s, r));
                win += Pays[s][r - 1];
            }

            return win;
        }

        private static (int Symbol, int Reel) Substitute(int symbol, int reel, int wild)
        {
            if (wild > 0 && Pays[P1][wild - 1] > Pays[symbol][reel - 1])
            {
                symbol = W;
                reel--;
            }

            return (symbol == W ? P1 : symbol, reel);
        }
    }
}
