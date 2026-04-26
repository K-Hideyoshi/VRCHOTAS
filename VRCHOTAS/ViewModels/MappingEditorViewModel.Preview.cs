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

        if (!IsSourceButtonDetected && SelectedTargetKind == MappingTargetKind.Button)
        {
            throw new InvalidOperationException("Axis source cannot be mapped to a button target.");
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
            FullPressThreshold = FullPressThreshold,
            Deadzone = Deadzone,
            Curve = Curve,
            Saturation = Saturation,
            Invert = Invert
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
        var shaped = MappingEngine.MapAxisValue(input, Deadzone, Curve, 1.0, Invert, AxisRange);
        return SelectedTargetKind == MappingTargetKind.AxisInput
            ? MappingEngine.MapAxisValue(input, Deadzone, Curve, Saturation, Invert, AxisRange)
            : shaped * Math.Clamp(Saturation, 0.0, 5.0);
    }

    private double ResolvePlotYRangeMax() => AxisRange == AxisRangeKind.Unidirectional
        ? Math.Max(1.0, Math.Clamp(Saturation, 0.0, 5.0))
        : Math.Max(1.0, Math.Clamp(Saturation, 0.0, 5.0));

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
            new TargetKindOption("VR Axis", MappingTargetKind.AxisInput),
            new TargetKindOption("VR Button", MappingTargetKind.Button),
            new TargetKindOption("Pose position X (m)", MappingTargetKind.PosePositionX),
            new TargetKindOption("Pose position Y (m)", MappingTargetKind.PosePositionY),
            new TargetKindOption("Pose position Z (m)", MappingTargetKind.PosePositionZ),
            new TargetKindOption("Orientation pitch X (rotation)", MappingTargetKind.PoseOrientationX),
            new TargetKindOption("Orientation yaw Y (rotation)", MappingTargetKind.PoseOrientationY),
            new TargetKindOption("Orientation roll Z (rotation)", MappingTargetKind.PoseOrientationZ),
            new TargetKindOption("Linear velocity X (m/s)", MappingTargetKind.LinearVelocityX),
            new TargetKindOption("Linear velocity Y (m/s)", MappingTargetKind.LinearVelocityY),
            new TargetKindOption("Linear velocity Z (m/s)", MappingTargetKind.LinearVelocityZ),
            new TargetKindOption("Angular velocity X (rad/s)", MappingTargetKind.AngularVelocityX),
            new TargetKindOption("Angular velocity Y (rad/s)", MappingTargetKind.AngularVelocityY),
            new TargetKindOption("Angular velocity Z (rad/s)", MappingTargetKind.AngularVelocityZ)
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
            new ButtonTargetOption("System Button", VirtualButtonTarget.System),
            new ButtonTargetOption("Recenter Hand", VirtualButtonTarget.RecenterHand)
        };
    }
}
