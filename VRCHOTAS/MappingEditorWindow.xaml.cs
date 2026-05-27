using System.Windows;
using System.Windows.Threading;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;
using VRCHOTAS.ViewModels;
using Controls = System.Windows.Controls;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKeyboardFocusChangedEventArgs = System.Windows.Input.KeyboardFocusChangedEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace VRCHOTAS;

public partial class MappingEditorWindow : Window
{
    private readonly IAppLogger _logger;
    private readonly MappingEditorViewModel _viewModel;
    private readonly DispatcherTimer _detectTimer;
    private readonly Dictionary<Controls.TextBox, string> _numericEditorOriginalTexts = new();

    public MappingEditorWindow(IAppLogger logger, Func<RawJoystickState> stateProvider, MappingEntry? existing)
    {
        InitializeComponent();
        _logger = logger;
        _viewModel = new MappingEditorViewModel(stateProvider, existing);
        DataContext = _viewModel;
        if (existing is null)
        {
            _logger.Info(nameof(MappingEditorWindow), "Source detection started automatically.");
        }
        else
        {
            _logger.Info(nameof(MappingEditorWindow), "Editing existing mapping without auto-start source detection.");
        }

        _detectTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _detectTimer.Tick += (_, _) => DetectTick();
        _detectTimer.Start();
        Closed += (_, _) => _detectTimer.Stop();
    }

    public MappingEntry? MappingResult { get; private set; }

    private void DetectTick()
    {
        _viewModel.UpdateLivePreview();

        if (!_viewModel.IsListening)
        {
            return;
        }

        if (_viewModel.TryAutoDetectSource())
        {
            _viewModel.IsListening = false;
            _logger.Info(nameof(MappingEditorWindow), "Source detection completed.");
        }
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            MappingResult = _viewModel.BuildResult();
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            _logger.Warning(nameof(MappingEditorWindow), "Mapping save validation failed.");
            System.Windows.MessageBox.Show(this, ex.Message, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearDetectionClick(object sender, RoutedEventArgs e)
    {
        _viewModel.StartAutoDetect(clearDetectedSource: true);
        _logger.Info(nameof(MappingEditorWindow), "Source detection cleared and restarted.");
    }

    private void NumericValueEditorGotKeyboardFocus(object sender, WpfKeyboardFocusChangedEventArgs e)
    {
        if (sender is not Controls.TextBox textBox)
        {
            return;
        }

        _numericEditorOriginalTexts[textBox] = textBox.Text;
        textBox.SelectAll();
    }

    private void NumericValueEditorLostFocus(object sender, WpfKeyboardFocusChangedEventArgs e)
    {
        CommitNumericValueEditor(sender as Controls.TextBox);
    }

    private void NumericValueEditorPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (sender is not Controls.TextBox textBox)
        {
            return;
        }

        if (e.Key == WpfKey.Enter)
        {
            CommitNumericValueEditor(textBox);
            e.Handled = true;
            return;
        }

        if (e.Key != WpfKey.Escape)
        {
            return;
        }

        RevertNumericValueEditor(textBox);
        e.Handled = true;
    }

    private void RootGridMouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<Controls.TextBox>(source) is not null)
        {
            return;
        }

        CommitFocusedNumericValueEditor();
        RootGrid.Focus();
        System.Windows.Input.Keyboard.ClearFocus();
    }

    private void CommitNumericValueEditor(Controls.TextBox? textBox)
    {
        if (textBox is null || textBox.Tag is not string fieldName)
        {
            return;
        }

        if (_viewModel.TrySetNumericEditorValue(fieldName, textBox.Text))
        {
            textBox.SetCurrentValue(Controls.TextBox.TextProperty, _viewModel.GetNumericEditorText(fieldName));
            _numericEditorOriginalTexts[textBox] = textBox.Text;
            return;
        }

        RevertNumericValueEditor(textBox);
    }

    private void RevertNumericValueEditor(Controls.TextBox textBox)
    {
        if (!_numericEditorOriginalTexts.TryGetValue(textBox, out var originalText))
        {
            originalText = textBox.Tag is string fieldName
                ? _viewModel.GetNumericEditorText(fieldName)
                : string.Empty;
        }

        textBox.SetCurrentValue(Controls.TextBox.TextProperty, originalText);
        textBox.SelectAll();
    }

    private void CommitFocusedNumericValueEditor()
    {
        if (System.Windows.Input.FocusManager.GetFocusedElement(this) is not Controls.TextBox textBox)
        {
            return;
        }

        CommitNumericValueEditor(textBox);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T result)
            {
                return result;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
