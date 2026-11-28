using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Qpm;

/// <summary>Outcomes given to the real demo through its scripted build's queue (D64).</summary>
public sealed partial class QpmStimulusBackend
{
    /// <summary>The capability a scripted build offers, beside naming itself in <c>machine.build</c>.</summary>
    public const string ScriptedOutcomesCapability = "stimulus.scripted_outcomes";

    /// <summary>What the scripted outcomes offer, read from a live marker; nothing is offered by a plain build but the step that then refuses.</summary>
    private static void OfferOutcomes(
        Dictionary<string, string?> values, EgmHardwareProfile profile, DemoProcess? demo, ScriptedOutcomes? outcomes)
    {
        if (!profile.TryFindButton("play", out _))
            return;

        // Offered either way, so a case on a plain build is skipped by preflight naming the missing marker.
        values["stimulus.progressive"] = "yes";

        if (outcomes?.WhyNotScripted(demo?.ExecutablePath) is null && outcomes is not null)
        {
            values[ScriptedOutcomesCapability] = "yes";
            values[Core.Scripting.RunProvenance.MachineBuildCapability] = ScriptedOutcomes.Label;
        }
    }

    /// <summary>Why no outcome can be given to the machine running now, or null when its build takes them.</summary>
    private string? WhyNoOutcomes(string described)
    {
        string? why = outcomes is null
            ? "no scripted-outcomes directory was given"
            : outcomes.WhyNotScripted(demo?.ExecutablePath);
        return why is null
            ? null
            : $"'{described}' cannot be driven: the machine decides its own outcomes unless it runs the scripted-outcomes "
              + $"build, and {why}";
    }

    private string? RefuseProgressive(ProgressiveWinStep win)
    {
        string described = win.Describe();
        IReadOnlyList<OutcomeAlternative> alternatives = DemoOutcomes.ForLevels(win.Levels);
        if (alternatives.Count == 0)
        {
            return $"'{described}' cannot be drawn: the machine's games reach level 1 alone, levels 1 and 2 together, or "
                   + "level 2 alone, and nothing else";
        }

        if (win.WinStated && alternatives.Any(a => a.PaytableCreditsPerMultiplier != 0 || win.WinCredits != 0))
        {
            return $"'{described}' cannot be drawn: the paytable part of the machine's jackpot outcome is its own (on the "
                   + $"slot the five jackpot symbols also pay {DemoOutcomes.SlotJackpotLineCredits} credits per bet "
                   + "multiplier), so a stated win cannot be honoured";
        }

        return WhyNoOutcomes(described) ?? Missing(Machine.Profile, "play") ?? (win.BetStated ? WhyNoBet(win.BetCredits) : null);
    }

    private string? RefusePlay(PlayGamesStep play)
    {
        if (!play.WinStated && play.WinCredits == 0)
            return Missing(Machine.Profile, "play") ?? WhyNoBet(play.BetCredits);

        if (play.WinCredits != 0)
        {
            return $"'{play.Describe()}' cannot be drawn: the machine decides its own outcomes, and only a losing game is "
                   + "scripted on it, because reel "
                   + "stops that pay an exact amount would need a second copy of its pay table here";
        }

        return WhyNoOutcomes(play.Describe()) ?? Missing(Machine.Profile, "play") ?? WhyNoBet(play.BetCredits);
    }

    // Confirmed by the build's own log, not by the wire: a jackpot can lock the machine up before its game ends.
    private void WinProgressive(ProgressiveWinStep win)
    {
        EgmButton? bet = win.BetStated ? BetFor(win.BetCredits) : null;
        if (bet is not null)
            Machine.PressButton(bet);

        DrawScripted(DemoOutcomes.ForLevels(win.Levels), Button("play"), win.Describe());
    }

    /// <summary>Queues the outcome, presses play and waits for the build to log it drawn in full.</summary>
    private void DrawScripted(IReadOnlyList<OutcomeAlternative> alternatives, EgmButton play, string described)
    {
        ScriptedOutcomes queue = outcomes!;
        int skip = queue.LogLineCount();
        string id = queue.Queue(alternatives);
        Machine.PressButton(play);

        if (queue.WaitForDrawn(id, skip, GameStartWithin) is { } why)
        {
            queue.Withdraw();
            throw new StimulusUnavailableException($"'{described}': {why}");
        }
    }
}
