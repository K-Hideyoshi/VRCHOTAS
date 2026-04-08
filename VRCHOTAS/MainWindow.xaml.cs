using System.Windows;
using System.Windows.Controls;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;
using VRCHOTAS.ViewModels;

namespace VRCHOTAS
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private LogWindow? _logWindow;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            _viewModel.LogWindowRequested += OnLogWindowRequested;
            _viewModel.MappingEditorRequested += OnMappingEditorRequested;
            _viewModel.SaveAsRequested += OnSaveAsRequested;
            Closed += OnClosed;
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

            var textBox = new TextBox
            {
                Margin = new Thickness(0, 8, 0, 12),
                MinWidth = 300
            };

            var okButton = new Button
            {
                Content = "Save",
                Width = 80,
                IsDefault = true,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 80,
                IsCancel = true
            };

            okButton.Click += (_, _) => dialog.DialogResult = true;

            var content = new StackPanel
            {
                Margin = new Thickness(12)
            };

            content.Children.Add(new TextBlock
            {
                Text = "Configuration file name:"
            });
            content.Children.Add(textBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
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

            _viewModel.SaveMappingFromDialog(dialog.MappingResult, e.MappingToEdit);
        }

        private void DeleteMappingClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not MappingEntry mapping)
            {
                return;
            }

            _viewModel.SelectedMapping = mapping;
            _viewModel.DeleteSelectedMappingCommand.Execute(null);
        }

        private void EditMappingClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not MappingEntry mapping)
            {
                return;
            }

            _viewModel.SelectedMapping = mapping;
            _viewModel.OpenEditMappingDialogCommand.Execute(null);
        }

        private void OnClosed(object? sender, System.EventArgs e)
        {
            _viewModel.LogWindowRequested -= OnLogWindowRequested;
            _viewModel.MappingEditorRequested -= OnMappingEditorRequested;
            _viewModel.SaveAsRequested -= OnSaveAsRequested;
            _logWindow?.Close();
            _viewModel.Dispose();
        }
    }
}