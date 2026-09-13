using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Session;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

public class SessionClockTests
{
    [Fact]
    public void ASourceStartingAtTheSessionZeroIsUnchanged()
    {
        ClockBase link = ClockBase.Session;

        Assert.Equal(TimeSpan.FromSeconds(5), link.Reconcile(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// The reconciliation this whole model exists for: an engine that started six seconds into the
    /// session reports 5927 ms of its own time for an instant that is 11.9 s of session time.
    /// </summary>
    [Fact]
    public void ASourceThatStartedLateIsShiftedOntoTheSessionClock()
    {
        var engine = new ClockBase(TimeSpan.FromSeconds(6));

        Assert.Equal(
            TimeSpan.FromMilliseconds(11927),
            engine.Reconcile(TimeSpan.FromMilliseconds(5927)));
    }

    [Fact]
    public void TwoSourcesWithDifferentZeroesOrderCorrectlyOnOneClock()
    {
        var engine = new ClockBase(TimeSpan.FromSeconds(6));
        var test = new ClockBase(TimeSpan.FromSeconds(10));

        // A wire record at 5000 ms of engine time is later than a step at 12 ms of test time,
        // even though the raw numbers say the opposite.
        Assert.True(engine.Reconcile(TimeSpan.FromMilliseconds(5000))
                    > test.Reconcile(TimeSpan.FromMilliseconds(12)));
    }

    [Fact]
    public void AClockMarkedLaterCarriesTheTimeAlreadyElapsed()
    {
        var clock = new SessionClock();
        Thread.Sleep(20);

        ClockBase mark = clock.Mark();

        Assert.True(mark.Offset >= TimeSpan.FromMilliseconds(15));
    }
}

public class SessionLogTests
{
    [Fact]
    public void WireRecordsLandInTheDirectionLaneWithTheirSymbols()
    {
        var log = new SessionLog();
        SasSymbol[] symbols = [SasSymbol.Address(0x01), SasSymbol.Data8(0x1F)];

        log.Wire(WireDirection.Transmit, SasChannel.One, null, symbols, TimeSpan.FromMilliseconds(100),
            ClockBase.Session, "long poll $1F");

        SessionEvent record = Assert.Single(log.Snapshot());
        Assert.Equal(SessionLane.Transmit, record.Lane);
        Assert.Equal("1.01 0.1F", record.ToHex());
        Assert.Equal("long poll $1F", record.Note);
        Assert.True(record.Symbols[0].Wakeup);
        Assert.False(record.Symbols[1].Wakeup);
    }

    [Fact]
    public void EveryEventCarriesTheChannelSoTwoHostsShareOneLog()
    {
        var log = new SessionLog();
        SasSymbol[] symbols = [SasSymbol.Address(0x01)];

        log.Wire(WireDirection.Transmit, SasChannel.One, null, symbols, TimeSpan.Zero, ClockBase.Session);
        log.Wire(WireDirection.Transmit, SasChannel.Two, null, symbols, TimeSpan.FromMilliseconds(1),
            ClockBase.Session);

        Assert.Equal(
            [SasChannel.One, SasChannel.Two],
            log.Snapshot().Select(e => e.Channel));
    }

    /// <summary>An assertion that held is a record, not an absence.</summary>
    [Fact]
    public void AnAssertionThatPassedIsRecordedInTheAssertionLane()
    {
        var log = new SessionLog();
        var step = new StepResult(SourceLocation.None, "expect meter games_played += 1", Verdict.Pass)
        {
            IsAssertion = true,
        };

        log.Step(step, SasChannel.One, ClockBase.Session);

        SessionEvent record = Assert.Single(log.Snapshot());
        Assert.Equal(SessionLane.Assertion, record.Lane);
        Assert.Equal(Verdict.Pass, record.Verdict);
    }

    [Fact]
    public void EventsInsideATestCarryTheTestName()
    {
        var log = new SessionLog();
        log.TestStarted("the link comes up", SourceLocation.None, SasChannel.One, TimeSpan.Zero);
        log.Exception(new ExceptionReport(0x4A, SasChannel.One, TimeSpan.FromSeconds(1), 1),
            ClockBase.Session);

        SessionEvent exception = log.Snapshot().Last();
        Assert.Equal("the link comes up", exception.Test);
    }

    /// <summary>
    /// A test's name is attached and detached inside the same lock as the event that opens or
    /// closes it, so a frame recorded from the poll thread can never be attributed to a test whose
    /// start marker has not been written yet, or to one that has already reached a verdict.
    /// </summary>
    [Fact]
    public void EventsOutsideATestCarryNoTestName()
    {
        var log = new SessionLog();
        log.Wire(WireDirection.Transmit, SasChannel.One, null, [SasSymbol.Address(1)], TimeSpan.Zero,
            ClockBase.Session);
        log.TestStarted("a test", SourceLocation.None, SasChannel.One,
            TimeSpan.FromMilliseconds(1));
        log.TestFinished(
            new TestOutcome
            {
                Name = "a test",
                Location = SourceLocation.None,
                Verdict = Verdict.Pass,
            },
            SasChannel.One);
        log.Wire(WireDirection.Transmit, SasChannel.One, null, [SasSymbol.Address(1)],
            TimeSpan.FromSeconds(30), ClockBase.Session);

        IReadOnlyList<SessionEvent> events = log.Snapshot();

        Assert.Null(events[0].Test);
        Assert.Equal("a test", events[1].Test);
        Assert.Null(events[^1].Test);
    }

    [Fact]
    public void TheOldestEventsAreForgottenAndCounted()
    {
        var log = new SessionLog(capacity: 3);
        for (int i = 0; i < 5; i++)
            log.Prompt($"step {i}", SasChannel.One, at: TimeSpan.FromMilliseconds(i));

        Assert.Equal(3, log.Snapshot().Count);
        Assert.Equal(2, log.Dropped);
        Assert.Equal(5, log.Recorded);
    }

    /// <summary>
    /// Arrival order is not occurrence order when two threads write, so a live view has to place
    /// each event by its clock rather than append it.
    /// </summary>
    [Fact]
    public void AnEventThatHappenedEarlierIsPlacedBeforeOneAlreadyHeld()
    {
        var log = new SessionLog();
        SessionEvent held = log.Wire(WireDirection.Transmit, SasChannel.One, null, [SasSymbol.Address(1)],
            TimeSpan.FromMilliseconds(100), ClockBase.Session);
        SessionEvent arrivedLate = log.Wire(WireDirection.Receive, SasChannel.One, null,
            [SasSymbol.Data8(2)], TimeSpan.FromMilliseconds(40), ClockBase.Session);

        IReadOnlyList<SessionEvent> ordered = log.Snapshot();

        Assert.Equal(SessionLane.Receive, ordered[0].Lane);
        Assert.Equal(0, SessionLog.PlaceOf([held], arrivedLate));
        Assert.Equal(1, SessionLog.PlaceOf([arrivedLate], held));
    }

    [Fact]
    public void ALaneFilterKeepsOnlyTheLanesAsked()
    {
        var log = new SessionLog();
        log.Wire(WireDirection.Transmit, SasChannel.One, null, [SasSymbol.Address(1)], TimeSpan.Zero,
            ClockBase.Session);
        log.Prompt("insert a note", SasChannel.One, at: TimeSpan.FromMilliseconds(1));

        IReadOnlyList<SessionEvent> wire = log.Snapshot(
            new HashSet<SessionLane> { SessionLane.Transmit, SessionLane.Receive });

        Assert.Equal(SessionLane.Transmit, Assert.Single(wire).Lane);
    }
}

public class SessionStamperTests
{
    /// <summary>
    /// The three modes on one general-poll loop: a reply 30.7 ms after a poll that came 200 ms
    /// after the one before it. Since-previous-poll reads the loop; since-previous-message reads
    /// the turnaround. They are different questions and they get different numbers.
    /// </summary>
    private static IReadOnlyList<SessionEvent> Loop()
    {
        var log = new SessionLog();
        log.TestStarted("a loop", SourceLocation.None, SasChannel.One, TimeSpan.FromSeconds(1));
        log.Wire(WireDirection.Transmit, SasChannel.One, null, [SasSymbol.Address(0x81)],
            TimeSpan.FromMilliseconds(1000), ClockBase.Session);
        log.Wire(WireDirection.Transmit, SasChannel.One, null, [SasSymbol.Address(0x81)],
            TimeSpan.FromMilliseconds(1200), ClockBase.Session);
        log.Wire(WireDirection.Receive, SasChannel.One, null, [SasSymbol.Data8(0x18)],
            TimeSpan.FromMilliseconds(1230.7), ClockBase.Session);
        return log.Snapshot();
    }

    [Fact]
    public void SincePreviousPollReadsThePollPeriod()
    {
        IReadOnlyList<SessionEvent> events = Loop();
        var stamper = new SessionStamper(TimestampMode.SincePreviousPoll);

        TimeSpan[] offsets = events.Select(stamper.Stamp).ToArray();

        Assert.Equal(TimeSpan.FromMilliseconds(200), offsets[2]);
        Assert.Equal(TimeSpan.FromMilliseconds(30.7), offsets[3]);
    }

    [Fact]
    public void SincePreviousMessageReadsTheTurnaround()
    {
        IReadOnlyList<SessionEvent> events = Loop();
        var stamper = new SessionStamper(TimestampMode.SincePreviousMessage);

        TimeSpan[] offsets = events.Select(stamper.Stamp).ToArray();

        Assert.Equal(TimeSpan.FromMilliseconds(200), offsets[2]);
        Assert.Equal(TimeSpan.FromMilliseconds(30.7), offsets[3]);
    }

    [Fact]
    public void SincePreviousPollAndSincePreviousMessageDisagreeWhenTheyShould()
    {
        // The reply sits between the two polls, so the poll period the next poll reports still
        // measures 200 ms while the message gap it reports is only the remainder.
        var log = new SessionLog();
        log.Wire(WireDirection.Transmit, SasChannel.One, null, [SasSymbol.Address(0x81)],
            TimeSpan.FromMilliseconds(1000), ClockBase.Session);
        log.Wire(WireDirection.Receive, SasChannel.One, null, [SasSymbol.Data8(0x18)],
            TimeSpan.FromMilliseconds(1030), ClockBase.Session);
        log.Wire(WireDirection.Transmit, SasChannel.One, null, [SasSymbol.Address(0x81)],
            TimeSpan.FromMilliseconds(1200), ClockBase.Session);
        IReadOnlyList<SessionEvent> events = log.Snapshot();

        TimeSpan byPoll = SessionStamper.StampAll(events, TimestampMode.SincePreviousPoll)[2].Offset;
        TimeSpan byMessage =
            SessionStamper.StampAll(events, TimestampMode.SincePreviousMessage)[2].Offset;

        Assert.Equal(TimeSpan.FromMilliseconds(200), byPoll);
        Assert.Equal(TimeSpan.FromMilliseconds(170), byMessage);
    }

    [Fact]
    public void SinceTestStartCountsFromTheTestNotTheSession()
    {
        IReadOnlyList<SessionEvent> events = Loop();

        TimeSpan offset = SessionStamper.StampAll(events, TimestampMode.SinceTestStart)[3].Offset;

        Assert.Equal(TimeSpan.FromMilliseconds(230.7), offset);
    }

    /// <summary>A zero we never measured would claim a gap that never happened.</summary>
    [Fact]
    public void TheFirstEventReadsItsOwnTimeRatherThanZero()
    {
        var log = new SessionLog();
        log.Wire(WireDirection.Receive, SasChannel.One, null, [SasSymbol.Data8(0x18)],
            TimeSpan.FromMilliseconds(42), ClockBase.Session);

        Assert.Equal(
            TimeSpan.FromMilliseconds(42),
            SessionStamper.StampAll(log.Snapshot(), TimestampMode.SincePreviousPoll)[0].Offset);
    }

    [Fact]
    public void ThePollPeriodIsPerChannel()
    {
        var log = new SessionLog();
        log.Wire(WireDirection.Transmit, SasChannel.One, null, [SasSymbol.Address(0x81)],
            TimeSpan.FromMilliseconds(1000), ClockBase.Session);
        log.Wire(WireDirection.Transmit, SasChannel.Two, null, [SasSymbol.Address(0x82)],
            TimeSpan.FromMilliseconds(1100), ClockBase.Session);
        log.Wire(WireDirection.Transmit, SasChannel.One, null, [SasSymbol.Address(0x81)],
            TimeSpan.FromMilliseconds(1200), ClockBase.Session);

        IReadOnlyList<(SessionEvent Event, TimeSpan Offset)> stamped =
            SessionStamper.StampAll(log.Snapshot(), TimestampMode.SincePreviousPoll);

        Assert.Equal(TimeSpan.FromMilliseconds(200), stamped[2].Offset);
    }

    // ---- wall-clock time, the fourth mode ----------------------------------------------------

    /// <summary>
    /// Wall-clock time is the session's own beginning plus the elapsed time a record already
    /// holds, so it needs nothing added to the record.
    /// </summary>
    /// <remarks>
    /// The mode for matching a run against something outside it — another tool's log, a machine's
    /// own log, or somebody's account of what they were doing. Local rather than universal,
    /// because the thing it is compared against was read off a screen in a room.
    /// </remarks>
    [Fact]
    public void WallClockIsTheSessionsBeginningPlusTheElapsedTimeTheRecordHolds()
    {
        var origin = new DateTimeOffset(2026, 9, 7, 21, 48, 51, TimeSpan.Zero);
        var log = new SessionLog();
        log.Wire(WireDirection.Transmit, SasChannel.One, null, [SasSymbol.Address(0x81)],
            TimeSpan.FromMilliseconds(1230.7), ClockBase.Session);

        TimeSpan offset =
            SessionStamper.StampAll(log.Snapshot(), TimestampMode.WallClock, origin)[0].Offset;

        Assert.Equal(
            (origin + TimeSpan.FromMilliseconds(1230.7)).ToLocalTime().TimeOfDay,
            offset);
    }

    /// <summary>
    /// Wall-clock time without the instant the session began is refused, not answered from now.
    /// </summary>
    /// <remarks>
    /// A recorded session read back tomorrow would otherwise be stamped with tomorrow's clock and
    /// nothing would say so — a wrong answer indistinguishable from a right one, which is exactly
    /// what this project refuses to produce.
    /// </remarks>
    [Fact]
    public void WallClockWithoutAnOriginIsRefusedRatherThanReadFromTheCurrentTime()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => new SessionStamper(TimestampMode.WallClock));

        Assert.Contains("instant the session began", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOtherThreeModesNeedNoOrigin()
    {
        foreach (TimestampMode mode in Enum.GetValues<TimestampMode>()
                     .Where(m => m != TimestampMode.WallClock))
        {
            Assert.Equal(mode, new SessionStamper(mode).Mode);
        }
    }

    /// <summary>A time of day is written as one; the other three modes are durations.</summary>
    [Fact]
    public void ATimeOfDayIsWrittenAsATimeAndADurationAsMilliseconds()
    {
        var noon = new TimeSpan(0, 12, 34, 56, 789);

        Assert.Equal("12:34:56.789", SessionStamper.Format(noon, TimestampMode.WallClock));
        Assert.Equal(
            "  45296.8",
            SessionStamper.Format(TimeSpan.FromMilliseconds(45296.789),
                TimestampMode.SinceTestStart));
    }

    /// <summary>D28's fourth mode exists, so a view offering the modes offers four.</summary>
    [Fact]
    public void ThereAreFourTimestampModes()
    {
        Assert.Equal(4, Enum.GetValues<TimestampMode>().Length);
    }
}
