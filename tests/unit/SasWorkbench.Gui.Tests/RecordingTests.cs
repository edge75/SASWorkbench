using SasWorkbench.Core.Recording;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Gui.ViewModels;

namespace SasWorkbench.Gui.Tests;

/// <summary>
/// A person drives the window, and what they did comes out as a script.
/// </summary>
/// <remarks>
/// <para>
/// Driven through the window's own commands rather than through the core directly, because the
/// claim being tested is about the window: a click connects, sends, opens a door and prints a
/// ticket through exactly these commands, and the recorder is fed from them. What a person's mouse
/// adds beyond this is the mouse.
/// </para>
/// <para>
/// The assertions are mostly about what the script does <b>not</b> say. A recorded script that
/// passes for the wrong reason is worse than no recorder, so the things that would make one — a
/// literal for a value the machine chose, a step for a stimulus nothing carried out, a wait as
/// long as somebody's pause — are what this file pins down.
/// </para>
/// </remarks>
public class RecordingTests
{
    private static WorkbenchViewModel Connected()
    {
        var model = new WorkbenchViewModel
        {
            SelectedTransport = "loopback",
            Address = "01",
            PollInterval = "50ms",
            RunStimulus = StimulusBackend.Software,
        };

        model.ConnectCommand.Execute(null);
        Assert.True(model.IsConnected, model.Status);
        return model;
    }

    private static void Do(WorkbenchViewModel model, string label)
    {
        model.Stimulus.Selected = model.Stimulus.Actions.Single(a => a.Label == label);
        model.ApplyStimulusCommand.Execute(null);
    }

    /// <summary>
    /// Sends the selected long poll and waits for it, the way a person's click does.
    /// </summary>
    /// <remarks>
    /// The window's command is asynchronous so the window stays responsive while the machine is
    /// answering. Firing it and not waiting stopped the recording before the reply arrived, so the
    /// script came out without the assertions the reply licenses.
    /// </remarks>
    private static async Task Send(WorkbenchViewModel model, byte command)
    {
        model.SelectedLongPoll = model.LongPolls.Single(l => l.Code == command);
        await model.SendLongPollCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task What_a_person_did_comes_out_as_a_script_that_does_it_again()
    {
        using WorkbenchViewModel model = Connected();

        model.RecordingName = "a door, a note and a ticket";
        model.StartRecordingCommand.Execute(null);
        Assert.True(model.IsRecording);

        Do(model, "Settle: shut every door");
        Do(model, "Open the slot door");
        Do(model, "Shut the slot door");
        Do(model, "Feed a $20 note");
        Do(model, "Press collect: print a ticket");

        await Send(model, 0x3D);

        model.StopRecordingCommand.Execute(null);
        Assert.False(model.IsRecording);

        string script = Assert.IsType<string>(model.RecordedScript);

        // The steps a person took, in the words a script uses.
        Assert.Contains("host connect loopback address 01", script);
        Assert.Contains("host poll every 50ms", script);
        Assert.Contains("egm settle", script);
        Assert.Contains("egm door slot open", script);
        Assert.Contains("egm door slot close", script);
        Assert.Contains("egm bill insert 2000", script);
        Assert.Contains("egm press collect", script);
        Assert.Contains("host send lp 3D", script);

        // The claims the machine's own answers licensed.
        Assert.Contains("host expect exception slot_door_opened within", script);
        Assert.Contains("egm expect ticket printed within", script);

        // And the ticket's number, compared against the ticket the printer filed rather than
        // against the number this machine happened to choose.
        Assert.Contains(
            "host expect field validation_number of lp 3D == ticket.validation_number", script);
        Assert.DoesNotContain("host expect field validation_number of lp 3D == 0", script);

        // What it needs, worked out from the steps rather than written by anybody.
        Assert.Contains("requires stimulus.bill", script);
        Assert.Contains("requires stimulus.door.slot", script);
        Assert.Contains("requires stimulus.button.collect", script);
        Assert.Contains("requires decoder.lp3D", script);
    }

    /// <summary>
    /// A stimulus the environment would not carry out is recorded as one that did not happen.
    /// </summary>
    /// <remarks>
    /// The worst thing a recorder could do. The person asked for a door on a machine with no such
    /// door; nothing happened; and a step for it would open a door the session never opened, with
    /// every assertion after it describing a machine that had not had one opened.
    /// </remarks>
    [Fact]
    public void A_stimulus_the_machine_refused_is_recorded_as_one_that_did_not_happen()
    {
        using WorkbenchViewModel model = Connected();

        model.StartRecordingCommand.Execute(null);

        // A game on a machine with nothing on it. The machine refuses, and the refusal is its own
        // words rather than this build's guess at them.
        Do(model, "Play one game at one credit");
        model.StopRecordingCommand.Execute(null);

        string script = Assert.IsType<string>(model.RecordedScript);

        Assert.DoesNotContain("egm play games=1", script);
        Assert.Contains("# not recorded:", script);
        Assert.Contains("Play one game at one credit", script);

        CaveatLine caveat = Assert.Single(
            model.Caveats, c => c.What.Contains("Play one game", StringComparison.Ordinal));
        Assert.Contains("a stimulus nobody wired", caveat.Kind);
        Assert.Contains("accounting unit", caveat.Why);
    }

    /// <summary>
    /// A value the machine chose is reported rather than asserted, and the value is shown.
    /// </summary>
    [Fact]
    public async Task A_value_the_machine_chose_is_reported_rather_than_asserted()
    {
        using WorkbenchViewModel model = Connected();

        model.StartRecordingCommand.Execute(null);
        Do(model, "Feed a $20 note");

        await Send(model, 0x20);
        model.StopRecordingCommand.Execute(null);

        string script = Assert.IsType<string>(model.RecordedScript);

        Assert.Contains("host send lp 20", script);
        Assert.DoesNotContain("expect field total_bill_value", script);

        CaveatLine caveat = Assert.Single(
            model.Caveats, c => c.What.StartsWith("total_bill_value", StringComparison.Ordinal));
        Assert.Contains("a value the machine chose", caveat.Kind);
        Assert.Contains("= 20", caveat.What);
    }

    /// <summary>
    /// Reading every meter is recorded as something that happened and asserted on not at all.
    /// </summary>
    [Fact]
    public async Task Reading_the_meters_produces_no_assertion()
    {
        using WorkbenchViewModel model = Connected();

        model.StartRecordingCommand.Execute(null);
        await model.RefreshMetersCommand.ExecuteAsync(null);
        model.StopRecordingCommand.Execute(null);

        string script = Assert.IsType<string>(model.RecordedScript);

        Assert.Contains("read every meter this build can read by name", script);
        Assert.DoesNotContain("expect meter", script);
        Assert.Contains(
            model.Caveats,
            c => c.What.Contains("read every meter", StringComparison.Ordinal));
    }

    [Fact]
    public void A_recording_in_which_nothing_was_done_produces_no_script()
    {
        using WorkbenchViewModel model = Connected();

        model.StartRecordingCommand.Execute(null);
        model.StopRecordingCommand.Execute(null);

        Assert.Null(model.RecordedScript);
        Assert.Contains("no script", model.Status);
    }

    [Fact]
    public void Nothing_a_person_does_before_pressing_record_is_recorded()
    {
        using WorkbenchViewModel model = Connected();

        Do(model, "Open the slot door");

        model.StartRecordingCommand.Execute(null);
        Do(model, "Shut the slot door");
        model.StopRecordingCommand.Execute(null);

        string script = Assert.IsType<string>(model.RecordedScript);

        Assert.Contains("egm door slot close", script);
        Assert.DoesNotContain("egm door slot open", script);
    }

    /// <summary>Every window a recording writes is this tool's number, and the script says so.</summary>
    [Fact]
    public void The_windows_a_recording_writes_are_the_tools_numbers_and_it_says_so()
    {
        using WorkbenchViewModel model = Connected();

        model.StartRecordingCommand.Execute(null);
        Do(model, "Open the slot door");
        model.StopRecordingCommand.Execute(null);

        string script = Assert.IsType<string>(model.RecordedScript);

        Assert.Contains("this tool's number and not the person's", script);
        Assert.Contains($"floored at {ScriptEmitter.ShortestWindow.TotalSeconds:F0}s", script);
        Assert.DoesNotContain("host wait", script);
    }
}
