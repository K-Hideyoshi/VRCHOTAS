using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Newtonsoft.Json;
using VRCHOTAS.Models;
using VRCHOTAS.Services;
using VRCHOTAS.ViewModels;

namespace VRCHOTAS;

public partial class PreferencesWindow : Window
{
    private readonly MainViewModel _main;
    private readonly PreferencesService _preferencesService;
    private readonly HotkeyPreferences _hotkeys;
    private readonly EulerAnglePreferences _eulerAngles;
    private readonly VrOverlayPreferences _vrOverlay;
    private ControllerOutputMode _controllerOutputMode;
    private IDisposable? _suspendScope;
    private DispatcherTimer? _joyTimer;
    private RawJoystickState? _joyPrev;
    private int? _captureSlot;
    private Key? _pendingMainKey;
    private ModifierKeys _pendingModifiers;
    private bool _prevLocked;
    private bool _nextLocked;
    private bool _masterLocked;

    public PreferencesWindow(MainViewModel main, PreferencesService preferencesService)
    {
        InitializeComponent();
        _main = main;
        DataContext = main;
        _preferencesService = preferencesService;
        _hotkeys = CloneHotkeys(preferencesService.LoadHotkeys());
        _eulerAngles = preferencesService.LoadEulerAngles().Clone();
        _vrOverlay = main.GetVrOverlayPreferencesSnapshot();
        _controllerOutputMode = preferencesService.LoadControllerOutputMode();
        ApplyTextsFromModel();
        ApplyEulerSelectionFromModel();
        ApplyControllerOutputModeSelectionFromModel();
        ApplyVrOverlaySelectionFromModel();
    }

    private static HotkeyPreferences CloneHotkeys(HotkeyPreferences source)
    {
        var json = JsonConvert.SerializeObject(source);
        return JsonConvert.DeserializeObject<HotkeyPreferences>(json) ?? new HotkeyPreferences();
    }

    private void ApplyTextsFromModel()
    {
        PreviousConfigBox.Text = FormatHotkey(_hotkeys.PreviousConfiguration);
        NextConfigBox.Text = FormatHotkey(_hotkeys.NextConfiguration);
        MasterSwitchBox.Text = FormatHotkey(_hotkeys.ToggleMasterSwitch);
        SetLockedUi(0, _hotkeys.PreviousConfiguration.Kind != HotkeyInputKind.None);
        SetLockedUi(1, _hotkeys.NextConfiguration.Kind != HotkeyInputKind.None);
        SetLockedUi(2, _hotkeys.ToggleMasterSwitch.Kind != HotkeyInputKind.None);
    }

    private void ApplyEulerSelectionFromModel()
    {
        switch (_eulerAngles.Order)
        {
            case EulerAngleOrder.PitchYawRoll:
                PitchYawRollRadio.IsChecked = true;
                break;
            case EulerAngleOrder.PitchRollYaw:
                PitchRollYawRadio.IsChecked = true;
                break;
            case EulerAngleOrder.YawPitchRoll:
                YawPitchRollRadio.IsChecked = true;
                break;
            case EulerAngleOrder.YawRollPitch:
                YawRollPitchRadio.IsChecked = true;
                break;
            case EulerAngleOrder.RollPitchYaw:
                RollPitchYawRadio.IsChecked = true;
                break;
            case EulerAngleOrder.RollYawPitch:
                RollYawPitchRadio.IsChecked = true;
                break;
            default:
                PitchRollYawRadio.IsChecked = true;
                break;
        }

        if (_eulerAngles.AxisReference == EulerAngleAxisReference.World)
        {
            WorldAxesRadio.IsChecked = true;
        }
        else
        {
            LocalAxesRadio.IsChecked = true;
        }
    }

    private void SyncEulerSelectionToModel()
    {
        _eulerAngles.Order = PitchYawRollRadio.IsChecked == true
            ? EulerAngleOrder.PitchYawRoll
            : PitchRollYawRadio.IsChecked == true
                ? EulerAngleOrder.PitchRollYaw
                : YawPitchRollRadio.IsChecked == true
                    ? EulerAngleOrder.YawPitchRoll
                    : YawRollPitchRadio.IsChecked == true
                        ? EulerAngleOrder.YawRollPitch
                        : RollPitchYawRadio.IsChecked == true
                            ? EulerAngleOrder.RollPitchYaw
                            : EulerAngleOrder.RollYawPitch;

        _eulerAngles.AxisReference = WorldAxesRadio.IsChecked == true
            ? EulerAngleAxisReference.World
            : EulerAngleAxisReference.Local;
    }

    private void ApplyControllerOutputModeSelectionFromModel()
    {
        switch (_controllerOutputMode)
        {
            case ControllerOutputMode.HybridKeepLeftReal:
                HybridKeepLeftRealRadio.IsChecked = true;
                break;
            case ControllerOutputMode.HybridKeepRightReal:
                HybridKeepRightRealRadio.IsChecked = true;
                break;
            default:
                FullVirtualModeRadio.IsChecked = true;
                break;
        }
    }

    private void SyncControllerOutputModeSelectionToModel()
    {
        _controllerOutputMode = HybridKeepLeftRealRadio.IsChecked == true
            ? ControllerOutputMode.HybridKeepLeftReal
            : HybridKeepRightRealRadio.IsChecked == true
                ? ControllerOutputMode.HybridKeepRightReal
                : ControllerOutputMode.FullVirtual;
    }

    private void ApplyVrOverlaySelectionFromModel()
    {
        EnableVrOverlayCheckBox.IsChecked = _vrOverlay.Enabled;
        ShowMasterStatusIndicatorCheckBox.IsChecked = _vrOverlay.StatusIndicatorEnabled;
        HideWhenDashboardIsVisibleCheckBox.IsChecked = _vrOverlay.HideWhenDashboardIsVisible;
        VrOverlayToastDurationSlider.Value = _vrOverlay.ToastDurationSeconds;
        VrOverlayToastDurationValueLabel.Text = _vrOverlay.ToastDurationSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        ToastTextSizeSlider.Value = _vrOverlay.ToastTextSize;
        ToastTextSizeValueLabel.Text = _vrOverlay.ToastTextSize.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        ToastBgColorLabel.Text = _vrOverlay.ToastBackgroundColor;
        ToastOpacitySlider.Value = _vrOverlay.ToastOpacity;
        ToastOpacityValueLabel.Text = _vrOverlay.ToastOpacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        string markerPath = string.IsNullOrWhiteSpace(_vrOverlay.MarkerImagePath) 
            ? "icons\\joystick.png" 
            : _vrOverlay.MarkerImagePath;

        _vrOverlay.MarkerImagePath = markerPath;

        UpdateImagePreview(markerPath);

        MarkerSizeSlider.Value = _vrOverlay.MarkerSize;
        MarkerSizeValueLabel.Text = _vrOverlay.MarkerSize.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        MarkerOpacitySlider.Value = _vrOverlay.MarkerOpacity;
        MarkerOpacityValueLabel.Text = _vrOverlay.MarkerOpacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        MarkerPosXBox.Text = _vrOverlay.MarkerPositionX.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        MarkerPosYBox.Text = _vrOverlay.MarkerPositionY.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        UpdateToastBgColorButtonAppearance();
        UpdateVrOverlayUiState();
    }

    private void TryUpdateImagePreview(string? path)
    {
        string actualPath = string.IsNullOrWhiteSpace(path) ? "icons\\joystick.png" : path;
        _vrOverlay.MarkerImagePath = actualPath;
        UpdateImagePreview(actualPath);
    }

    private void UpdateImagePreview(string path)
    {
        try
        {
            var absolutePath = System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            if (System.IO.File.Exists(absolutePath))
            {
                MarkerImagePreview.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(absolutePath, UriKind.Absolute));
            }
            else
            {
                MarkerImagePreview.Source = null;
            }
        }
        catch
        {
            MarkerImagePreview.Source = null;
        }
    }

    private void UpdateToastBgColorButtonAppearance()
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ToastBgColorLabel.Text);
            ToastBgColorButton.Background = new System.Windows.Media.SolidColorBrush(color);
        }
        catch
        {
        }
    }

    private bool TrySyncVrOverlaySelectionToModel()
    {
        _vrOverlay.Enabled = EnableVrOverlayCheckBox.IsChecked == true;
        _vrOverlay.StatusIndicatorEnabled = ShowMasterStatusIndicatorCheckBox.IsChecked == true;
        _vrOverlay.HideWhenDashboardIsVisible = HideWhenDashboardIsVisibleCheckBox.IsChecked == true;

        _vrOverlay.ToastDurationSeconds = VrOverlayToastDurationSlider.Value;

        if (double.TryParse(MarkerPosXBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var markerPosX)
            || double.TryParse(MarkerPosXBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out markerPosX))
        {
            _vrOverlay.MarkerPositionX = markerPosX;
        }

        if (double.TryParse(MarkerPosYBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var markerPosY)
            || double.TryParse(MarkerPosYBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out markerPosY))
        {
            _vrOverlay.MarkerPositionY = markerPosY;
        }

        _vrOverlay.ToastTextSize = ToastTextSizeSlider.Value;
        _vrOverlay.ToastBackgroundColor = string.IsNullOrWhiteSpace(ToastBgColorLabel.Text) ? "#80000000" : ToastBgColorLabel.Text.Trim();
        _vrOverlay.ToastOpacity = ToastOpacitySlider.Value;
        _vrOverlay.MarkerSize = MarkerSizeSlider.Value;
        _vrOverlay.MarkerOpacity = MarkerOpacitySlider.Value;
        _vrOverlay.Normalize();

        VrOverlayToastDurationSlider.Value = _vrOverlay.ToastDurationSeconds;
        MarkerPosXBox.Text = _vrOverlay.MarkerPositionX.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        MarkerPosYBox.Text = _vrOverlay.MarkerPositionY.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    private void OnNumericTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !System.Text.RegularExpressions.Regex.IsMatch(e.Text, @"^[0-9.\-]+$");
    }

    private void OnBrowseMarkerImage(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "PNG files (*.png)|*.png|All files (*.*)|*.*",
            CheckFileExists = true,
            Title = "Select marker image"
        };

        if (dialog.ShowDialog(this) == true)
        {
            _vrOverlay.MarkerImagePath = dialog.FileName;
            TryUpdateImagePreview(_vrOverlay.MarkerImagePath);
        }
    }

    private void OnToastDurationChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VrOverlayToastDurationValueLabel is not null)
        {
            VrOverlayToastDurationValueLabel.Text = e.NewValue.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private void OnToastTextSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ToastTextSizeValueLabel is not null)
        {
            ToastTextSizeValueLabel.Text = e.NewValue.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private void OnToastOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ToastOpacityValueLabel is not null)
        {
            ToastOpacityValueLabel.Text = e.NewValue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private void OnMarkerSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MarkerSizeValueLabel is not null)
        {
            MarkerSizeValueLabel.Text = e.NewValue.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private void OnMarkerOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MarkerOpacityValueLabel is not null)
        {
            MarkerOpacityValueLabel.Text = e.NewValue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private void OnPositionTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        TrySyncVrOverlaySelectionToModel();
    }

    private void OnToastBgColorClick(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.ColorDialog();
        try
        {
            var currentColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ToastBgColorLabel.Text);
            dialog.Color = System.Drawing.Color.FromArgb(currentColor.A, currentColor.R, currentColor.G, currentColor.B);
        }
        catch
        {
        }

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var newColor = dialog.Color;
            ToastBgColorLabel.Text = $"#{newColor.A:X2}{newColor.R:X2}{newColor.G:X2}{newColor.B:X2}";
            UpdateToastBgColorButtonAppearance();
        }
    }

    private void UpdateVrOverlayUiState()
    {
        if (VrOverlayOptionsPanel != null)
        {
            VrOverlayOptionsPanel.IsEnabled = EnableVrOverlayCheckBox.IsChecked == true;
        }

        if (MasterStatusIndicatorOptionsPanel != null)
        {
            MasterStatusIndicatorOptionsPanel.IsEnabled = ShowMasterStatusIndicatorCheckBox.IsChecked == true;
        }
    }

    private void OnMasterIndicatorEnabledChanged(object sender, RoutedEventArgs e)
    {
        UpdateVrOverlayUiState();
    }

    private string FormatHotkey(HotkeyBinding binding)
    {
        return HotkeyDisplayFormatter.Format(binding, ResolveJoystickDeviceName);
    }

    private string ResolveJoystickDeviceName(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return string.Empty;
        }

        var state = _main.GetLatestStateSnapshot();
        var device = state.Devices.FirstOrDefault(item => item.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
        return device?.DeviceName ?? deviceId;
    }

    private void SetLockedUi(int slot, bool locked)
    {
        switch (slot)
        {
            case 0:
                _prevLocked = locked;
                PreviousConfigBox.IsEnabled = !locked;
                break;
            case 1:
                _nextLocked = locked;
                NextConfigBox.IsEnabled = !locked;
                break;
            case 2:
                _masterLocked = locked;
                MasterSwitchBox.IsEnabled = !locked;
                break;
        }
    }

    private void BeginCapture(int slot)
    {
        if (slot == 0 && _prevLocked)
        {
            return;
        }

        if (slot == 1 && _nextLocked)
        {
            return;
        }

        if (slot == 2 && _masterLocked)
        {
            return;
        }

        StopJoystickCapture();
        _pendingMainKey = null;
        _captureSlot = slot;
        _suspendScope = HotkeyRuntime.AcquireSuspendScope();
        StartJoystickCapture();
    }

    private void StartJoystickCapture()
    {
        _joyPrev = _main.GetLatestStateSnapshot();
        _joyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _joyTimer.Tick += OnJoystickTimerTick;
        _joyTimer.Start();
    }

    private void StopJoystickCapture()
    {
        if (_joyTimer is not null)
        {
            _joyTimer.Stop();
            _joyTimer.Tick -= OnJoystickTimerTick;
            _joyTimer = null;
        }

        _joyPrev = null;
    }

    private void OnJoystickTimerTick(object? sender, EventArgs e)
    {
        if (_captureSlot is null)
        {
            return;
        }

        var now = _main.GetLatestStateSnapshot();
        if (_joyPrev is null)
        {
            _joyPrev = now;
            return;
        }

        if (!TryDetectJoystickButtonPressEdge(_joyPrev, now, out var deviceId, out var buttonIndex))
        {
            _joyPrev = now;
            return;
        }

        var conflict = _main.GetJoystickHotkeyConflictKeysForCapture();
        var key = HotkeyRuntime.ConflictKey(deviceId, buttonIndex);
        if (conflict.Contains(key))
        {
            _joyPrev = now;
            return;
        }

        var binding = new HotkeyBinding
        {
            Kind = HotkeyInputKind.Joystick,
            JoystickDeviceId = deviceId,
            JoystickDeviceName = ResolveJoystickDeviceName(deviceId),
            JoystickButtonIndex = buttonIndex
        };

        CommitBinding(_captureSlot.Value, binding);
    }

    private static bool TryDetectJoystickButtonPressEdge(RawJoystickState prev, RawJoystickState now, out string deviceId, out int buttonIndex)
    {
        deviceId = string.Empty;
        buttonIndex = 0;
        foreach (var device in now.Devices.Where(d => d.IsConnected))
        {
            var prevDevice = prev.Devices.FirstOrDefault(d =>
                d.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (prevDevice is null)
            {
                continue;
            }

            var count = Math.Min(device.Buttons.Count, prevDevice.Buttons.Count);
            for (var i = 0; i < count; i++)
            {
                if (!prevDevice.Buttons[i] && device.Buttons[i])
                {
                    deviceId = device.DeviceId;
                    buttonIndex = i;
                    return true;
                }
            }
        }

        return false;
    }

    private void CommitBinding(int slot, HotkeyBinding binding)
    {
        StopJoystickCapture();
        _suspendScope?.Dispose();
        _suspendScope = null;
        var text = FormatHotkey(binding);
        switch (slot)
        {
            case 0:
                _hotkeys.PreviousConfiguration = binding;
                PreviousConfigBox.Text = text;
                SetLockedUi(0, true);
                break;
            case 1:
                _hotkeys.NextConfiguration = binding;
                NextConfigBox.Text = text;
                SetLockedUi(1, true);
                break;
            case 2:
                _hotkeys.ToggleMasterSwitch = binding;
                MasterSwitchBox.Text = text;
                SetLockedUi(2, true);
                break;
        }

        _captureSlot = null;
        _pendingMainKey = null;
    }

    private void OnPreviousBoxGotFocus(object sender, RoutedEventArgs e)
    {
        BeginCapture(0);
    }

    private void OnNextBoxGotFocus(object sender, RoutedEventArgs e)
    {
        BeginCapture(1);
    }

    private void OnMasterBoxGotFocus(object sender, RoutedEventArgs e)
    {
        BeginCapture(2);
    }

    private void OnCaptureBoxLostFocus(object sender, RoutedEventArgs e)
    {
        _pendingMainKey = null;
        StopJoystickCapture();
        _suspendScope?.Dispose();
        _suspendScope = null;
        _captureSlot = null;
    }

    private void OnCapturePreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_captureSlot is null)
        {
            return;
        }

        if (HotkeyCaptureRules.HasIllegalModifierKeys(Keyboard.Modifiers))
        {
            e.Handled = true;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HotkeyCaptureRules.IsAllowedMainKey(key))
        {
            StopJoystickCapture();
            _pendingMainKey = key;
            _pendingModifiers = Keyboard.Modifiers;
            e.Handled = true;
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin)
        {
            e.Handled = true;
        }
    }

    private void TryCommitKeyboard(int slot, System.Windows.Input.KeyEventArgs e)
    {
        if (_captureSlot != slot || _pendingMainKey is null)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != _pendingMainKey)
        {
            return;
        }

        var binding = new HotkeyBinding
        {
            Kind = HotkeyInputKind.Keyboard,
            Keyboard = new KeyboardChordBinding
            {
                Modifiers = (int)_pendingModifiers,
                Key = (int)_pendingMainKey.Value
            }
        };

        CommitBinding(slot, binding);
        e.Handled = true;
    }

    private void OnPreviousBoxPreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        TryCommitKeyboard(0, e);
    }

    private void OnNextBoxPreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        TryCommitKeyboard(1, e);
    }

    private void OnMasterBoxPreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        TryCommitKeyboard(2, e);
    }

    private void OnClearPreviousClick(object sender, RoutedEventArgs e)
    {
        _hotkeys.PreviousConfiguration = new HotkeyBinding();
        PreviousConfigBox.Text = string.Empty;
        SetLockedUi(0, false);
    }

    private void OnClearNextClick(object sender, RoutedEventArgs e)
    {
        _hotkeys.NextConfiguration = new HotkeyBinding();
        NextConfigBox.Text = string.Empty;
        SetLockedUi(1, false);
    }

    private void OnClearMasterClick(object sender, RoutedEventArgs e)
    {
        _hotkeys.ToggleMasterSwitch = new HotkeyBinding();
        MasterSwitchBox.Text = string.Empty;
        SetLockedUi(2, false);
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (ApplyPreferences())
        {
            DialogResult = true;
            Close();
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        ApplyPreferences();
    }

    private void OnVrOverlayEnabledChanged(object sender, RoutedEventArgs e)
    {
        UpdateVrOverlayUiState();
    }

    private bool ApplyPreferences()
    {
        SyncEulerSelectionToModel();
        SyncControllerOutputModeSelectionToModel();
        if (!TrySyncVrOverlaySelectionToModel())
        {
            return false;
        }

        _preferencesService.SaveHotkeys(_hotkeys);
        _preferencesService.SaveEulerAngles(_eulerAngles);
        _preferencesService.SaveVrOverlay(_vrOverlay);
        _preferencesService.SaveControllerOutputMode(_controllerOutputMode);
        _main.ApplyHotkeyPreferences(_hotkeys);
        _main.ApplyEulerAnglePreferences(_eulerAngles);
        _main.ApplyVrOverlayPreferences(_vrOverlay);
        _main.ApplyControllerOutputMode(_controllerOutputMode);
        return true;
    }

    protected override void OnClosed(EventArgs e)
    {
        StopJoystickCapture();
        _suspendScope?.Dispose();
        base.OnClosed(e);
    }
}
