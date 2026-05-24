namespace VRCHOTAS.Models;

public sealed class RawJoystickState
{
    public IReadOnlyList<JoystickDeviceState> Devices { get; init; } = Array.Empty<JoystickDeviceState>();

    public bool HasConnectedDevice => Devices.Any(device => device.IsConnected);
}

public sealed class JoystickDeviceState
{
    public string DeviceId { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public bool IsConnected { get; init; }
    public IReadOnlyDictionary<string, double> Axes { get; init; } = new Dictionary<string, double>();
    public IReadOnlyList<bool> Buttons { get; init; } = Array.Empty<bool>();
    public IReadOnlyList<string> ButtonNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<PhysicalAxisState> PhysicalAxes { get; init; } = Array.Empty<PhysicalAxisState>();
    public IReadOnlyList<PhysicalButtonState> PhysicalButtons { get; init; } = Array.Empty<PhysicalButtonState>();
    public IReadOnlyList<PhysicalPovState> PhysicalPovs { get; init; } = Array.Empty<PhysicalPovState>();
}

public sealed class PhysicalAxisState
{
    public string Name { get; init; } = string.Empty;
    public double Value { get; init; }
}

public sealed class PhysicalButtonState
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsPressed { get; init; }
}

public sealed class PhysicalPovState
{
    public int Index { get; init; }
    public int RawValue { get; init; } = -1;
    public string DirectionDisplay { get; init; } = "Centered";
    public bool Up { get; init; }
    public bool Right { get; init; }
    public bool Down { get; init; }
    public bool Left { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
}
