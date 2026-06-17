namespace VRCHOTAS.Models;

public enum VirtualTargetHand
{
    Left = 0,
    Right = 1
}

public enum ControllerOutputMode
{
    FullVirtual = 0,
    HybridKeepLeftReal = 1,
    HybridKeepRightReal = 2
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
    Grip = 3,
    ThumbstickVector = 4
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
    ControllerPoseAction = 16,
    Keyboard = 17
}
