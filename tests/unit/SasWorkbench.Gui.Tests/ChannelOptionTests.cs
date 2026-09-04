using SasWorkbench.Core.Scripting;
using SasWorkbench.Gui.ViewModels;

namespace SasWorkbench.Gui.Tests;

/// <summary>
/// The channel selector must not offer a channel the environment says it does not have. Letting
/// somebody pick one and then saying nothing is the silent degradation this project refuses.
/// </summary>
public sealed class ChannelOptionTests
{
    [Fact]
    public void AllFourChannelsAreListed()
    {
        Assert.Equal(4, ChannelOption.Offer(CapabilityProbe.Baseline()).Count);
    }

    [Fact]
    public void OnlyTheChannelThisBuildHasOpenedIsAvailable()
    {
        IReadOnlyList<ChannelOption> channels = ChannelOption.Offer(CapabilityProbe.Baseline());

        Assert.True(channels[0].IsAvailable);
        Assert.All(channels.Skip(1), c => Assert.False(c.IsAvailable));
    }

    [Fact]
    public void AnUnavailableChannelSaysWhyRatherThanJustBeingDisabled()
    {
        ChannelOption third = ChannelOption.Offer(CapabilityProbe.Baseline())[2];

        Assert.Contains("unknown", third.Label, StringComparison.Ordinal);
        Assert.Contains("never opened channel 3", third.Automation, StringComparison.Ordinal);
    }

    [Fact]
    public void AChannelBecomesAvailableWhenTheEnvironmentClaimsIt()
    {
        CapabilitySet probed = CapabilityProbe.Baseline().With("channel.2", "yes");

        Assert.True(ChannelOption.Offer(probed)[1].IsAvailable);
    }
}
