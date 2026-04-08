using SharpDX.DirectInput;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;

namespace VRCHOTAS.Services;

public sealed class JoystickService : IDisposable
{
    private sealed class DeviceRuntime
    {
        public required Guid InstanceGuid { get; init; }
        public required string DeviceId { get; init; }
        public required string DeviceName { get; init; }
        public Joystick? Joystick { get; set; }
        public bool IsConnected { get; set; }
    }

    private readonly object _sync = new();
    private readonly DirectInput _directInput = new();
    private readonly IAppLogger _logger;
    private readonly Dictionary<Guid, DeviceRuntime> _devices = new();

    public event EventHandler? DevicesChanged;

    public JoystickService(IAppLogger logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<JoystickDeviceState> GetDeviceStatesSnapshot()
    {
        lock (_sync)
        {
            return _devices.Values
                .Select(device => new JoystickDeviceState
                {
                    DeviceId = device.DeviceId,
                    DeviceName = device.DeviceName,
                    IsConnected = device.IsConnected
                })
                .ToArray();
        }
    }

    public void RefreshDevices()
    {
        bool hasChange = false;

        try
        {
            var attachedDevices = _directInput
                .GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly)
                .ToArray();

            var attachedIds = attachedDevices.Select(device => device.InstanceGuid).ToHashSet();

            lock (_sync)
            {
                foreach (var removedId in _devices.Keys.Where(id => !attachedIds.Contains(id)).ToArray())
                {
                    var removedDevice = _devices[removedId];
                    ReleaseJoystick(removedDevice);
                    _devices.Remove(removedId);
                    hasChange = true;
                    _logger.Info(nameof(JoystickService), $"Device removed: {removedDevice.DeviceName}");
                }

                foreach (var attached in attachedDevices)
                {
                    if (_devices.ContainsKey(attached.InstanceGuid))
                    {
                        continue;
                    }

                    var runtime = new DeviceRuntime
                    {
                        InstanceGuid = attached.InstanceGuid,
                        DeviceId = attached.InstanceGuid.ToString("D"),
                        DeviceName = string.IsNullOrWhiteSpace(attached.InstanceName) ? attached.ProductName : attached.InstanceName
                    };

                    TryAcquire(runtime);
                    _devices[attached.InstanceGuid] = runtime;
                    hasChange = true;
                    _logger.Info(nameof(JoystickService), $"Device discovered: {runtime.DeviceName}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(JoystickService), "Device refresh failed.", ex);
        }

        if (hasChange)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public VRCHOTAS.Models.RawJoystickState PollStates()
    {
        var hasChange = false;
        var output = new List<JoystickDeviceState>();

        lock (_sync)
        {
            foreach (var runtime in _devices.Values)
            {
                if (!runtime.IsConnected || runtime.Joystick is null)
                {
                    output.Add(new JoystickDeviceState
                    {
                        DeviceId = runtime.DeviceId,
                        DeviceName = runtime.DeviceName,
                        IsConnected = false
                    });
                    continue;
                }

                try
                {
                    runtime.Joystick.Poll();
                    var current = runtime.Joystick.GetCurrentState();
                    if (current is null)
                    {
                        runtime.IsConnected = false;
                        hasChange = true;
                        _logger.Warning(nameof(JoystickService), $"Device state unavailable: {runtime.DeviceName}");

                        output.Add(new JoystickDeviceState
                        {
                            DeviceId = runtime.DeviceId,
                            DeviceName = runtime.DeviceName,
                            IsConnected = false
                        });

                        continue;
                    }

                    output.Add(new JoystickDeviceState
                    {
                        DeviceId = runtime.DeviceId,
                        DeviceName = runtime.DeviceName,
                        IsConnected = true,
                        Axes = new Dictionary<string, double>
                        {
                            ["X"] = Normalize(current.X),
                            ["Y"] = Normalize(current.Y),
                            ["Z"] = Normalize(current.Z),
                            ["RX"] = Normalize(current.RotationX),
                            ["RY"] = Normalize(current.RotationY),
                            ["RZ"] = Normalize(current.RotationZ),
                            ["SL0"] = Normalize(current.Sliders.Length > 0 ? current.Sliders[0] : 0),
                            ["SL1"] = Normalize(current.Sliders.Length > 1 ? current.Sliders[1] : 0)
                        },
                        Buttons = current.Buttons.Select(button => button).ToArray()
                    });
                }
                catch (Exception ex)
                {
                    runtime.IsConnected = false;
                    hasChange = true;
                    _logger.Warning(nameof(JoystickService), $"Device disconnected while polling: {runtime.DeviceName}. {ex.Message}");

                    output.Add(new JoystickDeviceState
                    {
                        DeviceId = runtime.DeviceId,
                        DeviceName = runtime.DeviceName,
                        IsConnected = false
                    });
                }
            }
        }

        if (hasChange)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }

        return new VRCHOTAS.Models.RawJoystickState
        {
            Devices = output
        };
    }

    public bool TryAcquireDisconnectedDevices()
    {
        var hasChange = false;

        lock (_sync)
        {
            foreach (var runtime in _devices.Values.Where(device => !device.IsConnected).ToArray())
            {
                if (TryAcquire(runtime))
                {
                    hasChange = true;
                }
            }
        }

        if (hasChange)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }

        return hasChange;
    }

    private bool TryAcquire(DeviceRuntime runtime)
    {
        try
        {
            ReleaseJoystick(runtime);
            runtime.Joystick = new Joystick(_directInput, runtime.InstanceGuid);
            runtime.Joystick.Properties.BufferSize = 64;
            runtime.Joystick.Acquire();
            runtime.IsConnected = true;
            _logger.Info(nameof(JoystickService), $"Device connected: {runtime.DeviceName}");
            return true;
        }
        catch (Exception ex)
        {
            runtime.IsConnected = false;
            _logger.Warning(nameof(JoystickService), $"Device not acquired: {runtime.DeviceName}. {ex.Message}");
            return false;
        }
    }

    private static double Normalize(int value)
    {
        const double max = 65535.0;
        return (value / max) * 2.0 - 1.0;
    }

    private void ReleaseJoystick(DeviceRuntime runtime)
    {
        if (runtime.Joystick is null)
        {
            return;
        }

        try
        {
            runtime.Joystick.Unacquire();
        }
        catch
        {
        }

        runtime.Joystick.Dispose();
        runtime.Joystick = null;
        runtime.IsConnected = false;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var runtime in _devices.Values)
            {
                ReleaseJoystick(runtime);
            }

            _devices.Clear();
        }

        _directInput.Dispose();
    }
}
