using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;
using SasWorkbench.Gui.Controls;

namespace SasWorkbench.Gui.ViewModels;

/// <summary>
/// One of the four SAS channels, and whether this build can actually drive it.
/// </summary>
/// <remarks>
/// The channel dimension is carried everywhere so that adding a second host later is not a
/// rewrite, but only the first is wired. Offering all four as ordinary choices would let somebody
/// select a channel the environment says it does not have and get no explanation — which is the
/// silent degradation this project refuses. The other three are shown as a data state with the
/// reason attached.
/// </remarks>
public sealed record ChannelOption(int Number, bool IsAvailable, string? Reason)
{
    /// <summary>What the list shows: the number, or the data state and why.</summary>
    public string Label => IsAvailable
        ? $"ch {Number}"
        : $"ch {Number}  {Typography.DataState("unknown")}";

    /// <summary>What a screen reader is told, including the reason.</summary>
    public string Automation => IsAvailable ? Label : $"channel {Number} — {Reason}";

    public override string ToString() => Label;

    /// <summary>Builds the four options from what the environment says it can do.</summary>
    public static IReadOnlyList<ChannelOption> Offer(CapabilitySet capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        return Enum.GetValues<SasChannel>()
            .Select(channel => (int)channel)
            .Order()
            .Select(number => capabilities.Has($"channel.{number}")
                ? new ChannelOption(number, true, null)
                : new ChannelOption(number, false,
                    $"this build has never opened channel {number}, so it claims nothing about it"))
            .ToList();
    }
}
