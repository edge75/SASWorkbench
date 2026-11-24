using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// Component authentication as the library handles long poll $6E (lp6E_SendAuthenticationInfo), with the
/// application half this machine supplies because QSP has none: two components, CRC-16 only, finished after
/// a configurable time. Every case runs with the host in a sequence, so nothing reaches the line the test did
/// not send, and with a short authentication time so a case need not wait five seconds.
/// </summary>
public sealed class FakeEgmAuthenticationTests : IDisposable
{
    private const byte ComponentListChanged = 0x8E;
    private const byte AuthenticationComplete = 0x8F;

    // Component 1, CRC-16, two-byte seed 0000, one-byte offset 00: the request every chapter 19 case sends.
    private static readonly byte[] AuthenticateFirst = Convert.FromHexString("0D02000101010000000200000100");

    private readonly LoopbackBus _bus = new();
    private FakeEgm? _machine;
    private PollEngine? _host;

    private FakeEgm Machine => _machine!;

    private PollEngine Host => _host!;

    private void Start(bool on = true, int milliseconds = 300)
    {
        _machine = new FakeEgm(_bus.Egm, new FakeEgmOptions
        {
            ComponentAuthentication = on,
            AuthenticationTime = TimeSpan.FromMilliseconds(milliseconds),
        });
        _machine.Start();
        _host = new PollEngine(_bus.Host, new PollEngineOptions
        {
            Address = SasAddress.Of(1),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(1),
            QuietGap = TimeSpan.FromMilliseconds(15),
            LinkDownAfter = TimeSpan.FromSeconds(30),
        });
        _host.Start();
        Assert.True(_host.BeginSequence(), "the round did not hand the line over");
        Drain();
    }

    public void Dispose()
    {
        _host?.Dispose();
        _machine?.Dispose();
        _bus.Dispose();
    }

    private SasDecoded Ask(string hex)
    {
        LongPollResult result = Host.Execute(0x6E, Convert.FromHexString(hex));
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x6E)!, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return decoded;
    }

    private SasDecoded Ask(byte[] data) => Ask(Convert.ToHexString(data));

    private static long Status(SasDecoded reply) => reply.Number("status")!.Value;

    private List<byte> Drain()
    {
        var codes = new List<byte>();
        Host.Synchronise();
        for (byte code = Poll(); code != 0x00; code = Poll())
        {
            codes.Add(code);
            Host.Synchronise();
        }

        return codes;
    }

    private byte Poll()
    {
        GeneralPollResult result = Host.PollGenerally();
        Assert.True(result.Answered, $"the general poll went unanswered: {result.Outcome}");
        return result.Code;
    }

    /// <summary>Polls until the machine raises authentication complete, or fails after a few seconds.</summary>
    private void AwaitCompletion()
    {
        DateTime until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until)
        {
            if (Drain().Contains(AuthenticationComplete))
                return;
            Thread.Sleep(50);
        }

        Assert.Fail("the machine never raised authentication complete");
    }

    [Fact]
    public void A_machine_taking_no_part_is_silent_and_clears_the_feature_bit()
    {
        Start(on: false);

        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x6E, [0x01, 0x00]).Outcome);

        LongPollResult features = Host.Execute(0xA0, [0x00, 0x00]);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0xA0)!, features.Frame);
        Assert.Equal(0, decoded.Number("feature_codes_2")!.Value & 0x10);
        Assert.NotNull(Machine.ChangeComponent());
    }

    [Fact]
    public void The_list_reports_two_components_and_the_check_sequence_over_their_names()
    {
        Start();

        LongPollResult features = Host.Execute(0xA0, [0x00, 0x00]);
        SasDecoded enabled = SasMessageDecoder.DecodeResponse(SasLayouts.For(0xA0)!, features.Frame);
        Assert.Equal(0x10, enabled.Number("feature_codes_2")!.Value & 0x10);

        SasDecoded list = Ask("0100");
        Assert.Equal(1, Status(list));
        Assert.Equal(0, list.Number("name_length"));
        Assert.Equal(1, list.Number("size_length"));
        Assert.Equal(2, list.Number("size"));
        Assert.Equal(SasCrc.Compute("SWB-GAMESWB-PLATFORM"u8), list.Number("component_list_crc"));
    }

    [Fact]
    public void A_component_is_read_by_index_or_by_name_and_an_unknown_one_is_status_80()
    {
        Start();

        SasDecoded byIndex = Ask("0401000101");
        Assert.Equal(0, Status(byIndex));
        Assert.Equal("SWB-GAME", byIndex.Text("name"));
        Assert.Equal(3, byIndex.Number("size_length"));
        Assert.Equal(0x10000, byIndex.Number("size"));
        Assert.Equal(1, byIndex.Number("available_methods"));
        Assert.Null(byIndex.Number("method"));

        // Mode $01 with "SWB-PLATFORM", twelve bytes.
        SasDecoded byName = Ask("0F01010C5357422D504C4154464F524D");
        Assert.Equal("SWB-PLATFORM", byName.Text("name"));

        Assert.Equal(0x80, Status(Ask("0401000109")));
        Assert.Equal(0x80, Status(Ask("0D02000100010000000200000100")));
    }

    [Fact]
    public void An_unknown_action_or_addressing_mode_is_refused()
    {
        Start();

        Assert.Equal(LongPollOutcome.Rejected, Host.Execute(0x6E, [0x01, 0x04]).Outcome);
        Assert.Equal(LongPollOutcome.Rejected, Host.Execute(0x6E, [0x04, 0x01, 0x02, 0x01, 0x01]).Outcome);
    }

    [Fact]
    public void Starting_answers_40_and_leaves_out_the_authentication_length_byte()
    {
        Start();

        SasDecoded started = Ask(AuthenticateFirst);
        Assert.Equal(0x40, Status(started));
        Assert.Equal(1, started.Number("method"));

        // The library writes the byte past the end of the reply (lp.cpp:3986), so it reads as absent.
        Assert.Null(started.Number("authentication_length"));
    }

    [Fact]
    public void The_result_is_a_crc_over_the_component_and_is_forgotten_once_read()
    {
        Start();
        Ask(AuthenticateFirst);
        AwaitCompletion();

        SasDecoded result = Ask("0103");
        Assert.Equal(0x41, Status(result));
        Assert.Equal(2, result.Number("authentication_length"));
        byte[] bytes = new byte[0x10000];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)((i * 7) + 1);
        ushort crc = SasCrc.Compute(bytes);
        Assert.Equal([SasCrc.LowByte(crc), SasCrc.HighByte(crc)], result["authentication_data"]!.Bytes);

        // Read once, it is gone before any acknowledgement (lp.cpp:4083).
        Assert.Equal(0xFF, Status(Ask("0103")));
        Assert.Equal(0, Status(Ask("0401000101")));
    }

    [Fact]
    public void Starting_another_aborts_the_first_which_then_reports_C1_once()
    {
        Start(milliseconds: 2000);
        Ask(AuthenticateFirst);

        SasDecoded second = Ask("0D02000102010000000200000100");
        Assert.Equal(0x40, Status(second));

        Assert.Equal(0xC1, Status(Ask("0401000101")));
        Assert.Equal(0, Status(Ask("0401000101")));
        Assert.Equal(0x40, Status(Ask("0401000102")));
    }

    [Theory]
    [InlineData("0D02000101800000000200000100", 0xC3)]
    [InlineData("0E0200010101000000030000000100", 0xC4)]
    public void A_request_the_library_checks_is_refused_at_once(string hex, int status)
    {
        Start();

        Assert.Equal(status, Status(Ask(hex)));
        Assert.Equal(0xFF, Status(Ask("0103")));
    }

    [Fact]
    public void A_seed_longer_than_the_library_holds_is_an_invalid_command()
    {
        Start();

        // Thirty-three bytes of seed, one past the library's buffer (lp.cpp:3914).
        byte[] request = [44, 0x02, 0x00, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 33, .. new byte[33], 0x01, 0x00];
        Assert.Equal(0x82, Status(Ask(request)));
    }

    [Fact]
    public void An_offset_past_the_end_is_accepted_and_then_fails()
    {
        Start();

        // An eight-byte offset of all ones: the library only catches one whose both halves exceed the size (lp.cpp:3942).
        Assert.Equal(0x40, Status(Ask("14020001010100000002000008FFFFFFFFFFFFFFFF")));
        AwaitCompletion();

        Assert.Equal(0xC0, Status(Ask("0103")));
        Assert.Equal(0xFF, Status(Ask("0103")));
    }

    [Fact]
    public void Completion_is_raised_again_until_the_result_is_read()
    {
        Start();
        Ask(AuthenticateFirst);
        AwaitCompletion();

        // Fifteen seconds is too long for a unit test; the reissue is armed, and reading the result disarms it.
        Assert.Equal(0x41, Status(Ask("0401000101")));
        Assert.DoesNotContain(AuthenticationComplete, Drain());
    }

    [Fact]
    public void A_changed_component_raises_8E_and_moves_the_check_sequence()
    {
        Start();
        long before = Ask("0100").Number("component_list_crc")!.Value;

        Assert.Null(Machine.ChangeComponent());

        Assert.Contains(ComponentListChanged, Drain());
        SasDecoded after = Ask("0100");
        Assert.Equal(3, after.Number("size"));
        Assert.NotEqual(before, after.Number("component_list_crc"));
    }

    [Fact]
    public void A_power_cycle_forgets_the_result_and_keeps_the_list()
    {
        Start();
        Machine.ChangeComponent();
        Ask(AuthenticateFirst);
        AwaitCompletion();

        Machine.PowerCycle();
        Host.Synchronise();
        Drain();

        Assert.Equal(0xFF, Status(Ask("0103")));
        Assert.Equal(3, Ask("0100").Number("size"));
    }
}
