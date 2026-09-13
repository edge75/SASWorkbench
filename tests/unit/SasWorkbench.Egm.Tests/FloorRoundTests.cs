using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// One host polling a round of machines on one multi-drop line.
/// </summary>
/// <remarks>
/// The second axis. Everything before this had one host per machine, or several hosts on one
/// machine; here one host reaches many machines through one link, told apart by address alone.
/// Both ends are our own code, so what a pass says is that our host walks a round and our machines
/// answer only their own polls, not anything about a floor of real ones.
/// </remarks>
public class FloorRoundTests
{
    private const byte MachineIdentification = 0x1F;

    private static PollEngineOptions Options(SoftwareFloor floor) => new()
    {
        Address = floor.Addresses[0],
        Addresses = floor.Addresses,
        PollInterval = TimeSpan.Zero,

        // No synchronisation polls, because in a round the next machine's poll is already a poll
        // for another address. Measured both ways in docs/M5-casino-simulation.md; here it only
        // keeps the tests quick.
        SyncPollEvery = 0,
        WireLogCapacity = 16384,
    };

    private static bool WaitFor(Func<bool> done, TimeSpan limit)
    {
        DateTime deadline = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < deadline)
        {
            if (done())
                return true;
            Thread.Sleep(5);
        }

        return done();
    }

    /// <summary>
    /// Every machine on the line answers, and each answers only the polls addressed to it.
    /// </summary>
    /// <remarks>
    /// The property the whole round rests on. All four machines see all four polls, because a
    /// multi-drop line delivers everything to everybody; what makes the round mean anything is
    /// that each one keeps quiet for the other three.
    /// </remarks>
    [Fact]
    public void EveryMachineOnTheLineAnswersItsOwnPollAndNobodyElses()
    {
        using SoftwareFloor floor = SoftwareFloor.Of(4);
        using var engine = new PollEngine(floor.Host, Options(floor));

        engine.Start();
        Assert.True(WaitFor(() => engine.RoundsCompleted >= 4, TimeSpan.FromSeconds(30)),
            $"only {engine.RoundsCompleted} round(s) were walked");
        engine.Stop();

        RoundMeasurement measured = RoundMeasurement.Of(engine.WireLog.Snapshot(), floor.Addresses);

        Assert.Equal(4, measured.Machines);
        Assert.Equal(4, measured.Answered.Count);
        Assert.Empty(measured.Silent);
        Assert.Equal(0, floor.Dropped);
    }

    /// <summary>The round visits every address in order, and comes back round.</summary>
    [Fact]
    public void TheRoundPollsEachAddressInTurn()
    {
        using SoftwareFloor floor = SoftwareFloor.Of(3);
        using var engine = new PollEngine(floor.Host, Options(floor));

        engine.Start();
        Assert.True(WaitFor(() => engine.RoundsCompleted >= 3, TimeSpan.FromSeconds(30)));
        engine.Stop();

        List<byte> polled =
        [
            .. engine.WireLog.Snapshot()
                .Where(r => r.Direction == WireDirection.Transmit && r.Symbols.Length == 1)
                .Select(r => (byte)(r.Symbols[0].Data & 0x7F))
                .Where(a => a != 0),
        ];

        Assert.True(polled.Count >= 9, $"only {polled.Count} general poll(s) went out");
        Assert.Equal<byte>([1, 2, 3, 1, 2, 3, 1, 2, 3], polled.Take(9));
    }

    /// <summary>
    /// A round of one is what every host in this build already was.
    /// </summary>
    /// <remarks>
    /// Here because the round was added to the engine every existing case runs through. A host
    /// given one address must behave exactly as it did, or the second axis was bought by breaking
    /// the first.
    /// </remarks>
    [Fact]
    public void AHostGivenOneMachineStillPollsOnlyThatOne()
    {
        using SoftwareFloor floor = SoftwareFloor.Of(1);
        using var engine = new PollEngine(floor.Host, Options(floor));

        Assert.Single(engine.Round);
        Assert.Equal(floor.Addresses[0], engine.Address);

        engine.Start();
        Assert.True(WaitFor(() => engine.RoundsCompleted >= 5, TimeSpan.FromSeconds(30)));
        engine.Stop();

        Assert.All(
            engine.WireLog.Snapshot()
                .Where(r => r.Direction == WireDirection.Transmit && r.Symbols.Length == 1),
            r => Assert.Equal(0x81, r.Symbols[0].Data));
    }

    // ---- what a round refuses ----------------------------------------------------------------

    /// <summary>
    /// A long poll on a round has to name its machine.
    /// </summary>
    /// <remarks>
    /// The refusal that matters most here. Sent without a name, a command would land on whichever
    /// machine the cycle had reached, and that machine would answer it correctly, so the case
    /// would pass while addressing the wrong machine. Refusing with a reason is the only honest
    /// outcome.
    /// </remarks>
    [Fact]
    public void ALongPollOnARoundIsRefusedUntilItNamesAMachine()
    {
        using SoftwareFloor floor = SoftwareFloor.Of(4);
        using var engine = new PollEngine(floor.Host, Options(floor));

        engine.Start();
        Assert.True(WaitFor(() => engine.RoundsCompleted >= 1, TimeSpan.FromSeconds(30)));

        LongPollResult anonymous = engine.Execute(MachineIdentification);

        Assert.Equal(LongPollOutcome.NotSent, anonymous.Outcome);
        Assert.Contains("has to name one", anonymous.Detail, StringComparison.Ordinal);

        engine.Stop();
    }

    /// <summary>A long poll to a machine this host does not poll is refused, naming the round.</summary>
    [Fact]
    public void ALongPollToAMachineThisHostDoesNotPollIsRefused()
    {
        using SoftwareFloor floor = SoftwareFloor.Of(2);
        using var engine = new PollEngine(floor.Host, Options(floor));

        engine.Start();
        Assert.True(WaitFor(() => engine.RoundsCompleted >= 1, TimeSpan.FromSeconds(30)));

        LongPollResult stranger = engine.Execute(MachineIdentification, to: SasAddress.Of(9));

        Assert.Equal(LongPollOutcome.NotSent, stranger.Outcome);
        Assert.Contains("does not poll $09", stranger.Detail, StringComparison.Ordinal);

        engine.Stop();
    }

    /// <summary>A named machine on the round answers, and answers as itself.</summary>
    [Fact]
    public void ALongPollThatNamesItsMachineIsAnsweredByThatMachine()
    {
        using SoftwareFloor floor = SoftwareFloor.Of(4);
        using var engine = new PollEngine(floor.Host, Options(floor));

        engine.Start();
        Assert.True(WaitFor(() => engine.RoundsCompleted >= 1, TimeSpan.FromSeconds(30)));

        SasAddress third = floor.Addresses[2];
        LongPollResult reply = engine.Execute(MachineIdentification, to: third);

        engine.Stop();

        Assert.Equal(LongPollOutcome.Answered, reply.Outcome);
        Assert.StartsWith($"{third}{MachineIdentification:X2}", reply.ToHex(), StringComparison.Ordinal);
    }

    /// <summary>A floor that would not fit in the address space is refused rather than wrapped.</summary>
    [Fact]
    public void AFloorThatWouldRunOffTheEndOfTheAddressSpaceIsRefused()
    {
        ArgumentOutOfRangeException refused = Assert.Throws<ArgumentOutOfRangeException>(
            () => SoftwareFloor.Of(10, SasAddress.Of(120)));

        Assert.Contains("addresses stop at", refused.Message, StringComparison.Ordinal);
    }

    // ---- the measurement ---------------------------------------------------------------------

    /// <summary>
    /// A round's cost is measured between two polls of the same machine, and grows with the floor.
    /// </summary>
    /// <remarks>
    /// Not an assertion about milliseconds, which would be a test that fails on a busy laptop.
    /// What is asserted is the shape the whole design rests on: a line carries one transaction at
    /// a time, so eight machines cost about twice what four cost. A build in which they cost the
    /// same is a build that has stopped measuring one line.
    /// </remarks>
    [Fact]
    public void ARoundOfEightCostsAboutTwiceARoundOfFour()
    {
        TimeSpan four = MeasureRound(4);
        TimeSpan eight = MeasureRound(8);

        double ratio = eight.TotalMilliseconds / four.TotalMilliseconds;

        Assert.InRange(ratio, 1.5, 2.5);
    }

    private static TimeSpan MeasureRound(int machines)
    {
        using SoftwareFloor floor = SoftwareFloor.Of(machines);
        using var engine = new PollEngine(floor.Host, Options(floor));

        engine.Start();
        Assert.True(WaitFor(() => engine.RoundsCompleted >= 6, TimeSpan.FromSeconds(60)),
            $"a floor of {machines} walked only {engine.RoundsCompleted} round(s)");
        engine.Stop();

        RoundMeasurement measured = RoundMeasurement.Of(engine.WireLog.Snapshot(), floor.Addresses);
        Assert.True(measured.Rounds >= 3, $"only {measured.Rounds} round(s) were timed");
        return measured.Median;
    }

    /// <summary>A machine that never answered is named, not left inside a timing table.</summary>
    [Fact]
    public void AMachineThatNeverAnsweredIsReportedAsSilent()
    {
        using SoftwareFloor floor = SoftwareFloor.Of(3);

        // A round that includes an address no machine on this line answers to.
        SasAddress absent = SasAddress.Of(40);
        var round = new List<SasAddress>(floor.Addresses) { absent };

        using var engine = new PollEngine(floor.Host, Options(floor) with { Addresses = round });

        engine.Start();
        Assert.True(WaitFor(() => engine.RoundsCompleted >= 3, TimeSpan.FromSeconds(30)));
        engine.Stop();

        RoundMeasurement measured = RoundMeasurement.Of(engine.WireLog.Snapshot(), round);

        Assert.Equal(absent, Assert.Single(measured.Silent));
        Assert.Equal(3, measured.Answered.Count);
    }
}
