namespace SasWorkbench.Core.Protocol;

public static partial class SasLayouts
{
    /// <summary>The replies to the remote hand pay reset and to the four tournament meters, alone and together.</summary>
    private static SasMessageLayout[] Tournament() =>
    [
        new()
        {
            Command = 0x94,
            Name = "remote_handpay_reset",
            Description = "Clears a hand pay from the host; the code says whether it was cleared.",
            Response =
            [
                Byte("reset_code", SasUnit.Code,
                    "00 the hand pay was reset, 01 it could not be, 02 there was no hand pay to reset."),
            ],
            DerivedFrom = "lp94_RemoteHandpayReset",
        },
        TournamentMeterLayout(0x95, "tournament_games_played",
            Bcd("games_played", 4, SasUnit.Count, "Games played in the tournament session."),
            "lp95_SendTournamentGamesPlayed"),
        TournamentMeterLayout(0x96, "tournament_games_won",
            Bcd("games_won", 4, SasUnit.Count, "Games won in the tournament session."),
            "lp96_SendTournamentGamesWon"),
        TournamentMeterLayout(0x97, "tournament_credits_wagered",
            Bcd("credits_wagered", 4, SasUnit.Credits, "Credits wagered in the tournament session."),
            "lp97_SendTournamentCreditsWagered"),
        TournamentMeterLayout(0x98, "tournament_credits_won",
            Bcd("credits_won", 4, SasUnit.Credits, "Credits won in the tournament session."),
            "lp98_SendTournamentCreditsWon"),
        TournamentMeterLayout(0x99, "tournament_meters",
            [
                Bcd("games_played", 4, SasUnit.Count, "Games played in the tournament session."),
                Bcd("games_won", 4, SasUnit.Count, "Games won in the tournament session."),
                Bcd("credits_wagered", 4, SasUnit.Credits, "Credits wagered in the tournament session."),
                Bcd("credits_won", 4, SasUnit.Credits, "Credits won in the tournament session."),
            ],
            "lp99_SendMeters95Through98"),
    ];

    /// <summary>A tournament meter reply: the game number echoed, then four-byte BCD counts (lp.cpp:274, :9264).</summary>
    private static SasMessageLayout TournamentMeterLayout(byte command, string name, SasField meter, string handler) =>
        TournamentMeterLayout(command, name, [meter], handler);

    private static SasMessageLayout TournamentMeterLayout(byte command, string name, SasField[] meters, string handler) =>
        GameMeterLayout(command, name, "Tournament meters for one game.", handler, meters) with { ConfirmedByCapture = false };
}
