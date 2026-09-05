using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The host and the fake machine, joined over a link that carries all nine bits. Both ends of
/// every test here are our own code, so a pass says the two agree — not that either agrees with
/// a real gaming machine. The one test that reaches further is the identification reply, which
/// is checked against bytes captured from the real thing.
/// </summary>
public class FakeEgmTests
{
    private static readonly SasAddress Address = SasAddress.Of(1);

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
                // Generous on purpose. Nothing in this class is a measurement of latency except
                // the one test that says so, and everything else was a race against the clock:
                // four test assemblies running at once can starve the machine's thread for longer
                // than a tight timeout allows, which failed a different test most runs. A test
                // that expects silence asks for a short timeout of its own.
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

    private static bool WaitUntil(Func<bool> condition, int milliseconds = 3000)
    {
        long deadline = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(5);
        }

        return condition();
    }

    /// <summary>
    /// The strongest test in the suite. With its default configuration the fake machine must
    /// produce the identification reply byte for byte as the real library produced it over a real
    /// link — check sequence included. If the encoder, the layout or the defaults are wrong, this
    /// is where it shows.
    /// </summary>
    [Fact]
    public void Answers_machine_identification_exactly_as_the_real_machine_did()
    {
        using var rig = new Rig();

        LongPollResult result = rig.Host.Execute(0x1F);

        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        Assert.Equal("011F5158303031" + "02" + "FF" + "00" + "0000" + "303031000000"
                     + "39323130" + "863F", result.ToHex());
    }

    [Fact]
    public void An_empty_exception_queue_answers_a_general_poll_with_nothing_to_report()
    {
        using var rig = new Rig();

        Assert.True(WaitUntil(() => rig.Machine.GeneralPolls >= 3));
        Assert.Equal(LinkState.Up, rig.Host.State);
        Assert.Equal(0, rig.Host.Statistics.Exceptions);
    }

    [Fact]
    public void A_queued_exception_reaches_the_host()
    {
        using var rig = new Rig();
        var reported = new List<byte>();
        rig.Host.ExceptionReported += report =>
        {
            lock (reported)
                reported.Add(report.Code);
        };

        rig.Machine.OpenSlotDoor();
        rig.Machine.CloseSlotDoor();

        Assert.True(WaitUntil(() =>
        {
            lock (reported)
                return reported.Count >= 2;
        }));

        lock (reported)
        {
            Assert.Equal((byte)SasExceptionCode.SlotDoorOpened, reported[0]);
            Assert.Equal((byte)SasExceptionCode.SlotDoorClosed, reported[1]);
        }
    }

    /// <summary>
    /// The two-ended shape the whole project is for: stimulate the machine, then assert over the
    /// wire that the protocol reported it. The exception and the meter must both move, because
    /// either one alone can be right while the machine is wrong.
    /// </summary>
    [Fact]
    public void A_note_raises_its_exception_and_moves_its_meters()
    {
        using var rig = new Rig();
        var reported = new List<byte>();
        rig.Host.ExceptionReported += report =>
        {
            lock (reported)
                reported.Add(report.Code);
        };

        long before = rig.Host.Execute(0x20) is { Outcome: LongPollOutcome.Answered } baseline
            ? SasMessageDecoder.DecodeResponse(SasLayouts.For(0x20)!, baseline.Frame)
                .Number("total_bill_value") ?? -1
            : -1;

        rig.Machine.InsertBill(2000);

        Assert.True(WaitUntil(() =>
        {
            lock (reported)
                return reported.Contains((byte)SasExceptionCode.USD20BillAccepted);
        }));

        LongPollResult after = rig.Host.Execute(0x20);
        long value = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x20)!, after.Frame)
            .Number("total_bill_value") ?? -1;

        Assert.Equal(0, before);

        // Twenty, not two thousand: the money meters of this protocol are in credits, and a
        // twenty-dollar note is twenty of them at a one-dollar credit.
        Assert.Equal(20, value);
    }

    [Fact]
    public void Playing_games_moves_the_meters_the_host_can_read()
    {
        using var rig = new Rig();
        rig.Machine.InsertBill(2000);

        rig.Machine.PlayGames(games: 2, betCredits: 5);

        LongPollResult played = rig.Host.Execute(0x15);
        LongPollResult lost = rig.Host.Execute(0x17);
        LongPollResult credits = rig.Host.Execute(0x1A);

        Assert.Equal(2, Number(0x15, played, "games_played"));
        Assert.Equal(2, Number(0x17, lost, "games_lost"));
        Assert.Equal(200, Number(0x1A, credits, "current_credits"));
    }

    [Fact]
    public void Six_meters_come_back_in_one_reply()
    {
        using var rig = new Rig();
        rig.Machine.InsertBill(1000);
        rig.Machine.PlayGames(games: 3, betCredits: 2, winCredits: 5);

        LongPollResult result = rig.Host.Execute(0x0F);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x0F)!, result.Frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        // Five cents to the accounting unit, so a ten-dollar note is two hundred units, a
        // two-credit wager on a one-dollar credit is forty, and a five-credit win is one hundred.
        Assert.Equal(120, decoded.Number("total_coin_in"));
        Assert.Equal(100, decoded.Number("total_coin_out"));
        Assert.Equal(3, decoded.Number("games_played"));
        Assert.Equal(200, decoded.Number("total_drop"));
    }

    [Fact]
    public void Bill_meters_count_notes_rather_than_their_value()
    {
        using var rig = new Rig();
        rig.Machine.InsertBill(2000);
        rig.Machine.InsertBill(2000);
        rig.Machine.InsertBill(500);

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(
            SasLayouts.For(0x1E)!, rig.Host.Execute(0x1E).Frame);

        Assert.Equal(2, decoded.Number("bills_20"));
        Assert.Equal(1, decoded.Number("bills_5"));
        Assert.Equal(0, decoded.Number("bills_100"));
    }

    [Fact]
    public void Shutdown_and_startup_are_acknowledged_and_take_effect()
    {
        using var rig = new Rig();

        Assert.True(rig.Machine.IsEnabled);

        LongPollResult shutdown = rig.Host.Execute(0x01);
        Assert.Equal(LongPollOutcome.Acknowledged, shutdown.Outcome);
        Assert.False(rig.Machine.IsEnabled);

        LongPollResult startup = rig.Host.Execute(0x02);
        Assert.Equal(LongPollOutcome.Acknowledged, startup.Outcome);
        Assert.True(rig.Machine.IsEnabled);
    }

    /// <summary>
    /// A fixture that invented a reply for a command it does not implement would be worse than
    /// useless: a test would come to depend on the invention. Refusing is the only honest answer.
    /// </summary>
    [Fact]
    public void A_command_this_machine_does_not_implement_is_refused()
    {
        using var rig = new Rig();

        LongPollResult result = rig.Host.Execute(0x72, [0x01, 0x02, 0x03]);

        Assert.Equal(LongPollOutcome.Rejected, result.Outcome);
        Assert.Equal(1, rig.Machine.Rejections);
    }

    [Fact]
    public void A_request_with_a_bad_check_sequence_is_refused()
    {
        using var bus = new LoopbackBus();
        using var machine = new FakeEgm(bus.Egm, new FakeEgmOptions());
        machine.Start();

        // Long poll $0E with one data byte, and a check sequence that is deliberately wrong.
        bus.Host.Write(
        [
            SasSymbol.Address(0x01),
            SasSymbol.Data8(0x0E),
            SasSymbol.Data8(0x01),
            SasSymbol.Data8(0xFF),
            SasSymbol.Data8(0xFF),
        ]);

        SasSymbol[] reply = new SasSymbol[4];
        int count = bus.Host.Read(reply, TimeSpan.FromSeconds(1));

        Assert.Equal(1, count);
        Assert.Equal(0x81, reply[0].Data);
        Assert.Equal(1, machine.Rejections);
    }

    [Fact]
    public void Real_time_event_reporting_rejects_a_value_that_is_neither_on_nor_off()
    {
        using var rig = new Rig();

        Assert.Equal(LongPollOutcome.Acknowledged, rig.Host.Execute(0x0E, [0x01]).Outcome);
        Assert.Equal(LongPollOutcome.Rejected, rig.Host.Execute(0x0E, [0x07]).Outcome);
    }

    [Fact]
    public void The_version_and_serial_reply_is_read_back_in_full()
    {
        using var rig = new Rig(new FakeEgmOptions { SerialNumber = "QX-TEST-42" });

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(
            SasLayouts.For(0x54)!, rig.Host.Execute(0x54).Frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal("602", decoded.Text("sas_version"));
        Assert.Equal("QX-TEST-42", decoded.Text("serial_number"));
    }

    [Fact]
    public void A_machine_at_another_address_ignores_polls_meant_for_someone_else()
    {
        // Silence is the expected answer, so this one waits briefly rather than the full default.
        // A loaded machine can only make silence more likely, never less, so a short wait here is
        // safe in a way it is not anywhere else in this class.
        using var rig = new Rig(
            new FakeEgmOptions { Address = SasAddress.Of(9) },
            responseTimeout: TimeSpan.FromMilliseconds(200));

        LongPollResult result = rig.Host.Execute(0x1F);

        Assert.Equal(LongPollOutcome.Silent, result.Outcome);
        Assert.Equal(0, rig.Machine.GeneralPolls);
    }

    /// <summary>
    /// A machine slower than the host expects is still answered, and the wait is reported as the
    /// latency rather than hidden.
    /// </summary>
    /// <remarks>
    /// The timeout is set far above the delay on purpose. What this test is about is the ratio
    /// between the two, not either number: with the default two hundred milliseconds it failed
    /// roughly twice in twelve whole-suite runs, because four test assemblies running at once can
    /// starve the machine's thread for longer than a delay of forty milliseconds allows for.
    /// </remarks>
    [Fact]
    public void A_slow_machine_is_still_answered_within_the_timeout()
    {
        using var rig = new Rig(new FakeEgmOptions { ResponseDelay = TimeSpan.FromMilliseconds(40) });

        LongPollResult result = rig.Host.Execute(0x1F);

        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        Assert.True(result.Latency >= TimeSpan.FromMilliseconds(30),
            $"latency was {result.Latency.TotalMilliseconds:F1}ms");
    }

    /// <summary>
    /// A request for two meters, answered as a machine answers one: each meter with its own code,
    /// its own width, and its value. Read back through the same decoder a script uses.
    /// </summary>
    [Fact]
    public void A_selected_meter_sweep_is_answered_and_reads_back()
    {
        using var rig = new Rig();
        rig.Machine.InsertBill(2000);
        rig.Machine.PlayGames(games: 2, betCredits: 5);

        // Length, game zero, then total coin in and total credits from bills, least significant
        // byte of each code first.
        LongPollResult result = rig.Host.Execute(0x6F, [0x06, 0x00, 0x00, 0x00, 0x00, 0x0B, 0x00]);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x6F)!, result.Frame);

        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(2, decoded.Number("meters.count"));
        Assert.Equal(0x0000, decoded.Number("meters[0].code"));
        Assert.Equal(200, decoded.Number("meters[0].value"));
        Assert.Equal(0x000B, decoded.Number("meters[1].code"));
        Assert.Equal(400, decoded.Number("meters[1].value"));
    }

    /// <summary>
    /// The door and power counts are excluded from the selected-meter polls by the machine, so a
    /// request for one comes back with the other meters and no entry for it - not with a zero,
    /// which a host would read as a door that has never opened.
    /// </summary>
    [Fact]
    public void A_meter_the_selected_polls_exclude_is_left_out_of_the_answer()
    {
        using var rig = new Rig();

        // Total coin in, the slot door opened count, total credits from bills.
        LongPollResult result = rig.Host.Execute(
            0x6F, [0x08, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x0B, 0x00]);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x6F)!, result.Frame);

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(2, decoded.Number("meters.count"));
        Assert.Equal(0x0000, decoded.Number("meters[0].code"));
        Assert.Equal(0x000B, decoded.Number("meters[1].code"));
    }

    /// <summary>
    /// The worked example from the machine-validation plan. At ten centavos a note of one thousand
    /// centavos is a hundred accounting units, and the identification reply publishes the code for
    /// the denomination actually in force.
    /// </summary>
    [Fact]
    public void At_ten_centavos_a_note_is_counted_in_accounting_units()
    {
        using var rig = new Rig(new FakeEgmOptions
        {
            AccountingDenomination = 0x03,
            PlayerDenominationsInCents = [20, 50, 100, 200, 500, 1000, 2000, 5000, 10000],
        });

        rig.Machine.InsertBill(1000);

        LongPollResult result = rig.Host.Execute(0x6F, [0x04, 0x00, 0x00, 0x0B, 0x00]);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x6F)!, result.Frame);

        Assert.Equal(3, rig.Machine.DenominationCode);
        Assert.Equal(10, rig.Machine.AccountingCents);
        Assert.Null(rig.Machine.DenominationRefusal);
        Assert.Equal(100, decoded.Number("meters[0].value"));
        Assert.Equal(3, Number(0x1F, rig.Host.Execute(0x1F), "denomination"));
    }

    /// <summary>
    /// Twenty-five centavos does not divide twenty, so section 16 forbids it. The machine reports
    /// in cents instead of using it, and says why - a machine that used it would report every
    /// credit meter in a unit none of its own denominations is a whole number of.
    /// </summary>
    [Fact]
    public void An_indivisible_accounting_denomination_is_refused_in_favour_of_cents()
    {
        using var rig = new Rig(new FakeEgmOptions
        {
            AccountingDenomination = 0x04,
            PlayerDenominationsInCents = [20, 50, 100],
        });

        Assert.Equal(1, rig.Machine.DenominationCode);
        Assert.Equal(1, rig.Machine.AccountingCents);
        Assert.NotNull(rig.Machine.DenominationRefusal);
        Assert.Equal(1, Number(0x1F, rig.Host.Execute(0x1F), "denomination"));
    }

    /// <summary>
    /// A sub-cent code cannot be an accounting denomination: the protocol's own table gives it no
    /// value, and a build that accepted one would divide by zero rather than refuse.
    /// </summary>
    [Fact]
    public void A_sub_cent_accounting_denomination_is_refused()
    {
        using var rig = new Rig(new FakeEgmOptions { AccountingDenomination = 0x1C });

        Assert.Equal(1, rig.Machine.DenominationCode);
        Assert.NotNull(rig.Machine.DenominationRefusal);
    }

    /// <summary>
    /// A wager is a number of game credits and does not move when the accounting denomination
    /// does, but the coin in meter it feeds is in accounting units. Twenty-five centavos of coin
    /// in at ten centavos is two units, and the remaining five centavos are dropped rather than
    /// rounded up into money the machine never took.
    /// </summary>
    [Fact]
    public void Coin_in_is_truncated_into_accounting_units_and_never_rounded_up()
    {
        using var rig = new Rig(new FakeEgmOptions
        {
            AccountingDenomination = 0x03,
            CentsPerGameCredit = 25,
        });

        rig.Machine.InsertBill(1000);
        rig.Machine.PlayGames(games: 1, betCredits: 1);

        LongPollResult result = rig.Host.Execute(0x6F, [0x04, 0x00, 0x00, 0x00, 0x00]);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(0x6F)!, result.Frame);

        Assert.Equal(2, decoded.Number("meters[0].value"));
    }

    private static long Number(byte command, LongPollResult result, string field)
    {
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(SasLayouts.For(command)!, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return decoded.Number(field) ?? -1;
    }
}

public class MeterStoreTests
{
    [Fact]
    public void An_untouched_meter_reads_as_zero()
    {
        var meters = new MeterStore();

        Assert.Equal(0, meters.Read(SasMeterCode.GamesPlayed));
    }

    /// <summary>
    /// SAS meters only accumulate. A fixture that let one fall would let a test pass that should
    /// have failed, and relative assertions depend on the direction being guaranteed.
    /// </summary>
    [Fact]
    public void A_meter_cannot_be_decremented()
    {
        var meters = new MeterStore();
        meters.Increment(SasMeterCode.GamesPlayed, 5);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => meters.Increment(SasMeterCode.GamesPlayed, -1));
        Assert.Equal(5, meters.Read(SasMeterCode.GamesPlayed));
    }

    [Fact]
    public void Credits_on_the_machine_may_go_either_way()
    {
        var meters = new MeterStore();
        meters.SetCurrentCredits(2000);
        meters.SetCurrentCredits(1800);

        Assert.Equal(1800, meters.Read(SasMeterCode.CurrentCredits));
    }

    [Fact]
    public void A_snapshot_holds_only_the_meters_that_were_touched()
    {
        var meters = new MeterStore();
        meters.Increment(SasMeterCode.GamesPlayed, 1);
        meters.Increment(SasMeterCode.TotalCoinInCredits, 100);

        IReadOnlyDictionary<ushort, long> snapshot = meters.Snapshot();

        Assert.Equal(2, snapshot.Count);
        Assert.Equal(100, snapshot[(ushort)SasMeterCode.TotalCoinInCredits]);
    }
}

public class EncoderTests
{
    [Fact]
    public void A_value_too_large_for_its_field_is_refused_rather_than_truncated()
    {
        byte[] buffer = new byte[32];

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            SasMessageEncoder.WriteResponse(
                SasLayouts.For(0x11)!,
                SasAddress.Of(1),
                new Dictionary<string, object> { ["total_coin_in"] = 123456789L },
                buffer));

        Assert.Contains("decimal digits", error.Message);
    }

    [Fact]
    public void A_missing_value_is_refused_by_name()
    {
        byte[] buffer = new byte[32];

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            SasMessageEncoder.WriteResponse(
                SasLayouts.For(0x1F)!,
                SasAddress.Of(1),
                new Dictionary<string, object> { ["game_id"] = "QX" },
                buffer));

        Assert.Contains("additional_id", error.Message);
    }

    [Fact]
    public void What_the_encoder_writes_the_decoder_reads_back()
    {
        byte[] buffer = new byte[64];
        var values = new Dictionary<string, object>
        {
            ["progressive_group"] = 3,
            ["level"] = 1,
            ["amount"] = 125000L,
            ["partial_pay"] = 0L,
            ["reset_id"] = 0,
            ["session_game_win"] = 125000L,
            ["session_game_pay"] = 0L,
        };

        int length = SasMessageEncoder.WriteResponse(
            SasLayouts.For(0x1B)!, SasAddress.Of(1), values, buffer);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(
            SasLayouts.For(0x1B)!, buffer.AsSpan(0, length));

        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(24, length);
        Assert.Equal(3, decoded.Number("progressive_group"));
        Assert.Equal(125000, decoded.Number("amount"));
        Assert.Equal(0, decoded.Number("partial_pay"));
    }
}
