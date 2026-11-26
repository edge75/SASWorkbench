namespace SasWorkbench.Core.Scripting;

/// <summary>The capabilities of a game played on a chosen game, its bet button and its recall screen (D59).</summary>
public static class PlayedGameCapabilities
{
    /// <summary>The play stimulus plays the game the player chose, so each game's meters can be moved on their own.</summary>
    public const string PlayGame = "stimulus.play.game";

    /// <summary>The machine has a game recall screen.</summary>
    public const string GameRecall = "machine.game_recall";

    /// <summary>A script can put a past game back on the screen.</summary>
    public const string GameRecallStimulus = "stimulus.game_recall";

    /// <summary>The player can commit credits one at a time with a bet button.</summary>
    public const string BetButton = "stimulus.bet_button";

    /// <summary>The machine reports that it waits for the player, a wager placed and the game not started.</summary>
    public const string PlayerInputState = "machine.player_input_state";

    /// <summary>The machine answers some long poll with the busy reply while it cannot serve it.</summary>
    public const string GameBusy = "machine.game_busy";

    /// <summary>The host can assert the busy reply, the address followed by a zero.</summary>
    public const string BusyReply = "host.busy_reply";

    /// <summary>A case can add the movements of several games' meters and compare them with the machine's.</summary>
    public const string GameMeterDelta = "host.game_meter_delta";

    /// <summary>The host can assert silence on a poll whose length is carried in the message.</summary>
    public const string LengthPrefixedSilence = "host.length_prefixed_silence";
}

/// <summary>The player presses the bet-one button once, committing one more credit to the next game.</summary>
public sealed record BetButtonStep(SourceLocation Location) : StimulusStep(Location)
{
    public override string RequiredCapability => PlayedGameCapabilities.BetButton;

    public override string Describe() => "press bet";

    public override string? Guidance => "Press the bet-one button once.";
}

/// <summary>The attendant puts a past game on the recall screen, zero being the last game played.</summary>
public sealed record GameRecallStep(SourceLocation Location, int Index) : StimulusStep(Location)
{
    public override string RequiredCapability => PlayedGameCapabilities.GameRecallStimulus;

    public override string Describe() => $"game recall {Index}";

    public override string? Guidance => Index == 0
        ? "Open the game recall screen on the last game played."
        : $"On the game recall screen, show the game played {Index} game(s) before the last.";
}

/// <summary>
/// Expects a long poll to be met with the busy reply: the machine's address followed by a single zero.
/// </summary>
/// <remarks>
/// A busy reply is neither an answer nor a refusal: the command was received and the machine could not serve it in
/// that moment. Anything else - the poll's own reply, an acknowledgement, a refusal or silence - fails the step.
/// </remarks>
public sealed record ExpectBusyReplyStep(SourceLocation Location, byte Command, byte[] Data) : ScriptStep(Location)
{
    public override string Describe() => $"expect busy reply to lp {Command:X2}";
}
