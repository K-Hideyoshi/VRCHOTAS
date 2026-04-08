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
}
