using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using SasWorkbench.Gui.ViewModels;

namespace SasWorkbench.Gui.Views;

/// <summary>The spine: one time-ordered view of the session, filtered by lane.</summary>
public partial class SessionStream : UserControl
{
    private SessionStreamViewModel? _watched;
    private bool _scrollQueued;

    public SessionStream() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_watched is not null)
        {
            _watched.Rows.CollectionChanged -= OnRowsChanged;
            _watched.PropertyChanged -= OnStreamChanged;
        }

        _watched = DataContext as SessionStreamViewModel;
        if (_watched is null)
            return;

        _watched.Rows.CollectionChanged += OnRowsChanged;
        _watched.PropertyChanged += OnStreamChanged;
        FollowNewest();
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset)
            FollowNewest();
    }

    private void OnStreamChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionStreamViewModel.Follow))
            FollowNewest();
    }

    /// <summary>Scrolls to the newest row once per batch, since a flush adds its rows one at a time.</summary>
    private void FollowNewest()
    {
        if (_scrollQueued || _watched is not { Follow: true })
            return;

        _scrollQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollQueued = false;
            if (_watched is { Follow: true, Rows.Count: > 0 } stream)
                Events.ScrollIntoView(stream.Rows.Count - 1);
        }, DispatcherPriority.Background);
    }
}
