using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace VRCHOTAS.Models;

public enum VirtualTargetHand
{
    Left = 0,
    Right = 1
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
    public int TargetAxisIndex { get; set; }
    public int TargetButtonIndex { get; set; }
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
    public string TargetControlDisplay
    {
        get
        {
            var k = ResolvedTargetKind;
            return k switch
            {
                MappingTargetKind.AxisInput => $"VR Axis {TargetAxisIndex}",
                MappingTargetKind.Button => $"VR Button {TargetButtonIndex}",
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
}

public sealed class AppConfiguration
{
    public List<MappingEntry> Mappings { get; set; } = new();
}
