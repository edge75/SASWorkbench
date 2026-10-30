using System.Xml.Linq;
using SasWorkbench.Gui.ViewModels;

namespace SasWorkbench.Gui.Tests;

/// <summary>
/// Two things the window used to say wrongly or not at all: a seal reading PROVEN over no link,
/// and a stream narrowed to one test with nothing on screen saying so.
/// </summary>
public sealed class LinkBandAndScopeTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    /// <summary>
    /// With no link there is nothing for the band to vouch for, so it claims neither strength.
    /// The loopback link is the one this build has measured end to end, so it is proven while
    /// open, and closing it takes the claim away again rather than leaving the last one standing.
    /// </summary>
    [Fact]
    public void The_band_claims_nothing_without_a_link_and_drops_its_claim_on_disconnect()
    {
        using var model = new WorkbenchViewModel();
        Assert.False(model.IsConnected);
        Assert.False(model.IsLinkProven);
        Assert.False(model.IsLinkUnverified);

        model.SelectedTransport = "loopback";
        model.Address = "01";
        model.PollInterval = "50ms";
        model.ConnectCommand.Execute(null);
        Assert.True(model.IsConnected, model.Status);
        Assert.True(model.IsLinkProven);
        Assert.False(model.IsLinkUnverified);

        model.DisconnectCommand.Execute(null);
        Assert.False(model.IsLinkProven);
        Assert.False(model.IsLinkUnverified);
    }

    /// <summary>The seal itself is only drawn while a link is open.</summary>
    [Fact]
    public void The_seal_is_drawn_only_while_a_link_is_open()
    {
        XElement seal = View("ProvenanceRail.axaml")
            .Descendants()
            .Single(e => e.Name.LocalName == "ProvenanceSeal");

        Assert.Equal("{Binding IsConnected}", (string?)seal.Attribute("IsVisible"));
    }

    /// <summary>
    /// A narrowed stream names what it is narrowed to and by how much, and says nothing once it is
    /// widened again.
    /// </summary>
    [Fact]
    public void A_narrowed_stream_says_what_it_is_narrowed_to_until_it_is_widened()
    {
        using var stream = new SessionStreamViewModel();
        Assert.Null(stream.ScopeLine);

        stream.ScopeTo(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "4.6 a general poll");
        Assert.Equal("Narrowed to 4.6 a general poll ±250 ms", stream.ScopeLine);

        stream.ClearScopeCommand.Execute(null);
        Assert.Null(stream.ScopeLine);
    }

    /// <summary>The strip that says so carries the way back to the whole session.</summary>
    [Fact]
    public void The_strip_over_the_stream_carries_the_way_back_to_the_whole_session()
    {
        XElement strip = View("SessionStream.axaml")
            .Descendants(Avalonia + "Border")
            .Single(b => (string?)b.Attribute("Classes") == "scope");

        Assert.Contains("ScopeLine", (string?)strip.Attribute("IsVisible"), StringComparison.Ordinal);
        Assert.Contains(
            strip.Descendants(Avalonia + "Button"),
            button => (string?)button.Attribute("Command") == "{Binding ClearScopeCommand}");
    }

    private static XElement View(string file) =>
        XDocument.Load(Path.Combine(Gui(), "Views", file)).Root!;

    private static string Gui()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "SasWorkbench.Gui");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "could not find src/SasWorkbench.Gui above the test binaries");
    }
}
