using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;

namespace VRCHOTAS.Models;

public sealed partial class MappingEntry : ObservableObject
{
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isSourceDeviceConnected;

    /// <summary>Runtime-only: when true, this mapping is skipped until toggled back (not saved to configuration).</summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isTemporarilyDisabled;

    [ObservableProperty]
    [property: JsonIgnore]
    private MediaBrush _sourceDuplicateBackground = MediaBrushes.Transparent;

    [ObservableProperty]
    [property: JsonIgnore]
    private MediaBrush _targetDuplicateBackground = MediaBrushes.Transparent;

    /// <summary>
    /// When null (legacy configs), derived from <see cref="IsAxisMapping"/>.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public MappingTargetKind? TargetKind { get; set; }

    public bool IsAxisMapping { get; set; } = true;
    public VirtualTargetHand TargetHand { get; set; } = VirtualTargetHand.Left;
    public string SourceDeviceId { get; set; } = string.Empty;
    public string SourceDeviceName { get; set; } = string.Empty;
    public string SourceAxis { get; set; } = "X";
    public int SourceButtonIndex { get; set; }
    public AxisRangeKind AxisRange { get; set; } = AxisRangeKind.Bidirectional;
    public VirtualAxisTarget TargetAxis { get; set; } = VirtualAxisTarget.ThumbstickX;
    public VirtualButtonTarget TargetButton { get; set; } = VirtualButtonTarget.ThumbstickClick;
    public ControllerPoseTarget TargetControllerPose { get; set; } = ControllerPoseTarget.PositionX;
    public ControllerPoseActionTarget TargetControllerPoseAction { get; set; } = ControllerPoseActionTarget.ResetPositionX;
    public double FullPressThreshold { get; set; } = 0.95;
    public bool ToggleMode { get; set; }
    public bool TriggerInvert { get; set; }
    public bool ThresholdBidirectional { get; set; } = true;
    public double Deadzone { get; set; }
    public double Curve { get; set; }
    public double Saturation { get; set; } = 1.0;
    public bool InputInvert { get; set; }
    public bool Invert { get; set; }

    /// <summary>Virtual-key code for Keyboard target mappings.</summary>
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int KeyboardKey { get; set; }

    /// <summary>Modifier bitmask for Keyboard target mappings (0=None, 1=Ctrl, 2=Shift, 4=Alt).</summary>
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int KeyboardModifiers { get; set; }

    /// <summary>Optional window title substring to target for keyboard events.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? KeyboardTargetWindowTitle { get; set; }

    /// <summary>Optional process name to target for keyboard events.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? KeyboardTargetProcessName { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? Description { get; set; }

    [JsonIgnore]
    public MappingTargetKind ResolvedTargetKind =>
        TargetKind ?? (IsAxisMapping ? MappingTargetKind.AxisInput : MappingTargetKind.Button);

    [JsonIgnore]
    public MappingTargetKind NormalizedTargetKind => ResolvedTargetKind == MappingTargetKind.AxisAction
        ? MappingTargetKind.ControllerPoseAction
        : IsLegacyControllerPoseKind(ResolvedTargetKind)
            ? MappingTargetKind.ControllerPose
            : ResolvedTargetKind;

    [JsonIgnore]
    public ControllerPoseTarget ResolvedControllerPoseTarget => IsLegacyControllerPoseKind(ResolvedTargetKind)
        ? MapLegacyControllerPoseTarget(ResolvedTargetKind)
        : TargetControllerPose;

    [JsonIgnore]
    public string SourceControlDisplay => IsAxisMapping ? $"Axis {SourceAxis}" : $"Button {SourceButtonIndex + 1}";

    [JsonIgnore]
    public string SourceGroupingKey => IsAxisMapping
        ? $"{SourceDeviceId}|Axis|{SourceAxis}"
        : $"{SourceDeviceId}|Button|{SourceButtonIndex}";

    [JsonIgnore]
    public string TargetControlDisplay => MappingDisplayHelper.GetTargetControlDisplay(this);

    [JsonIgnore]
    public string TargetGroupingKey => NormalizedTargetKind switch
    {
        MappingTargetKind.AxisInput => $"{TargetHand}|Axis|{TargetAxis}",
        MappingTargetKind.Button => $"{TargetHand}|Button|{TargetButton}",
        MappingTargetKind.ControllerPose => $"{TargetHand}|ControllerPose|{ResolvedControllerPoseTarget}",
        MappingTargetKind.ControllerPoseAction => $"{TargetHand}|ControllerPoseAction|{TargetControllerPoseAction}",
        MappingTargetKind.Keyboard => $"Keyboard|{KeyboardKey}|{KeyboardModifiers}",
        _ => $"{TargetHand}|{NormalizedTargetKind}"
    };

    [JsonIgnore]
    public string SourceDisplay => IsAxisMapping ? $"{SourceDeviceName} / Axis {SourceAxis}" : $"{SourceDeviceName} / Button {SourceButtonIndex + 1}";

    [JsonIgnore]
    public string TargetDisplay
    {
        get
        {
            if (NormalizedTargetKind == MappingTargetKind.Keyboard)
            {
                return $"Keyboard / {TargetControlDisplay}";
            }

            var hand = TargetHand == VirtualTargetHand.Right ? "Right" : "Left";
            return $"{hand} / {TargetControlDisplay}";
        }
    }

    [JsonIgnore]
    public string MappingTypeDisplay
    {
        get
        {
            var sourceType = IsAxisMapping ? "Axis" : "Button";
            var targetType = MappingDisplayHelper.GetTargetTypeLabel(NormalizedTargetKind);
            var toggleSuffix = ToggleMode ? " Toggled" : string.Empty;
            return $"{sourceType} -> {targetType}{toggleSuffix}";
        }
    }

    public static string GetTargetTypeLabel(MappingTargetKind targetKind) =>
        MappingDisplayHelper.GetTargetTypeLabel(targetKind);

    private static bool IsLegacyControllerPoseKind(MappingTargetKind targetKind) =>
        targetKind is >= MappingTargetKind.PosePositionX and <= MappingTargetKind.AngularVelocityZ;

    private static ControllerPoseTarget MapLegacyControllerPoseTarget(MappingTargetKind targetKind) => targetKind switch
    {
        MappingTargetKind.PosePositionX => ControllerPoseTarget.PositionX,
        MappingTargetKind.PosePositionY => ControllerPoseTarget.PositionY,
        MappingTargetKind.PosePositionZ => ControllerPoseTarget.PositionZ,
        MappingTargetKind.PoseOrientationX => ControllerPoseTarget.OrientationPitch,
        MappingTargetKind.PoseOrientationY => ControllerPoseTarget.OrientationYaw,
        MappingTargetKind.PoseOrientationZ => ControllerPoseTarget.OrientationRoll,
        MappingTargetKind.LinearVelocityX => ControllerPoseTarget.LinearVelocityX,
        MappingTargetKind.LinearVelocityY => ControllerPoseTarget.LinearVelocityY,
        MappingTargetKind.LinearVelocityZ => ControllerPoseTarget.LinearVelocityZ,
        MappingTargetKind.AngularVelocityX => ControllerPoseTarget.AngularVelocityX,
        MappingTargetKind.AngularVelocityY => ControllerPoseTarget.AngularVelocityY,
        MappingTargetKind.AngularVelocityZ => ControllerPoseTarget.AngularVelocityZ,
        _ => ControllerPoseTarget.PositionX
    };
}

public sealed class AppConfiguration
{
    public List<MappingEntry> Mappings { get; set; } = new();
}
