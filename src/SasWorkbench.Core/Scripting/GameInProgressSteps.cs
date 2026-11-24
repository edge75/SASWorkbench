namespace SasWorkbench.Core.Scripting;

/// <summary>Starts one game and leaves it running, so a poll or a bonus can arrive before it ends.</summary>
public sealed record StartGameStep(SourceLocation Location, long BetCredits) : StimulusStep(Location)
{
    public override string RequiredCapability => "stimulus.game_in_progress";

    public override string Describe() => $"game start bet={BetCredits}";

    public override string? Guidance =>
        $"Start a game at {BetCredits} credit(s) and leave it running, for example in a bonus round or before the draw.";
}

/// <summary>Ends the game a <see cref="StartGameStep"/> left running, with what it won.</summary>
public sealed record EndGameStep(SourceLocation Location, long WinCredits) : StimulusStep(Location)
{
    public override string RequiredCapability => "stimulus.game_in_progress";

    public override string Describe() => $"game end win={WinCredits}";

    public override string? Guidance =>
        WinCredits > 0 ? $"Finish the running game with a win of {WinCredits} credit(s)." : "Finish the running game with no win.";
}

/// <summary>Presses collect and keeps the cash out in progress for a while, so a poll can arrive during it.</summary>
public sealed record SlowCashoutStep(SourceLocation Location, TimeSpan Lasting) : StimulusStep(Location)
{
    public override string RequiredCapability => "stimulus.cashout_in_progress";

    public override string Describe() => $"press collect lasting {Lasting.TotalSeconds:0.#}s";

    public override string? Guidance =>
        $"Press collect; the cash out must still be under way about {Lasting.TotalSeconds:0.#} seconds later (a ticket printing).";
}

/// <summary>Sets the jackpot limit in cents at the machine's own configuration; a win above it is paid by hand, and zero removes it.</summary>
public sealed record SetJackpotLimitStep(SourceLocation Location, long Cents) : StimulusStep(Location)
{
    public override string RequiredCapability => "stimulus.jackpot_limit";

    public override string Describe() => $"set jackpot limit {Cents} cents";
}

/// <summary>Installs or updates a component that takes part in authentication, changing the component list.</summary>
public sealed record ComponentChangeStep(SourceLocation Location) : StimulusStep(Location)
{
    public override string RequiredCapability => "stimulus.component_change";

    public override string Describe() => "component change";

    public override string? Guidance =>
        "Add, remove, update or reorder a component that takes part in authentication, by the manufacturer's means.";
}
