using CommunityToolkit.Mvvm.ComponentModel;
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
}
