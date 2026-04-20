using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Newtonsoft.Json;
using VRCHOTAS.Models;
using VRCHOTAS.Services;
using VRCHOTAS.ViewModels;

namespace VRCHOTAS;

public partial class HotkeysWindow : Window
{
    private readonly MainViewModel _main;
    private readonly PreferencesService _preferencesService;
    private readonly HotkeyPreferences _model;
    private IDisposable? _suspendScope;
    private DispatcherTimer? _joyTimer;
    private RawJoystickState? _joyPrev;
    private int? _captureSlot;
    private Key? _pendingMainKey;
    private ModifierKeys _pendingModifiers;
    private bool _prevLocked;
    private bool _nextLocked;
    private bool _masterLocked;

    public HotkeysWindow(MainViewModel main, PreferencesService preferencesService)
    {
        InitializeComponent();
        _main = main;
        _preferencesService = preferencesService;
        _model = CloneHotkeys(preferencesService.LoadHotkeys());
        ApplyTextsFromModel();
    }

    private static HotkeyPreferences CloneHotkeys(HotkeyPreferences source)
    {
        var json = JsonConvert.SerializeObject(source);
        return JsonConvert.DeserializeObject<HotkeyPreferences>(json) ?? new HotkeyPreferences();
    }

    private void ApplyTextsFromModel()
    {
        PreviousConfigBox.Text = HotkeyDisplayFormatter.Format(_model.PreviousConfiguration);
        NextConfigBox.Text = HotkeyDisplayFormatter.Format(_model.NextConfiguration);
        MasterSwitchBox.Text = HotkeyDisplayFormatter.Format(_model.ToggleMasterSwitch);
        SetLockedUi(0, _model.PreviousConfiguration.Kind != HotkeyInputKind.None);
        SetLockedUi(1, _model.NextConfiguration.Kind != HotkeyInputKind.None);
        SetLockedUi(2, _model.ToggleMasterSwitch.Kind != HotkeyInputKind.None);
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
        var text = HotkeyDisplayFormatter.Format(binding);
        switch (slot)
        {
            case 0:
                _model.PreviousConfiguration = binding;
                PreviousConfigBox.Text = text;
                SetLockedUi(0, true);
                break;
            case 1:
                _model.NextConfiguration = binding;
                NextConfigBox.Text = text;
                SetLockedUi(1, true);
                break;
            case 2:
                _model.ToggleMasterSwitch = binding;
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
        _model.PreviousConfiguration = new HotkeyBinding();
        PreviousConfigBox.Text = string.Empty;
        SetLockedUi(0, false);
    }

    private void OnClearNextClick(object sender, RoutedEventArgs e)
    {
        _model.NextConfiguration = new HotkeyBinding();
        NextConfigBox.Text = string.Empty;
        SetLockedUi(1, false);
    }

    private void OnClearMasterClick(object sender, RoutedEventArgs e)
    {
        _model.ToggleMasterSwitch = new HotkeyBinding();
        MasterSwitchBox.Text = string.Empty;
        SetLockedUi(2, false);
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        _preferencesService.SaveHotkeys(_model);
        _main.ApplyHotkeyPreferences(_preferencesService.LoadHotkeys());
        DialogResult = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopJoystickCapture();
        _suspendScope?.Dispose();
        base.OnClosed(e);
    }
}
