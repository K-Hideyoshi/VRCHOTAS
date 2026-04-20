using System.Text;
using System.Windows.Input;
using VRCHOTAS.Models;

namespace VRCHOTAS.Services;

public static class HotkeyCaptureRules
{
    public static bool IsAllowedMainKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return true;
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return true;
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return true;
        }

        if (key is >= Key.F1 and <= Key.F12)
        {
            return true;
        }

        return false;
    }

    public static bool HasIllegalModifierKeys(ModifierKeys mods)
    {
        const ModifierKeys illegal = ModifierKeys.Windows;
        return (mods & illegal) != 0;
    }
}

public static class HotkeyDisplayFormatter
{
    public static string Format(HotkeyBinding binding)
    {
        if (binding.Kind == HotkeyInputKind.None)
        {
            return string.Empty;
        }

        if (binding.Kind == HotkeyInputKind.Keyboard && binding.Keyboard is not null)
        {
            return FormatKeyboard(binding.Keyboard);
        }

        if (binding.Kind == HotkeyInputKind.Joystick)
        {
            var id = binding.JoystickDeviceId ?? string.Empty;
            var shortId = id.Length > 8 ? id[..8] + "…" : id;
            return $"{shortId} / Button {binding.JoystickButtonIndex}";
        }

        return string.Empty;
    }

    private static string FormatKeyboard(KeyboardChordBinding chord)
    {
        var mods = (ModifierKeys)chord.Modifiers;
        var sb = new StringBuilder();
        if (mods.HasFlag(ModifierKeys.Control))
        {
            sb.Append("Ctrl+");
        }

        if (mods.HasFlag(ModifierKeys.Shift))
        {
            sb.Append("Shift+");
        }

        if (mods.HasFlag(ModifierKeys.Alt))
        {
            sb.Append("Alt+");
        }

        var key = (Key)chord.Key;
        sb.Append(key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (char)('0' + (key - Key.NumPad0)),
            >= Key.F1 and <= Key.F12 => key.ToString(),
            >= Key.A and <= Key.Z => key.ToString(),
            _ => key.ToString()
        });

        return sb.ToString();
    }
}
