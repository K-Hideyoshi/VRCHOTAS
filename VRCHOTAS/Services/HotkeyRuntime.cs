using System.Runtime.InteropServices;
using System.Windows.Input;
using VRCHOTAS.Models;

namespace VRCHOTAS.Services;

/// <summary>
/// Evaluates hotkey bindings each frame (keyboard via GetAsyncKeyState, joystick via polled state).
/// </summary>
public sealed class HotkeyRuntime
{
    private static int _suspensionCount;

    public static bool IsSuspended => Volatile.Read(ref _suspensionCount) > 0;

    public static IDisposable AcquireSuspendScope()
    {
        Interlocked.Increment(ref _suspensionCount);
        return new SuspendScope();
    }

    private sealed class SuspendScope : IDisposable
    {
        public void Dispose()
        {
            Interlocked.Decrement(ref _suspensionCount);
        }
    }

    private bool _prevKeyboardPrevious;
    private bool _prevKeyboardNext;
    private bool _prevKeyboardMaster;
    private bool _prevJoyPrevious;
    private bool _prevJoyNext;
    private bool _prevJoyMaster;

    public void ProcessFrame(
        RawJoystickState raw,
        HotkeyPreferences preferences,
        HashSet<string> joystickHotkeyConflictKeys,
        Action onPreviousConfiguration,
        Action onNextConfiguration,
        Action onToggleMasterSwitch)
    {
        if (IsSuspended)
        {
            _prevKeyboardPrevious = _prevKeyboardNext = _prevKeyboardMaster = false;
            _prevJoyPrevious = _prevJoyNext = _prevJoyMaster = false;
            return;
        }

        EvaluateBinding(preferences.PreviousConfiguration, raw, joystickHotkeyConflictKeys, ref _prevKeyboardPrevious, ref _prevJoyPrevious, onPreviousConfiguration);
        EvaluateBinding(preferences.NextConfiguration, raw, joystickHotkeyConflictKeys, ref _prevKeyboardNext, ref _prevJoyNext, onNextConfiguration);
        EvaluateBinding(preferences.ToggleMasterSwitch, raw, joystickHotkeyConflictKeys, ref _prevKeyboardMaster, ref _prevJoyMaster, onToggleMasterSwitch);
    }

    private static void EvaluateBinding(
        HotkeyBinding binding,
        RawJoystickState raw,
        HashSet<string> joystickHotkeyConflictKeys,
        ref bool prevKeyboardDown,
        ref bool prevJoystickDown,
        Action action)
    {
        if (binding.Kind == HotkeyInputKind.None)
        {
            prevKeyboardDown = false;
            prevJoystickDown = false;
            return;
        }

        if (binding.Kind == HotkeyInputKind.Keyboard)
        {
            var down = IsKeyboardChordDown(binding.Keyboard);
            if (down && !prevKeyboardDown)
            {
                action();
            }

            prevKeyboardDown = down;
            prevJoystickDown = false;
            return;
        }

        if (binding.Kind == HotkeyInputKind.Joystick)
        {
            var down = TryGetJoystickButtonDown(raw, binding, joystickHotkeyConflictKeys, out var conflict);
            if (conflict)
            {
                prevJoystickDown = false;
                prevKeyboardDown = false;
                return;
            }

            if (down && !prevJoystickDown)
            {
                action();
            }

            prevJoystickDown = down;
            prevKeyboardDown = false;
        }
    }

    private static bool TryGetJoystickButtonDown(RawJoystickState raw, HotkeyBinding binding, HashSet<string> conflictKeys, out bool conflict)
    {
        conflict = false;
        if (string.IsNullOrWhiteSpace(binding.JoystickDeviceId))
        {
            return false;
        }

        var key = ConflictKey(binding.JoystickDeviceId, binding.JoystickButtonIndex);
        if (conflictKeys.Contains(key))
        {
            conflict = true;
            return false;
        }

        var device = raw.Devices.FirstOrDefault(d =>
            d.IsConnected && d.DeviceId.Equals(binding.JoystickDeviceId, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            return false;
        }

        if (binding.JoystickButtonIndex < 0 || binding.JoystickButtonIndex >= device.Buttons.Count)
        {
            return false;
        }

        return device.Buttons[binding.JoystickButtonIndex];
    }

    public static string ConflictKey(string deviceId, int buttonIndex) => $"{deviceId}|{buttonIndex}";

    private static bool IsKeyboardChordDown(KeyboardChordBinding? chord)
    {
        if (chord is null || chord.Key == 0)
        {
            return false;
        }

        var mods = (ModifierKeys)chord.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control) != IsCtrlDown())
        {
            return false;
        }

        if (mods.HasFlag(ModifierKeys.Shift) != IsShiftDown())
        {
            return false;
        }

        if (mods.HasFlag(ModifierKeys.Alt) != IsAltDown())
        {
            return false;
        }

        var vk = KeyInterop.VirtualKeyFromKey((Key)chord.Key);
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    private static bool IsCtrlDown() =>
        (GetAsyncKeyState(0xA2) & 0x8000) != 0 || (GetAsyncKeyState(0xA3) & 0x8000) != 0;

    private static bool IsShiftDown() =>
        (GetAsyncKeyState(0xA0) & 0x8000) != 0 || (GetAsyncKeyState(0xA1) & 0x8000) != 0;

    private static bool IsAltDown() =>
        (GetAsyncKeyState(0xA4) & 0x8000) != 0 || (GetAsyncKeyState(0xA5) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
