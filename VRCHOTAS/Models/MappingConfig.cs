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
    System = 3
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
    AngularVelocityZ = 13
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
    public double FullPressThreshold { get; set; } = 0.95;
    public double Deadzone { get; set; }
    public double Curve { get; set; }
    public double Saturation { get; set; } = 1.0;
    public bool Invert { get; set; }

    [JsonIgnore]
    public MappingTargetKind ResolvedTargetKind =>
        TargetKind ?? (IsAxisMapping ? MappingTargetKind.AxisInput : MappingTargetKind.Button);

    [JsonIgnore]
    public string SourceControlDisplay => IsAxisMapping ? $"Axis {SourceAxis}" : $"Button {SourceButtonIndex}";

    [JsonIgnore]
    public string SourceGroupingKey => IsAxisMapping
        ? $"{SourceDeviceId}|Axis|{SourceAxis}"
        : $"{SourceDeviceId}|Button|{SourceButtonIndex}";

    [JsonIgnore]
    public string TargetControlDisplay
    {
        get
        {
            var k = ResolvedTargetKind;
            return k switch
            {
                MappingTargetKind.AxisInput => GetAxisTargetDisplay(),
                MappingTargetKind.Button => GetButtonTargetDisplay(),
                MappingTargetKind.PosePositionX => "Pose X (m)",
                MappingTargetKind.PosePositionY => "Pose Y (m)",
                MappingTargetKind.PosePositionZ => "Pose Z (m)",
                MappingTargetKind.PoseOrientationX => "Orient Pitch X (rotation)",
                MappingTargetKind.PoseOrientationY => "Orient Yaw Y (rotation)",
                MappingTargetKind.PoseOrientationZ => "Orient Roll Z (rotation)",
                MappingTargetKind.LinearVelocityX => "LinVel X (m/s)",
                MappingTargetKind.LinearVelocityY => "LinVel Y (m/s)",
                MappingTargetKind.LinearVelocityZ => "LinVel Z (m/s)",
                MappingTargetKind.AngularVelocityX => "AngVel X (rad/s)",
                MappingTargetKind.AngularVelocityY => "AngVel Y (rad/s)",
                MappingTargetKind.AngularVelocityZ => "AngVel Z (rad/s)",
                _ => k.ToString()
            };
        }
    }

    [JsonIgnore]
    public string TargetGroupingKey => ResolvedTargetKind switch
    {
        MappingTargetKind.AxisInput => $"{TargetHand}|Axis|{TargetAxis}",
        MappingTargetKind.Button => $"{TargetHand}|Button|{TargetButton}",
        _ => $"{TargetHand}|{ResolvedTargetKind}"
    };

    public string SourceDisplay => IsAxisMapping ? $"{SourceDeviceName} / Axis {SourceAxis}" : $"{SourceDeviceName} / Button {SourceButtonIndex}";

    public string TargetDisplay
    {
        get
        {
            var hand = TargetHand == VirtualTargetHand.Right ? "Right" : "Left";
            var k = ResolvedTargetKind;
            return $"{hand} / {TargetControlDisplay}";
        }
    }

    private string GetAxisTargetDisplay()
    {
        return TargetAxis switch
        {
            VirtualAxisTarget.ThumbstickX => "Thumbstick X (-1..1)",
            VirtualAxisTarget.ThumbstickY => "Thumbstick Y (-1..1)",
            VirtualAxisTarget.Trigger => "Trigger (0..1)",
            VirtualAxisTarget.Grip => "Grip (0..1)",
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
            _ => TargetButton.ToString()
        };
    }
}

public sealed class AppConfiguration
{
    public List<MappingEntry> Mappings { get; set; } = new();
}
