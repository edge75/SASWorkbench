using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

public class SasCrcTests
{
    /// <summary>
    /// The one test that matters most in this file. These 22 bytes and the two that follow them
    /// were captured off a real link, from a real gaming machine library answering long poll $1F
    /// with no Quixant board present. If our check sequence disagrees with these bytes then it
    /// disagrees with the machine, and every frame we send is rejected for a reason no test
    /// would otherwise explain.
    /// </summary>
    [Fact]
    public void Matches_the_captured_reply_to_long_poll_1F()
    {
        byte[] message = Convert.FromHexString("011F5158303031" + "02" + "FF" + "00" + "0000"
                                               + "303031000000" + "39323130");

        ushort crc = SasCrc.Compute(message);

        Assert.Equal(0x86, SasCrc.LowByte(crc));
        Assert.Equal(0x3F, SasCrc.HighByte(crc));
    }

    [Fact]
    public void Verifies_a_frame_it_produced()
    {
        byte[] body = Convert.FromHexString("017201020304");
        Span<byte> frame = stackalloc byte[body.Length + 2];
        body.CopyTo(frame);

        int total = SasCrc.Append(frame, body.Length);

        Assert.Equal(body.Length + 2, total);
        Assert.True(SasCrc.Verify(frame[..total]));
    }

    [Fact]
    public void Rejects_a_frame_with_one_flipped_bit()
    {
        byte[] frame = Convert.FromHexString("011F51583030310200FF000000303031000000393231308A3F");

        Assert.False(SasCrc.Verify(frame));
    }

    [Fact]
    public void Rejects_a_frame_too_short_to_hold_a_check_sequence()
    {
        Assert.False(SasCrc.Verify(Convert.FromHexString("011F")));
        Assert.False(SasCrc.Verify([]));
    }

    [Fact]
    public void An_empty_message_has_a_zero_check_sequence()
    {
        Assert.Equal(0, SasCrc.Compute([]));
    }

    [Fact]
    public void Append_refuses_a_buffer_with_no_room_for_the_check_bytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            byte[] tooSmall = new byte[3];
            SasCrc.Append(tooSmall, 2);
        });
    }
}
