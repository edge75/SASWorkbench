using System.IO.Ports;
using System.Runtime.InteropServices;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

public class LoopbackBusTests
{
    [Fact]
    public void The_wakeup_bit_survives_the_link()
    {
        using var bus = new LoopbackBus();
        SasSymbol[] sent = [SasSymbol.Address(0x01), SasSymbol.Data8(0x1F)];

        bus.Host.Write(sent);

        SasSymbol[] received = new SasSymbol[4];
        int count = bus.Egm.Read(received, TimeSpan.FromSeconds(1));

        Assert.Equal(2, count);
        Assert.Equal(sent[0], received[0]);
        Assert.Equal(sent[1], received[1]);
        Assert.True(received[0].Wakeup);
        Assert.False(received[1].Wakeup);
    }

    [Fact]
    public void Each_direction_is_independent()
    {
        using var bus = new LoopbackBus();

        bus.Egm.Write([SasSymbol.Data8(0x11)]);

        SasSymbol[] atMachine = new SasSymbol[1];
        Assert.Equal(0, bus.Egm.Read(atMachine, TimeSpan.FromMilliseconds(20)));

        SasSymbol[] atHost = new SasSymbol[1];
        Assert.Equal(1, bus.Host.Read(atHost, TimeSpan.FromSeconds(1)));
        Assert.Equal(0x11, (int)atHost[0].Data);
    }

    [Fact]
    public void A_read_with_nothing_to_read_returns_nothing()
    {
        using var bus = new LoopbackBus();
        SasSymbol[] buffer = new SasSymbol[4];

        Assert.Equal(0, bus.Host.Read(buffer, TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public void A_reader_that_falls_behind_loses_the_oldest_symbols_and_says_so()
    {
        using var bus = new LoopbackBus(capacity: 4);
        SasSymbol[] tooMany =
        [
            SasSymbol.Data8(1), SasSymbol.Data8(2), SasSymbol.Data8(3),
            SasSymbol.Data8(4), SasSymbol.Data8(5),
        ];

        bus.Host.Write(tooMany);

        SasSymbol[] received = new SasSymbol[8];
        int count = bus.Egm.Read(received, TimeSpan.FromSeconds(1));

        Assert.Equal(4, count);
        Assert.Equal(2, (int)received[0].Data);
        Assert.Equal(1, bus.Dropped);
    }

    [Fact]
    public void A_loopback_link_reports_that_it_proves_nothing_about_a_machine()
    {
        using var bus = new LoopbackBus();

        TransportCapabilities caps = bus.Host.Capabilities;

        Assert.Equal(TransportKind.Loopback, caps.Kind);
        Assert.True(caps.CarriesWakeupBit);
        Assert.NotNull(caps.Caveat);
    }
}

public class SasLinkTests
{
    [Fact]
    public void A_reply_ends_when_the_line_goes_quiet()
    {
        using var bus = new LoopbackBus();
        var link = new SasLink(bus.Host);

        bus.Egm.Write([SasSymbol.Data8(0x01), SasSymbol.Data8(0x1F), SasSymbol.Data8(0x51)]);

        byte[] buffer = new byte[16];
        int count = link.Receive(buffer, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(20), out bool wakeup);

        Assert.Equal(3, count);
        Assert.False(wakeup);
        Assert.Equal<byte[]>([0x01, 0x1F, 0x51], buffer[..3]);
    }

    [Fact]
    public void Two_replies_separated_by_a_gap_are_two_replies()
    {
        using var bus = new LoopbackBus();
        var link = new SasLink(bus.Host);
        byte[] buffer = new byte[16];

        bus.Egm.Write([SasSymbol.Data8(0x15)]);
        int first = link.Receive(buffer, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(20), out _);
        Assert.Equal(1, first);
        Assert.Equal(0x15, buffer[0]);

        Thread.Sleep(40);

        bus.Egm.Write([SasSymbol.Data8(0x11)]);
        int second = link.Receive(buffer, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(20), out _);
        Assert.Equal(1, second);
        Assert.Equal(0x11, buffer[0]);
    }

    [Fact]
    public void A_wakeup_bit_from_the_machine_is_reported()
    {
        using var bus = new LoopbackBus();
        var link = new SasLink(bus.Host);

        bus.Egm.Write([SasSymbol.Address(0x01)]);

        byte[] buffer = new byte[4];
        link.Receive(buffer, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(20), out bool wakeup);

        Assert.True(wakeup);
    }

    [Fact]
    public void Silence_reads_as_nothing_rather_than_as_a_zero_byte()
    {
        using var bus = new LoopbackBus();
        var link = new SasLink(bus.Host);

        byte[] buffer = new byte[4];
        int count = link.Receive(buffer, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(5), out _);

        Assert.Equal(0, count);
    }
}

public class SerialTransportTests
{
    /// <summary>
    /// The point of this test is the refusal. On a platform whose serial layer discards mark and
    /// space parity without complaining, a host that carried on would send eight-bit bytes for
    /// ever and every test would pass over a link no machine can hear.
    /// </summary>
    [Fact]
    public void Refuses_to_open_where_the_wakeup_bit_cannot_be_sent()
    {
        using var transport = new SerialTransport(new SerialTransportOptions("COM99"));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.True(transport.Capabilities.CarriesWakeupBit);
            Assert.Equal(TransportFidelity.Unverified, transport.Capabilities.Fidelity);
            // No such port on a build agent, so opening still refuses — with a different reason.
            Assert.Throws<TransportRefusedException>(transport.Open);
        }
        else
        {
            Assert.False(transport.Capabilities.CarriesWakeupBit);
            TransportRefusedException refusal =
                Assert.Throws<TransportRefusedException>(transport.Open);
            Assert.Contains("parity", refusal.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_serial_link_never_claims_to_be_proven()
    {
        using var transport = new SerialTransport(new SerialTransportOptions("COM1"));

        Assert.Equal(TransportFidelity.Unverified, transport.Capabilities.Fidelity);
        Assert.NotNull(transport.Capabilities.Caveat);
    }

    [Fact]
    public void Listing_ports_never_throws_on_a_platform_without_any()
    {
        Assert.NotNull(SerialTransport.AvailablePorts());
    }

    /// <summary>
    /// A UART driver completes a write once the byte is in the transmit FIFO, not once it is on
    /// the wire. Changing the parity straight after queueing the address byte re-frames that byte
    /// while it is still being sent, so it leaves without its wakeup bit and the machine ignores
    /// the poll. Seen on an on-board UART on 29/10: one-byte polls, which never switch parity
    /// mid-message, were always answered; longer messages almost never.
    /// </summary>
    [Fact]
    public void The_line_is_drained_before_every_parity_change()
    {
        var line = new RecordingSerialLine(Parity.Space);

        SerialTransport.WriteSymbols(line, [SasSymbol.Address(0x01), SasSymbol.Data8(0x1F)]);

        Assert.Equal(
            ["drain", "parity Mark", "write 01", "drain", "parity Space", "write 1F", "flush"],
            line.Calls);
    }

    /// <summary>
    /// Draining costs about a millisecond, so it is spent only where the parity actually
    /// changes: a general poll sent while the line is already in mark goes straight out.
    /// </summary>
    [Fact]
    public void A_message_that_needs_no_parity_change_is_not_drained()
    {
        var line = new RecordingSerialLine(Parity.Mark);

        SerialTransport.WriteSymbols(line, [SasSymbol.Address(0x81)]);

        Assert.Equal(["write 81", "flush"], line.Calls);
    }

    /// <summary>
    /// The port is left in mark parity after a general poll, so the machine's answer arrives with
    /// a parity error. The framework's default replaces such a byte with '?', which is 0x3F, and
    /// the host then reported exception $3F for every general poll on 29/10. A gaming machine
    /// never sets the wakeup bit, so the byte as received is the byte that was sent.
    /// </summary>
    [Fact]
    public void A_parity_error_does_not_turn_a_received_byte_into_a_question_mark()
    {
        using SerialPort port = SerialTransport.CreatePort(new SerialTransportOptions("COM99"));

        Assert.Equal(0, port.ParityReplace);
        Assert.Equal(19200, port.BaudRate);
        Assert.Equal(8, port.DataBits);
        Assert.Equal(Parity.Space, port.Parity);
        Assert.Equal(StopBits.One, port.StopBits);
    }

    /// <summary>
    /// Start bit, eight data bits, the parity bit that carries the wakeup flag, and a stop bit:
    /// eleven bit times, which at 19200 baud is just under 573 microseconds.
    /// </summary>
    [Fact]
    public void One_character_is_eleven_bit_times()
    {
        Assert.InRange(SerialTransport.CharacterTime(19200).TotalMicroseconds, 572.9, 573.0);
    }

    private sealed class RecordingSerialLine(Parity parity) : ISerialLine
    {
        private Parity _parity = parity;

        public List<string> Calls { get; } = [];

        public Parity Parity
        {
            get => _parity;
            set
            {
                _parity = value;
                Calls.Add($"parity {value}");
            }
        }

        public void Drain() => Calls.Add("drain");

        public void WriteByte(byte value) => Calls.Add($"write {value:X2}");

        public void Flush() => Calls.Add("flush");
    }
}

public class TcpTransportOptionsTests
{
    [Theory]
    [InlineData("127.0.0.1:7200", "127.0.0.1", 7200)]
    [InlineData(":9000", "127.0.0.1", 9000)]
    [InlineData("192.168.1.5", "192.168.1.5", TcpTransportOptions.DefaultPort)]
    public void Parses_the_endpoint_forms_a_command_line_offers(string text, string address, int port)
    {
        Assert.True(TcpTransportOptions.TryParseEndpoint(text, out var endpoint));
        Assert.Equal(address, endpoint.Address.ToString());
        Assert.Equal(port, endpoint.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData(null)]
    public void Refuses_what_is_not_an_endpoint(string? text)
    {
        Assert.False(TcpTransportOptions.TryParseEndpoint(text, out _));
    }
}
