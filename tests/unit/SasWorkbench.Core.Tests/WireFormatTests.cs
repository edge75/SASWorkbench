using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

public class SasSymbolTests
{
    [Fact]
    public void A_data_byte_carries_no_wakeup_bit()
    {
        SasSymbol symbol = SasSymbol.Data8(0x1F);

        Assert.False(symbol.Wakeup);
        Assert.Equal(0x1F, symbol.Data);
        Assert.Equal(0x001F, symbol.Raw);
        Assert.Equal("0.1F", symbol.ToString());
    }

    [Fact]
    public void An_address_byte_carries_the_wakeup_bit()
    {
        SasSymbol symbol = SasSymbol.Address(0x81);

        Assert.True(symbol.Wakeup);
        Assert.Equal(0x81, symbol.Data);
        Assert.Equal(0x0181, symbol.Raw);
        Assert.Equal("1.81", symbol.ToString());
    }

    [Fact]
    public void A_raw_value_above_nine_bits_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SasSymbol.FromRaw(0x0200));
    }

    [Fact]
    public void Truncating_keeps_only_the_nine_bits_that_exist()
    {
        Assert.Equal(SasSymbol.Address(0x81), SasSymbol.FromRawTruncating(0xFF81));
    }

    [Fact]
    public void Two_symbols_with_the_same_bits_are_equal()
    {
        Assert.Equal(SasSymbol.Data8(0x42), SasSymbol.FromRaw(0x42));
        Assert.NotEqual(SasSymbol.Data8(0x42), SasSymbol.Address(0x42));
    }
}

public class SasAddressTests
{
    [Fact]
    public void Address_zero_is_the_synchronisation_address()
    {
        Assert.True(SasAddress.Sync.IsSync);
        Assert.Equal(0, SasAddress.Sync.Value);
    }

    [Fact]
    public void Bit_seven_is_not_part_of_an_address()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SasAddress.Of(0x80));
        Assert.Equal(127, SasAddress.Of(127).Value);
    }

    [Theory]
    [InlineData("01", 1)]
    [InlineData("1F", 31)]
    [InlineData("0x20", 32)]
    [InlineData("7", 7)]
    [InlineData("100", 100)]
    public void Parses_the_forms_people_actually_write(string text, int expected)
    {
        Assert.True(SasAddress.TryParse(text, out SasAddress address));
        Assert.Equal(expected, address.Value);
    }

    [Theory]
    [InlineData("128")]
    [InlineData("0x80")]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData(null)]
    public void Refuses_what_is_not_an_address(string? text)
    {
        Assert.False(SasAddress.TryParse(text, out _));
    }
}

public class HostMessageTests
{
    [Fact]
    public void A_sync_poll_is_one_symbol_with_the_wakeup_bit()
    {
        Span<SasSymbol> buffer = stackalloc SasSymbol[HostMessage.MaxSymbols];

        int length = HostMessage.WriteSyncPoll(buffer);

        Assert.Equal(1, length);
        Assert.Equal("1.80", buffer[0].ToString());
    }

    [Fact]
    public void A_general_poll_sets_bit_seven_on_the_address()
    {
        Span<SasSymbol> buffer = stackalloc SasSymbol[HostMessage.MaxSymbols];

        int length = HostMessage.WriteGeneralPoll(buffer, SasAddress.Of(1));

        Assert.Equal(1, length);
        Assert.Equal("1.81", buffer[0].ToString());
    }

    /// <summary>
    /// Byte for byte what the captured session put on the wire, including the absence of a check
    /// sequence: a two-byte long poll carries none, and the machine answered this one in full.
    /// </summary>
    [Fact]
    public void A_long_poll_with_no_data_matches_the_captured_frame()
    {
        Span<SasSymbol> buffer = stackalloc SasSymbol[HostMessage.MaxSymbols];

        int length = HostMessage.WriteLongPoll(buffer, SasAddress.Of(1), 0x1F);

        Assert.Equal(2, length);
        Assert.Equal("1.01", buffer[0].ToString());
        Assert.Equal("0.1F", buffer[1].ToString());
    }

    [Fact]
    public void A_long_poll_with_data_carries_a_check_sequence()
    {
        Span<SasSymbol> buffer = stackalloc SasSymbol[HostMessage.MaxSymbols];
        byte[] data = [0x01, 0x02];

        int length = HostMessage.WriteLongPoll(buffer, SasAddress.Of(1), 0x08, data);

        Assert.Equal(6, length);
        byte[] frame = new byte[length];
        for (int i = 0; i < length; i++)
            frame[i] = buffer[i].Data;
        Assert.True(SasCrc.Verify(frame));
    }

    [Fact]
    public void The_wakeup_bit_is_on_the_address_byte_and_nowhere_else()
    {
        Span<SasSymbol> buffer = stackalloc SasSymbol[HostMessage.MaxSymbols];
        byte[] data = [0xAA, 0xBB, 0xCC, 0xDD];

        int length = HostMessage.WriteLongPoll(buffer, SasAddress.Of(0x0A), 0x72, data);

        Assert.True(buffer[0].Wakeup);
        for (int i = 1; i < length; i++)
            Assert.False(buffer[i].Wakeup);
    }

    [Fact]
    public void A_long_poll_to_the_broadcast_address_is_still_an_address_byte()
    {
        Span<SasSymbol> buffer = stackalloc SasSymbol[HostMessage.MaxSymbols];

        int length = HostMessage.WriteLongPoll(buffer, SasAddress.Sync, 0x7E, [0x01]);

        Assert.Equal("1.00", buffer[0].ToString());
        Assert.Equal(5, length);
    }

    [Fact]
    public void A_buffer_too_small_for_the_message_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            SasSymbol[] tooSmall = new SasSymbol[2];
            HostMessage.WriteLongPoll(tooSmall, SasAddress.Of(1), 0x08, [0x01]);
        });
    }

    [Fact]
    public void Messages_of_two_symbols_or_fewer_carry_no_check_sequence()
    {
        Assert.False(HostMessage.CarriesCrc(1));
        Assert.False(HostMessage.CarriesCrc(2));
        Assert.True(HostMessage.CarriesCrc(3));
    }
}
