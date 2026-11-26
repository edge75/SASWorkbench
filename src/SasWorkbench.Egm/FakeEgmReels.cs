using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Egm;

/// <summary>
/// Five physical reels, built only when a case declares them (D58): their stops after each game, as the platform
/// reports a game's reels, and their tilts, which the platform has no report for.
/// </summary>
public sealed partial class FakeEgm
{
    /// <summary>How many reels a machine built with them has.</summary>
    private const int ReelCount = 5;

    /// <summary>How many stops each reel has.</summary>
    private const int StopsPerReel = 22;

    // GSA 4.46 and 4.47: one code per reel, $40 when the reel cannot be named, $46 for a mechanism disconnected; each interrupts play.
    private static readonly Dictionary<string, PeripheralCondition> ReelConditions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tilt"] = new("reel tilt", SasExceptionCode.ReelTilt, SasExceptionCode.NoActivity, true),
        ["reel_1"] = new("reel 1 tilt", SasExceptionCode.Reel1Tilt, SasExceptionCode.NoActivity, true),
        ["reel_2"] = new("reel 2 tilt", SasExceptionCode.Reel2Tilt, SasExceptionCode.NoActivity, true),
        ["reel_3"] = new("reel 3 tilt", SasExceptionCode.Reel3Tilt, SasExceptionCode.NoActivity, true),
        ["reel_4"] = new("reel 4 tilt", SasExceptionCode.Reel4Tilt, SasExceptionCode.NoActivity, true),
        ["reel_5"] = new("reel 5 tilt", SasExceptionCode.Reel5Tilt, SasExceptionCode.NoActivity, true),
        ["disconnected"] = new("reel mechanism disconnected", SasExceptionCode.ReelMechanismDisconnected, SasExceptionCode.NoActivity, true),
    };

    // The library's reelInfo, zeroed at start (libsas_internal.cpp:366) and set by SAS_RegisterReels.
    private readonly byte[] _reelInfo = new byte[9];
    private int _reelSpins;

    /// <summary>Puts a reel into a fault, or clears every reel fault; null when done, otherwise why not.</summary>
    public string? ReelFault(string fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        if (!_options.Reels)
            return "the machine has no reels";

        if (fault.Equals(Core.Scripting.PeripheralFaultStep.Cleared, StringComparison.OrdinalIgnoreCase))
        {
            ClearConditions(ReelConditions.Values);
            return null;
        }

        return ReelConditions.TryGetValue(fault, out PeripheralCondition? condition)
            ? Activate(condition)
            : $"the reels have no fault called '{fault}'";
    }

    /// <summary>
    /// The game's reels come to rest: QSP registers the set and raises $88 for every stop new since the game started,
    /// which is every reel, since it clears its copy at game start (QSP SasNetwork.cpp:1740, :1752).
    /// </summary>
    private void StopReelsLocked()
    {
        if (!_options.Reels)
            return;

        int spin = ++_reelSpins;
        byte[] stops = new byte[ReelCount];
        for (int reel = 0; reel < ReelCount; reel++)
            stops[reel] = (byte)(((spin * 7) + (reel * 5)) % StopsPerReel);

        // SAS_RegisterReels pads the reels a game does not have with $FF (libsas.cpp:1387).
        Array.Fill(_reelInfo, (byte)0xFF);
        stops.CopyTo(_reelInfo, 0);

        for (int reel = 0; reel < ReelCount; reel++)
            RaiseEvent((byte)SasExceptionCode.ReelNStopped, (byte)(reel + 1), stops[reel]);
    }

    /// <summary>Long poll 8F on a machine with reels: the nine positions the library holds (lp.cpp:8930).</summary>
    private Dictionary<string, object> ReelStopInformation()
    {
        lock (_gate)
        {
            var values = new Dictionary<string, object>();
            for (int reel = 0; reel < _reelInfo.Length; reel++)
                values[$"reel_{reel + 1}"] = _reelInfo[reel];
            return values;
        }
    }
}
