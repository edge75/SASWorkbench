using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Reporting;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// The extended log: a description of each case before it runs, and of every packet with its
/// meaning.
/// </summary>
/// <remarks>
/// The property these tests exist for is the one in the first fact. A log asked for during a run
/// and a log produced afterwards from the record must be the same text, because that is what makes
/// the extended log a rendering rather than a second recording — and therefore what keeps the raw
/// record the only artefact that has to be right. Everything else here guards the promise in
/// "complete meaning": a packet this build cannot read says so and names the command, and nowhere
/// does it offer a plausible reading instead.
/// </remarks>
public sealed class ExtendedLogTests
{
    private static WireRecord Tx(long sequence, double atMs, params byte[] bytes) =>
        new(sequence, WireDirection.Transmit, SasChannel.One, TimeSpan.FromMilliseconds(atMs),
            Symbols(bytes, wakeupFirst: true), null);

    private static WireRecord Rx(long sequence, double atMs, params byte[] bytes) =>
        new(sequence, WireDirection.Receive, SasChannel.One, TimeSpan.FromMilliseconds(atMs),
            Symbols(bytes, wakeupFirst: false), null);

    private static SasSymbol[] Symbols(byte[] bytes, bool wakeupFirst)
    {
        var symbols = new SasSymbol[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            symbols[i] = i == 0 && wakeupFirst
                ? SasSymbol.Address(bytes[i])
                : SasSymbol.Data8(bytes[i]);
        }

        return symbols;
    }

    /// <summary>A long poll with its check sequence appended, as the host would put it on the wire.</summary>
    private static WireRecord Command(long sequence, byte address, byte command, params byte[] data)
    {
        var body = new byte[2 + data.Length + 2];
        body[0] = address;
        body[1] = command;
        data.CopyTo(body, 2);
        SasCrc.Append(body, 2 + data.Length);
        return Tx(sequence, sequence * 10, body);
    }

    private static WireRecord Reply(long sequence, byte address, byte command, params byte[] payload)
    {
        var body = new byte[2 + payload.Length + 2];
        body[0] = address;
        body[1] = command;
        payload.CopyTo(body, 2);
        SasCrc.Append(body, 2 + payload.Length);
        return Rx(sequence, sequence * 10, body);
    }

    private static RunReport Sample() => new()
    {
        StartedAt = new DateTimeOffset(2026, 9, 7, 8, 30, 0, TimeSpan.Zero),
        Duration = TimeSpan.FromSeconds(4.25),
        Capabilities = ["decoder.lp1F", "stimulus.bill"],
        Provenance =
        [
            new RunProvenance("tcp 127.0.0.1:9760", TransportFidelity.Proven,
                StimulusBackend.Software, "In-process link."),
        ],
        Tests =
        [
            new TestOutcome
            {
                Name = "10.1.3 Cashout Ticket 1",
                Location = new SourceLocation("tests/conformance/10.1/10.1.3.sas", 26),
                Verdict = Verdict.Pass,
                StartedAt = new DateTimeOffset(2026, 9, 7, 8, 30, 1, TimeSpan.Zero),
                Duration = TimeSpan.FromSeconds(4.1),
                Tags = ["needs-stimulus"],
                Requirements =
                [
                    new Requirement(new SourceLocation("tests/conformance/10.1/10.1.3.sas", 29),
                        "egm.validation", "standard"),
                    new Requirement(new SourceLocation("tests/conformance/10.1/10.1.3.sas", 30),
                        "printer"),
                ],
                Provenance = new RunProvenance("tcp 127.0.0.1:9760", TransportFidelity.Proven,
                    StimulusBackend.Software, "In-process link."),
                Steps =
                [
                    new StepResult(new SourceLocation("tests/conformance/10.1/10.1.3.sas", 32),
                        "send lp 3D", Verdict.Pass, "a reply", "answered",
                        At: TimeSpan.FromMilliseconds(120)) { IsAssertion = true },
                    new StepResult(new SourceLocation("tests/conformance/10.1/10.1.3.sas", 33),
                        "bill insert 2000", Verdict.Skip, Reason: "nothing drives the machine",
                        At: TimeSpan.FromMilliseconds(140)),
                ],
                Wire =
                [
                    Tx(0, 0, 0x80),
                    Tx(1, 20, 0x81),
                    Rx(2, 45, 0x00),

                    // $21 is named in the catalogue and has no layout in this build; $1A has one.
                    Command(3, 0x01, 0x21),
                    Rx(4, 70, 0x81),
                    Command(5, 0x01, 0x1A),
                    Reply(6, 0x01, 0x1A, 0x00, 0x00, 0x12, 0x34),
                ],
            },
        ],
    };

    // ---- the load-bearing property ---------------------------------------------------------

    /// <summary>
    /// A log rendered during a run and a log rendered from that run's own record are the same text.
    /// </summary>
    /// <remarks>
    /// This is what makes the extended log a rendering and not a capture. If the live rendering
    /// could ever say more than the recorded one, somebody would come to depend on something that
    /// does not survive a reload, and the record would stop being sufficient — which is the whole
    /// arrangement D34 chose. Written as an equality rather than as a list of fields so that a
    /// field added to the renderer and forgotten in the record fails here.
    /// </remarks>
    [Fact]
    public void ARunExplainedAfterwardsReadsTheSameAsOneExplainedAsItRan()
    {
        RunReport report = Sample();
        var writer = new ExtendedLogWriter();

        string live = writer.Render(report);
        string recorded = writer.Render(
            JsonLinesReader.Read(Lines(new JsonLinesReportWriter().Render(report))).Report);

        Assert.Equal(live, recorded);
    }

    [Fact]
    public void ReadingBackARecordReportsNoProblemsWithIt()
    {
        JsonLinesReader.Result read = JsonLinesReader.Read(
            Lines(new JsonLinesReportWriter().Render(Sample())));

        Assert.Empty(read.Problems);
        Assert.False(read.IsEmpty);
    }

    /// <summary>
    /// What a case declared it needs survives into the record, because a description is built from
    /// it.
    /// </summary>
    [Fact]
    public void WhatACaseNeedsSurvivesIntoTheRecordAndBackOut()
    {
        TestOutcome test = JsonLinesReader
            .Read(Lines(new JsonLinesReportWriter().Render(Sample())))
            .Report.Tests[0];

        Assert.Equal(2, test.Requirements.Count);
        Assert.Equal("egm.validation", test.Requirements[0].Key);
        Assert.Equal("standard", test.Requirements[0].ExpectedValue);
        Assert.Equal(29, test.Requirements[0].Location.Line);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 8, 30, 1, TimeSpan.Zero), test.StartedAt);
    }

    // ---- the description ---------------------------------------------------------------------

    [Fact]
    public void ACaseIsDescribedFromItsOwnNameTagsRequirementsAndLines()
    {
        string log = new ExtendedLogWriter().Render(Sample());

        Assert.Contains("10.1.3 Cashout Ticket 1", log, StringComparison.Ordinal);
        Assert.Contains("tests/conformance/10.1/10.1.3.sas:26", log, StringComparison.Ordinal);
        Assert.Contains("@needs-stimulus", log, StringComparison.Ordinal);
        Assert.Contains("egm.validation == standard", log, StringComparison.Ordinal);
        Assert.Contains("declared at line 29", log, StringComparison.Ordinal);
        Assert.Contains("between lines 32 and 33", log, StringComparison.Ordinal);
    }

    /// <summary>
    /// A case that says nothing about itself is told so, rather than given prose nobody wrote.
    /// </summary>
    [Fact]
    public void ACaseThatSaysNothingAboutItselfIsToldSoRatherThanGivenInventedProse()
    {
        RunReport report = Sample();
        report = report with
        {
            Tests = [report.Tests[0] with { Tags = [], Requirements = [] }],
        };

        string log = new ExtendedLogWriter().Render(report);

        Assert.Contains("says nothing about itself", Flat(log), StringComparison.Ordinal);
        Assert.Contains("say in the script what the case assumes", Flat(log),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ACaseThatDescribesItselfIsNotLecturedAboutIt()
    {
        Assert.DoesNotContain(
            "says nothing about itself",
            Flat(new ExtendedLogWriter().Render(Sample())),
            StringComparison.Ordinal);
    }

    // ---- every packet gets a line -------------------------------------------------------------

    [Fact]
    public void EveryPacketInTheRecordGetsALine()
    {
        RunReport report = Sample();
        string log = new ExtendedLogWriter().Render(report);

        foreach (WireRecord record in report.Tests[0].Wire)
            Assert.Contains(record.ToHex(), log, StringComparison.Ordinal);
    }

    [Fact]
    public void AGeneralPollAndASynchronisationPollAreToldApart()
    {
        IReadOnlyList<PacketExplanation> packets =
            PacketExplainer.ExplainAll([Tx(0, 0, 0x80), Tx(1, 10, 0x81)]);

        Assert.Contains("synchronisation poll", packets[0].Headline, StringComparison.Ordinal);
        Assert.Contains("general poll of the machine at $01", packets[1].Headline,
            StringComparison.Ordinal);
        Assert.All(packets, p => Assert.Equal(PacketMeaning.Explained, p.Meaning));
    }

    [Fact]
    public void ACommandWithAnEstablishedLayoutIsExplainedFieldByField()
    {
        PacketExplanation reply = PacketExplainer
            .ExplainAll([Tx(0, 0, 0x81), Reply(1, 0x01, 0x1A, 0x00, 0x00, 0x12, 0x34)])[1];

        Assert.Equal(PacketMeaning.Explained, reply.Meaning);
        Assert.Equal<byte?>(0x1A, reply.Command);
        Assert.NotEmpty(reply.Fields);
        Assert.All(reply.Fields, f => Assert.False(string.IsNullOrWhiteSpace(f.Rendered)));

        // The claim is checkable: the layout says where it was read from.
        Assert.False(string.IsNullOrWhiteSpace(reply.DerivedFrom));
    }

    /// <summary>
    /// A command this build cannot decode is named and marked undecoded, and nothing about its
    /// bytes is claimed.
    /// </summary>
    /// <remarks>
    /// The honest form of "complete meaning" on a reply nobody has modelled. A plausible reading
    /// would be indistinguishable downstream from a real one, which is worse than no reading:
    /// a test could assert on it and pass.
    /// </remarks>
    [Fact]
    public void ACommandWithNoEstablishedLayoutIsNamedAndSaysItIsNotDecoded()
    {
        PacketExplanation packet = PacketExplainer.ExplainAll([Command(0, 0x01, 0x21)])[0];

        Assert.Equal(PacketMeaning.NamedOnly, packet.Meaning);
        Assert.Equal("known but not decoded", packet.MeaningLabel);
        Assert.Equal<byte?>(0x21, packet.Command);
        Assert.Contains("$21", packet.Headline, StringComparison.Ordinal);
        Assert.Contains("has not established", packet.Limit!, StringComparison.Ordinal);

        // Named, and no fields invented for it.
        Assert.Empty(packet.Fields);
    }

    [Fact]
    public void ACommandInNoListAtAllIsNotEvenNamed()
    {
        // $23 is not a long poll the gaming machine library defines, so nothing can name it.
        Assert.False(SasCatalogue.LongPolls.ContainsKey(0x23));

        PacketExplanation packet = PacketExplainer.ExplainAll([Command(0, 0x01, 0x23)])[0];

        Assert.Equal(PacketMeaning.Unknown, packet.Meaning);
        Assert.Equal("unknown", packet.MeaningLabel);
        Assert.Contains("cannot even be named", packet.Limit!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A reply whose bytes do not fit its layout is a finding, and never a partial reading
    /// presented as a whole one.
    /// </summary>
    [Fact]
    public void AReplyThatDoesNotFitItsLayoutSaysSoRatherThanDecodingWhatItCan()
    {
        // Long poll $1A answers with four bytes of current credits. Two is not that reply.
        PacketExplanation packet = PacketExplainer
            .ExplainAll([Tx(0, 0, 0x81), Reply(1, 0x01, 0x1A, 0x00, 0x00)])[1];

        Assert.Equal(PacketMeaning.LayoutDidNotFit, packet.Meaning);
        Assert.NotNull(packet.Limit);
        Assert.Contains("byte", packet.Limit!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- bytes that are not a message ---------------------------------------------------------

    /// <summary>
    /// A reply whose check sequence does not check out is not read as a reply to anything.
    /// </summary>
    /// <remarks>
    /// These are the bytes the conformance toolkit's tracer printed as one line when it split a
    /// fourteen-byte reply to long poll $54 across two. Read as written, the second byte is $54
    /// and the packet claims to be a $54 that does not fit — which is a claim about the machine
    /// made from a fault in the recording. The check sequence is the protocol's own test of
    /// whether a run of bytes is one message, and it fails here, so nothing is read out of them.
    /// </remarks>
    [Fact]
    public void AReplyWhoseCheckSequenceDoesNotCheckOutIsNotReadAsAReply()
    {
        PacketExplanation packet = PacketExplainer.ExplainAll(
        [
            Tx(0, 0, 0x81),
            Rx(1, 10, 0x01, 0x54, 0x09, 0x36, 0x30, 0x32, 0x31, 0x30),
        ])[1];

        Assert.Equal(PacketMeaning.NotAMessage, packet.Meaning);
        Assert.Equal("not a message", packet.MeaningLabel);
        Assert.Null(packet.Command);
        Assert.Contains("do not stand as a SAS message", packet.Headline, StringComparison.Ordinal);

        // The evidence stays on the packet: the bytes, and the check that failed.
        Assert.Contains(packet.Findings,
            f => f.Contains("check sequence does not check out", StringComparison.Ordinal));
    }

    /// <summary>
    /// The pieces of a split reply neither prove nor deny that the command was understood.
    /// </summary>
    /// <remarks>
    /// The tally rule is that a command counts as explained only when every packet carrying it
    /// was, and that rule is right for whole messages. Applied to a fragment it turned a hundred
    /// good $54 replies into an undecoded command, and it read a piece of one reply as traffic
    /// for a command the capture never contained.
    /// </remarks>
    [Fact]
    public void APieceOfASplitReplyNeitherProvesNorDeniesTheCommand()
    {
        PacketCoverage coverage = PacketCoverage.Of(PacketExplainer.ExplainAll(
        [
            Tx(0, 0, 0x81),
            Command(1, 0x01, 0x54),
            Reply(2, 0x01, 0x54, 0x09, 0x36, 0x30, 0x32, 0x31, 0x30, 0x30, 0x30, 0x30, 0x31),
            Rx(3, 30, 0x01, 0x54, 0x09, 0x36, 0x30, 0x32, 0x31, 0x30),
            Rx(4, 40, 0x30, 0x30, 0x30, 0x31, 0x5B, 0xC9),
        ]));

        Assert.Equal([0x54], coverage.CommandsExplained);
        Assert.Empty(coverage.CommandsNamedOnly);
        Assert.Empty(coverage.CommandsUnknown);
        Assert.Equal(2, coverage.NotAMessage);
    }

    /// <summary>
    /// A two-byte machine message is not a reply, and the second byte is not a command.
    /// </summary>
    /// <remarks>
    /// This is the pair the tracer printed at the tail of a split reply to long poll $7E. Read as
    /// a reply it names long poll $20 at a machine address the link never used, which is how a
    /// command the corpus does not contain appeared in a coverage tally.
    /// </remarks>
    [Fact]
    public void TwoBytesFromTheMachineAreNotReadAsAReply()
    {
        PacketExplanation packet = PacketExplainer
            .ExplainAll([Tx(0, 0, 0x81), Rx(1, 10, 0x07, 0x20)])[1];

        Assert.Equal(PacketMeaning.NotAMessage, packet.Meaning);
        Assert.Null(packet.Command);
    }

    /// <summary>
    /// The busy response is the one reply to a long poll that echoes no command, so it has to be
    /// recognised before anything reads a command out of the second byte.
    /// </summary>
    [Fact]
    public void TheBusyResponseIsNamedRatherThanReadAsALongPollZero()
    {
        PacketExplanation packet = PacketExplainer
            .ExplainAll([Tx(0, 0, 0x81), Rx(1, 10, 0x01, 0x00)])[1];

        Assert.Equal(PacketMeaning.Explained, packet.Meaning);
        Assert.Null(packet.Command);
        Assert.Contains("busy", packet.Headline, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A long poll with no data is two bytes and carries no check sequence, so the rule above must
    /// not reach it. Getting this wrong makes every bare long poll unreadable.
    /// </summary>
    [Fact]
    public void ALongPollWithNoDataIsStillReadAsALongPoll()
    {
        PacketExplanation packet = PacketExplainer.ExplainAll([Tx(0, 0, 0x01, 0x54)])[0];

        Assert.Equal(PacketMeaning.Explained, packet.Meaning);
        Assert.Equal<byte?>(0x54, packet.Command);
    }

    /// <summary>
    /// The same rule runs in both directions. A host message whose check sequence does not check
    /// out is still a finding, and it is still not evidence that this build understands the
    /// command somebody would have read out of its second byte.
    /// </summary>
    [Fact]
    public void AHostMessageWhoseCheckSequenceIsWrongIsNotReadAsALongPollEither()
    {
        PacketExplanation packet = PacketExplainer.ExplainAll(
            [Tx(0, 0, 0x01, 0x4C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x24, 0xF7)])[0];

        Assert.Equal(PacketMeaning.NotAMessage, packet.Meaning);
        Assert.Null(packet.Command);
        Assert.Contains(packet.Findings,
            f => f.Contains("check sequence does not check out", StringComparison.Ordinal));
    }

    // ---- the one-byte answer, which is genuinely ambiguous -----------------------------------

    [Fact]
    public void AnExceptionIsNamedAndExplainedOnceTheAddressIsKnown()
    {
        PacketExplanation packet = PacketExplainer
            .ExplainAll([Tx(0, 0, 0x81), Rx(1, 30, 0x4A)])[1];

        Assert.Equal(PacketMeaning.Explained, packet.Meaning);
        Assert.Equal<byte?>(0x4A, packet.Exception);
        Assert.Contains(SasNames.ExceptionName(0x4A), packet.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMachinesOwnAddressComingBackIsAnAcknowledgementRatherThanAnException()
    {
        PacketExplanation packet = PacketExplainer
            .ExplainAll([Tx(0, 0, 0xC1), Rx(1, 30, 0x41)])[1];

        Assert.Equal(PacketMeaning.Explained, packet.Meaning);
        Assert.Contains("acknowledgement", packet.Headline, StringComparison.Ordinal);
        Assert.Null(packet.Exception);
    }

    [Fact]
    public void ARefusalIsToldFromAnAcknowledgement()
    {
        PacketExplanation packet = PacketExplainer
            .ExplainAll([Tx(0, 0, 0x81), Rx(1, 30, 0x81)])[1];

        Assert.Contains("refusal", packet.Headline, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no address anywhere in the record, both readings of a single byte are stated and
    /// neither is chosen.
    /// </summary>
    [Fact]
    public void ASingleByteWithNoAddressInTheRecordIsNotGuessedAt()
    {
        PacketExplanation packet = PacketExplainer.ExplainAll([Rx(0, 0, 0x4A)])[0];

        Assert.Equal(PacketMeaning.NamedOnly, packet.Meaning);
        Assert.Contains("either exception", packet.Headline, StringComparison.Ordinal);
        Assert.Contains("acknowledgement", packet.Headline, StringComparison.Ordinal);
        Assert.Contains("cannot be said", packet.Limit!, StringComparison.Ordinal);
    }

    [Fact]
    public void SilenceFromTheMachineIsExplainedRatherThanReadAsAnException()
    {
        PacketExplanation packet = PacketExplainer
            .ExplainAll([Tx(0, 0, 0x81), Rx(1, 30, 0x00)])[1];

        Assert.Equal(PacketMeaning.Explained, packet.Meaning);
        Assert.Contains("no exception", packet.Headline, StringComparison.Ordinal);
    }

    // ---- findings about the packet itself ----------------------------------------------------

    [Fact]
    public void ACheckSequenceThatDoesNotCheckOutIsAFinding()
    {
        WireRecord broken = Tx(0, 0, 0x01, 0x08, 0x02, 0x00, 0x00);

        PacketExplanation packet = PacketExplainer.ExplainAll([broken])[0];

        Assert.Contains(
            packet.Findings,
            f => f.Contains("check sequence", StringComparison.Ordinal));
    }

    [Fact]
    public void AWakeupBitOnAnythingButTheAddressByteIsAFinding()
    {
        var bad = new WireRecord(0, WireDirection.Transmit, SasChannel.One, TimeSpan.Zero,
            [SasSymbol.Address(0x01), SasSymbol.Address(0x1A)], null);

        Assert.Contains(
            PacketExplainer.ExplainAll([bad])[0].Findings,
            f => f.Contains("belongs on the address byte alone", StringComparison.Ordinal));
    }

    [Fact]
    public void AHostMessageWithNoWakeupBitAtAllIsAFinding()
    {
        var bad = new WireRecord(0, WireDirection.Transmit, SasChannel.One, TimeSpan.Zero,
            [SasSymbol.Data8(0x01), SasSymbol.Data8(0x1A)], null);

        Assert.Contains(
            PacketExplainer.ExplainAll([bad])[0].Findings,
            f => f.Contains("none would answer", StringComparison.Ordinal));
    }

    /// <summary>A machine may never set the wakeup bit, so one coming back is always a fault.</summary>
    [Fact]
    public void AWakeupBitFromTheMachineIsAFinding()
    {
        var bad = new WireRecord(0, WireDirection.Receive, SasChannel.One, TimeSpan.Zero,
            [SasSymbol.Address(0x00)], null);

        Assert.Contains(
            PacketExplainer.ExplainAll([bad])[0].Findings,
            f => f.Contains("only a host may set it", StringComparison.Ordinal));
    }

    // ---- the packets no step accounts for ----------------------------------------------------

    /// <summary>
    /// The host's own polls are marked as such, with the reason the record carries.
    /// </summary>
    /// <remarks>
    /// The capability probe asks a machine which validation style it performs before a case that
    /// requires one, and it happens before any step runs. A reader counting packets against steps
    /// would come up short with no way of knowing why, so the reason is written onto the record as
    /// the poll is sent and read back here — never inferred from where the packet sits.
    /// </remarks>
    [Fact]
    public void APollTheHostSentOnItsOwnBehalfIsMarkedWithTheReason()
    {
        WireRecord probe = Command(0, 0x01, 0xA0, 0x00, 0x00) with
        {
            Note = WirePurpose.Label("long poll $A0", WirePurpose.CapabilityProbe),
        };

        PacketExplanation packet = PacketExplainer.ExplainAll([probe])[0];

        Assert.True(packet.IsHostsOwn);
        Assert.Equal(WirePurpose.CapabilityProbe, packet.Purpose);
    }

    [Fact]
    public void APollAStepAskedForIsNotMarkedAsTheHostsOwn()
    {
        WireRecord asked = Command(0, 0x01, 0xA0, 0x00, 0x00) with { Note = "long poll $A0" };

        Assert.False(PacketExplainer.ExplainAll([asked])[0].IsHostsOwn);
    }

    [Fact]
    public void TheLogSaysHowManyPacketsNoStepAccountsForAndWhy()
    {
        RunReport report = Sample();
        report = report with
        {
            Tests =
            [
                report.Tests[0] with
                {
                    Wire =
                    [
                        Command(0, 0x01, 0xA0, 0x00, 0x00) with
                        {
                            Note = WirePurpose.Label("long poll $A0", WirePurpose.CapabilityProbe),
                        },
                    ],
                },
            ],
        };

        string log = new ExtendedLogWriter().Render(report);

        Assert.Contains("on its own behalf", Flat(log), StringComparison.Ordinal);
        Assert.Contains(WirePurpose.CapabilityProbe, Flat(log), StringComparison.Ordinal);
    }

    [Fact]
    public void ARunWhereEveryPacketIsAccountedForSaysThatInstead()
    {
        Assert.Contains(
            "Every one was sent by the poll loop or asked for by a step",
            Flat(new ExtendedLogWriter().Render(Sample())),
            StringComparison.Ordinal);
    }

    // ---- coverage, as a number ---------------------------------------------------------------

    [Fact]
    public void CoverageCountsWhatWasExplainedInFullAndWhatWasOnlyNamed()
    {
        PacketCoverage coverage = PacketCoverage.Of(
            PacketExplainer.ExplainAll(Sample().Tests[0].Wire));

        Assert.Equal(7, coverage.Packets);
        Assert.Equal(1, coverage.NamedOnly);
        Assert.Equal(6, coverage.Explained);
        Assert.Equal(0, coverage.Unknown);

        // Two distinct commands crossed the wire; one of them is a layout this build has not
        // established, and it is named so that breadth work knows where to go.
        Assert.Equal(2, coverage.Commands);
        Assert.Equal([0x21], coverage.CommandsNamedOnly);
        Assert.Equal([0x1A], coverage.CommandsExplained);
    }

    /// <summary>
    /// A command whose request decodes and whose reply does not is not an explained command.
    /// </summary>
    /// <remarks>
    /// Rounding it up to explained would hide the exact gap this tally exists to expose: the
    /// interesting half of a long poll is almost always the answer.
    /// </remarks>
    [Fact]
    public void ACommandIsOnlyExplainedWhenEveryPacketCarryingItWas()
    {
        PacketCoverage coverage = PacketCoverage.Of(PacketExplainer.ExplainAll(
        [
            Tx(0, 0, 0x81),
            Command(1, 0x01, 0x1A),
            Reply(2, 0x01, 0x1A, 0x00, 0x00),
        ]));

        Assert.Empty(coverage.CommandsExplained);
        Assert.Equal([0x1A], coverage.CommandsNamedOnly);
    }

    [Fact]
    public void TheLogReportsCoverageAsANumberAndNamesWhatItCouldNotExplain()
    {
        string log = new ExtendedLogWriter().Render(Sample());

        Assert.Contains("explained in full", log, StringComparison.Ordinal);
        Assert.Contains("known but not decoded", log, StringComparison.Ordinal);
        Assert.Contains("$21", log, StringComparison.Ordinal);
        Assert.Contains("where breadth work is worth most", Flat(log), StringComparison.Ordinal);
    }

    /// <summary>
    /// A run that explained a fraction of its traffic must not read like one that explained all
    /// of it.
    /// </summary>
    [Fact]
    public void ARunThatExplainedLittleDoesNotReadLikeOneThatExplainedEverything()
    {
        RunReport poor = Sample();
        poor = poor with
        {
            Tests =
            [
                poor.Tests[0] with
                {
                    Wire = [Command(0, 0x01, 0x21), Command(1, 0x01, 0x6E), Command(2, 0x01, 0x72)],
                },
            ],
        };

        string log = new ExtendedLogWriter().Render(poor);

        Assert.Contains("known but not decoded         3  (100%)", log, StringComparison.Ordinal);
        Assert.Contains("explained in full             0  (0%)", log, StringComparison.Ordinal);
    }

    // ---- a record from somewhere else --------------------------------------------------------

    /// <summary>
    /// A link weight this build does not know is reported, not rounded up to a better one.
    /// </summary>
    /// <remarks>
    /// Honest labelling applies to reading a record as much as to writing one. Reading an unknown
    /// weight as "proven" would make somebody else's run look worth more than it is.
    /// </remarks>
    [Fact]
    public void AnUnknownLinkWeightIsReportedRatherThanReadAsProven()
    {
        string[] lines =
        [
            """{"kind":"run","startedAt":"2026-09-07T08:30:00+00:00","durationMs":10,"provenance":[{"transport":"a link from the future","fidelity":"blessed","stimulus":"software"}],"capabilities":[]}""",
            """{"kind":"test","name":"t","file":"t.sas","line":1,"verdict":"Pass","durationMs":1,"tags":[],"requires":[]}""",
        ];

        JsonLinesReader.Result read = JsonLinesReader.Read(lines);

        Assert.Contains(read.Problems, p => p.Contains("blessed", StringComparison.Ordinal));
        Assert.Equal(TransportFidelity.Unverified, read.Report.Provenance[0].Fidelity);
    }

    [Fact]
    public void AVerdictThisBuildDoesNotKnowIsReadAsAnErrorAndReported()
    {
        string[] lines =
        [
            """{"kind":"test","name":"t","file":"t.sas","line":1,"verdict":"Marvellous","durationMs":1}""",
        ];

        JsonLinesReader.Result read = JsonLinesReader.Read(lines);

        Assert.Contains(read.Problems, p => p.Contains("Marvellous", StringComparison.Ordinal));
        Assert.Equal(Verdict.Error, read.Report.Tests[0].Verdict);
    }

    [Fact]
    public void ALineThatIsNotJsonIsReportedRatherThanSkippedInSilence()
    {
        JsonLinesReader.Result read = JsonLinesReader.Read(["this is not a record"]);

        Assert.Contains(read.Problems, p => p.Contains("is not JSON", StringComparison.Ordinal));
        Assert.True(read.IsEmpty);
    }

    [Fact]
    public void APacketBelongingToNoTestInTheFileIsReported()
    {
        string[] lines =
        [
            """{"kind":"wire","test":"a test this file does not describe","symbols":"1.80"}""",
        ];

        Assert.Contains(
            JsonLinesReader.Read(lines).Problems,
            p => p.Contains("which this file does not describe", StringComparison.Ordinal));
    }

    [Fact]
    public void ASkippedCaseWithNoPacketsSaysSoRatherThanShowingAnEmptyList()
    {
        RunReport report = Sample();
        report = report with
        {
            Tests =
            [
                report.Tests[0] with
                {
                    Verdict = Verdict.Skip,
                    SkipReason = "printer: this environment provides no 'printer'",
                    Wire = [],
                },
            ],
        };

        string log = new ExtendedLogWriter().Render(report);

        Assert.Contains("holds no packets for this case", Flat(log), StringComparison.Ordinal);
        Assert.Contains("this environment provides no 'printer'", log, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// The log with its line breaks flattened, so a sentence assertion is about the words rather
    /// than about where the renderer chose to wrap them.
    /// </summary>
    private static string Flat(string log) =>
        string.Join(' ', log.Split(
            (char[])['\r', '\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
}
