using System.Windows;
using System.Windows.Media;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Specialized;
using System.ComponentModel;
using Forms = System.Windows.Forms;
using Controls = System.Windows.Controls;
using WpfExecutedRoutedEventArgs = System.Windows.Input.ExecutedRoutedEventArgs;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfMouseButtonState = System.Windows.Input.MouseButtonState;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfPoint = System.Windows.Point;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;
using VRCHOTAS.ViewModels;

namespace VRCHOTAS
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly Forms.NotifyIcon _notifyIcon;
        private LogWindow? _logWindow;
        private bool _isExitRequested;
        private bool _hasShownTrayHint;
        private WpfPoint _mappingDragStartPoint;
        private MappingEntry? _mappingDragCandidate;
        private int? _mappingDropTargetIndex;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            _notifyIcon = CreateNotifyIcon();
            _viewModel.LogWindowRequested += OnLogWindowRequested;
            _viewModel.MappingEditorRequested += OnMappingEditorRequested;
            _viewModel.SaveAsRequested += OnSaveAsRequested;
            _viewModel.Mappings.CollectionChanged += OnMappingsCollectionChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Closing += OnClosing;
            Closed += OnClosed;
        }

        private void OnMappingsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            AutoSizeMappingColumns();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedMapping))
            {
                ScrollSelectedMappingIntoView();
            }
        }

        private void MappingsGridLoaded(object sender, RoutedEventArgs e)
        {
            AutoSizeMappingColumns();
        }

        private void AutoSizeMappingColumns()
        {
            if (!IsLoaded)
            {
                return;
            }

            AutoSizeMappingColumn(SourceDeviceColumn, _viewModel.Mappings.Select(mapping => mapping.SourceDeviceName), 140, 360);
            AutoSizeMappingColumn(SourceControlColumn, _viewModel.Mappings.Select(mapping => mapping.SourceControlDisplay), 140, 220);
            AutoSizeMappingColumn(TargetHandColumn, _viewModel.Mappings.Select(mapping => mapping.TargetHand.ToString()), 90, 160);
            AutoSizeMappingColumn(TargetControlColumn, _viewModel.Mappings.Select(mapping => mapping.TargetControlDisplay), 170, 360);
        }

        private void AutoSizeMappingColumn(Controls.DataGridColumn? column, IEnumerable<string> values, double minWidth, double maxWidth)
        {
            if (column is null)
            {
                return;
            }

            var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
            var maxText = new[] { column.Header?.ToString() ?? string.Empty }
                .Concat(values.Where(value => !string.IsNullOrWhiteSpace(value)));

            double widest = minWidth;
            foreach (var text in maxText)
            {
                var formatted = new FormattedText(
                    text,
                    CultureInfo.CurrentUICulture,
                    System.Windows.FlowDirection.LeftToRight,
                    typeface,
                    12,
                    System.Windows.Media.Brushes.Black,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                widest = Math.Max(widest, formatted.Width + 28);
            }

            column.Width = new Controls.DataGridLength(Math.Clamp(widest, minWidth, maxWidth));
        }

        private void ScrollSelectedMappingIntoView()
        {
            var selected = _viewModel.SelectedMapping;
            if (selected is null || !IsLoaded)
            {
                return;
            }

            Dispatcher.BeginInvoke(() =>
            {
                if (_viewModel.SelectedMapping is null)
                {
                    return;
                }

                MappingsGrid.UpdateLayout();
                MappingsGrid.ScrollIntoView(selected);
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private Forms.NotifyIcon CreateNotifyIcon()
        {
            var contextMenu = new Forms.ContextMenuStrip();
            contextMenu.Items.Add("Open", null, (_, _) => ShowFromTray());
            contextMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

            var notifyIcon = new Forms.NotifyIcon
            {
                Text = "VRCHOTAS Mapper",
                Icon = System.Drawing.SystemIcons.Application,
                Visible = true,
                ContextMenuStrip = contextMenu
            };

            notifyIcon.MouseDoubleClick += OnNotifyIconMouseDoubleClick;
            return notifyIcon;
        }

        private void SaveAsConfigurationClick(object sender, RoutedEventArgs e)
        {
            RequestSaveAsConfiguration();
        }

        private void NewConfigurationClick(object sender, RoutedEventArgs e)
        {
            RequestNewConfiguration();
        }

        private void NewConfigurationCommandExecuted(object sender, WpfExecutedRoutedEventArgs e)
        {
            RequestNewConfiguration();
        }

        private void RevealConfigFolderClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderPath = _viewModel.GetConfigurationDirectoryPathForUi();
                Process.Start(new ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogManager.Logger.Warning(nameof(MainWindow), $"Failed to open config folder. {ex.Message}");
                System.Windows.MessageBox.Show(this, ex.Message, "Reveal Config Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnSaveAsRequested(object? sender, System.EventArgs e)
        {
            RequestSaveAsConfiguration();
        }

        private void RequestSaveAsConfiguration()
        {
            var fileName = PromptForConfigurationFileName("Save Configuration As", "Save");
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            _viewModel.SaveAsConfiguration(fileName);
        }

        private void RequestNewConfiguration()
        {
            if (!ConfirmNewConfigurationSwitch())
            {
                return;
            }

            while (true)
            {
                var fileName = PromptForConfigurationFileName("Create New Configuration", "Create");
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    return;
                }

                if (_viewModel.TryCreateNewConfiguration(fileName, out var errorMessage))
                {
                    return;
                }

                System.Windows.MessageBox.Show(this, errorMessage, "New Configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private bool ConfirmNewConfigurationSwitch()
        {
            if (!_viewModel.IsConfigurationDirty)
            {
                return true;
            }

            var result = System.Windows.MessageBox.Show(
                this,
                "The current configuration has unsaved changes. Save before creating a new configuration?\n\nYes = Save\nNo = Discard changes\nCancel = Keep editing current configuration",
                "Unsaved Configuration Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Cancel)
            {
                return false;
            }

            if (result != MessageBoxResult.Yes)
            {
                return true;
            }

            _viewModel.SaveCurrentConfigurationForUi();
            return !_viewModel.IsConfigurationDirty;
        }

        private string? PromptForConfigurationFileName(string title, string confirmButtonText)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 380,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = this
            };

            var textBox = new Controls.TextBox
            {
                Margin = new Thickness(0, 8, 0, 12),
                MinWidth = 300
            };

            var okButton = new Controls.Button
            {
                Content = confirmButtonText,
                Width = 80,
                IsDefault = true,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var cancelButton = new Controls.Button
            {
                Content = "Cancel",
                Width = 80,
                IsCancel = true
            };

            okButton.Click += (_, _) => dialog.DialogResult = true;

            var content = new Controls.StackPanel
            {
                Margin = new Thickness(12)
            };

            content.Children.Add(new Controls.TextBlock
            {
                Text = "Configuration file name:"
            });
            content.Children.Add(textBox);

            var buttons = new Controls.StackPanel
            {
                Orientation = Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);
            content.Children.Add(buttons);

            dialog.Content = content;

            var result = dialog.ShowDialog();
            return result == true ? textBox.Text.Trim() : null;
        }

        private void OnLogWindowRequested(object? sender, System.EventArgs e)
        {
            if (_logWindow is not null)
            {
                _logWindow.Activate();
                return;
            }

            _logWindow = new LogWindow
            {
                Owner = this,
                DataContext = _viewModel
            };

            _logWindow.Closed += (_, _) => _logWindow = null;
            _logWindow.Show();
        }

        private void OnMappingEditorRequested(object? sender, MappingEditorRequestEventArgs e)
        {
            var dialog = new MappingEditorWindow(LogManager.Logger, _viewModel.GetLatestStateSnapshot, e.MappingToEdit)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true || dialog.MappingResult is null)
            {
                return;
            }

            try
            {
                _viewModel.SaveMappingFromDialog(dialog.MappingResult, e.MappingToEdit);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DeleteMappingClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Controls.Button button || button.DataContext is not MappingEntry mapping)
            {
                return;
            }

            _viewModel.SelectedMapping = mapping;
            _viewModel.DeleteSelectedMappingCommand.Execute(null);
        }

        private void EditMappingClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Controls.Button button || button.DataContext is not MappingEntry mapping)
            {
                return;
            }

            _viewModel.SelectedMapping = mapping;
            _viewModel.OpenEditMappingDialogCommand.Execute(null);
        }

        private void CopyMappingClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Controls.Button button || button.DataContext is not MappingEntry mapping)
            {
                return;
            }

            _viewModel.DuplicateMapping(mapping);
        }

        private void MappingDragHandleMouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not MappingEntry mapping)
            {
                return;
            }

            _mappingDragCandidate = mapping;
            _mappingDragStartPoint = e.GetPosition(this);
            _viewModel.SelectedMapping = mapping;
        }

        private void MappingDragHandleMouseMove(object sender, WpfMouseEventArgs e)
        {
            if (e.LeftButton != WpfMouseButtonState.Pressed || _mappingDragCandidate is null)
            {
                return;
            }

            var currentPosition = e.GetPosition(this);
            if (Math.Abs(currentPosition.X - _mappingDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(currentPosition.Y - _mappingDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            DragDrop.DoDragDrop((DependencyObject)sender, _mappingDragCandidate, WpfDragDropEffects.Move);
            _mappingDragCandidate = null;
        }

        private void MappingsGridDragOver(object sender, WpfDragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(MappingEntry)))
            {
                HideMappingDropIndicator();
                e.Effects = WpfDragDropEffects.None;
                e.Handled = true;
                return;
            }

            var dragged = (MappingEntry?)e.Data.GetData(typeof(MappingEntry));
            if (dragged is null)
            {
                HideMappingDropIndicator();
                e.Effects = WpfDragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = WpfDragDropEffects.Move;
            UpdateMappingDropIndicator(e.GetPosition(MappingsGrid), dragged);
            e.Handled = true;
        }

        private void MappingsGridDragLeave(object sender, WpfDragEventArgs e)
        {
            if (sender is not Controls.DataGrid dataGrid)
            {
                HideMappingDropIndicator();
                return;
            }

            var position = e.GetPosition(dataGrid);
            if (position.X < 0 || position.Y < 0 || position.X > dataGrid.ActualWidth || position.Y > dataGrid.ActualHeight)
            {
                HideMappingDropIndicator();
            }
        }

        private void MappingsGridDrop(object sender, WpfDragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(MappingEntry)))
            {
                HideMappingDropIndicator();
                return;
            }

            var dragged = (MappingEntry?)e.Data.GetData(typeof(MappingEntry));
            if (dragged is null)
            {
                HideMappingDropIndicator();
                return;
            }

            var targetIndex = _mappingDropTargetIndex;
            if (!targetIndex.HasValue && TryResolveMappingDropTarget(e.GetPosition(MappingsGrid), dragged, out var resolvedIndex, out _))
            {
                targetIndex = resolvedIndex;
            }

            if (targetIndex.HasValue)
            {
                _viewModel.MoveMappingToIndex(dragged, targetIndex.Value);
            }

            HideMappingDropIndicator();
        }

        private void UpdateMappingDropIndicator(WpfPoint position, MappingEntry dragged)
        {
            if (!TryResolveMappingDropTarget(position, dragged, out var targetIndex, out var indicatorY))
            {
                HideMappingDropIndicator();
                return;
            }

            var dropIndicator = GetMappingDropIndicator();
            if (dropIndicator is null)
            {
                return;
            }

            _mappingDropTargetIndex = targetIndex;
            dropIndicator.Margin = new Thickness(0, Math.Max(0, indicatorY - (dropIndicator.Height / 2.0)), 0, 0);
            dropIndicator.Visibility = Visibility.Visible;
        }

        private bool TryResolveMappingDropTarget(WpfPoint position, MappingEntry dragged, out int targetIndex, out double indicatorY)
        {
            targetIndex = 0;
            indicatorY = 0;

            var row = FindAncestor<Controls.DataGridRow>(MappingsGrid.InputHitTest(position) as DependencyObject);

            if (row?.Item is not MappingEntry target)
            {
                var itemCount = _viewModel.Mappings.Count;
                if (itemCount == 0)
                {
                    return true;
                }

                var firstRow = FindDataGridRowByIndex(0);
                var lastRow = FindDataGridRowByIndex(itemCount - 1);
                if (firstRow is null || lastRow is null)
                {
                    return false;
                }

                var firstRowTop = firstRow.TransformToAncestor(MappingsGrid).Transform(new WpfPoint(0, 0)).Y;
                var lastRowTop = lastRow.TransformToAncestor(MappingsGrid).Transform(new WpfPoint(0, 0)).Y;
                if (position.Y <= firstRowTop + firstRow.ActualHeight / 2.0)
                {
                    targetIndex = 0;
                    indicatorY = firstRowTop;
                    return true;
                }

                targetIndex = itemCount;
                indicatorY = lastRowTop + lastRow.ActualHeight;
                var draggedIndexAtBottom = _viewModel.Mappings.IndexOf(dragged);
                if (draggedIndexAtBottom >= 0 && targetIndex > draggedIndexAtBottom)
                {
                    targetIndex--;
                }

                targetIndex = Math.Clamp(targetIndex, 0, Math.Max(0, itemCount - 1));
                return true;
            }

            var insertionIndex = _viewModel.Mappings.IndexOf(target);
            if (insertionIndex < 0)
            {
                return false;
            }

            var rowTop = row.TransformToAncestor(MappingsGrid).Transform(new WpfPoint(0, 0)).Y;
            var insertAfter = position.Y >= rowTop + row.ActualHeight / 2.0;
            if (insertAfter)
            {
                insertionIndex++;
                indicatorY = rowTop + row.ActualHeight;
            }
            else
            {
                indicatorY = rowTop;
            }

            var draggedIndex = _viewModel.Mappings.IndexOf(dragged);
            targetIndex = insertionIndex;
            if (draggedIndex >= 0 && insertionIndex > draggedIndex)
            {
                targetIndex--;
            }

            targetIndex = Math.Clamp(targetIndex, 0, Math.Max(0, _viewModel.Mappings.Count - 1));
            return true;
        }

        private Controls.DataGridRow? FindDataGridRowByIndex(int index)
        {
            if (MappingsGrid.ItemContainerGenerator.ContainerFromIndex(index) is Controls.DataGridRow row)
            {
                return row;
            }

            MappingsGrid.UpdateLayout();
            MappingsGrid.ScrollIntoView(MappingsGrid.Items[index]);
            return MappingsGrid.ItemContainerGenerator.ContainerFromIndex(index) as Controls.DataGridRow;
        }

        private void HideMappingDropIndicator()
        {
            _mappingDropTargetIndex = null;
            var dropIndicator = GetMappingDropIndicator();
            if (dropIndicator is not null)
            {
                dropIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private Controls.Border? GetMappingDropIndicator()
        {
            return FindName("MappingDropIndicator") as Controls.Border;
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current is not null)
            {
                if (current is T result)
                {
                    return result;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void OnHotkeysMenuClick(object sender, RoutedEventArgs e)
        {
            var dialog = new HotkeysWindow(_viewModel, _viewModel.Preferences)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }

        private void ExitApplicationClick(object sender, RoutedEventArgs e)
        {
            ExitApplication();
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isExitRequested)
            {
                return;
            }

            e.Cancel = true;
            HideToTray();
        }

        private void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
            WindowState = WindowState.Normal;

            if (_hasShownTrayHint)
            {
                return;
            }

            _notifyIcon.ShowBalloonTip(2000, "VRCHOTAS Mapper", "The application is still running in the system tray.", Forms.ToolTipIcon.Info);
            _hasShownTrayHint = true;
        }

        private void ShowFromTray()
        {
            Show();
            ShowInTaskbar = true;
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
        }

        private void OnNotifyIconMouseDoubleClick(object? sender, Forms.MouseEventArgs e)
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                ShowFromTray();
            }
        }

        private void ExitApplication()
        {
            _isExitRequested = true;
            _notifyIcon.Visible = false;
            Close();
        }

        private void OnClosed(object? sender, System.EventArgs e)
        {
            _viewModel.LogWindowRequested -= OnLogWindowRequested;
            _viewModel.MappingEditorRequested -= OnMappingEditorRequested;
            _viewModel.SaveAsRequested -= OnSaveAsRequested;
            _viewModel.Mappings.CollectionChanged -= OnMappingsCollectionChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            Closing -= OnClosing;
            _notifyIcon.MouseDoubleClick -= OnNotifyIconMouseDoubleClick;
            _notifyIcon.Dispose();
            _logWindow?.Close();
            _viewModel.Dispose();
        }
    }
}