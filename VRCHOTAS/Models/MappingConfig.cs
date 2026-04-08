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
    /// <summary>Pitch about +X axis (degrees).</summary>
    PoseOrientationX = 5,
    /// <summary>Yaw about +Y axis (degrees).</summary>
    PoseOrientationY = 6,
    /// <summary>Roll about +Z axis (degrees).</summary>
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

public sealed class MappingEntry
{
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

    public string SourceDisplay => IsAxisMapping ? $"{SourceDeviceName} / Axis {SourceAxis}" : $"{SourceDeviceName} / Button {SourceButtonIndex}";

    public string TargetDisplay
    {
        get
        {
            var hand = TargetHand == VirtualTargetHand.Right ? "Right" : "Left";
            var k = ResolvedTargetKind;
            var label = k switch
            {
                MappingTargetKind.AxisInput => $"VR Axis {TargetAxisIndex}",
                MappingTargetKind.Button => $"VR Button {TargetButtonIndex}",
                MappingTargetKind.PosePositionX => "Pose X (m)",
                MappingTargetKind.PosePositionY => "Pose Y (m)",
                MappingTargetKind.PosePositionZ => "Pose Z (m)",
                MappingTargetKind.PoseOrientationX => "Orient Pitch X (deg)",
                MappingTargetKind.PoseOrientationY => "Orient Yaw Y (deg)",
                MappingTargetKind.PoseOrientationZ => "Orient Roll Z (deg)",
                MappingTargetKind.LinearVelocityX => "LinVel X (m/s)",
                MappingTargetKind.LinearVelocityY => "LinVel Y (m/s)",
                MappingTargetKind.LinearVelocityZ => "LinVel Z (m/s)",
                MappingTargetKind.AngularVelocityX => "AngVel X (rad/s)",
                MappingTargetKind.AngularVelocityY => "AngVel Y (rad/s)",
                MappingTargetKind.AngularVelocityZ => "AngVel Z (rad/s)",
                _ => k.ToString()
            };
            return $"{hand} / {label}";
        }
    }
}

public sealed class AppConfiguration
{
    public List<MappingEntry> Mappings { get; set; } = new();
}
