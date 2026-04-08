using CommunityToolkit.Mvvm.ComponentModel;
using VRCHOTAS.Models;
using VRCHOTAS.Services;

namespace VRCHOTAS.ViewModels;

public sealed class MappingEditorViewModel : ObservableObject
{
    private readonly Func<RawJoystickState> _stateProvider;
    private bool _isListening;
    private MappingTargetKind _selectedTargetKind = MappingTargetKind.AxisInput;
    private string _sourceDeviceId = string.Empty;
    private string _sourceDeviceName = string.Empty;
    private string _sourceAxis = "X";
    private int _sourceButtonIndex;
    private VirtualTargetHand _targetHand = VirtualTargetHand.Left;
    private int _targetAxisIndex;
    private int _targetButtonIndex;
    private double _deadzone;
    private double _curve;
    private double _saturation = 1.0;
    private bool _invert;
    private double _currentInputValue;
    private double _currentOutputValue;
    private double _currentInputPlotX = 100;
    private double _currentInputPlotY = 100;
    private double _currentOutputPlotX = 100;
    private double _currentOutputPlotY = 100;
    private string _curvePlotPoints = string.Empty;

    public MappingEditorViewModel(Func<RawJoystickState> stateProvider, MappingEntry? existing)
    {
        _stateProvider = stateProvider;
        TargetKindOptions = BuildTargetKindOptions();

        if (existing is null)
        {
            RebuildCurvePlot();
            return;
        }

        SelectedTargetKind = existing.ResolvedTargetKind;
        SourceDeviceId = existing.SourceDeviceId;
        SourceDeviceName = existing.SourceDeviceName;
        SourceAxis = existing.SourceAxis;
        SourceButtonIndex = existing.SourceButtonIndex;
        TargetHand = existing.TargetHand;
        TargetAxisIndex = existing.TargetAxisIndex;
        TargetButtonIndex = existing.TargetButtonIndex;
        Deadzone = existing.Deadzone;
        Curve = existing.Curve;
        Saturation = existing.Saturation;
        Invert = existing.Invert;

        RebuildCurvePlot();
    }

    public IReadOnlyList<TargetKindOption> TargetKindOptions { get; }

    public MappingTargetKind SelectedTargetKind
    {
        get => _selectedTargetKind;
        set
        {
            if (SetProperty(ref _selectedTargetKind, value))
            {
                OnPropertyChanged(nameof(UsesAxisSource));
                OnPropertyChanged(nameof(ShowAxisPicker));
                OnPropertyChanged(nameof(ShowButtonPicker));
                OnPropertyChanged(nameof(SourceSummary));
            }
        }
    }

    /// <summary>True when the mapping reads a joystick axis (all targets except VR Button).</summary>
    public bool UsesAxisSource => SelectedTargetKind != MappingTargetKind.Button;

    public bool ShowAxisPicker => SelectedTargetKind == MappingTargetKind.AxisInput;

    public bool ShowButtonPicker => SelectedTargetKind == MappingTargetKind.Button;

    public IReadOnlyList<int> AxisTargets { get; } = Enumerable.Range(0, 16).ToArray();
    public IReadOnlyList<int> ButtonTargets { get; } = Enumerable.Range(0, 32).ToArray();
    public IReadOnlyList<VirtualTargetHand> HandTargets { get; } = new[] { VirtualTargetHand.Left, VirtualTargetHand.Right };

    public VirtualTargetHand TargetHand
    {
        get => _targetHand;
        set => SetProperty(ref _targetHand, value);
    }

    public bool IsListening
    {
        get => _isListening;
        set => SetProperty(ref _isListening, value);
    }

    public string SourceDeviceId
    {
        get => _sourceDeviceId;
        set
        {
            if (SetProperty(ref _sourceDeviceId, value))
            {
                OnPropertyChanged(nameof(SourceSummary));
            }
        }
    }

    public string SourceDeviceName
    {
        get => _sourceDeviceName;
        set
        {
            if (SetProperty(ref _sourceDeviceName, value))
            {
                OnPropertyChanged(nameof(SourceSummary));
            }
        }
    }

    public string SourceAxis
    {
        get => _sourceAxis;
        set
        {
            if (SetProperty(ref _sourceAxis, value))
            {
                OnPropertyChanged(nameof(SourceSummary));
            }
        }
    }

    public int SourceButtonIndex
    {
        get => _sourceButtonIndex;
        set
        {
            if (SetProperty(ref _sourceButtonIndex, value))
            {
                OnPropertyChanged(nameof(SourceSummary));
            }
        }
    }

    public int TargetAxisIndex
    {
        get => _targetAxisIndex;
        set => SetProperty(ref _targetAxisIndex, value);
    }

    public int TargetButtonIndex
    {
        get => _targetButtonIndex;
        set => SetProperty(ref _targetButtonIndex, value);
    }

    public double Deadzone
    {
        get => _deadzone;
        set
        {
            if (SetProperty(ref _deadzone, value))
            {
                RebuildCurvePlot();
            }
        }
    }

    public double Curve
    {
        get => _curve;
        set
        {
            if (SetProperty(ref _curve, value))
            {
                RebuildCurvePlot();
            }
        }
    }

    public double Saturation
    {
        get => _saturation;
        set
        {
            if (SetProperty(ref _saturation, value))
            {
                RebuildCurvePlot();
            }
        }
    }

    public bool Invert
    {
        get => _invert;
        set
        {
            if (SetProperty(ref _invert, value))
            {
                RebuildCurvePlot();
            }
        }
    }

    public double CurrentInputValue
    {
        get => _currentInputValue;
        private set => SetProperty(ref _currentInputValue, value);
    }

    public double CurrentOutputValue
    {
        get => _currentOutputValue;
        private set => SetProperty(ref _currentOutputValue, value);
    }

    public double CurrentInputPlotX
    {
        get => _currentInputPlotX;
        private set => SetProperty(ref _currentInputPlotX, value);
    }

    public double CurrentInputPlotY
    {
        get => _currentInputPlotY;
        private set => SetProperty(ref _currentInputPlotY, value);
    }

    public double CurrentOutputPlotX
    {
        get => _currentOutputPlotX;
        private set => SetProperty(ref _currentOutputPlotX, value);
    }

    public double CurrentOutputPlotY
    {
        get => _currentOutputPlotY;
        private set => SetProperty(ref _currentOutputPlotY, value);
    }

    public string CurvePlotPoints
    {
        get => _curvePlotPoints;
        private set => SetProperty(ref _curvePlotPoints, value);
    }

    public string SourceSummary => string.IsNullOrWhiteSpace(SourceDeviceName)
        ? "No source selected"
        : UsesAxisSource
            ? $"{SourceDeviceName} / Axis {SourceAxis}"
            : $"{SourceDeviceName} / Button {SourceButtonIndex}";

    public bool TryAutoDetectSource()
    {
        var state = _stateProvider();
        foreach (var device in state.Devices.Where(item => item.IsConnected))
        {
            var activeButtonIndex = device.Buttons.ToList().FindIndex(button => button);
            if (activeButtonIndex >= 0)
            {
                SourceDeviceId = device.DeviceId;
                SourceDeviceName = device.DeviceName;
                SelectedTargetKind = MappingTargetKind.Button;
                SourceButtonIndex = activeButtonIndex;
                UpdateLivePreview();
                return true;
            }

            var activeAxis = device.Axes.FirstOrDefault(axis => Math.Abs(axis.Value) > 0.6);
            if (!string.IsNullOrWhiteSpace(activeAxis.Key))
            {
                SourceDeviceId = device.DeviceId;
                SourceDeviceName = device.DeviceName;
                if (SelectedTargetKind == MappingTargetKind.Button)
                {
                    SelectedTargetKind = MappingTargetKind.AxisInput;
                }

                SourceAxis = activeAxis.Key;
                UpdateLivePreview();
                return true;
            }
        }

        return false;
    }

    public MappingEntry BuildResult()
    {
        if (string.IsNullOrWhiteSpace(SourceDeviceId) || string.IsNullOrWhiteSpace(SourceDeviceName))
        {
            throw new InvalidOperationException("No source input has been detected.");
        }

        var isAxis = SelectedTargetKind != MappingTargetKind.Button;
        return new MappingEntry
        {
            TargetKind = SelectedTargetKind,
            IsAxisMapping = isAxis,
            TargetHand = TargetHand,
            SourceDeviceId = SourceDeviceId,
            SourceDeviceName = SourceDeviceName,
            SourceAxis = SourceAxis,
            SourceButtonIndex = SourceButtonIndex,
            TargetAxisIndex = TargetAxisIndex,
            TargetButtonIndex = TargetButtonIndex,
            Deadzone = Deadzone,
            Curve = Curve,
            Saturation = Saturation,
            Invert = Invert
        };
    }

    public void UpdateLivePreview()
    {
        if (!UsesAxisSource)
        {
            return;
        }

        var state = _stateProvider();
        var device = state.Devices.FirstOrDefault(item => item.IsConnected && item.DeviceId.Equals(SourceDeviceId, StringComparison.OrdinalIgnoreCase));
        if (device is null || !device.Axes.TryGetValue(SourceAxis, out var input))
        {
            CurrentInputValue = 0;
            CurrentOutputValue = 0;
            CurrentInputPlotX = 100;
            CurrentInputPlotY = 100;
            CurrentOutputPlotX = 100;
            CurrentOutputPlotY = 100;
            return;
        }

        CurrentInputValue = input;
        var shaped = MappingEngine.MapAxisValue(input, Deadzone, Curve, 1.0, Invert);
        CurrentOutputValue = SelectedTargetKind == MappingTargetKind.AxisInput
            ? MappingEngine.MapAxisValue(input, Deadzone, Curve, Saturation, Invert)
            : shaped * Saturation;

        CurrentInputPlotX = ToPlotX(CurrentInputValue);
        CurrentInputPlotY = 100;
        CurrentOutputPlotX = ToPlotX(CurrentInputValue);
        CurrentOutputPlotY = ToPlotY(CurrentOutputValue);
    }

    private void RebuildCurvePlot()
    {
        var points = new List<string>();
        for (var step = 0; step <= 200; step++)
        {
            var input = step / 100.0 - 1.0;
            var output = MappingEngine.MapAxisValue(input, Deadzone, Curve, 1.0, Invert);
            points.Add($"{ToPlotX(input):F2},{ToPlotY(output):F2}");
        }

        CurvePlotPoints = string.Join(" ", points);
        UpdateLivePreview();
    }

    private static double ToPlotX(double value) => (Math.Clamp(value, -1.0, 1.0) + 1.0) * 100.0;
    private static double ToPlotY(double value) => (1.0 - Math.Clamp(value, -1.0, 1.0)) * 100.0;

    private static IReadOnlyList<TargetKindOption> BuildTargetKindOptions()
    {
        return new[]
        {
            new TargetKindOption("VR Axis (slot 0-15)", MappingTargetKind.AxisInput),
            new TargetKindOption("VR Button (0-31)", MappingTargetKind.Button),
            new TargetKindOption("Pose position X (m)", MappingTargetKind.PosePositionX),
            new TargetKindOption("Pose position Y (m)", MappingTargetKind.PosePositionY),
            new TargetKindOption("Pose position Z (m)", MappingTargetKind.PosePositionZ),
            new TargetKindOption("Orientation pitch X (deg)", MappingTargetKind.PoseOrientationX),
            new TargetKindOption("Orientation yaw Y (deg)", MappingTargetKind.PoseOrientationY),
            new TargetKindOption("Orientation roll Z (deg)", MappingTargetKind.PoseOrientationZ),
            new TargetKindOption("Linear velocity X (m/s)", MappingTargetKind.LinearVelocityX),
            new TargetKindOption("Linear velocity Y (m/s)", MappingTargetKind.LinearVelocityY),
            new TargetKindOption("Linear velocity Z (m/s)", MappingTargetKind.LinearVelocityZ),
            new TargetKindOption("Angular velocity X (rad/s)", MappingTargetKind.AngularVelocityX),
            new TargetKindOption("Angular velocity Y (rad/s)", MappingTargetKind.AngularVelocityY),
            new TargetKindOption("Angular velocity Z (rad/s)", MappingTargetKind.AngularVelocityZ)
        };
    }
}
