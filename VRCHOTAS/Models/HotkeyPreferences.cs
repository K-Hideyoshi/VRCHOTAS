using Newtonsoft.Json;

namespace VRCHOTAS.Models;

public enum HotkeyInputKind
{
    None = 0,
    Keyboard = 1,
    Joystick = 2
}

/// <summary>Serialized keyboard shortcut (WPF Key + modifier flags).</summary>
public sealed class KeyboardChordBinding
{
    public int Modifiers { get; set; }
    public int Key { get; set; }
}

/// <summary>One assignable hotkey slot.</summary>
public sealed class HotkeyBinding
{
    public HotkeyInputKind Kind { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public KeyboardChordBinding? Keyboard { get; set; }

    public string? JoystickDeviceId { get; set; }
    public int JoystickButtonIndex { get; set; }
}

public sealed class HotkeyPreferences
{
    public HotkeyBinding PreviousConfiguration { get; set; } = new();
    public HotkeyBinding NextConfiguration { get; set; } = new();
    public HotkeyBinding ToggleMasterSwitch { get; set; } = new();
}
