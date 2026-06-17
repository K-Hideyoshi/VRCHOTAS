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
            ToggleMode = ToggleMode,
            TriggerInvert = TriggerInvert,
            ThresholdBidirectional = ThresholdBidirectional,
            Deadzone = Deadzone,
            Curve = Curve,
            Saturation = Saturation,
            InputInvert = InputInvert,
            Invert = OutputInvert,
            VectorAngle = VectorAngle,
            VectorMagnitude = VectorMagnitude,
            KeyboardKey = KeyboardKey,
            KeyboardModifiers = KeyboardModifiers,
            KeyboardTargetWindowTitle = string.IsNullOrWhiteSpace(KeyboardTargetWindowTitle) ? null : KeyboardTargetWindowTitle.Trim(),
            KeyboardTargetProcessName = string.IsNullOrWhiteSpace(KeyboardTargetProcessName) ? null : KeyboardTargetProcessName.Trim(),
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim()
        };
    }

    public void UpdateLivePreview()
    {
        PlotYRangeMax = ResolvePlotYRangeMax();

        var state = _stateProvider();
        var device = state.Devices.FirstOrDefault(item => item.IsConnected && item.DeviceId.Equals(SourceDeviceId, StringComparison.OrdinalIgnoreCase));

        // Always update state panel properties regardless of target kind
        UpdateStatePanelProperties(device);

        // Update ThumbstickVector preview output
        if (ShowVectorPanel)
        {
            var angleRad = VectorAngle * Math.PI / 180.0;
            var sat = Math.Max(Saturation, 0.0);
            var active = _previewToggleActive;
            if (!ToggleMode)
            {
                active = SourceButtonPressed;
            }

            if (active)
            {
                var mag = VectorMagnitude * sat;
                VectorOutputX = mag * Math.Sin(angleRad);
                VectorOutputY = mag * Math.Cos(angleRad);
            }
            else
            {
                VectorOutputX = 0;
                VectorOutputY = 0;
            }
        }

        if (!UsesAxisSource)
        {
            return;
        }

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

    private void UpdateStatePanelProperties(JoystickDeviceState? device)
    {
        if (device is null)
        {
            SourceAxisValue = 0;
            SourceButtonPressed = false;
            TargetTriggered = false;
            _previewToggleActive = false;
            _wasAboveThresholdRaw = false;
            return;
        }

        if (IsSourceButtonDetected)
        {
            var pressed = SourceButtonIndex >= 0 && SourceButtonIndex < device.Buttons.Count
                && device.Buttons[SourceButtonIndex];
            SourceButtonPressed = pressed;

            if (ToggleMode && SelectedTargetKind is MappingTargetKind.Button or MappingTargetKind.Keyboard or MappingTargetKind.ControllerPoseAction)
            {
                var justPressed = pressed && !_wasAboveThresholdRaw;
                if (justPressed)
                {
                    _previewToggleActive = !_previewToggleActive;
                }

                _wasAboveThresholdRaw = pressed;
                TargetTriggered = _previewToggleActive;
            }
            else
            {
                TargetTriggered = pressed;
            }

            return;
        }

        var axisValue = device.Axes.TryGetValue(SourceAxis, out var v) ? v : 0.0;

        // For AxisInput targets, apply the curve pipeline before threshold comparison
        var effectiveValue = SelectedTargetKind == MappingTargetKind.AxisInput
            ? MappingEngine.MapAxisValue(axisValue, Deadzone, Curve, Saturation, InputInvert, OutputInvert, AxisRange)
            : axisValue;

        SourceAxisValue = effectiveValue;

        var threshold = Math.Clamp(FullPressThreshold, 0.0, 1.0);
        var isAboveThreshold = ThresholdBidirectional
            ? Math.Abs(effectiveValue) >= threshold
            : effectiveValue >= threshold;
        var triggered = TriggerInvert ? !isAboveThreshold : isAboveThreshold;

        if (ToggleMode && SelectedTargetKind is MappingTargetKind.Button or MappingTargetKind.Keyboard or MappingTargetKind.ControllerPoseAction)
        {
            var crossed = triggered && !_wasAboveThresholdRaw;
            if (crossed)
            {
                _previewToggleActive = !_previewToggleActive;
            }

            _wasAboveThresholdRaw = triggered;
            TargetTriggered = _previewToggleActive;
        }
        else
        {
            _wasAboveThresholdRaw = triggered;
            TargetTriggered = triggered;
        }
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
            new TargetKindOption(MappingEntry.GetTargetTypeLabel(MappingTargetKind.ControllerPoseAction), MappingTargetKind.ControllerPoseAction),
            new TargetKindOption(MappingEntry.GetTargetTypeLabel(MappingTargetKind.Keyboard), MappingTargetKind.Keyboard)
        };
    }

    private static IReadOnlyList<AxisTargetOption> BuildAxisTargetOptions()
    {
        return new[]
        {
            new AxisTargetOption("Thumbstick X", VirtualAxisTarget.ThumbstickX),
            new AxisTargetOption("Thumbstick Y", VirtualAxisTarget.ThumbstickY),
            new AxisTargetOption("Trigger", VirtualAxisTarget.Trigger),
            new AxisTargetOption("Grip", VirtualAxisTarget.Grip),
            new AxisTargetOption("Thumbstick Vector", VirtualAxisTarget.ThumbstickVector)
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
