using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;

namespace VRCHOTAS.Models;

public enum VirtualTargetHand
{
    Left = 0,
    Right = 1
}

public enum AxisRangeKind
{
    Bidirectional = 0,
    Unidirectional = 1
}

public enum VirtualAxisTarget
{
    ThumbstickX = 0,
    ThumbstickY = 1,
    Trigger = 2,
    Grip = 3
}

public enum VirtualButtonTarget
{
    ThumbstickClick = 0,
    PrimaryFaceButton = 1,
    SecondaryFaceButton = 2,
    System = 3,
    RecenterHand = 4
}

public enum ControllerPoseTarget
{
    PositionX = 0,
    PositionY = 1,
    PositionZ = 2,
    OrientationPitch = 3,
    OrientationYaw = 4,
    OrientationRoll = 5,
    LinearVelocityX = 6,
    LinearVelocityY = 7,
    LinearVelocityZ = 8,
    AngularVelocityX = 9,
    AngularVelocityY = 10,
    AngularVelocityZ = 11
}

public enum ControllerPoseActionTarget
{
    ResetPositionX = 0,
    ResetPositionY = 1,
    ResetPositionZ = 2,
    ResetOrientPitch = 3,
    ResetOrientRoll = 4,
    ResetOrientYaw = 5,
    ResetHand = 6
}

public enum MappingTargetKind
{
    AxisInput = 0,
    Button = 1,
    PosePositionX = 2,
    PosePositionY = 3,
    PosePositionZ = 4,
    /// <summary>Pitch about +X axis (rotation).</summary>
    PoseOrientationX = 5,
    /// <summary>Yaw about +Y axis (rotation).</summary>
    PoseOrientationY = 6,
    /// <summary>Roll about +Z axis (rotation).</summary>
    PoseOrientationZ = 7,
    LinearVelocityX = 8,
    LinearVelocityY = 9,
    LinearVelocityZ = 10,
    /// <summary>Angular velocity about +X (rad/s).</summary>
    AngularVelocityX = 11,
    /// <summary>Angular velocity about +Y (rad/s).</summary>
    AngularVelocityY = 12,
    /// <summary>Angular velocity about +Z (rad/s).</summary>
    AngularVelocityZ = 13,
    AxisAction = 14,
    ControllerPose = 15,
    ControllerPoseAction = 16
}

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
    public double Deadzone { get; set; }
    public double Curve { get; set; }
    public double Saturation { get; set; } = 1.0;
    public bool Invert { get; set; }

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
    public string TargetControlDisplay
    {
        get
        {
            var k = NormalizedTargetKind;
            return k switch
            {
                MappingTargetKind.AxisInput => GetAxisTargetDisplay(),
                MappingTargetKind.Button => GetButtonTargetDisplay(),
                MappingTargetKind.ControllerPose => GetControllerPoseDisplay(),
                MappingTargetKind.ControllerPoseAction => GetControllerPoseActionDisplay(),
                _ => k.ToString()
            };
        }
    }

    [JsonIgnore]
    public string TargetGroupingKey => NormalizedTargetKind switch
    {
        MappingTargetKind.AxisInput => $"{TargetHand}|Axis|{TargetAxis}",
        MappingTargetKind.Button => $"{TargetHand}|Button|{TargetButton}",
        MappingTargetKind.ControllerPose => $"{TargetHand}|ControllerPose|{ResolvedControllerPoseTarget}",
        MappingTargetKind.ControllerPoseAction => $"{TargetHand}|ControllerPoseAction|{TargetControllerPoseAction}",
        _ => $"{TargetHand}|{NormalizedTargetKind}"
    };

    [JsonIgnore]
    public string SourceDisplay => IsAxisMapping ? $"{SourceDeviceName} / Axis {SourceAxis}" : $"{SourceDeviceName} / Button {SourceButtonIndex + 1}";

    [JsonIgnore]
    public string TargetDisplay
    {
        get
        {
            var hand = TargetHand == VirtualTargetHand.Right ? "Right" : "Left";
            var k = NormalizedTargetKind;
            return $"{hand} / {TargetControlDisplay}";
        }
    }

    [JsonIgnore]
    public string MappingTypeDisplay
    {
        get
        {
            var sourceType = IsAxisMapping ? "Axis" : "Button";
            var targetType = GetTargetKindDisplayName();
            var inverted = IsTargetAxisType() && Invert ? " Inverted" : "";
            return $"{sourceType} ¡ú {targetType}{inverted}";
        }
    }

    private string GetTargetKindDisplayName()
    {
        return NormalizedTargetKind switch
        {
            MappingTargetKind.AxisInput => "VR Axis",
            MappingTargetKind.Button => "VR Button",
            MappingTargetKind.ControllerPose => "Controller Pose",
            MappingTargetKind.ControllerPoseAction => "Controller Pose Action",
            _ => NormalizedTargetKind.ToString()
        };
    }

    private bool IsTargetAxisType()
    {
        return NormalizedTargetKind is MappingTargetKind.AxisInput or MappingTargetKind.ControllerPose;
    }

    private string GetAxisTargetDisplay()
    {
        return TargetAxis switch
        {
            VirtualAxisTarget.ThumbstickX => "Thumbstick X",
            VirtualAxisTarget.ThumbstickY => "Thumbstick Y",
            VirtualAxisTarget.Trigger => "Trigger",
            VirtualAxisTarget.Grip => "Grip",
            _ => TargetAxis.ToString()
        };
    }

    private string GetButtonTargetDisplay()
    {
        return TargetButton switch
        {
            VirtualButtonTarget.ThumbstickClick => "Thumbstick Click",
            VirtualButtonTarget.PrimaryFaceButton => TargetHand == VirtualTargetHand.Right ? "A Button" : "X Button",
            VirtualButtonTarget.SecondaryFaceButton => TargetHand == VirtualTargetHand.Right ? "B Button" : "Y Button",
            VirtualButtonTarget.System => "System Button",
            VirtualButtonTarget.RecenterHand => "Recenter Hand",
            _ => TargetButton.ToString()
        };
    }

    private string GetControllerPoseDisplay()
    {
        return ResolvedControllerPoseTarget switch
        {
            ControllerPoseTarget.PositionX => "Pose position X (m)",
            ControllerPoseTarget.PositionY => "Pose position Y (m)",
            ControllerPoseTarget.PositionZ => "Pose position Z (m)",
            ControllerPoseTarget.OrientationPitch => "Orient Pitch (rotation)",
            ControllerPoseTarget.OrientationYaw => "Orient Yaw (rotation)",
            ControllerPoseTarget.OrientationRoll => "Orient Roll (rotation)",
            ControllerPoseTarget.LinearVelocityX => "Linear velocity X (m/s)",
            ControllerPoseTarget.LinearVelocityY => "Linear velocity Y (m/s)",
            ControllerPoseTarget.LinearVelocityZ => "Linear velocity Z (m/s)",
            ControllerPoseTarget.AngularVelocityX => "Angular Velocity Pitch (rad/s)",
            ControllerPoseTarget.AngularVelocityY => "Angular Velocity Yaw (rad/s)",
            ControllerPoseTarget.AngularVelocityZ => "Angular Velocity Roll (rad/s)",
            _ => ResolvedControllerPoseTarget.ToString()
        };
    }

    private string GetControllerPoseActionDisplay()
    {
        return TargetControllerPoseAction switch
        {
            ControllerPoseActionTarget.ResetPositionX => "Reset Pos X",
            ControllerPoseActionTarget.ResetPositionY => "Reset Pos Y",
            ControllerPoseActionTarget.ResetPositionZ => "Reset Pos Z",
            ControllerPoseActionTarget.ResetOrientPitch => "Reset Orient Pitch",
            ControllerPoseActionTarget.ResetOrientRoll => "Reset Orient Roll",
            ControllerPoseActionTarget.ResetOrientYaw => "Reset Orient Yaw",
            ControllerPoseActionTarget.ResetHand => "Reset Hand",
            _ => TargetControllerPoseAction.ToString()
        };
    }

    private static bool IsLegacyControllerPoseKind(MappingTargetKind targetKind) => targetKind is >= MappingTargetKind.PosePositionX and <= MappingTargetKind.AngularVelocityZ;

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
