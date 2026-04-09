using CommunityToolkit.Mvvm.ComponentModel;
using VRCHOTAS.Models;
using VRCHOTAS.Services;

namespace VRCHOTAS.ViewModels;

public sealed class MappingEditorViewModel : ObservableObject
{
    private const double AxisDetectSpeedThreshold = 1.0;
    private const double DefaultDeadzone = 0.0;
    private const double DefaultCurve = 0.0;
    private const double DefaultSaturation = 1.0;

    private sealed class DetectionSnapshot
    {
        public required bool[] Buttons { get; init; }
        public required Dictionary<string, double> Axes { get; init; }
    }

    private readonly Func<RawJoystickState> _stateProvider;
    private readonly Dictionary<string, DetectionSnapshot> _detectionSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastDetectionSampleUtc;
    private bool _hasDetectedSource;
    private bool _isSourceButtonDetected;
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
    private double _plotYRangeMax = 1.0;
    private string _curvePlotPoints = string.Empty;

    public MappingEditorViewModel(Func<RawJoystickState> stateProvider, MappingEntry? existing)
    {
        _stateProvider = stateProvider;
        TargetKindOptions = BuildTargetKindOptions();

        if (existing is null)
        {
            RebuildCurvePlot();
            StartAutoDetect(clearDetectedSource: false);
            return;
        }

        SelectedTargetKind = existing.ResolvedTargetKind;
        SourceDeviceId = existing.SourceDeviceId;
        SourceDeviceName = existing.SourceDeviceName;
        HasDetectedSource = !string.IsNullOrWhiteSpace(existing.SourceDeviceId) && !string.IsNullOrWhiteSpace(existing.SourceDeviceName);
        IsSourceButtonDetected = !existing.IsAxisMapping;
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
        StartAutoDetect(clearDetectedSource: false);
    }

    public IReadOnlyList<TargetKindOption> TargetKindOptions { get; }

    public IReadOnlyList<TargetKindOption> AvailableTargetKindOptions =>
        IsSourceButtonDetected
            ? TargetKindOptions
            : TargetKindOptions.Where(option => option.Kind != MappingTargetKind.Button).ToArray();

    public MappingTargetKind SelectedTargetKind
    {
        get => _selectedTargetKind;
        set
        {
            if (_hasDetectedSource && !_isSourceButtonDetected && value == MappingTargetKind.Button)
            {
                return;
            }

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

    public bool HasDetectedSource
    {
        get => _hasDetectedSource;
        private set
        {
            if (SetProperty(ref _hasDetectedSource, value))
            {
                OnPropertyChanged(nameof(CanEditTarget));
            }
        }
    }

    public bool IsSourceButtonDetected
    {
        get => _isSourceButtonDetected;
        private set
        {
            if (SetProperty(ref _isSourceButtonDetected, value))
            {
                OnPropertyChanged(nameof(SourceSummary));
                OnPropertyChanged(nameof(AvailableTargetKindOptions));
            }
        }
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
        set
        {
            if (SetProperty(ref _targetAxisIndex, value) && SelectedTargetKind == MappingTargetKind.AxisInput)
            {
                ResetAxisShapingParameters();
            }
        }
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

    public double PlotYRangeMax
    {
        get => _plotYRangeMax;
        private set
        {
            if (SetProperty(ref _plotYRangeMax, value))
            {
                OnPropertyChanged(nameof(PlotYMaxLabel));
                OnPropertyChanged(nameof(PlotYMinLabel));
            }
        }
    }

    public string PlotYMaxLabel => PlotYRangeMax.ToString("F2");

    public string PlotYMinLabel => (-PlotYRangeMax).ToString("F2");

    public string CurvePlotPoints
    {
        get => _curvePlotPoints;
        private set => SetProperty(ref _curvePlotPoints, value);
    }

    public string SourceSummary => string.IsNullOrWhiteSpace(SourceDeviceName)
        ? "No source detected"
        : IsSourceButtonDetected
            ? $"{SourceDeviceName} / Button {SourceButtonIndex}"
            : $"{SourceDeviceName} / Axis {SourceAxis}";

    public bool CanEditTarget => HasDetectedSource;

    public void StartAutoDetect(bool clearDetectedSource)
    {
        if (clearDetectedSource)
        {
            HasDetectedSource = false;
            IsSourceButtonDetected = false;
            SourceDeviceId = string.Empty;
            SourceDeviceName = string.Empty;
            SourceAxis = "X";
            SourceButtonIndex = 0;
        }

        CaptureDetectionBaseline();
        IsListening = true;
        UpdateLivePreview();
    }

    public bool TryAutoDetectSource()
    {
        if (!IsListening)
        {
            return false;
        }

        var state = _stateProvider();
        var now = DateTime.UtcNow;
        var elapsedSeconds = (now - _lastDetectionSampleUtc).TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            RefreshDetectionSnapshots(state);
            _lastDetectionSampleUtc = now;
            return false;
        }

        foreach (var device in state.Devices.Where(item => item.IsConnected))
        {
            if (!_detectionSnapshots.TryGetValue(device.DeviceId, out var previous))
            {
                continue;
            }

            var buttonCount = Math.Min(previous.Buttons.Length, device.Buttons.Count);
            for (var index = 0; index < buttonCount; index++)
            {
                if (previous.Buttons[index] == device.Buttons[index])
                {
                    continue;
                }

                SourceDeviceId = device.DeviceId;
                SourceDeviceName = device.DeviceName;
                HasDetectedSource = true;
                IsSourceButtonDetected = true;
                SourceButtonIndex = index;
                UpdateLivePreview();
                return true;
            }

            foreach (var axis in device.Axes)
            {
                if (!previous.Axes.TryGetValue(axis.Key, out var previousValue))
                {
                    continue;
                }

                var speed = Math.Abs(axis.Value - previousValue) / elapsedSeconds;
                if (speed <= AxisDetectSpeedThreshold)
                {
                    continue;
                }

                SourceDeviceId = device.DeviceId;
                SourceDeviceName = device.DeviceName;
                HasDetectedSource = true;
                IsSourceButtonDetected = false;
                if (SelectedTargetKind == MappingTargetKind.Button)
                {
                    SelectedTargetKind = MappingTargetKind.AxisInput;
                }

                SourceAxis = axis.Key;
                UpdateLivePreview();
                return true;
            }
        }

        RefreshDetectionSnapshots(state);
        _lastDetectionSampleUtc = now;

        return false;
    }

    public MappingEntry BuildResult()
    {
        if (!HasDetectedSource || string.IsNullOrWhiteSpace(SourceDeviceId) || string.IsNullOrWhiteSpace(SourceDeviceName))
        {
            throw new InvalidOperationException("No source input has been detected.");
        }

        if (!IsSourceButtonDetected && SelectedTargetKind == MappingTargetKind.Button)
        {
            throw new InvalidOperationException("Axis source cannot be mapped to a button target.");
        }

        var isAxis = !IsSourceButtonDetected;
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
        PlotYRangeMax = ResolvePlotYRangeMax();

        if (!UsesAxisSource)
        {
            return;
        }

        var state = _stateProvider();
        var device = state.Devices.FirstOrDefault(item => item.IsConnected && item.DeviceId.Equals(SourceDeviceId, StringComparison.OrdinalIgnoreCase));
        if (!TryGetPreviewInput(device, out var input))
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
        CurrentOutputValue = ComputeMappedOutput(input);

        CurrentInputPlotX = ToPlotX(CurrentInputValue);
        CurrentInputPlotY = 100;
        CurrentOutputPlotX = ToPlotX(CurrentInputValue);
        CurrentOutputPlotY = ToPlotY(CurrentOutputValue, PlotYRangeMax);
    }

    private void RebuildCurvePlot()
    {
        PlotYRangeMax = ResolvePlotYRangeMax();
        var points = new List<string>();
        for (var step = 0; step <= 200; step++)
        {
            var input = step / 100.0 - 1.0;
            var output = ComputeMappedOutput(input);
            points.Add($"{ToPlotX(input):F2},{ToPlotY(output, PlotYRangeMax):F2}");
        }

        CurvePlotPoints = string.Join(" ", points);
        UpdateLivePreview();
    }

    private double ComputeMappedOutput(double input)
    {
        var shaped = MappingEngine.MapAxisValue(input, Deadzone, Curve, 1.0, Invert);
        return SelectedTargetKind == MappingTargetKind.AxisInput
            ? MappingEngine.MapAxisValue(input, Deadzone, Curve, Saturation, Invert)
            : shaped * Math.Clamp(Saturation, 0.0, 5.0);
    }

    private double ResolvePlotYRangeMax() => Math.Max(1.0, Math.Clamp(Saturation, 0.0, 5.0));

    private bool TryGetPreviewInput(JoystickDeviceState? device, out double input)
    {
        input = 0;
        if (device is null)
        {
            return false;
        }

        if (IsSourceButtonDetected)
        {
            if (SourceButtonIndex < 0 || SourceButtonIndex >= device.Buttons.Count)
            {
                return false;
            }

            input = device.Buttons[SourceButtonIndex] ? 1.0 : 0.0;
            return true;
        }

        return device.Axes.TryGetValue(SourceAxis, out input);
    }

    private void ResetAxisShapingParameters()
    {
        Deadzone = DefaultDeadzone;
        Curve = DefaultCurve;
        Saturation = DefaultSaturation;
    }

    private static double ToPlotX(double value) => (Math.Clamp(value, -1.0, 1.0) + 1.0) * 100.0;
    private static double ToPlotY(double value, double range) => (1.0 - (Math.Clamp(value, -range, range) / Math.Max(range, 0.0001))) * 100.0;

    private void CaptureDetectionBaseline()
    {
        var state = _stateProvider();
        _detectionSnapshots.Clear();
        RefreshDetectionSnapshots(state);
        _lastDetectionSampleUtc = DateTime.UtcNow;
    }

    private void RefreshDetectionSnapshots(RawJoystickState state)
    {
        var connectedIds = state.Devices.Where(item => item.IsConnected).Select(item => item.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var removedId in _detectionSnapshots.Keys.Where(id => !connectedIds.Contains(id)).ToList())
        {
            _detectionSnapshots.Remove(removedId);
        }

        foreach (var device in state.Devices.Where(item => item.IsConnected))
        {
            _detectionSnapshots[device.DeviceId] = new DetectionSnapshot
            {
                Buttons = device.Buttons.ToArray(),
                Axes = device.Axes.ToDictionary(axis => axis.Key, axis => axis.Value, StringComparer.OrdinalIgnoreCase)
            };
        }
    }

    private static IReadOnlyList<TargetKindOption> BuildTargetKindOptions()
    {
        return new[]
        {
            new TargetKindOption("VR Axis (slot 0-15)", MappingTargetKind.AxisInput),
            new TargetKindOption("VR Button (0-31)", MappingTargetKind.Button),
            new TargetKindOption("Pose position X (m)", MappingTargetKind.PosePositionX),
            new TargetKindOption("Pose position Y (m)", MappingTargetKind.PosePositionY),
            new TargetKindOption("Pose position Z (m)", MappingTargetKind.PosePositionZ),
            new TargetKindOption("Orientation pitch X (rotation)", MappingTargetKind.PoseOrientationX),
            new TargetKindOption("Orientation yaw Y (rotation)", MappingTargetKind.PoseOrientationY),
            new TargetKindOption("Orientation roll Z (rotation)", MappingTargetKind.PoseOrientationZ),
            new TargetKindOption("Linear velocity X (m/s)", MappingTargetKind.LinearVelocityX),
            new TargetKindOption("Linear velocity Y (m/s)", MappingTargetKind.LinearVelocityY),
            new TargetKindOption("Linear velocity Z (m/s)", MappingTargetKind.LinearVelocityZ),
            new TargetKindOption("Angular velocity X (rad/s)", MappingTargetKind.AngularVelocityX),
            new TargetKindOption("Angular velocity Y (rad/s)", MappingTargetKind.AngularVelocityY),
            new TargetKindOption("Angular velocity Z (rad/s)", MappingTargetKind.AngularVelocityZ)
        };
    }
}
