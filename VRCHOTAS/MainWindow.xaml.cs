using System.Windows;
using Forms = System.Windows.Forms;
using Controls = System.Windows.Controls;
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

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            _notifyIcon = CreateNotifyIcon();
            _viewModel.LogWindowRequested += OnLogWindowRequested;
            _viewModel.MappingEditorRequested += OnMappingEditorRequested;
            _viewModel.SaveAsRequested += OnSaveAsRequested;
            Closing += OnClosing;
            Closed += OnClosed;
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

        private void OnSaveAsRequested(object? sender, System.EventArgs e)
        {
            RequestSaveAsConfiguration();
        }

        private void RequestSaveAsConfiguration()
        {
            var fileName = PromptForConfigurationFileName();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            _viewModel.SaveAsConfiguration(fileName);
        }

        private string? PromptForConfigurationFileName()
        {
            var dialog = new Window
            {
                Title = "Save Configuration As",
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
                Content = "Save",
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
            Closing -= OnClosing;
            _notifyIcon.MouseDoubleClick -= OnNotifyIconMouseDoubleClick;
            _notifyIcon.Dispose();
            _logWindow?.Close();
            _viewModel.Dispose();
        }
    }
}