using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// What the software gaming machine will and will not answer, and why each is honest.
/// </summary>
/// <remarks>
/// <para>
/// The first group is the one worth having: eleven replies where this machine and the real
/// machine the catalogue was swept against on 2026-09-08 send the <em>same bytes</em>, check
/// sequence included. Both were fresh, so both answered zeroes — and a layout, a field width or a
/// length byte one out on either side breaks the match. Every expected string here is copied from
/// the capture in <c>SweepLayoutTests</c>, not from this machine's own output.
/// </para>
/// <para>
/// The rest pin the rule the machine is built on: it answers a meter it keeps or a meter that is
/// genuinely zero for want of the machinery, is silent exactly where the machine library is
/// silent, and refuses the rest. A layout added without values arrives here as a failing test
/// rather than as a reply nobody checked.
/// </para>
/// </remarks>
public class SoftwareMachineRepliesTests
{
    private static readonly SasAddress Address = SasAddress.Of(1);

    /// <summary>
    /// Replies where the two peers of D7 send the same bytes, so one can be diffed against the
    /// other. The request column is what the sweep sends, in the shape the layout describes.
    /// </summary>
    public static TheoryData<byte, string, string> SameBytesAsTheRealMachine => new()
    {
        { 0x18, "", "0118000000004BEA" },
        { 0x19, "", "01190000000000000000000000000000000000000000B343" },
        { 0x2D, "0000", "012D00000000000073CC" },
        { 0x48, "", "01480000000000005598" },
        { 0x50, "00", "0150000000000000000000001539" },
        { 0x52, "0000", "0152000000000000000000000000000000000000E975" },
        { 0x53, "0000", "01530000515830303102FF0000003030310000003932313191A3" },
        { 0x55, "", "01550000E5A6" },
        { 0x57, "", "0157800000000000CA31" },
        { 0xB3, "", "01B300DA4A" },
        { 0xB4, "00000000", "01B40D00000000393231310400000000D86F" },
    };

    [Theory]
    [MemberData(nameof(SameBytesAsTheRealMachine))]
    public void The_software_machine_answers_these_with_the_bytes_the_real_machine_sent(
        byte command, string request, string expected)
    {
        using var rig = new Rig();

        LongPollResult result = rig.Host.Execute(command, Convert.FromHexString(request));

        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        Assert.Equal(expected, result.ToHex());
    }

    /// <summary>
    /// The commands this machine has no machinery for, and answers with silence. Every one of
    /// them has a layout, so nothing here is unanswered for want of a decoder.
    /// </summary>
    /// <remarks>
    /// A zero from a machine that has no reels is not the same statement as a zero from a machine
    /// whose reels have not moved, and only the second is worth asserting on. So these are not
    /// answered until the machinery exists, and this list is what says so out loud.
    /// <para>
    /// Silence rather than a refusal, because that is what the machine library does and it was
    /// read rather than assumed: not one of these eight ever sends a refusal. A
    /// machine answers them or says nothing, so a fixture that refused was the only one of the
    /// three peers doing something no real machine does.
    /// </para>
    /// </remarks>
    public static TheoryData<byte, string> UnansweredForWantOfMachinery => new()
    {
        { 0x80, "00010000000100" },
        { 0x83, "0000" },
        { 0x84, "" },
        { 0x85, "" },
        { 0x86, "020001" },
        { 0x87, "" },
        { 0x8E, "" },
        { 0x8F, "" },
    };

    [Theory]
    [MemberData(nameof(UnansweredForWantOfMachinery))]
    public void A_machine_with_no_progressive_reels_or_cards_says_nothing_rather_than_zero(
        byte command, string request)
    {
        using var rig = new Rig();

        LongPollResult result = rig.Host.Execute(command, Convert.FromHexString(request));

        Assert.Equal(LongPollOutcome.Silent, result.Outcome);
    }

    /// <summary>
    /// Every reply in the catalogue is answered, refused or answered with silence, and which of
    /// the three it is was decided deliberately.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole catalogue is asked, in the shape the machine library's own receiver frames each
    /// command in, so a layout added later cannot slip through unclassified: it arrives here as a
    /// failure naming the command, which is the moment to decide whether this machine can answer
    /// it honestly. The two lists below are the whole of what it cannot.
    /// </para>
    /// <para>
    /// Every request is zero-filled, which is deliberate: game zero is the machine as a whole,
    /// validation type zero is the cashable ticket, and wager category zero is the game itself, so
    /// zeroes ask the broadest question each command has.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_reply_in_the_catalogue_is_classified()
    {
        // Nothing in the catalogue is refused any more: a refusal is now reserved for this
        // fixture's own gaps, and there are none among the commands it has layouts for.
        byte[] refused = [];
        byte[] silent =
        [
            0x4C, 0x4D, 0xB2,

            // No progressive, no cards, no reels — see WithoutMachinery.
            0x80, 0x83, 0x84, 0x85, 0x86, 0x87, 0x8E, 0x8F,
        ];

        using var rig = new Rig(responseTimeout: TimeSpan.FromMilliseconds(400));
        var wrong = new List<string>();
        int answered = 0;

        foreach (byte command in SasLayouts.All.Keys.Order())
        {
            LongPollOutcome outcome = rig.Host.Execute(command, RequestFor(command)).Outcome;
            LongPollOutcome wanted =
                refused.Contains(command) ? LongPollOutcome.Rejected
                : silent.Contains(command) ? LongPollOutcome.Silent
                : SasLayouts.All[command].Shape == SasResponseShape.Acknowledgement
                    ? LongPollOutcome.Acknowledged
                    : LongPollOutcome.Answered;

            if (outcome != wanted)
                wrong.Add($"${command:X2} {outcome}, expected {wanted}");
            else if (outcome is LongPollOutcome.Answered or LongPollOutcome.Acknowledged)
                answered++;
        }

        Assert.Empty(wrong);
        Assert.Equal(89, SasLayouts.All.Count);
        Assert.Equal(78, answered);
    }

    /// <summary>
    /// A zero-filled request of the length the machine library's receiver frames this command at.
    /// </summary>
    /// <remarks>
    /// Five commands are the exception, because a zero-filled request of the right length is not
    /// a well-formed one: the two selected-meter polls carry their own length byte, and the
    /// redemption, funds transfer and registration commands are asked in the forms that only read
    /// the current status, which are the forms of them that move no money.
    /// </remarks>
    private static byte[] RequestFor(byte command) => command switch
    {
        0x6F or 0xAF => [0x04, 0x00, 0x00, 0x00, 0x00],
        0x2F => [0x03, 0x00, 0x00, 0x00],
        0x71 => [0x01, (byte)SasTicketTransferCode.TTC_REQUEST_FOR_CURRENT_TICKET_STATUS],
        0x72 => [0x02, 0xFF, 0x00],
        0x73 => [0x01, 0xFF],
        _ => new byte[SasCommandFraming.RequestDataBytes(command) ?? 0],
    };

    /// <summary>
    /// The three commands this machine answers with silence, which is not the same as refusing
    /// them.
    /// </summary>
    /// <remarks>
    /// All three are the machine library's own behaviour. It answers the enhanced validation
    /// commands only on a machine configured for that validation, and the enabled player
    /// denominations only on one advertising more than one denomination; otherwise its handler
    /// returns without sending anything. A refusal would tell a host the command was understood
    /// and declined, which is a different thing to have to debug.
    /// </remarks>
    [Theory]
    [InlineData(0x4C, "010000010000")]
    [InlineData(0x4D, "FF")]
    [InlineData(0xB2, "")]
    public void A_command_the_library_answers_nothing_to_draws_silence_and_not_a_refusal(
        byte command, string request)
    {
        using var rig = new Rig(responseTimeout: TimeSpan.FromMilliseconds(400));

        LongPollResult result = rig.Host.Execute(command, Convert.FromHexString(request));

        Assert.Equal(LongPollOutcome.Silent, result.Outcome);
    }

    /// <summary>
    /// A machine performing secure enhanced validation does answer the identifier command, and
    /// answers with what it now holds.
    /// </summary>
    [Fact]
    public void A_secure_enhanced_machine_keeps_the_validation_identifier_it_is_given()
    {
        using var rig = new Rig(new FakeEgmOptions
        {
            CashoutValidation = SasCashoutValidation.SecureEnhanced,
        });

        // Both numbers are plain binary, least significant byte first.
        LongPollResult set = rig.Host.Execute(0x4C, Convert.FromHexString("010010010000"));
        Assert.Equal(LongPollOutcome.Answered, set.Outcome);

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x4C)!, set.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0x100001, decoded.Number("validation_id"));
        Assert.Equal(1, decoded.Number("sequence_number"));

        // A zero identifier is the library's way of asking without setting, so the machine still
        // has what it was given.
        LongPollResult asked = rig.Host.Execute(0x4C, Convert.FromHexString("000000000000"));
        SasDecoded again = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x4C)!, asked.Frame);

        Assert.Equal(0x100001, again.Number("validation_id"));
    }

    /// <summary>
    /// A machine built with player denominations publishes them; the one built without is silent,
    /// which the test above pins.
    /// </summary>
    [Fact]
    public void A_machine_with_player_denominations_lists_them()
    {
        using var rig = new Rig(new FakeEgmOptions
        {
            AccountingDenomination = 0x01,
            PlayerDenominationsInCents = [5, 25, 100],
        });

        LongPollResult result = rig.Host.Execute(0xB2);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0xB2)!, result.Frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(3, decoded.Number("count"));
        Assert.Equal(0x02, decoded.Number("denominations[0].denomination"));
        Assert.Equal(0x04, decoded.Number("denominations[1].denomination"));
        Assert.Equal(0x06, decoded.Number("denominations[2].denomination"));
    }

    /// <summary>
    /// A question about a game this machine does not have draws silence, because that is what the
    /// library's handlers do — they return before writing anything.
    /// </summary>
    [Theory]
    [InlineData(0x2D)]
    [InlineData(0x52)]
    [InlineData(0x53)]
    [InlineData(0xB5)]
    public void A_question_about_a_game_this_machine_does_not_have_draws_silence(byte command)
    {
        using var rig = new Rig(responseTimeout: TimeSpan.FromMilliseconds(400));

        LongPollResult result = rig.Host.Execute(command, Convert.FromHexString("0009"));

        Assert.Equal(LongPollOutcome.Silent, result.Outcome);
    }

    /// <summary>
    /// The counters this machine's own stimuli move, and which it therefore keeps: the slot door's
    /// opening count, the games played since it was last shut, and the notes now in the stacker.
    /// </summary>
    /// <remarks>
    /// This is the test that would have caught the dishonest version of these replies. A machine
    /// that opened its own door and then answered zero to the door count would be reporting
    /// something its own stimulus had just disproved, and a script asserting on that zero would
    /// pass here and fail against anything real.
    /// </remarks>
    [Fact]
    public void The_counters_this_machine_moves_are_the_counters_it_reports()
    {
        using var rig = new Rig();

        rig.Machine.InsertBill(2000);
        rig.Machine.InsertBill(2000);
        rig.Machine.SetDoor("slot", open: true);
        rig.Machine.SetDoor("slot", open: false);
        rig.Machine.PlayGames(3, betCredits: 1);

        SasDecoded eight = SasMessageDecoder.DecodeResponse(
            SasLayouts.For(0x1C)!, rig.Host.Execute(0x1C).Frame);
        Assert.Equal(1, eight.Number("slot_door_opened"));

        // Nothing power-cycles this machine, so this one is genuinely zero rather than unkept.
        Assert.Equal(0, eight.Number("power_reset"));

        SasDecoded since = SasMessageDecoder.DecodeResponse(
            SasLayouts.For(0x18)!, rig.Host.Execute(0x18).Frame);
        Assert.Equal(3, since.Number("games_since_power_up"));
        Assert.Equal(3, since.Number("games_since_door_closure"));

        SasDecoded stacker = SasMessageDecoder.DecodeResponse(
            SasLayouts.For(0x49)!, rig.Host.Execute(0x49).Frame);
        Assert.Equal(2, stacker.Number("bills_in_stacker"));

        // Two twenties at five cents to the unit.
        SasDecoded value = SasMessageDecoder.DecodeResponse(
            SasLayouts.For(0x4A)!, rig.Host.Execute(0x4A).Frame);
        Assert.Equal(800, value.Number("stacker_credits"));
    }

    /// <summary>
    /// Shutting the slot door starts the games-since-door-closure count again, and leaves the
    /// since-power-up count alone. The two would be indistinguishable without a door in the middle.
    /// </summary>
    [Fact]
    public void Shutting_the_slot_door_restarts_one_of_the_two_game_counts()
    {
        using var rig = new Rig();

        rig.Machine.InsertBill(10000);
        rig.Machine.PlayGames(2, betCredits: 1);
        rig.Machine.SetDoor("slot", open: true);
        rig.Machine.SetDoor("slot", open: false);
        rig.Machine.PlayGames(1, betCredits: 1);

        SasDecoded since = SasMessageDecoder.DecodeResponse(
            SasLayouts.For(0x18)!, rig.Host.Execute(0x18).Frame);

        Assert.Equal(3, since.Number("games_since_power_up"));
        Assert.Equal(1, since.Number("games_since_door_closure"));
    }

    /// <summary>
    /// The last-note reply names what went in and how many of that denomination the machine has.
    /// </summary>
    [Fact]
    public void The_last_accepted_note_is_the_last_note_that_went_in()
    {
        using var rig = new Rig();

        rig.Machine.InsertBill(2000);
        rig.Machine.InsertBill(500);
        rig.Machine.InsertBill(500);

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(
            SasLayouts.For(0x48)!, rig.Host.Execute(0x48).Frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(37, decoded.Number("country_code"));
        Assert.Equal(2, decoded.Number("denomination_code"));
        Assert.Equal(2, decoded.Number("count"));
    }

    /// <summary>
    /// A cashout moves the validation meters the validation meter poll reads, not only the
    /// machine-wide ticket totals.
    /// </summary>
    /// <remarks>
    /// A real machine moves both families for one ticket, and a fixture that moved only one would
    /// answer this poll with a zero its own cashout had just disproved.
    /// </remarks>
    [Fact]
    public void A_printed_ticket_shows_up_in_the_validation_meters()
    {
        string wallet = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        using var rig = new Rig();

        try
        {
            rig.Machine.InsertBill(2000);
            Assert.NotNull(rig.Machine.Cashout(wallet));

            SasDecoded decoded = SasMessageDecoder.DecodeResponse(
                SasLayouts.For(0x50)!, rig.Host.Execute(0x50, [0x00]).Frame);

            Assert.True(decoded.Succeeded, decoded.Failure);
            Assert.Equal(0, decoded.Number("validation_type"));
            Assert.Equal(1, decoded.Number("quantity"));
            Assert.Equal(2000, decoded.Number("amount"));
        }
        finally
        {
            if (Directory.Exists(wallet))
                Directory.Delete(wallet, recursive: true);
        }
    }

    /// <summary>
    /// A machine with no hopper says it cannot measure how full one is, which leaves the level out
    /// of the reply altogether.
    /// </summary>
    [Fact]
    public void A_machine_with_no_hopper_says_it_cannot_measure_one()
    {
        using var rig = new Rig();

        LongPollResult result = rig.Host.Execute(0x4F);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x4F)!, result.Frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(2, decoded.Number("length"));
        Assert.Equal(0xFF, decoded.Number("percent_full"));
        Assert.Null(decoded.Number("hopper_level"));
        Assert.Equal(7, result.Frame.Length);
    }

    /// <summary>
    /// The extended game information reply carries two length-prefixed names, and the length byte
    /// counts everything after itself.
    /// </summary>
    [Fact]
    public void The_extended_game_information_reply_names_the_game_and_its_paytable()
    {
        using var rig = new Rig();

        LongPollResult result = rig.Host.Execute(0xB5, [0x00, 0x00]);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0xB5)!, result.Frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal("QXFAKE", decoded.Text("game_name"));
        Assert.Equal("001", decoded.Text("paytable_name"));
        Assert.Equal(255, decoded.Number("max_bet"));

        // No progressive group, so no progressive levels — which the library zeroes for the same
        // reason rather than leaving whatever a game configured.
        Assert.Equal(0, decoded.Number("progressive_levels"));
        Assert.Equal(0, decoded.Number("wager_categories"));
        Assert.Equal(result.Frame.Length - 5, decoded.Number("length"));
    }

    /// <summary>
    /// The fixed-width selected-meter reply states no width per meter, so the value is as wide as
    /// the library's own table makes that meter.
    /// </summary>
    [Fact]
    public void The_fixed_width_selected_meter_reply_takes_its_widths_from_the_library_table()
    {
        using var rig = new Rig();

        rig.Machine.InsertBill(2000);

        // Length, game zero, and one meter code: the games-played count, which is four bytes.
        LongPollResult result = rig.Host.Execute(
            0x2F, [0x03, 0x00, 0x00, (byte)SasMeterCode.TotalDropIn_Crd]);

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x2F)!, result.Frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(0, decoded.Number("game_number"));
        Assert.Equal((long)SasMeterCode.TotalDropIn_Crd, decoded.Number("meters[0].code"));
        Assert.Equal(400, decoded.Number("meters[0].value"));
    }

    private sealed class Rig : IDisposable
    {
        private readonly LoopbackBus _bus = new();

        public Rig(FakeEgmOptions? options = null, TimeSpan? responseTimeout = null)
        {
            Machine = new FakeEgm(_bus.Egm, options);
            Machine.Start();

            Host = new PollEngine(_bus.Host, new PollEngineOptions
            {
                Address = Address,
                PollInterval = TimeSpan.FromMilliseconds(20),
                ResponseTimeout = responseTimeout ?? TimeSpan.FromSeconds(5),
                QuietGap = TimeSpan.FromMilliseconds(15),
                LinkDownAfter = TimeSpan.FromSeconds(30),
            });
            Host.Start();
        }

        public FakeEgm Machine { get; }

        public PollEngine Host { get; }

        public void Dispose()
        {
            Host.Dispose();
            Machine.Dispose();
            _bus.Dispose();
        }
    }
}
