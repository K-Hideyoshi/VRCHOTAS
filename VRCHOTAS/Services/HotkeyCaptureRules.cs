using System.Windows.Input;

namespace VRCHOTAS.Services;

/// <summary>
/// Validation rules for keyboard hotkey capture in the UI.
/// </summary>
public static class HotkeyCaptureRules
{
    public static bool IsAllowedMainKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return true;
        if (key is >= Key.D0 and <= Key.D9) return true;
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return true;
        if (key is >= Key.F1 and <= Key.F12) return true;
        return false;
    }

    public static bool HasIllegalModifierKeys(ModifierKeys mods)
    {
        const ModifierKeys illegal = ModifierKeys.Windows;
        return (mods & illegal) != 0;
    }
}
