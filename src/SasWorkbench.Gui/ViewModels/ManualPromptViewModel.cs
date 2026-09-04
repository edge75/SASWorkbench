using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Gui.ViewModels;

/// <summary>
/// Asks the person at the window to do something to the machine, and gives up when nobody answers.
/// </summary>
/// <remarks>
/// <para>
/// The runner is synchronous and must block while a person acts, so the card is raised on the
/// interface thread and the runner's thread waits on it. When nobody answers, the step is skipped
/// with a reason rather than the run hanging for ever with no verdict — which is the whole point
/// of the deadline, and the reason it cannot be omitted.
/// </para>
/// <para>
/// The card is a band, not a modal: the poll loop must stay visible and the stream must keep
/// scrolling while somebody walks over to the machine.
/// </para>
/// </remarks>
public sealed partial class ManualPromptViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _countdown;
    private TaskCompletionSource<PromptAnswer>? _reply;
    private DateTimeOffset _until;

    public ManualPromptViewModel()
    {
        _countdown = new DispatcherTimer(
            TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Tick());
        _countdown.Start();
    }

    /// <summary>Whether a person is being asked to do something right now.</summary>
    [ObservableProperty]
    public partial bool IsAsking { get; set; }

    /// <summary>What they are being asked to do.</summary>
    [ObservableProperty]
    public partial string? Request { get; set; }

    /// <summary>How long they have left.</summary>
    [ObservableProperty]
    public partial string? Remaining { get; set; }

    /// <summary>How long a person is given before the step is skipped.</summary>
    [ObservableProperty]
    public partial string Deadline { get; set; } = "2m";

    /// <summary>
    /// Asks, from the runner's thread, and blocks until answered or until the deadline passes.
    /// </summary>
    public PromptAnswer Ask(string request, TimeSpan deadline)
    {
        // Whichever of the person and the deadline gets here first settles it, once. The two do
        // race — a click can land in the same millisecond the deadline passes — and the answer has
        // to be single-valued, or the run and the screen would disagree about what happened.
        var settled = new TaskCompletionSource<PromptAnswer>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        DateTimeOffset until = DateTimeOffset.UtcNow + deadline;

        Dispatcher.UIThread.Post(() =>
        {
            Request = request;
            IsAsking = true;
            _until = until;
            _reply = settled;
        });

        if (!settled.Task.Wait(deadline))
            settled.TrySetResult(PromptAnswer.NoAnswer);

        PromptAnswer answer = settled.Task.Result;

        Dispatcher.UIThread.Post(() =>
        {
            IsAsking = false;
            _reply = null;
            Remaining = answer == PromptAnswer.NoAnswer ? "nobody answered; the step was skipped" : null;
        });

        return answer;
    }

    /// <summary>Says the thing was done, and lets the run continue.</summary>
    [RelayCommand]
    private void Done() => Answer(PromptAnswer.Done);

    /// <summary>Says it could not be done, which skips the step rather than failing it.</summary>
    [RelayCommand]
    private void Cannot() => Answer(PromptAnswer.Declined);

    public void Dispose() => _countdown.Stop();

    private void Answer(PromptAnswer answer)
    {
        TaskCompletionSource<PromptAnswer>? reply = _reply;
        _reply = null;

        // A click that lost the race to the deadline changes nothing, and says so rather than
        // looking as though it counted: the step has already been skipped.
        if (reply?.TrySetResult(answer) == false)
        {
            Remaining = "too late; the step was already skipped";
            return;
        }

        IsAsking = false;
    }

    private void Tick()
    {
        if (!IsAsking)
            return;

        TimeSpan left = _until - DateTimeOffset.UtcNow;
        Remaining = left > TimeSpan.Zero ? $"{left.TotalSeconds:F0}s left" : "giving up";
    }
}
