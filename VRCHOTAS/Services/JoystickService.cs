using SharpDX.DirectInput;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;

namespace VRCHOTAS.Services;

public sealed class JoystickService : IDisposable
{
    private const int MaxButtonsPerDevice = 64;

    private readonly record struct PovDirectionState(bool Up, bool Right, bool Down, bool Left)
    {
        public double X => (Right ? 1.0 : 0.0) + (Left ? -1.0 : 0.0);
        public double Y => (Up ? 1.0 : 0.0) + (Down ? -1.0 : 0.0);
    }

    private sealed class DeviceRuntime
    {
        public required Guid InstanceGuid { get; init; }
        public required string DeviceId { get; init; }
        public required string DeviceName { get; init; }
        public Joystick? Joystick { get; set; }
        public bool IsConnected { get; set; }
        public IReadOnlyList<string> PhysicalAxisNames { get; set; } = Array.Empty<string>();
        public IReadOnlyList<int> PhysicalButtonIndices { get; set; } = Array.Empty<int>();
        public IReadOnlyList<int> PhysicalPovIndices { get; set; } = Array.Empty<int>();
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
                .Select(CreateDeviceSnapshot)
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
                    output.Add(CreateDeviceSnapshot(runtime));
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

                        output.Add(CreateDeviceSnapshot(runtime));

                        continue;
                    }

                    output.Add(new JoystickDeviceState
                    {
                        DeviceId = runtime.DeviceId,
                        DeviceName = runtime.DeviceName,
                        IsConnected = true,
                        Axes = BuildAxes(current),
                        Buttons = BuildButtons(current, out var buttonNames),
                        ButtonNames = buttonNames,
                        PhysicalAxes = BuildPhysicalAxes(current, runtime.PhysicalAxisNames),
                        PhysicalButtons = BuildPhysicalButtons(current, runtime.PhysicalButtonIndices),
                        PhysicalPovs = BuildPhysicalPovs(current, runtime.PhysicalPovIndices)
                    });
                }
                catch (Exception ex)
                {
                    runtime.IsConnected = false;
                    hasChange = true;
                    _logger.Warning(nameof(JoystickService), $"Device disconnected while polling: {runtime.DeviceName}. {ex.Message}");

                    output.Add(CreateDeviceSnapshot(runtime));
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
            LoadPhysicalCapabilities(runtime);
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

    private void LoadPhysicalCapabilities(DeviceRuntime runtime)
    {
        if (runtime.Joystick is null)
        {
            runtime.PhysicalAxisNames = Array.Empty<string>();
            runtime.PhysicalButtonIndices = Array.Empty<int>();
            runtime.PhysicalPovIndices = Array.Empty<int>();
            return;
        }

        try
        {
            var objects = runtime.Joystick.GetObjects();
            runtime.PhysicalAxisNames = BuildPhysicalAxisNames(objects);

            runtime.PhysicalButtonIndices = objects
                .Where(IsPhysicalButtonObject)
                .Select(@object => @object.ObjectId.InstanceNumber)
                .Distinct()
                .OrderBy(index => index)
                .ToArray();

            runtime.PhysicalPovIndices = objects
                .Where(IsPhysicalPovObject)
                .Select(@object => @object.ObjectId.InstanceNumber)
                .Distinct()
                .OrderBy(index => index)
                .ToArray();
        }
        catch (Exception ex)
        {
            runtime.PhysicalAxisNames = Array.Empty<string>();
            runtime.PhysicalButtonIndices = Array.Empty<int>();
            runtime.PhysicalPovIndices = Array.Empty<int>();
            _logger.Warning(nameof(JoystickService), $"Failed to read device capabilities: {runtime.DeviceName}. {ex.Message}");
        }
    }

    private static JoystickDeviceState CreateDeviceSnapshot(DeviceRuntime runtime)
    {
        return new JoystickDeviceState
        {
            DeviceId = runtime.DeviceId,
            DeviceName = runtime.DeviceName,
            IsConnected = runtime.IsConnected,
            PhysicalAxes = runtime.PhysicalAxisNames
                .Select(name => new PhysicalAxisState
                {
                    Name = name,
                    Value = 0
                })
                .ToArray(),
            PhysicalButtons = runtime.PhysicalButtonIndices
                .Select(index => new PhysicalButtonState
                {
                    Index = index,
                    Name = (index + 1).ToString(),
                    IsPressed = false
                })
                .ToArray(),
            PhysicalPovs = runtime.PhysicalPovIndices
                .Select(index => new PhysicalPovState
                {
                    Index = index,
                    RawValue = -1,
                    DirectionDisplay = "Centered",
                    X = 0,
                    Y = 0
                })
                .ToArray()
        };
    }

    private static double Normalize(int value)
    {
        const double max = 65535.0;
        return (value / max) * 2.0 - 1.0;
    }

    private static IReadOnlyDictionary<string, double> BuildAxes(JoystickState state)
    {
        var axes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["X"] = Normalize(state.X),
            ["Y"] = Normalize(state.Y),
            ["Z"] = Normalize(state.Z),
            ["RX"] = Normalize(state.RotationX),
            ["RY"] = Normalize(state.RotationY),
            ["RZ"] = Normalize(state.RotationZ),
            ["SL0"] = Normalize(state.Sliders.Length > 0 ? state.Sliders[0] : 0),
            ["SL1"] = Normalize(state.Sliders.Length > 1 ? state.Sliders[1] : 0)
        };

        var povControllers = state.PointOfViewControllers ?? [];
        for (var index = 0; index < povControllers.Length; index++)
        {
            var pov = DecodePovDirection(povControllers[index]);
            axes[$"POV{index}X"] = pov.X;
            axes[$"POV{index}Y"] = pov.Y;
        }

        return axes;
    }

    private static IReadOnlyList<PhysicalAxisState> BuildPhysicalAxes(JoystickState state, IReadOnlyList<string> axisNames)
    {
        return axisNames
            .Select(name => new PhysicalAxisState
            {
                Name = name,
                Value = TryGetAxisValue(state, name, out var value) ? value : 0
            })
            .ToArray();
    }

    private static IReadOnlyList<bool> BuildButtons(JoystickState state, out IReadOnlyList<string> buttonNames)
    {
        var buttons = state.Buttons.Take(MaxButtonsPerDevice).ToList();
        var names = Enumerable.Range(0, buttons.Count)
            .Select(index => (index + 1).ToString())
            .ToList();

        var povControllers = state.PointOfViewControllers ?? [];
        for (var index = 0; index < povControllers.Length; index++)
        {
            var pov = DecodePovDirection(povControllers[index]);
            buttons.Add(pov.Up);
            names.Add($"POV{index} Up");
            buttons.Add(pov.Right);
            names.Add($"POV{index} Right");
            buttons.Add(pov.Down);
            names.Add($"POV{index} Down");
            buttons.Add(pov.Left);
            names.Add($"POV{index} Left");
        }

        buttonNames = names;
        return buttons;
    }

    private static IReadOnlyList<PhysicalButtonState> BuildPhysicalButtons(JoystickState state, IReadOnlyList<int> buttonIndices)
    {
        return buttonIndices
            .Select(index => new PhysicalButtonState
            {
                Index = index,
                Name = (index + 1).ToString(),
                IsPressed = index >= 0 && index < state.Buttons.Length && state.Buttons[index]
            })
            .ToArray();
    }

    private static IReadOnlyList<PhysicalPovState> BuildPhysicalPovs(JoystickState state, IReadOnlyList<int> povIndices)
    {
        var povControllers = state.PointOfViewControllers ?? [];
        return povIndices
            .Select(index =>
            {
                var rawValue = index >= 0 && index < povControllers.Length ? povControllers[index] : -1;
                var direction = DecodePovDirection(rawValue);
                return new PhysicalPovState
                {
                    Index = index,
                    RawValue = rawValue,
                    DirectionDisplay = FormatPovDirection(direction),
                    Up = direction.Up,
                    Right = direction.Right,
                    Down = direction.Down,
                    Left = direction.Left,
                    X = direction.X,
                    Y = direction.Y
                };
            })
            .ToArray();
    }

    private static IReadOnlyList<string> BuildPhysicalAxisNames(IEnumerable<DeviceObjectInstance> objects)
    {
        var axisNames = new List<string>();
        var sliderIndex = 0;

        foreach (var @object in objects)
        {
            var axisName = TryResolvePhysicalAxisName(@object, ref sliderIndex);
            if (!string.IsNullOrWhiteSpace(axisName)
                && !axisNames.Contains(axisName, StringComparer.OrdinalIgnoreCase))
            {
                axisNames.Add(axisName);
            }
        }

        return axisNames;
    }

    private static bool TryGetAxisValue(JoystickState state, string axisName, out double value)
    {
        switch (axisName.ToUpperInvariant())
        {
            case "X":
                value = Normalize(state.X);
                return true;
            case "Y":
                value = Normalize(state.Y);
                return true;
            case "Z":
                value = Normalize(state.Z);
                return true;
            case "RX":
                value = Normalize(state.RotationX);
                return true;
            case "RY":
                value = Normalize(state.RotationY);
                return true;
            case "RZ":
                value = Normalize(state.RotationZ);
                return true;
            case "SL0":
                value = Normalize(state.Sliders.Length > 0 ? state.Sliders[0] : 0);
                return true;
            case "SL1":
                value = Normalize(state.Sliders.Length > 1 ? state.Sliders[1] : 0);
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static PovDirectionState DecodePovDirection(int value)
    {
        if (value < 0)
        {
            return new PovDirectionState(false, false, false, false);
        }

        var normalized = ((value % 36000) + 36000) % 36000;
        var up = normalized >= 31500 || normalized <= 4500;
        var right = normalized >= 4500 && normalized <= 13500;
        var down = normalized >= 13500 && normalized <= 22500;
        var left = normalized >= 22500 && normalized <= 31500;
        return new PovDirectionState(up, right, down, left);
    }

    private static string FormatPovDirection(PovDirectionState direction)
    {
        if (!direction.Up && !direction.Right && !direction.Down && !direction.Left)
        {
            return "Centered";
        }

        return string.Join('-', new[]
        {
            direction.Up ? "Up" : null,
            direction.Right ? "Right" : null,
            direction.Down ? "Down" : null,
            direction.Left ? "Left" : null
        }.Where(text => text is not null));
    }

    private static string? TryResolvePhysicalAxisName(DeviceObjectInstance @object, ref int sliderIndex)
    {
        if (@object.ObjectType == ObjectGuid.XAxis)
        {
            return "X";
        }

        if (@object.ObjectType == ObjectGuid.YAxis)
        {
            return "Y";
        }

        if (@object.ObjectType == ObjectGuid.ZAxis)
        {
            return "Z";
        }

        if (@object.ObjectType == ObjectGuid.RxAxis)
        {
            return "RX";
        }

        if (@object.ObjectType == ObjectGuid.RyAxis)
        {
            return "RY";
        }

        if (@object.ObjectType == ObjectGuid.RzAxis)
        {
            return "RZ";
        }

        if (@object.ObjectType == ObjectGuid.Slider)
        {
            return $"SL{sliderIndex++}";
        }

        return null;
    }

    private static bool IsPhysicalButtonObject(DeviceObjectInstance @object)
    {
        return @object.ObjectType == ObjectGuid.Button;
    }

    private static bool IsPhysicalPovObject(DeviceObjectInstance @object)
    {
        return @object.ObjectType == ObjectGuid.PovController;
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
