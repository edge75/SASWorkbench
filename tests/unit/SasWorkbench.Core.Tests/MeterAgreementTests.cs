using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// Every meter more than one command reports, read through each of those commands off a real
/// gaming machine, and the two answers compared.
/// </summary>
/// <remarks>
/// <para>
/// The bytes were produced by sweeping <c>QSP_DemoOpenGL</c> on 2026-09-09 with the standard
/// validation memory image installed, and by a second run that asked the selected-meter poll for
/// three meters by code. They are copied verbatim, check sequence included.
/// </para>
/// <para>
/// <b>What this establishes and what it does not.</b> Every pair agrees, and every pair agrees at
/// zero, because that machine has never taken money and never played a game — its licence is gone
/// and it returns every note (<c>BLOCKERS.md</c> Trap D). Zero divided by anything is zero, so
/// the agreement confirms that the two replies put the meter where this build reads it, and
/// establishes nothing about the division. What settles the division is two other things: the
/// platform above the library puts the machine in full manual, in which the library divides
/// nothing at all, and the software machine — which can be built in a mode where it does —
/// answers two different numbers, measured in <c>MeterScalingTests</c>.
/// </para>
/// <para>
/// The two non-zero meters this machine holds, its slot door openings and its power resets, are
/// reported by exactly one command: the library's selected-meter handler skips both by name.
/// </para>
/// </remarks>
public class MeterAgreementTests
{
    /// <summary>What the machine answered, command by command, on 2026-09-09.</summary>
    private static readonly Dictionary<byte, string> Answered = new()
    {
        [0x0F] = "010F000000000000000000000000000000000000000000000000E9F3",
        [0x10] = "0110000000006BB0",
        [0x11] = "0111000000002FBB",
        [0x12] = "011200000000E3A6",
        [0x13] = "011300000000A7AD",
        [0x14] = "0114000000007B9D",
        [0x15] = "0115000000003F96",
        [0x16] = "011600000000F38B",
        [0x17] = "011700000000B780",
        [0x18] = "0118000000004BEA",
        [0x19] = "01190000000000000000000000000000000000000000B343",
        [0x1A] = "011A00000000C3FC",
        [0x1C] = "011C00000000000000000000000000000000000000000000000000000023000000189A8C",
        [0x1E] = "011E00000000000000000000000000000000000000000000000022B0",
        [0x20] = "012000000000BA64",
        [0x2A] = "012A000000001228",
        [0x2B] = "012B000000005623",
        [0x31] = "013100000000BEDB",
        [0x32] = "01320000000072C6",
        [0x33] = "01330000000036CD",
        [0x34] = "013400000000EAFD",
        [0x35] = "013500000000AEF6",
        [0x36] = "01360000000062EB",
        [0x37] = "01370000000026E0",
        [0x38] = "013800000000DA8A",
        [0x39] = "0139000000009E81",
        [0x3A] = "013A00000000529C",
        [0x3B] = "013B000000001697",
        [0x3C] = "013C00000000CAA7",
        [0x3E] = "013E0000000042B1",
        [0x3F] = "013F0000000006BA",
        [0x40] = "01400000000009C5",
        [0x41] = "0141000000004DCE",
        [0x42] = "01420000000081D3",
        [0x43] = "014300000000C5D8",
        [0x44] = "01440000000019E8",
        [0x45] = "0145000000005DE3",
        [0x46] = "01460000000091FE",
        [0x47] = "014700000000D5F5",
        [0x49] = "0149000000006D94",
        [0x4A] = "014A00000000A189",
    };

    private static long Read(byte command, string field)
    {
        SasMessageLayout layout = SasLayouts.For(command)!;
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(
            layout, Convert.FromHexString(Answered[command]));

        Assert.True(decoded.Succeeded, $"${command:X2}: {decoded.Failure}");
        long? value = decoded.Number(field);
        Assert.NotNull(value);
        return value.Value;
    }

    /// <summary>
    /// The names more than one command answers, checked through every one of those commands.
    /// </summary>
    [Fact]
    public void Every_command_that_reports_a_name_answered_the_same_number()
    {
        int compared = 0;

        foreach (MeterSource chosen in MeterIndex.All)
        {
            IReadOnlyList<MeterSource> all = MeterIndex.SourcesFor(chosen.Name);
            if (all.Count < 2)
                continue;

            long expected = Read(all[0].Command, all[0].Field);
            foreach (MeterSource other in all.Skip(1))
            {
                Assert.Equal(expected, Read(other.Command, other.Field));
                compared++;
            }
        }

        // Twelve names with more than one command behind them, twenty-three comparisons in all.
        Assert.Equal(23, compared);
    }

    /// <summary>
    /// A name read through the command the index picked, for every name the sweep answered.
    /// </summary>
    /// <remarks>
    /// The point is not the values, which are zero except two. It is that the chosen command's
    /// reply decodes and holds the field the index says it does, for all forty names — so a name
    /// added to the index without a reply behind it fails here.
    /// </remarks>
    [Fact]
    public void Every_name_in_the_index_reads_off_the_machine_that_was_swept()
    {
        foreach (MeterSource source in MeterIndex.All)
        {
            Assert.True(Answered.ContainsKey(source.Command),
                $"{source.Name} is read from ${source.Command:X2}, which the sweep did not record");
            Assert.True(Read(source.Command, source.Field) >= 0);
        }
    }

    /// <summary>
    /// The two meters this machine does hold, and the one command that reports them.
    /// </summary>
    /// <remarks>
    /// Twenty-three slot door openings and eighteen power resets, which is what makes the
    /// eight-meter reply's last two offsets checkable rather than a row of zeroes that fits any
    /// reading. The power reset figure is eighteen and not the twenty-one the sweep of
    /// 2026-09-08 recorded, because the machine's memory was put back from the committed fixture
    /// in between: it is the same meter in two states of one machine, not a meter going down.
    /// </remarks>
    [Fact]
    public void The_door_and_power_counts_are_the_only_meters_this_machine_holds()
    {
        Assert.Equal(23, Read(0x1C, "slot_door_opened"));
        Assert.Equal(18, Read(0x1C, "power_reset"));

        Assert.Single(MeterIndex.SourcesFor("slot_door_opened"));
        Assert.Single(MeterIndex.SourcesFor("power_reset"));
    }

    /// <summary>
    /// Three meters asked for by code through the selected-meter poll, and the same three read
    /// through the commands named for them.
    /// </summary>
    /// <remarks>
    /// A second way of asking for a meter, and the one that shows the two counts long poll
    /// <c>$18</c> answers really are meters: the machine echoed codes <c>$25</c> and <c>$26</c>
    /// and answered them. One meter per message, because this library frames <c>$2F</c> at eight
    /// bytes and drops a request for two as incomplete.
    /// </remarks>
    [Theory]
    [InlineData(0x25, "012F0700002500000000FD97", 0x18, "games_since_power_up")]
    [InlineData(0x26, "012F0700002600000000318A", 0x18, "games_since_door_closure")]
    [InlineData(0x24, "012F0700002400000000B99C", 0x13, "total_drop")]
    public void A_meter_asked_for_by_code_answers_what_its_own_command_answers(
        int code, string selected, int command, string field)
    {
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(
            SasLayouts.For(0x2F)!, Convert.FromHexString(selected));

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(code, decoded.Number("meters[0].code"));
        Assert.Equal(Read((byte)command, field), decoded.Number("meters[0].value"));
    }
}
