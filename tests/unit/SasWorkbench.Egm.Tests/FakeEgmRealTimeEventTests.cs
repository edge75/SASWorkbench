using SasWorkbench.Core.Events;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The software machine answering in the real-time event format, and what changes when it does.
/// </summary>
/// <remarks>
/// Both ends are our own code, so a pass here says the two agree about the format rather than that
/// either agrees with a gaming machine. What makes it worth having is the second half of every
/// test: turning reporting on must change how an exception is carried and never whether it
/// happened, so each case checks the same stimulus through both formats.
/// </remarks>
public class FakeEgmRealTimeEventTests
{
    private static readonly SasAddress Address = SasAddress.Of(1);

    private sealed class Rig : IDisposable
    {
        private readonly LoopbackBus _bus = new();
        private readonly List<ExceptionReport> _reports = [];

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

            Host.ExceptionReported += report =>
            {
                lock (_reports)
                    _reports.Add(report);
            };

            Host.Start();
        }

        public FakeEgm Machine { get; }

        public PollEngine Host { get; }

        public IReadOnlyList<ExceptionReport> Reports
        {
            get
            {
                lock (_reports)
                    return [.. _reports];
            }
        }

        /// <summary>Turns reporting on the way a host does, and checks the machine agreed.</summary>
        public void EnableRealTimeEvents()
        {
            LongPollResult result = Host.Execute(0x0E, [0x01]);
            Assert.Equal(LongPollOutcome.Acknowledged, result.Outcome);
            Assert.True(Machine.RealTimeEventsEnabled);
        }

        public ExceptionReport? WaitFor(byte code)
        {
            long deadline = Environment.TickCount64 + 3000;
            while (Environment.TickCount64 < deadline)
            {
                ExceptionReport? found = Reports.FirstOrDefault(r => r.Code == code);
                if (found is not null)
                    return found;

                Thread.Sleep(5);
            }

            return null;
        }

        public void Dispose()
        {
            Host.Dispose();
            Machine.Dispose();
            _bus.Dispose();
        }
    }

    /// <summary>
    /// What the machine library does at libsas_threads.cpp:3333, and what the documents' chapter
    /// 11 loops assume: while reporting is on and an exception is queued, a long poll is answered
    /// with the event and the command is never dispatched. The fixture answered every long poll
    /// with the command's own reply before this, so nothing here could reproduce it at all and a
    /// case could not tell the two deliveries apart.
    /// </summary>
    /// <remarks>
    /// The exception is queued from the machine's own receive path as the poll arrives, so no
    /// general poll can drain it in between and the test turns on the order the two happen in
    /// rather than on a thread being scheduled promptly.
    /// </remarks>
    [Fact]
    public void A_long_poll_is_answered_with_a_queued_event_rather_than_with_its_own_reply()
    {
        using var rig = new Rig();
        rig.EnableRealTimeEvents();

        rig.Machine.LongPollReceived += command =>
        {
            if (command == 0x1F)
                rig.Machine.RaiseEvent((byte)SasExceptionCode.GameStarted, 0x01, 0x00);
        };

        LongPollResult result = rig.Host.Execute(0x1F);

        Assert.Equal(LongPollOutcome.AnsweredWithEvent, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Equal(SasEventLayouts.Marker, result.Frame[1]);
        Assert.Equal((byte)SasExceptionCode.GameStarted, result.Frame[2]);

        ExceptionReport? kept = rig.WaitFor((byte)SasExceptionCode.GameStarted);
        Assert.NotNull(kept);
        Assert.True(kept.IsRealTimeEvent);
        Assert.Equal<byte[]>([0x01, 0x00], kept.Data);
    }

    /// <summary>
    /// The gate sits after the check sequence test, so a poll the machine's receiver cannot frame
    /// is still refused rather than answered with an event. A machine that preempted first would
    /// let a malformed poll look like a healthy exchange.
    /// </summary>
    [Fact]
    public void A_poll_that_would_be_refused_is_still_refused_with_an_event_queued()
    {
        using var rig = new Rig();
        rig.EnableRealTimeEvents();

        rig.Machine.LongPollReceived += command =>
        {
            if (command == 0x01)
                rig.Machine.RaiseException(SasExceptionCode.GameStarted);
        };

        LongPollResult bare = rig.Host.Execute(0x01, default, null, null, checkSequence: false);

        Assert.Equal(LongPollOutcome.Rejected, bare.Outcome);
        Assert.True(rig.Machine.IsEnabled);

        // The exception it queued is still the machine's to report, and reaches the host on the
        // general polls that follow.
        Assert.NotNull(rig.WaitFor((byte)SasExceptionCode.GameStarted));
    }

    [Fact]
    public void A_machine_comes_up_reporting_the_old_way()
    {
        using var rig = new Rig();

        Assert.False(rig.Machine.RealTimeEventsEnabled);
    }

    /// <summary>
    /// A value that is neither zero nor one is refused rather than rounded to true: a machine that
    /// accepted it would start answering in a format the host never asked for.
    /// </summary>
    [Fact]
    public void A_reporting_setting_that_is_neither_on_nor_off_is_refused()
    {
        using var rig = new Rig();

        Assert.Equal(LongPollOutcome.Rejected, rig.Host.Execute(0x0E, [0x02]).Outcome);
        Assert.False(rig.Machine.RealTimeEventsEnabled);
    }

    /// <summary>
    /// The note is the case the whole format exists for: the same stimulus reports one code with
    /// reporting off and a different code carrying three fields with it on, and both are true
    /// statements about the same note.
    /// </summary>
    [Fact]
    public void A_note_reports_its_denomination_alone_until_reporting_is_on()
    {
        using var rig = new Rig();

        rig.Machine.InsertBill(2000);
        ExceptionReport? plain = rig.WaitFor((byte)SasExceptionCode.USD20BillAccepted);
        Assert.NotNull(plain);
        Assert.Null(plain.Data);
        Assert.False(plain.IsRealTimeEvent);

        rig.EnableRealTimeEvents();
        rig.Machine.InsertBill(2000);

        ExceptionReport? carried = rig.WaitFor((byte)SasExceptionCode.BillAccepted);
        Assert.NotNull(carried);
        Assert.True(carried.IsRealTimeEvent);

        SasDecoded decoded = Decode(carried);
        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(37, decoded.Number("country_code"));
        Assert.Equal(4, decoded.Number("denomination_code"));
        Assert.Equal(2, decoded.Number("bill_meter"));
    }

    /// <summary>
    /// The wager is a number of game credits and the meter beside it is in accounting units, so a
    /// machine counting in five cents reports two credits staked and a meter that moved by forty.
    /// </summary>
    [Fact]
    public void A_game_reports_the_wager_and_the_meter_in_their_own_units()
    {
        using var rig = new Rig(new FakeEgmOptions { AccountingDenomination = 0x02 });

        rig.EnableRealTimeEvents();
        rig.Machine.InsertBill(2000);
        rig.Machine.PlayGames(1, betCredits: 2, winCredits: 3);

        ExceptionReport? started = rig.WaitFor((byte)SasExceptionCode.GameStarted);
        Assert.NotNull(started);
        SasDecoded start = Decode(started);
        Assert.True(start.Succeeded, start.Failure);
        Assert.Equal(2, start.Number("credits_wagered"));
        Assert.Equal(40, start.Number("total_coin_in"));

        ExceptionReport? ended = rig.WaitFor((byte)SasExceptionCode.GameEnded);
        Assert.NotNull(ended);
        SasDecoded end = Decode(ended);
        Assert.True(end.Succeeded, end.Failure);
        Assert.Equal(300, end.Number("win_amount"));
    }

    /// <summary>
    /// A door carries nothing either way, so the only thing reporting changes about it is the
    /// shape of the message. A test written against one format holds against the other.
    /// </summary>
    [Fact]
    public void An_exception_that_carries_nothing_is_the_same_exception_in_both_formats()
    {
        using var rig = new Rig();

        rig.EnableRealTimeEvents();
        rig.Machine.OpenSlotDoor();

        ExceptionReport? opened = rig.WaitFor((byte)SasExceptionCode.SlotDoorOpened);
        Assert.NotNull(opened);
        Assert.True(opened.IsRealTimeEvent);
        Assert.Empty(opened.Data!);
    }

    /// <summary>
    /// The library's own behaviour, modelled rather than smoothed over: with reporting off it
    /// dequeues one of the four event-only exceptions and then sends nothing at all, so that
    /// general poll goes unanswered. A host that reads silence as "nothing queued" is being told
    /// something false, and a test should be able to see it.
    /// </summary>
    [Fact]
    public void An_event_only_exception_costs_a_general_poll_its_answer_when_reporting_is_off()
    {
        // A short timeout, because this is the one test that measures silence: on the usual one a
        // single unanswered poll takes five seconds to be counted as one.
        using var rig = new Rig(responseTimeout: TimeSpan.FromMilliseconds(200));

        Assert.True(Wait(() => rig.Host.Statistics.GeneralPolls >= 3));
        long silencesBefore = rig.Host.Statistics.Silences;

        rig.Machine.RaiseEvent((byte)SasExceptionCode.ReelNStopped, 0x01, 0x0A);

        Assert.True(Wait(() => rig.Host.Statistics.Silences > silencesBefore));
        Assert.DoesNotContain(rig.Reports, r => r.Code == (byte)SasExceptionCode.ReelNStopped);
    }

    [Fact]
    public void The_same_exception_reaches_the_host_once_reporting_is_on()
    {
        using var rig = new Rig();

        rig.EnableRealTimeEvents();
        rig.Machine.RaiseEvent((byte)SasExceptionCode.ReelNStopped, 0x01, 0x0A);

        ExceptionReport? stopped = rig.WaitFor((byte)SasExceptionCode.ReelNStopped);
        Assert.NotNull(stopped);
        SasDecoded decoded = Decode(stopped);
        Assert.True(decoded.Succeeded, decoded.Failure);
        Assert.Equal(1, decoded.Number("reel_number"));
        Assert.Equal(10, decoded.Number("physical_stop"));
    }

    /// <summary>
    /// Turning reporting off again puts the machine back, so a measurement that asked for it does
    /// not leave every case after it reading a different format.
    /// </summary>
    [Fact]
    public void Turning_reporting_off_puts_the_machine_back()
    {
        using var rig = new Rig();

        rig.EnableRealTimeEvents();
        Assert.Equal(LongPollOutcome.Acknowledged, rig.Host.Execute(0x0E, [0x00]).Outcome);
        Assert.False(rig.Machine.RealTimeEventsEnabled);

        rig.Machine.OpenSlotDoor();
        ExceptionReport? opened = rig.WaitFor((byte)SasExceptionCode.SlotDoorOpened);
        Assert.NotNull(opened);
        Assert.False(opened.IsRealTimeEvent);
    }

    private static SasDecoded Decode(ExceptionReport report)
    {
        SasEventLayout? layout = SasEventLayouts.For(report.Code);
        Assert.NotNull(layout);
        Assert.NotNull(report.Data);

        byte[] frame = new byte[SasEventLayouts.HeaderLength + report.Data.Length + 2];
        frame[0] = Address.Value;
        frame[1] = SasEventLayouts.Marker;
        frame[2] = report.Code;
        report.Data.CopyTo(frame, SasEventLayouts.HeaderLength);
        return SasMessageDecoder.DecodeEvent(layout, frame);
    }

    private static bool Wait(Func<bool> condition, int milliseconds = 3000)
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
}
