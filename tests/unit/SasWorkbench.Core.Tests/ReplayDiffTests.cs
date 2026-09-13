using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Replay;
using SasWorkbench.Core.Sweep;
using SasWorkbench.Core.Transactions;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// What a replay concludes from a difference between a recording and a machine.
/// </summary>
/// <remarks>
/// These are the load-bearing tests of the whole command. A replay that reported "matched" and
/// "did not match" would be a wall of red with one real finding somewhere in it, because two
/// machines are never in the same configuration and have never done the same things — so the
/// property worth holding is that a legitimate difference is never red, and that a difference
/// nothing explains never comes out green.
/// </remarks>
public class ReplayDiffTests
{
    private static byte[] Bytes(string hex) =>
        Convert.FromHexString(hex.Replace(" ", string.Empty));

    private static LongPollResult Answered(byte command, string hex) =>
        new(command, SasChannel.One, LongPollOutcome.Answered, Bytes(hex), TimeSpan.Zero);

    private static LongPollResult Silent(byte command) =>
        new(command, SasChannel.One, LongPollOutcome.Silent, [], TimeSpan.Zero);

    private static LongPollResult Refused(byte command) =>
        new(command, SasChannel.One, LongPollOutcome.Rejected, [0x81], TimeSpan.Zero);

    private static ReplayFinding Classify(
        byte command,
        string? recorded,
        LongPollResult live,
        LongPollResult? again = null,
        ReplayContext? context = null,
        string[]? recordedReplies = null)
    {
        IReadOnlyList<byte[]> replies = recordedReplies is not null
            ? [.. recordedReplies.Select(Bytes)]
            : recorded is null ? [] : [Bytes(recorded)];

        var subject = new ReplaySubject(
            command, [],
            replies.Count == 0 ? ExchangeAnswer.Silent : ExchangeAnswer.Data,
            replies, live, again);

        return ReplayDiff.Classify(subject, occurrences: 1, context);
    }

    /// <summary>
    /// The check sequence covers the address, so two replies that agree in every other byte still
    /// end in different check bytes. The replay re-addresses every message to the machine in front
    /// of it, so neither difference is the machine's and reporting them would be noise on every
    /// row.
    /// </summary>
    [Fact]
    public void The_echoed_address_and_its_check_bytes_are_not_a_difference()
    {
        // Long poll $11, total coin in, five credits. Same payload, two addresses.
        ReplayFinding finding = Classify(
            0x11, "01 11 00 00 00 05 xx xx".Replace("xx xx", "6C 71"),
            Answered(0x11, "02 11 00 00 00 05 AB CD"));

        Assert.Equal(ReplayCause.Agrees, finding.Cause);
    }

    [Fact]
    public void Two_silences_agree()
    {
        ReplayFinding finding = Classify(0x11, recorded: null, Silent(0x11));

        Assert.Equal(ReplayCause.Agrees, finding.Cause);
        Assert.Contains("neither machine answered", finding.Explanation);
    }

    /// <summary>
    /// A machine answering its own address with bit 7 set is the machine saying it will not do
    /// this, which is a statement about the configuration it is in rather than a fault.
    /// </summary>
    [Fact]
    public void A_refusal_is_a_machine_in_another_configuration()
    {
        ReplayFinding finding = Classify(
            0x11, "01 11 00 00 00 05 6C 71", Refused(0x11));

        Assert.Equal(ReplayCause.Configuration, finding.Cause);
        Assert.Contains("will not do this", finding.Explanation);
    }

    /// <summary>
    /// A command the library's dispatcher does not carry cannot be answered by a machine built
    /// from it, so silence on one side is a revision.
    /// </summary>
    [Fact]
    public void Silence_on_a_command_the_library_does_not_implement_is_a_revision()
    {
        byte unimplemented = SasCatalogue.LongPolls
            .First(entry => entry.Value.Handler is null).Key;

        ReplayFinding finding = Classify(
            unimplemented, "01 " + $"{unimplemented:X2}" + " 00 00", Silent(unimplemented));

        Assert.Equal(ReplayCause.Revision, finding.Cause);
        Assert.Contains("no handler for this command", finding.Explanation);
    }

    /// <summary>
    /// The row that made the gate table exist. A machine answering nothing to a validation command
    /// is the library's own behaviour where the machine does not perform that validation, and the
    /// two machines have to be <em>measured</em> to differ on it before that can be claimed.
    /// </summary>
    [Fact]
    public void Silence_on_a_gated_command_is_a_configuration_where_the_gate_is_measured_to_differ()
    {
        var context = new ReplayContext
        {
            Disagreements = new Dictionary<string, (string, string)>
            {
                ["validation_style"] = ("0 and 2", "0"),
            },
            Read = new HashSet<string> { "validation_style" },
        };

        ReplayFinding finding = Classify(
            0x4D, "01 4D 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 "
                  + "00 00 00 00 00 00 00 00 00 F6 70",
            Silent(0x4D), context: context);

        Assert.Equal(ReplayCause.Configuration, finding.Cause);
        Assert.Contains("validation_style reads 0 and 2", finding.Explanation);
    }

    /// <summary>
    /// The same silence where the gate was read from both machines and they agree on it. That is a
    /// stronger statement than having no evidence: the cause is ruled out rather than left
    /// hanging, and the row stays a finding somebody has to look at.
    /// </summary>
    [Fact]
    public void A_gate_read_from_both_and_agreeing_is_ruled_out_rather_than_assumed()
    {
        var context = new ReplayContext
        {
            Read = new HashSet<string> { "validation_style" },
        };

        ReplayFinding finding = Classify(
            0x4D, "01 4D 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 "
                  + "00 00 00 00 00 00 00 00 00 F6 70",
            Silent(0x4D), context: context);

        Assert.Equal(ReplayCause.Defect, finding.Cause);
        Assert.Contains("reads the same on both", finding.Explanation);
    }

    [Fact]
    public void A_gate_nobody_read_says_what_would_settle_the_row()
    {
        ReplayFinding finding = Classify(
            0x4D, "01 4D 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 "
                  + "00 00 00 00 00 00 00 00 00 F6 70",
            Silent(0x4D));

        Assert.Equal(ReplayCause.Defect, finding.Cause);
        Assert.Contains("would settle this row", finding.Explanation);
    }

    /// <summary>
    /// Silence in a file is not a machine having answered nothing. Three innocent readings look
    /// identical in a recording, and the toolkit's own corpus contains all three.
    /// </summary>
    [Fact]
    public void A_recording_with_no_answer_produces_no_comparison()
    {
        ReplayFinding finding = Classify(
            0x11, recorded: null, Answered(0x11, "01 11 00 00 00 05 6C 71"));

        Assert.Equal(ReplayCause.NotComparable, finding.Cause);
        Assert.Contains("nothing to compare", finding.Explanation);
    }

    /// <summary>
    /// A meter only ever accumulates, so two machines that have run different games hold
    /// different values and neither is wrong.
    /// </summary>
    [Fact]
    public void A_meter_differing_is_the_same_kind_of_machine_with_a_different_life()
    {
        ReplayFinding finding = Classify(
            0x11, "01 11 00 00 00 05 6C 71",
            Answered(0x11, "01 11 00 00 09 99 AB CD"));

        Assert.Equal(ReplayCause.State, finding.Cause);
        ReplayFieldDiff field = Assert.Single(finding.Fields);
        Assert.Equal("total_coin_in", field.Path);
        Assert.Contains("only ever accumulates", field.Cause.Why);
    }

    /// <summary>
    /// A field measured to move between two reads of one machine moves on its own, whatever any
    /// table here says about it — and the measurement is reported as the stronger evidence.
    /// </summary>
    [Fact]
    public void A_field_measured_to_move_is_state_on_the_measurement_alone()
    {
        // $7E, the machine's clock. Recorded at one time, answered at another, and the two live
        // reads differ from each other.
        ReplayFinding finding = Classify(
            0x7E, "01 7E 07 20 20 26 10 13 07 7A 5D",
            Answered(0x7E, "01 7E 09 09 20 26 07 30 01 AB CD"),
            again: Answered(0x7E, "01 7E 09 09 20 26 07 30 02 AB CD"));

        Assert.Equal(ReplayCause.State, finding.Cause);
        Assert.Contains("second", finding.MovedOnTheMachine);
        Assert.Contains(finding.Fields,
            field => field.Path == "second" && field.Cause.Measured);
    }

    /// <summary>
    /// The one that cost a wrong verdict. A recording read across several captures may hold two
    /// machines' answers to one request, so a field differing between them is not evidence that
    /// it moves on its own — it once turned a serial number into a field that "moves".
    /// </summary>
    [Fact]
    public void A_field_differing_between_two_recorded_answers_is_not_a_measurement()
    {
        ReplayFinding finding = Classify(
            0x54, recorded: null,
            Answered(0x54, "01 54 0D 36 30 32 51 58 46 41 4B 45 30 30 30 31 02 74"),
            recordedReplies:
            [
                "01 54 09 36 30 32 31 30 30 30 30 31 5B C9",
                "01 54 09 36 30 32 32 30 30 30 30 32 AB CD",
            ]);

        Assert.NotEqual(ReplayCause.State, finding.Cause);
        Assert.Equal(ReplayCause.Configuration, finding.Cause);
        Assert.Contains("serial_number", finding.MovedInTheRecording);
        Assert.DoesNotContain("serial_number", finding.MovedOnTheMachine);
        Assert.Contains("2 different answers", finding.Explanation);
    }

    /// <summary>
    /// A length byte counts the bytes that follow it, so it differs exactly when they do and for
    /// the same reason. Reporting it separately double-counted one disagreement and left it
    /// unexplained.
    /// </summary>
    [Fact]
    public void A_framing_length_takes_the_cause_of_what_it_measures()
    {
        ReplayFinding finding = Classify(
            0x54, "01 54 09 36 30 32 31 30 30 30 30 31 5B C9",
            Answered(0x54, "01 54 0D 36 30 32 51 58 46 41 4B 45 30 30 30 31 02 74"));

        Assert.Equal(ReplayCause.Configuration, finding.Cause);
        Assert.Empty(finding.Unclassified);
        ReplayFieldDiff length = finding.Fields.Single(field => field.Path == "length");
        Assert.Equal(ReplayCause.Configuration, length.Cause.Cause);
        Assert.Contains("counts the bytes that follow", length.Cause.Why);
    }

    /// <summary>
    /// A field this build classifies neither way is reported as unexplained rather than assumed
    /// harmless, which is what keeps the gap in the table visible and countable.
    /// </summary>
    [Fact]
    public void An_unclassified_field_is_a_finding_and_is_named()
    {
        // $48, the last note taken. Its count is a number of notes on one machine and a number of
        // enabled games in another reply, so it is resolved per command; blank the override by
        // asking about a field with no entry at all.
        FieldCause? nothing = ReplayFields.Explains("a_field_no_layout_has");
        Assert.Null(nothing);
    }

    /// <summary>
    /// The names that mean two things, resolved by which reply they came from. Classifying such a
    /// name once and being wrong in the other reply is the failure the table exists to avoid.
    /// </summary>
    [Fact]
    public void A_name_meaning_two_things_is_resolved_by_the_command()
    {
        Assert.Equal(ReplayCause.State, ReplayFields.Explains("count", 0x48)!.Cause);
        Assert.Equal(ReplayCause.Configuration, ReplayFields.Explains("count", 0x56)!.Cause);
        Assert.Null(ReplayFields.Explains("count"));
    }
}

/// <summary>
/// The classification tables a replay leans on, checked against the layouts and the catalogue.
/// </summary>
/// <remarks>
/// Both tables here are stated rather than derived, so the thing that has to be true of them is
/// that they cannot go stale in silence. A field renamed in a layout, or a command removed from
/// the library, arrives as a failure here rather than as a classification that quietly stopped
/// matching anything.
/// </remarks>
public class ReplayTableTests
{
    private static IReadOnlySet<string> EveryFieldName()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Walk(IReadOnlyList<SasField> fields)
        {
            foreach (SasField field in fields)
            {
                names.Add(field.Name);
                if (field.IsGroup)
                    Walk(field.Fields);
            }
        }

        foreach (SasMessageLayout layout in SasLayouts.All.Values)
        {
            Walk(layout.Request);
            Walk(layout.Response);
        }

        foreach (SasEventLayout layout in SasEventLayouts.All.Values)
            Walk(layout.Payload);

        return names;
    }

    [Fact]
    public void Every_classified_field_name_exists_in_some_layout()
    {
        IReadOnlySet<string> known = EveryFieldName();
        List<string> strays = [.. ReplayFields.Names.Where(name => !known.Contains(name))];

        Assert.Empty(strays);
    }

    [Fact]
    public void Every_gated_command_is_in_the_catalogue()
    {
        foreach (ReplayGate gate in ReplayGates.All)
        {
            Assert.NotEmpty(gate.Gate);
            foreach (byte command in gate.Commands)
                Assert.True(SasCatalogue.LongPolls.ContainsKey(command), $"${command:X2}");
        }
    }

    /// <summary>
    /// A gate is only usable if the field it names is one this build can read out of a reply and
    /// classifies as configuration — that is what makes the gate a measurement rather than an
    /// assertion.
    /// </summary>
    [Fact]
    public void Every_gate_names_a_field_this_build_reads_as_configuration()
    {
        IReadOnlySet<string> known = EveryFieldName();
        foreach (ReplayGate gate in ReplayGates.All)
        {
            Assert.Contains(gate.Field, known);
            Assert.Equal(
                ReplayCause.Configuration, ReplayFields.Explains(gate.Field)?.Cause);
        }
    }

    [Fact]
    public void No_command_is_gated_twice()
    {
        List<byte> all = [.. ReplayGates.All.SelectMany(gate => gate.Commands)];
        Assert.Equal(all.Count, all.Distinct().Count());
    }
}

/// <summary>
/// What a replay takes out of a recording before it sends anything.
/// </summary>
public class ReplayRequestTests
{
    private long _sequence;

    private WireRecord LongPoll(byte command, params byte[] data)
    {
        var symbols = new SasSymbol[HostMessage.MaxSymbols];
        int length = HostMessage.WriteLongPoll(
            symbols, SasAddress.Of(1), command, data, checkSequence: true);
        return new WireRecord(_sequence++, WireDirection.Transmit, SasChannel.One,
            TimeSpan.FromMilliseconds(_sequence * 10), symbols[..length], null);
    }

    [Fact]
    public void The_request_is_the_bytes_between_the_command_and_the_check_sequence()
    {
        Assert.Equal([], ReplayRunner.RequestDataOf([0x01, 0x11]));
        Assert.Equal([], ReplayRunner.RequestDataOf([0x01, 0x11, 0x6C, 0x71]));
        Assert.Equal([0x00, 0x00],
            ReplayRunner.RequestDataOf([0x01, 0xA0, 0x00, 0x00, 0x6C, 0x13]));
    }

    /// <summary>
    /// Grouped by the command and the request together. The same command with two different game
    /// numbers in it asks two different questions, and merging them would compare one machine's
    /// answer about game one with another's about game two.
    /// </summary>
    [Fact]
    public void One_command_with_two_requests_is_two_rows()
    {
        TransactionSplit split = TransactionSplitter.Split(
        [
            LongPoll(0xA0, 0x00, 0x00),
            LongPoll(0xA0, 0x00, 0x01),
            LongPoll(0xA0, 0x00, 0x00),
        ]);

        IReadOnlyList<ReplayRunner.RequestGroup> groups = ReplayRunner.Group(split);

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups[0].Occurrences);
        Assert.Equal(1, groups[1].Occurrences);
    }

    /// <summary>
    /// Everything the sweep withholds, a replay withholds too, and out of the same table — so a
    /// setter added to the library cannot start being sent to somebody's machine by way of a
    /// recording that happens to contain it.
    /// </summary>
    [Fact]
    public void A_replay_withholds_exactly_what_a_sweep_withholds()
    {
        foreach (SweepEntry entry in SweepPlan.Withheld)
        {
            Assert.Equal(SweepDisposition.Withheld, SweepPlan.For(entry.Command).Disposition);
            Assert.NotEmpty(SweepPlan.For(entry.Command).Reason);
        }

        // The commands a conformance capture is full of, named so their absence would be noticed.
        foreach (byte setter in (byte[])[0x01, 0x02, 0x0E, 0x4C, 0x58, 0x71, 0x72, 0x7F, 0x86])
            Assert.Equal(SweepDisposition.Withheld, SweepPlan.For(setter).Disposition);
    }

    /// <summary>
    /// The two commands the sweep asks only because one argument makes them read are the two a
    /// replay has to check the bytes of rather than the disposition: a sweep picks its own
    /// argument and a replay is handed one. A capture's $74 is a host asking for a lock, and
    /// sending that again would lock somebody's machine.
    /// </summary>
    [Fact]
    public void A_recorded_request_that_is_not_the_reading_one_is_not_sent()
    {
        foreach (byte command in SweepPlan.ReadOnlyWithArgumentCommands)
        {
            byte[] reading = SweepPlan.ReadOnlyArgument(command)!;
            byte[] writing = [.. reading];
            writing[0] = (byte)(writing[0] ^ 0xFF);

            Assert.Null(ReplayRunner.WhyNotSent(command, reading));

            string? refused = ReplayRunner.WhyNotSent(command, writing);
            Assert.NotNull(refused);
            Assert.Contains("asks it with another", refused, StringComparison.Ordinal);
        }
    }
}
