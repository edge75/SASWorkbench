using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Sweep;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// The classification that decides what a sweep may send to somebody's gaming machine.
/// </summary>
/// <remarks>
/// The point of these is that the classification cannot go stale in silence. It is read against
/// the generated catalogue, so a command added to the gaming machine library arrives here as a
/// failing test rather than as a command the sweep quietly sends.
/// </remarks>
public class SweepPlanTests
{
    [Fact]
    public void Every_command_in_the_catalogue_is_classified()
    {
        Assert.Equal(SasCatalogue.LongPolls.Count, SweepPlan.All.Count);

        foreach (byte command in SasCatalogue.LongPolls.Keys)
        {
            SweepEntry entry = SweepPlan.For(command);
            Assert.Equal(command, entry.Command);
            Assert.NotEmpty(entry.Reason);
        }
    }

    /// <summary>
    /// The rule the whole plan rests on, enforced rather than documented: a command the plan
    /// sends is one the library answers in a handler named for sending something back. A setter
    /// added to the library fails here instead of being swept.
    /// </summary>
    [Fact]
    public void Nothing_is_sent_whose_handler_does_more_than_answer()
    {
        var wrong = SweepPlan.Asked
            .Where(e => !SweepPlan.HandlerOnlyAnswers(e.Command))
            .Where(e => SweepPlan.ReadOnlyArgument(e.Command) is null)
            .Select(e => $"${e.Command:X2} {e.Name}")
            .ToList();

        Assert.Empty(wrong);
    }

    /// <summary>
    /// The exception to that rule, named one command at a time. A third entry has to be added
    /// here deliberately, which is the point: each one is a claim that for this argument the
    /// library takes no branch that writes, and nobody should be able to make that claim by
    /// editing a table somewhere else.
    /// </summary>
    [Fact]
    public void Only_two_commands_are_asked_because_an_argument_makes_them_read()
    {
        Assert.Equal(
            new byte[] { 0x74, 0x7B },
            SweepPlan.ReadOnlyWithArgumentCommands.Order());

        // Lock code $FF is LC_INTERROGATE. $00 asks for a lock and $80 cancels one.
        Assert.Equal(0xFF, SweepPlan.ReadOnlyArgument(0x74)![0]);

        // A control mask of nothing selects no status bit for the handler to change.
        Assert.Equal(new byte[] { 0x00, 0x00 }, SweepPlan.ReadOnlyArgument(0x7B)![1..3]);

        // And the request is the full eight bytes, with both expiry dates zero. libsas reads the
        // two BCD expiry fields at fixed offsets whatever the length byte says, so a four-byte
        // request has it take them from the check sequence and anything after it - and a non-zero
        // value there is written to NVRAM as the machine's default ticket expiry. Zero is the
        // handler's "leave as is".
        byte[] request = SweepPlan.ReadOnlyArgument(0x7B)!;
        Assert.Equal(0x08, request[0]);
        Assert.Equal(9, request.Length);
        Assert.All(request[5..9], b => Assert.Equal(0x00, b));

        foreach (byte command in SweepPlan.ReadOnlyWithArgumentCommands)
        {
            Assert.Equal(SweepDisposition.Ask, SweepPlan.For(command).Disposition);

            // And the reply is one this build can actually read, or asking would prove nothing.
            Assert.True(SasLayouts.All.ContainsKey(command));
        }
    }

    /// <summary>
    /// And the other direction, so the withheld list stays a list of decisions rather than a list
    /// of everything: a command withheld for a stated reason must not be one the rule would have
    /// let through by accident. Both halves being checked is what makes the count meaningful.
    /// </summary>
    [Fact]
    public void Every_withheld_command_says_what_it_would_change()
    {
        List<SweepEntry> withheld = SweepPlan.Withheld.ToList();

        Assert.NotEmpty(withheld);
        foreach (SweepEntry entry in withheld)
        {
            Assert.DoesNotContain("not classified here", entry.Reason, StringComparison.Ordinal);
            Assert.NotEmpty(entry.Reason);
        }
    }

    [Fact]
    public void The_commands_that_move_money_are_all_withheld()
    {
        // Named one by one rather than derived, because this is the check that would have to fail
        // for the sweep to redeem a ticket or move funds on somebody's machine.
        byte[] money = [0x71, 0x72, 0x73, 0x80, 0x86, 0x8A, 0x8B, 0x94, 0x58, 0x06, 0x07];

        foreach (byte command in money)
        {
            Assert.Equal(SweepDisposition.Withheld, SweepPlan.For(command).Disposition);
        }
    }

    /// <summary>
    /// The request the sweep sends comes from the receiver's own framing table, so for all but a
    /// handful of commands there is one form and it is the right length.
    /// </summary>
    [Fact]
    public void A_swept_command_is_asked_the_length_the_receiver_frames()
    {
        foreach (SweepEntry entry in SweepPlan.Asked)
        {
            IReadOnlyList<byte[]> ladder = CatalogueSweep.RequestLadder(entry);

            Assert.NotEmpty(ladder);
            if (entry.Request is not null)
                continue;

            if (SasCommandFraming.RequestDataBytes(entry.Command) is { } width)
            {
                Assert.Single(ladder);
                Assert.Equal(width, ladder[0].Length);
            }
            else
            {
                // Length-prefixed, or a command the receiver does not frame. Either way the sweep
                // says what it sent rather than claiming to know the shape. Two forms where the
                // command carries a repeating group: asked for nothing, and asked for one entry,
                // because two revisions of the library frame such a command differently and only
                // the machine can say which it is.
                Assert.True(ladder.Count is 1 or 2 or 3);

                if (ladder.Count == 2)
                {
                    Assert.True(CatalogueSweep.OneElementLonger(entry.Command) is not null);
                    Assert.True(ladder[1].Length > ladder[0].Length);
                }
            }
        }
    }

    /// <summary>
    /// Where this build has both a layout and the receiver's own byte count for a command, the
    /// two must agree about how long the request is. A disagreement would mean one of them is
    /// wrong about the protocol, and this is where it becomes a failing test rather than a
    /// message the sweep sends in one shape while the machine frames it in another.
    /// </summary>
    [Fact]
    public void A_layouts_request_is_as_long_as_the_receiver_frames_it()
    {
        var mismatched = new List<string>();
        foreach (SweepEntry entry in SweepPlan.All)
        {
            if (CatalogueSweep.MinimalRequest(entry.Command) is not { } shaped)
                continue;
            if (SasCommandFraming.RequestDataBytes(entry.Command) is not { } framed)
                continue;

            if (shaped.Length != framed)
            {
                mismatched.Add(
                    $"${entry.Command:X2}: the layout builds {shaped.Length} request byte(s), "
                    + $"the receiver frames {framed}");
            }
        }

        Assert.Empty(mismatched);
    }

    [Fact]
    public void The_validation_buffer_is_read_without_being_advanced()
    {
        // Function code zero reads the current entry and moves the machine past it, which would
        // consume a record nobody has seen. $FF reads the same entry and leaves it alone.
        SweepEntry entry = SweepPlan.For(0x4D);

        Assert.Equal(SweepDisposition.Ask, entry.Disposition);
        Assert.Equal([0xFF], entry.Request);
    }
}
