using VRCHOTAS.Models;
using VRCHOTAS.Services;

namespace VRCHOTAS.ViewModels;

public sealed partial class MappingEditorViewModel
{
    public MappingEntry BuildResult()
    {
        if (!HasDetectedSource || string.IsNullOrWhiteSpace(SourceDeviceId) || string.IsNullOrWhiteSpace(SourceDeviceName))
        {
            throw new InvalidOperationException("No source input has been detected.");
        }

        if (!IsSourceButtonDetected && SelectedTargetKind is MappingTargetKind.Button or MappingTargetKind.ControllerPoseAction)
        {
            throw new InvalidOperationException("Axis source cannot be mapped to a button-driven target.");
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
            AxisRange = AxisRange,
            TargetAxis = TargetAxis,
            TargetButton = TargetButton,
            TargetControllerPose = TargetControllerPose,
            TargetControllerPoseAction = TargetControllerPoseAction,
            FullPressThreshold = FullPressThreshold,
            ToggleMode = ToggleMode && IsSourceButtonDetected,
            Deadzone = Deadzone,
            Curve = Curve,
            Saturation = Saturation,
            InputInvert = InputInvert,
            Invert = OutputInvert,
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim()
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
        var shaped = MappingEngine.MapAxisValue(input, Deadzone, Curve, 1.0, InputInvert, OutputInvert, AxisRange);
        return SelectedTargetKind == MappingTargetKind.AxisInput
            ? MappingEngine.MapAxisValue(input, Deadzone, Curve, Saturation, InputInvert, OutputInvert, AxisRange)
            : shaped * Saturation;
    }

    private double ResolvePlotYRangeMax() => Math.Max(Saturation, 0.0001);

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
        SyncAxisRangeWithTarget();
        Deadzone = DefaultDeadzone;
        Curve = DefaultCurve;
        Saturation = DefaultSaturation;
    }

    private static double ToPlotX(double value) => (Math.Clamp(value, -1.0, 1.0) + 1.0) * 100.0;

    private static double ToPlotY(double value, double range) => (1.0 - (Math.Clamp(value, -range, range) / Math.Max(range, 0.0001))) * 100.0;

    private static IReadOnlyList<TargetKindOption> BuildTargetKindOptions()
    {
        return new[]
        {
            new TargetKindOption(MappingEntry.GetTargetTypeLabel(MappingTargetKind.AxisInput), MappingTargetKind.AxisInput),
            new TargetKindOption(MappingEntry.GetTargetTypeLabel(MappingTargetKind.Button), MappingTargetKind.Button),
            new TargetKindOption(MappingEntry.GetTargetTypeLabel(MappingTargetKind.ControllerPose), MappingTargetKind.ControllerPose),
            new TargetKindOption(MappingEntry.GetTargetTypeLabel(MappingTargetKind.ControllerPoseAction), MappingTargetKind.ControllerPoseAction)
        };
    }

    private static IReadOnlyList<AxisTargetOption> BuildAxisTargetOptions()
    {
        return new[]
        {
            new AxisTargetOption("Thumbstick X", VirtualAxisTarget.ThumbstickX),
            new AxisTargetOption("Thumbstick Y", VirtualAxisTarget.ThumbstickY),
            new AxisTargetOption("Trigger", VirtualAxisTarget.Trigger),
            new AxisTargetOption("Grip", VirtualAxisTarget.Grip)
        };
    }

    private static IReadOnlyList<ButtonTargetOption> BuildButtonTargetOptions()
    {
        return new[]
        {
            new ButtonTargetOption("Thumbstick Click", VirtualButtonTarget.ThumbstickClick),
            new ButtonTargetOption("Primary Face Button (A/X)", VirtualButtonTarget.PrimaryFaceButton),
            new ButtonTargetOption("Secondary Face Button (B/Y)", VirtualButtonTarget.SecondaryFaceButton),
            new ButtonTargetOption("System Button", VirtualButtonTarget.System)
        };
    }

    private static IReadOnlyList<ControllerPoseTargetOption> BuildControllerPoseTargetOptions()
    {
        return new[]
        {
            new ControllerPoseTargetOption("Pose position X (m)", ControllerPoseTarget.PositionX),
            new ControllerPoseTargetOption("Pose position Y (m)", ControllerPoseTarget.PositionY),
            new ControllerPoseTargetOption("Pose position Z (m)", ControllerPoseTarget.PositionZ),
            new ControllerPoseTargetOption("Orient Pitch (rotation)", ControllerPoseTarget.OrientationPitch),
            new ControllerPoseTargetOption("Orient Yaw (rotation)", ControllerPoseTarget.OrientationYaw),
            new ControllerPoseTargetOption("Orient Roll (rotation)", ControllerPoseTarget.OrientationRoll),
            new ControllerPoseTargetOption("Linear velocity X (m/s)", ControllerPoseTarget.LinearVelocityX),
            new ControllerPoseTargetOption("Linear velocity Y (m/s)", ControllerPoseTarget.LinearVelocityY),
            new ControllerPoseTargetOption("Linear velocity Z (m/s)", ControllerPoseTarget.LinearVelocityZ),
            new ControllerPoseTargetOption("Angular Velocity Pitch (rad/s)", ControllerPoseTarget.AngularVelocityX),
            new ControllerPoseTargetOption("Angular Velocity Yaw (rad/s)", ControllerPoseTarget.AngularVelocityY),
            new ControllerPoseTargetOption("Angular Velocity Roll (rad/s)", ControllerPoseTarget.AngularVelocityZ)
        };
    }

    private static IReadOnlyList<AxisActionTargetOption> BuildAxisActionTargetOptions()
    {
        return new[]
        {
            new AxisActionTargetOption("Reset Pos X", ControllerPoseActionTarget.ResetPositionX),
            new AxisActionTargetOption("Reset Pos Y", ControllerPoseActionTarget.ResetPositionY),
            new AxisActionTargetOption("Reset Pos Z", ControllerPoseActionTarget.ResetPositionZ),
            new AxisActionTargetOption("Reset Orient Pitch", ControllerPoseActionTarget.ResetOrientPitch),
            new AxisActionTargetOption("Reset Orient Roll", ControllerPoseActionTarget.ResetOrientRoll),
            new AxisActionTargetOption("Reset Orient Yaw", ControllerPoseActionTarget.ResetOrientYaw),
            new AxisActionTargetOption("Reset Hand", ControllerPoseActionTarget.ResetHand)
        };
    }
}
