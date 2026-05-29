using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using VRCHOTAS.Models;
using VRCHOTAS.Services;

namespace VRCHOTAS.ViewModels;

public sealed partial class MappingEditorViewModel : ObservableObject
{
    private const double AxisDetectSpeedThreshold = 1.0;
    private const double DefaultDeadzone = 0.0;
    private const double DefaultCurve = 0.0;
    private const double DefaultSaturation = 1.0;

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
    private AxisRangeKind _axisRange = AxisRangeKind.Bidirectional;
    private VirtualAxisTarget _targetAxis = VirtualAxisTarget.ThumbstickX;
    private VirtualButtonTarget _targetButton = VirtualButtonTarget.ThumbstickClick;
    private ControllerPoseTarget _targetControllerPose = ControllerPoseTarget.PositionX;
    private ControllerPoseActionTarget _targetControllerPoseAction = ControllerPoseActionTarget.ResetPositionX;
    private double _deadzone;
    private double _curve;
    private double _saturation = 1.0;
    private double _saturationSliderMaximum = 5.0;
    private double _fullPressThreshold = 0.95;
    private bool _toggleMode;
    private bool _inputInvert;
    private bool _outputInvert;
    private string _description = string.Empty;
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
        AxisTargetOptions = BuildAxisTargetOptions();
        ButtonTargetOptions = BuildButtonTargetOptions();
        ControllerPoseTargetOptions = BuildControllerPoseTargetOptions();
        AxisActionTargetOptions = BuildAxisActionTargetOptions();

        if (existing is null)
        {
            RebuildCurvePlot();
            StartAutoDetect(clearDetectedSource: false);
            return;
        }

        SelectedTargetKind = existing.NormalizedTargetKind;
        SourceDeviceId = existing.SourceDeviceId;
        SourceDeviceName = existing.SourceDeviceName;
        HasDetectedSource = !string.IsNullOrWhiteSpace(existing.SourceDeviceId) && !string.IsNullOrWhiteSpace(existing.SourceDeviceName);
        IsSourceButtonDetected = !existing.IsAxisMapping;
        SourceAxis = existing.SourceAxis;
        SourceButtonIndex = existing.SourceButtonIndex;
        TargetHand = existing.TargetHand;
        AxisRange = existing.AxisRange;
        TargetAxis = existing.TargetAxis;
        TargetButton = existing.TargetButton;
        TargetControllerPose = existing.ResolvedControllerPoseTarget;
        TargetControllerPoseAction = existing.TargetControllerPoseAction;
        FullPressThreshold = existing.FullPressThreshold;
        ToggleMode = existing.ToggleMode;
        Deadzone = existing.Deadzone;
        Curve = existing.Curve;
        Saturation = existing.Saturation;
        InputInvert = existing.InputInvert;
        OutputInvert = existing.Invert;
        Description = existing.Description ?? string.Empty;

        RebuildCurvePlot();
    }

    public IReadOnlyList<TargetKindOption> TargetKindOptions { get; }
    public IReadOnlyList<AxisTargetOption> AxisTargetOptions { get; }
    public IReadOnlyList<ButtonTargetOption> ButtonTargetOptions { get; }
    public IReadOnlyList<ControllerPoseTargetOption> ControllerPoseTargetOptions { get; }
    public IReadOnlyList<AxisActionTargetOption> AxisActionTargetOptions { get; }
    public IReadOnlyList<TargetKindOption> AvailableTargetKindOptions =>
        IsSourceButtonDetected
            ? TargetKindOptions
            : TargetKindOptions.Where(option => option.Kind is not (MappingTargetKind.Button or MappingTargetKind.ControllerPoseAction)).ToArray();

    public MappingTargetKind SelectedTargetKind
    {
        get => _selectedTargetKind;
        set
        {
            if (_hasDetectedSource && !_isSourceButtonDetected && value is MappingTargetKind.Button or MappingTargetKind.ControllerPoseAction)
            {
                return;
            }

            if (SetProperty(ref _selectedTargetKind, value))
            {
                OnPropertyChanged(nameof(UsesAxisSource));
                OnPropertyChanged(nameof(ShowAxisPicker));
                OnPropertyChanged(nameof(ShowButtonPicker));
                OnPropertyChanged(nameof(ShowControllerPosePicker));
                OnPropertyChanged(nameof(ShowAxisActionPicker));
                OnPropertyChanged(nameof(ShowToggleMode));
                OnPropertyChanged(nameof(ShowFullPressThreshold));
                OnPropertyChanged(nameof(SourceSummary));

                if (value == MappingTargetKind.AxisInput)
                {
                    SyncAxisRangeWithTarget();
                }
            }
        }
    }

    /// <summary>True when the current target uses continuous shaping controls.</summary>
    public bool UsesAxisSource => SelectedTargetKind is not (MappingTargetKind.Button or MappingTargetKind.ControllerPoseAction);

    public bool ShowAxisPicker => SelectedTargetKind == MappingTargetKind.AxisInput;

    public bool ShowButtonPicker => SelectedTargetKind == MappingTargetKind.Button;

    public bool ShowControllerPosePicker => SelectedTargetKind == MappingTargetKind.ControllerPose;

    public bool ShowAxisActionPicker => SelectedTargetKind == MappingTargetKind.ControllerPoseAction;

    public bool ShowToggleMode => HasDetectedSource && IsSourceButtonDetected && SelectedTargetKind != MappingTargetKind.ControllerPoseAction;

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
                OnPropertyChanged(nameof(ShowToggleMode));
                OnPropertyChanged(nameof(SourceDetectionInstruction));
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
                if (!value)
                {
                    ToggleMode = false;
                }

                OnPropertyChanged(nameof(SourceSummary));
                OnPropertyChanged(nameof(AvailableTargetKindOptions));
                OnPropertyChanged(nameof(ShowControllerPosePicker));
                OnPropertyChanged(nameof(ShowAxisActionPicker));
                OnPropertyChanged(nameof(ShowToggleMode));
            }
        }
    }

    public bool IsListening
    {
        get => _isListening;
        set
        {
            if (SetProperty(ref _isListening, value))
            {
                OnPropertyChanged(nameof(SourceDetectionInstruction));
            }
        }
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

    public AxisRangeKind AxisRange
    {
        get => _axisRange;
        set
        {
            if (SetProperty(ref _axisRange, value))
            {
                OnPropertyChanged(nameof(PlotYMinLabel));
                RebuildCurvePlot();
            }
        }
    }

    public VirtualAxisTarget TargetAxis
    {
        get => _targetAxis;
        set
        {
            if (SetProperty(ref _targetAxis, value) && SelectedTargetKind == MappingTargetKind.AxisInput)
            {
                SyncAxisRangeWithTarget();
                ResetAxisShapingParameters();
            }
        }
    }

    public VirtualButtonTarget TargetButton
    {
        get => _targetButton;
        set => SetProperty(ref _targetButton, value);
    }

    public ControllerPoseTarget TargetControllerPose
    {
        get => _targetControllerPose;
        set => SetProperty(ref _targetControllerPose, value);
    }

    public ControllerPoseActionTarget TargetControllerPoseAction
    {
        get => _targetControllerPoseAction;
        set => SetProperty(ref _targetControllerPoseAction, value);
    }

    public double FullPressThreshold
    {
        get => _fullPressThreshold;
        set => SetProperty(ref _fullPressThreshold, Math.Clamp(value, 0.0, 1.0));
    }

    public bool ToggleMode
    {
        get => _toggleMode;
        set => SetProperty(ref _toggleMode, value && IsSourceButtonDetected);
    }

    public bool ShowFullPressThreshold => SelectedTargetKind == MappingTargetKind.AxisInput
        && TargetAxis is VirtualAxisTarget.Trigger or VirtualAxisTarget.Grip;

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
            var normalizedValue = Math.Max(0.0, value);
            EnsureSaturationSliderMaximum(normalizedValue);

            if (SetProperty(ref _saturation, normalizedValue))
            {
                RebuildCurvePlot();
            }
        }
    }

    public double SaturationSliderMaximum
    {
        get => _saturationSliderMaximum;
        private set => SetProperty(ref _saturationSliderMaximum, value);
    }

    public bool InputInvert
    {
        get => _inputInvert;
        set
        {
            if (SetProperty(ref _inputInvert, value))
            {
                RebuildCurvePlot();
            }
        }
    }

    public bool OutputInvert
    {
        get => _outputInvert;
        set
        {
            if (SetProperty(ref _outputInvert, value))
            {
                RebuildCurvePlot();
            }
        }
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value ?? string.Empty);
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

    public string PlotYMaxLabel => Saturation.ToString("F2");

    public string PlotYMinLabel => AxisRange == AxisRangeKind.Unidirectional ? "0.00" : (-Saturation).ToString("F2");

    public string CurvePlotPoints
    {
        get => _curvePlotPoints;
        private set => SetProperty(ref _curvePlotPoints, value);
    }

    public string SourceSummary => string.IsNullOrWhiteSpace(SourceDeviceName)
        ? "No source detected"
        : IsSourceButtonDetected
            ? $"{SourceDeviceName} / Button {SourceButtonIndex + 1}"
            : $"{SourceDeviceName} / Axis {SourceAxis}";

    public string SourceDetectionInstruction => HasDetectedSource
        ? "Use Reset button to listen for a new input."
        : "Change a button state or move an axis quickly on the device to select input source.";

    public bool CanEditTarget => HasDetectedSource;

    public string GetNumericEditorText(string fieldName)
    {
        return GetNumericFieldValue(fieldName).ToString(GetNumericFieldFormat(fieldName), CultureInfo.CurrentCulture);
    }

    public bool TrySetNumericEditorValue(string fieldName, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var value))
        {
            return false;
        }

        var (min, max) = GetNumericFieldRange(fieldName);
        if (value < min || value > max)
        {
            return false;
        }

        switch (fieldName)
        {
            case nameof(FullPressThreshold):
                FullPressThreshold = value;
                return true;
            case nameof(Deadzone):
                Deadzone = value;
                return true;
            case nameof(Curve):
                Curve = value;
                return true;
            case nameof(Saturation):
                Saturation = value;
                return true;
            default:
                return false;
        }
    }

    private double GetNumericFieldValue(string fieldName) => fieldName switch
    {
        nameof(FullPressThreshold) => FullPressThreshold,
        nameof(Deadzone) => Deadzone,
        nameof(Curve) => Curve,
        nameof(Saturation) => Saturation,
        _ => 0.0
    };

    private static string GetNumericFieldFormat(string fieldName) => fieldName switch
    {
        nameof(Deadzone) or nameof(Curve) or nameof(Saturation) => "F3",
        _ => "F2"
    };

    private (double Min, double Max) GetNumericFieldRange(string fieldName) => fieldName switch
    {
        nameof(FullPressThreshold) => (0.0, 1.0),
        nameof(Deadzone) => (0.0, 0.8),
        nameof(Curve) => (-1.0, 1.0),
        nameof(Saturation) => (0.0, double.MaxValue),
        _ => (double.NaN, double.NaN)
    };

    private void EnsureSaturationSliderMaximum(double value)
    {
        var nextMaximum = Math.Max(SaturationSliderMaximum, Math.Max(5.0, value));
        if (nextMaximum > SaturationSliderMaximum)
        {
            SaturationSliderMaximum = nextMaximum;
        }
    }

    private void SyncAxisRangeWithTarget()
    {
        AxisRange = TargetAxis is VirtualAxisTarget.Trigger or VirtualAxisTarget.Grip
            ? AxisRangeKind.Unidirectional
            : AxisRangeKind.Bidirectional;
    }
}
