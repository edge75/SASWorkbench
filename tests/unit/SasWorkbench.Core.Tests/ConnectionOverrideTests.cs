using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// The same corpus has to run against the software machine, a board over TCP and a serial
/// adapter. The override is what makes those the same tests rather than three edited copies, so
/// both front ends apply this one and it has to mean exactly the same thing in each.
/// </summary>
public class ConnectionOverrideTests
{
    private static IReadOnlyList<ScriptDocument> Suite() =>
    [
        ScriptParser.Parse("""
            host connect loopback address 01
            host expect link up within 2s
            """, "one.sas"),
    ];

    private static ConnectStep Connect(IReadOnlyList<ScriptDocument> documents) =>
        documents[0].Tests[0].Steps.OfType<ConnectStep>().Single();

    [Fact]
    public void AnEmptyOverrideLeavesEveryTestOpeningTheLinkItNames()
    {
        IReadOnlyList<ScriptDocument> suite = Suite();

        Assert.Same(suite, ConnectionOverride.None.ApplyTo(suite));
    }

    [Fact]
    public void ALinkOverrideRewritesEveryConnectStep()
    {
        IReadOnlyList<ScriptDocument> moved = new ConnectionOverride(
            ScriptTransport.Tcp, "127.0.0.1:9760").ApplyTo(Suite());

        ConnectStep connect = Connect(moved);
        Assert.Equal(ScriptTransport.Tcp, connect.Transport);
        Assert.Equal("127.0.0.1:9760", connect.Target);
    }

    [Fact]
    public void TheOriginalDocumentsAreNotTouched()
    {
        IReadOnlyList<ScriptDocument> suite = Suite();

        new ConnectionOverride(ScriptTransport.Tcp, "127.0.0.1:9760").ApplyTo(suite);

        Assert.Equal(ScriptTransport.Loopback, Connect(suite).Transport);
    }

    [Fact]
    public void AddressAndChannelCanBeMovedWithoutMovingTheLink()
    {
        IReadOnlyList<ScriptDocument> moved = new ConnectionOverride(
            Address: SasAddress.Of(0x07), Channel: SasChannel.Two).ApplyTo(Suite());

        ConnectStep connect = Connect(moved);
        Assert.Equal(ScriptTransport.Loopback, connect.Transport);
        Assert.Equal(SasAddress.Of(0x07), connect.Address);
        Assert.Equal(SasChannel.Two, connect.Channel);
    }

    [Fact]
    public void AnEndpointThatIsNotAnEndpointIsRefusedBeforeAnythingRuns()
    {
        var change = new ConnectionOverride(ScriptTransport.Tcp, "not-an-endpoint");

        Assert.False(change.TryValidate(out string? error));
        Assert.Contains("127.0.0.1:7200", error);
    }

    [Fact]
    public void ASerialLinkWithoutAPortIsRefused()
    {
        var change = new ConnectionOverride(ScriptTransport.Serial);

        Assert.False(change.TryValidate(out string? error));
        Assert.Contains("port name", error);
    }
}
