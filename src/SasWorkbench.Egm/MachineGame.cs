using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Egm;

/// <summary>
/// One game of a multi-game software machine, as the platform configures it for the library.
/// </summary>
/// <remarks>
/// The fields are the ones <c>SAS_ConfigureGame</c> takes from the platform (QSP SasNetwork.cpp:1243): one
/// denomination, a maximum bet in credits of that denomination, a payback, a name and a paytable. The three switches
/// are the library's game status, which long poll $09 sets (lp.cpp:563), and the platform's operator and host
/// switches, whose conjunction it tells the library as the denomination being enabled (QSP SasNetwork.cpp:1407).
/// Its meters are the platform's per-game block, charged while it is the selected game.
/// </remarks>
internal sealed class MachineGame(int number, int cents, int maxBet, int payback)
{
    /// <summary>The game number, from one.</summary>
    public int Number { get; } = number;

    /// <summary>What one credit of this game is worth.</summary>
    public int Cents { get; } = cents;

    /// <summary>The Table C-4 code of its denomination.</summary>
    public byte Code { get; } = SasDenomination.CodeFor(cents) ?? 0;

    /// <summary>The largest bet, in credits of this game.</summary>
    public int MaxBet { get; } = maxBet;

    /// <summary>The theoretical payback, in hundredths of a percent.</summary>
    public int Payback { get; } = payback;

    /// <summary>The game's name as long poll $B5 reports it.</summary>
    public string Name => $"GAME {Number}";

    /// <summary>The paytable, six characters as long poll $53 carries it.</summary>
    public string Paytable => $"PT{Number:D4}";

    /// <summary>The library's game status, which long poll $09 sets.</summary>
    public bool Status { get; set; } = true;

    /// <summary>Whether the operator offers the game.</summary>
    public bool OperatorEnabled { get; set; } = true;

    /// <summary>Whether the host has left the game on with long poll $09.</summary>
    public bool HostEnabled { get; set; } = true;

    /// <summary>Whether its denomination is enabled, as the platform tells the library: operator and host both.</summary>
    public bool DenominationEnabled => OperatorEnabled && HostEnabled;

    /// <summary>A player can reach the game: its status and its denomination are both on (libsas_internal.cpp:1087).</summary>
    public bool Available => Status && DenominationEnabled;

    /// <summary>The game's own meters.</summary>
    public MeterStore Meters { get; } = new();
}
