using VRCHOTAS.Models;

namespace VRCHOTAS.Models;

/// <summary>
/// Provides human-readable display strings for mapping source/target kinds.
/// Extracted from MappingEntry to keep the model focused on data.
/// </summary>
public static class MappingDisplayHelper
{
    public static string GetTargetTypeLabel(MappingTargetKind targetKind) => targetKind switch
    {
        MappingTargetKind.AxisInput => "Axis",
        MappingTargetKind.Button => "Button",
        MappingTargetKind.ControllerPose => "Pose",
        MappingTargetKind.ControllerPoseAction => "Pose Action",
        _ => targetKind.ToString()
    };

    public static string GetAxisTargetDisplay(VirtualAxisTarget target) => target switch
    {
        VirtualAxisTarget.ThumbstickX => "Thumbstick X",
        VirtualAxisTarget.ThumbstickY => "Thumbstick Y",
        VirtualAxisTarget.Trigger => "Trigger",
        VirtualAxisTarget.Grip => "Grip",
        _ => target.ToString()
    };

    public static string GetButtonTargetDisplay(VirtualButtonTarget target, VirtualTargetHand hand) => target switch
    {
        VirtualButtonTarget.ThumbstickClick => "Thumbstick Click",
        VirtualButtonTarget.PrimaryFaceButton => hand == VirtualTargetHand.Right ? "A Button" : "X Button",
        VirtualButtonTarget.SecondaryFaceButton => hand == VirtualTargetHand.Right ? "B Button" : "Y Button",
        VirtualButtonTarget.System => "System Button",
        VirtualButtonTarget.RecenterHand => "Recenter Hand",
        _ => target.ToString()
    };

    public static string GetControllerPoseDisplay(ControllerPoseTarget target) => target switch
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
        _ => target.ToString()
    };

    public static string GetControllerPoseActionDisplay(ControllerPoseActionTarget target) => target switch
    {
        ControllerPoseActionTarget.ResetPositionX => "Reset Pos X",
        ControllerPoseActionTarget.ResetPositionY => "Reset Pos Y",
        ControllerPoseActionTarget.ResetPositionZ => "Reset Pos Z",
        ControllerPoseActionTarget.ResetOrientPitch => "Reset Orient Pitch",
        ControllerPoseActionTarget.ResetOrientRoll => "Reset Orient Roll",
        ControllerPoseActionTarget.ResetOrientYaw => "Reset Orient Yaw",
        ControllerPoseActionTarget.ResetHand => "Reset Hand",
        _ => target.ToString()
    };

    public static string GetTargetControlDisplay(MappingEntry entry)
    {
        var kind = entry.NormalizedTargetKind;
        return kind switch
        {
            MappingTargetKind.AxisInput => GetAxisTargetDisplay(entry.TargetAxis),
            MappingTargetKind.Button => GetButtonTargetDisplay(entry.TargetButton, entry.TargetHand),
            MappingTargetKind.ControllerPose => GetControllerPoseDisplay(entry.ResolvedControllerPoseTarget),
            MappingTargetKind.ControllerPoseAction => GetControllerPoseActionDisplay(entry.TargetControllerPoseAction),
            _ => kind.ToString()
        };
    }
}
