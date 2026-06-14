using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;
using VRCHOTAS.Services;
using VRCHOTAS.ViewModels;
using Controls = System.Windows.Controls;
using WpfGridUnitType = System.Windows.GridUnitType;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKeyInterop = System.Windows.Input.KeyInterop;
using WpfKeyboardFocusChangedEventArgs = System.Windows.Input.KeyboardFocusChangedEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace VRCHOTAS;

public partial class MappingEditorWindow : Window
{
    private readonly IAppLogger _logger;
    private readonly MappingEditorViewModel _viewModel;
    private readonly DispatcherTimer _detectTimer;
    private readonly Dictionary<Controls.TextBox, string> _numericEditorOriginalTexts = new();
    private readonly IDisposable _hotkeySuspendScope;

    public MappingEditorWindow(IAppLogger logger, Func<RawJoystickState> stateProvider, MappingEntry? existing)
    {
        InitializeComponent();
        _logger = logger;

        // Suppress hotkeys and locate-mapping while the editor is open so
        // joystick input intended for the mapping editor doesn't trigger
        // global shortcuts or auto-selection in the main window.
        _hotkeySuspendScope = HotkeyRuntime.AcquireSuspendScope();

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

    private void KeyboardCaptureClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsKeyboardCaptureActive)
        {
            _viewModel.IsKeyboardCaptureActive = false;
            return;
        }

        _viewModel.IsKeyboardCaptureActive = true;
        // Use PreviewKeyDown (tunneling) so we intercept keys BEFORE any control handles them
        // (e.g. Enter won't trigger a button click or dialog close).
        PreviewKeyDown += OnKeyboardCapturePreviewKeyDown;
        _logger.Info(nameof(MappingEditorWindow), "Keyboard capture started. Press a key (with optional modifiers)...");
    }

    private void OnKeyboardCapturePreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (!_viewModel.IsKeyboardCaptureActive)
        {
            return;
        }

        // Ignore modifier-only key presses (they auto-repeat when held)
        if (e.Key is WpfKey.LeftCtrl or WpfKey.RightCtrl
            or WpfKey.LeftShift or WpfKey.RightShift
            or WpfKey.LeftAlt or WpfKey.RightAlt
            or WpfKey.LWin or WpfKey.RWin
            or WpfKey.System)
        {
            return;
        }

        var keyCode = WpfKeyInterop.VirtualKeyFromKey(e.Key);

        // Auto-detect modifiers that are currently held down
        var mods = System.Windows.Input.Keyboard.Modifiers;
        var modifiers = 0;
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Control)) modifiers |= 1;
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Shift)) modifiers |= 2;
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Alt)) modifiers |= 4;

        _viewModel.KeyboardKey = keyCode;
        _viewModel.KeyboardModifiers = modifiers;
        _viewModel.IsKeyboardCaptureActive = false;
        PreviewKeyDown -= OnKeyboardCapturePreviewKeyDown;

        // Mark handled so nothing else processes this key (e.g. Enter won't trigger a button)
        e.Handled = true;

        _logger.Info(nameof(MappingEditorWindow), $"Keyboard key captured: {_viewModel.KeyboardKeyDisplay}");
    }

    private void SelectTargetWindowClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var processName = ShowWindowPickerDialog();
            if (processName is null)
            {
                return;
            }

            _viewModel.KeyboardTargetProcessName = processName;
            _viewModel.KeyboardTargetWindowTitle = string.Empty;

            _logger.Info(nameof(MappingEditorWindow), $"Target process selected: '{processName}'");
        }
        catch (Exception ex)
        {
            _logger.Warning(nameof(MappingEditorWindow), $"Failed to show window picker: {ex.Message}");
        }
    }

    private void ClearTargetWindowClick(object sender, RoutedEventArgs e)
    {
        _viewModel.KeyboardTargetProcessName = string.Empty;
        _viewModel.KeyboardTargetWindowTitle = string.Empty;
        _logger.Info(nameof(MappingEditorWindow), "Target window cleared.");
    }

    private string? ShowWindowPickerDialog()
    {
        var inputDialog = new Window
        {
            Title = "Select Target Process",
            Width = 450,
            Height = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Owner = this
        };

        var grid = new System.Windows.Controls.Grid { Margin = new Thickness(10) };
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

        var instruction = new Controls.TextBlock
        {
            Text = "Filter by process name, then select and click OK:",
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap
        };
        System.Windows.Controls.Grid.SetRow(instruction, 0);
        grid.Children.Add(instruction);

        var searchBox = new Controls.TextBox { Margin = new Thickness(0, 0, 0, 8), Height = 24 };
        System.Windows.Controls.Grid.SetRow(searchBox, 1);
        grid.Children.Add(searchBox);

        var processList = new Controls.ListBox { Margin = new Thickness(0, 0, 0, 8), DisplayMemberPath = "Display" };
        System.Windows.Controls.Grid.SetRow(processList, 2);
        grid.Children.Add(processList);

        var buttonPanel = new Controls.StackPanel
        {
            Orientation = Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        System.Windows.Controls.Grid.SetRow(buttonPanel, 3);
        grid.Children.Add(buttonPanel);

        var okButton = new Controls.Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        var cancelButton = new Controls.Button { Content = "Cancel", Width = 80, IsCancel = true };
        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        string? selectedProcess = null;

        // Build a list of unique process names with their window titles
        var processMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        EnumerateVisibleWindows().ForEach(item =>
        {
            if (!processMap.TryGetValue(item.ProcessName, out var titles))
            {
                titles = new List<string>();
                processMap[item.ProcessName] = titles;
            }

            if (!string.IsNullOrWhiteSpace(item.Title) && !titles.Contains(item.Title))
            {
                titles.Add(item.Title);
            }
        });

        var entries = processMap
            .Select(kv => new ProcessEntry(kv.Key, kv.Value))
            .OrderBy(e => e.ProcessName)
            .ToList();

        searchBox.TextChanged += (_, _) =>
        {
            var filter = searchBox.Text.Trim();
            processList.Items.Clear();
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(filter) || entry.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    processList.Items.Add(entry);
                }
            }

            if (processList.Items.Count > 0 && processList.SelectedIndex < 0)
            {
                processList.SelectedIndex = 0;
                okButton.IsEnabled = true;
            }
            else if (processList.Items.Count == 0)
            {
                okButton.IsEnabled = false;
            }
        };

        processList.SelectionChanged += (_, _) =>
        {
            okButton.IsEnabled = processList.SelectedItem is not null;
        };

        processList.MouseDoubleClick += (_, _) =>
        {
            if (processList.SelectedItem is ProcessEntry entry)
            {
                selectedProcess = entry.ProcessName;
                inputDialog.DialogResult = true;
                inputDialog.Close();
            }
        };

        okButton.Click += (_, _) =>
        {
            selectedProcess = (processList.SelectedItem as ProcessEntry)?.ProcessName;
            inputDialog.DialogResult = true;
            inputDialog.Close();
        };

        cancelButton.Click += (_, _) => inputDialog.Close();

        // Pre-populate
        foreach (var entry in entries)
        {
            processList.Items.Add(entry);
        }

        if (processList.Items.Count > 0)
        {
            processList.SelectedIndex = 0;
            okButton.IsEnabled = true;
        }

        inputDialog.Content = grid;
        var result = inputDialog.ShowDialog();
        return result == true ? selectedProcess : null;
    }

    private sealed class ProcessEntry
    {
        public ProcessEntry(string processName, List<string> windowTitles)
        {
            ProcessName = processName;
            var count = windowTitles.Count;
            var preview = count == 0 ? "(no visible windows)" :
                count == 1 ? windowTitles[0] :
                $"{windowTitles[0]} (+{count - 1} more)";
            Display = $"{processName}  —  {preview}";
        }

        public string ProcessName { get; }
        public string Display { get; }
    }

    private static List<(string Title, string ProcessName)> EnumerateVisibleWindows()
    {
        var result = new List<(string, string)>();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
            {
                return true;
            }

            var title = GetWindowTextString(hWnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            GetWindowThreadProcessId(hWnd, out var processId);
            string processName;
            try
            {
                using var proc = Process.GetProcessById((int)processId);
                processName = proc.ProcessName;
            }
            catch
            {
                processName = string.Empty;
            }

            result.Add((title, processName));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static string GetWindowTextString(IntPtr hWnd)
    {
        var sb = new System.Text.StringBuilder(256);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
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
