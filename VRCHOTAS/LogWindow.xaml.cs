using System.Windows;
using System.Collections.Specialized;
using System.Windows.Threading;
using VRCHOTAS.ViewModels;

namespace VRCHOTAS;

public partial class LogWindow : Window
{
    private MainViewModel? _viewModel;

    public LogWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.LogEntries.CollectionChanged -= OnLogEntriesCollectionChanged;
        }

        _viewModel = e.NewValue as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.LogEntries.CollectionChanged += OnLogEntriesCollectionChanged;
        }
    }

    private void OnLogEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null || e.NewItems.Count == 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var lastItem = LogEntriesGrid.Items.Count > 0 ? LogEntriesGrid.Items[LogEntriesGrid.Items.Count - 1] : null;
            if (lastItem is not null)
            {
                LogEntriesGrid.ScrollIntoView(lastItem);
            }
        }, DispatcherPriority.Background);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.LogEntries.CollectionChanged -= OnLogEntriesCollectionChanged;
        }

        DataContextChanged -= OnDataContextChanged;
        Closed -= OnClosed;
    }
}
