using System.Diagnostics;
using VRCHOTAS.Interop;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;

namespace VRCHOTAS.Services;

public sealed class MappingEngine
{
    private const double DegreesPerRotation = 360.0;

    private readonly IAppLogger _logger;
    private long _lastMapTimestamp;

    public MappingEngine(IAppLogger logger)
    {
        _logger = logger;
        _lastMapTimestamp = Stopwatch.GetTimestamp();
    }

    public VirtualControllerState Map(RawJoystickState raw, IEnumerable<MappingEntry> mappings, VirtualControllerState? previousState = null)
    {
        var output = previousState ?? VirtualControllerState.CreateDefault();
        output.EnsureInitialized();
        ResetTransientInputs(ref output.Left);
        ResetTransientInputs(ref output.Right);
        var deltaSeconds = GetFrameDeltaSeconds();

        if (!raw.HasConnectedDevice)
        {
            return output;
        }

        var leftPose = new HandPoseScratch();
        var rightPose = new HandPoseScratch();

        foreach (var mapping in mappings)
        {
            if (mapping.IsTemporarilyDisabled)
            {
                continue;
            }

            var sourceDevice = raw.Devices.FirstOrDefault(device =>
                device.DeviceId.Equals(mapping.SourceDeviceId, StringComparison.OrdinalIgnoreCase));

            if (sourceDevice is null || !sourceDevice.IsConnected)
            {
                continue;
            }

            ref var hand = ref SelectHand(ref output, mapping.TargetHand);
            ref var poseScratch = ref mapping.TargetHand == VirtualTargetHand.Right ? ref rightPose : ref leftPose;
            var kind = mapping.ResolvedTargetKind;

            if (kind == MappingTargetKind.Button)
            {
                if (mapping.IsAxisMapping)
                {
                    _logger.Debug(nameof(MappingEngine), "Skipped mapping: axis source cannot drive a button target.");
                    continue;
                }

                if (mapping.TargetButton == VirtualButtonTarget.RecenterHand)
                {
                    if (mapping.SourceButtonIndex >= 0
                        && mapping.SourceButtonIndex < sourceDevice.Buttons.Count
                        && sourceDevice.Buttons[mapping.SourceButtonIndex])
                    {
                        ResetHandPose(ref hand);
                        ResetPoseScratch(ref poseScratch);
                        poseScratch.ResetRequested = true;
                        _logger.Debug(nameof(MappingEngine),
                            $"Recenter hand requested: hand={mapping.TargetHand} sourceDevice={mapping.SourceDeviceId} button={mapping.SourceButtonIndex}");
                    }

                    continue;
                }

                if (!ApplyButtonMapping(mapping, sourceDevice, ref hand))
                {
                    continue;
                }
            }
            else
            {
                if (!TryGetAxisLikeInput(mapping, sourceDevice, out var axisValue))
                {
                    continue;
                }

                switch (kind)
                {
                    case MappingTargetKind.AxisInput:
                    {
                        var corrected = MapAxisValue(
                            axisValue,
                            mapping.Deadzone,
                            mapping.Curve,
                            mapping.Saturation,
                            mapping.Invert,
                            ResolveAxisRangeForSource(mapping));
                        hand.EnsureInitialized();
                        var axisIndex = ResolveAxisIndex(mapping.TargetAxis);
                        if (axisIndex >= 0 && axisIndex < hand.Axes.Length)
                        {
                            hand.Axes[axisIndex] = CombineAxisValue(hand.Axes[axisIndex], corrected, mapping.IsAxisMapping, mapping.TargetAxis);
                        }

                        ApplyDerivedAxisTouch(mapping.TargetAxis, corrected, ref hand);
                        ApplyDerivedAxisButtons(mapping, corrected, ref hand);
                        break;
                    }
                    default:
                    {
                        if (poseScratch.ResetRequested)
                        {
                            continue;
                        }

                        // Saturation scales world units (meters, rotation, rad/s) after normalized shaping.
                        var shaped = MapAxisValue(axisValue, mapping.Deadzone, mapping.Curve, 1.0, mapping.Invert, ResolveAxisRangeForSource(mapping));
                        var scaled = shaped * mapping.Saturation;
                        switch (kind)
                        {
                            case MappingTargetKind.PosePositionX:
                                poseScratch.Px += scaled;
                                break;
                            case MappingTargetKind.PosePositionY:
                                poseScratch.Py += scaled;
                                break;
                            case MappingTargetKind.PosePositionZ:
                                poseScratch.Pz += scaled;
                                break;
                            case MappingTargetKind.PoseOrientationX:
                                poseScratch.PitchDeg += scaled * DegreesPerRotation;
                                break;
                            case MappingTargetKind.PoseOrientationY:
                                poseScratch.YawDeg += scaled * DegreesPerRotation;
                                break;
                            case MappingTargetKind.PoseOrientationZ:
                                poseScratch.RollDeg += scaled * DegreesPerRotation;
                                break;
                            case MappingTargetKind.LinearVelocityX:
                                poseScratch.Vx += scaled;
                                break;
                            case MappingTargetKind.LinearVelocityY:
                                poseScratch.Vy += scaled;
                                break;
                            case MappingTargetKind.LinearVelocityZ:
                                poseScratch.Vz += scaled;
                                break;
                            case MappingTargetKind.AngularVelocityX:
                                poseScratch.Wx += scaled;
                                break;
                            case MappingTargetKind.AngularVelocityY:
                                poseScratch.Wy += scaled;
                                break;
                            case MappingTargetKind.AngularVelocityZ:
                                poseScratch.Wz += scaled;
                                break;
                            default:
                                _logger.Debug(nameof(MappingEngine), $"Unhandled target kind: {kind}.");
                                break;
                        }

                        break;
                    }
                }
            }
        }

        FinalizeHandPose(ref output.Left, leftPose, deltaSeconds);
        FinalizeHandPose(ref output.Right, rightPose, deltaSeconds);
        return output;
    }

    private double GetFrameDeltaSeconds()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedSeconds = (now - _lastMapTimestamp) / (double)Stopwatch.Frequency;
        _lastMapTimestamp = now;
        return Math.Clamp(elapsedSeconds, 0.0, 0.05);
    }

    private static void ResetTransientInputs(ref ControllerHandState hand)
    {
        hand.EnsureInitialized();
        Array.Clear(hand.Buttons);
        Array.Clear(hand.Axes);
        Array.Clear(hand.LinearVelocity);
        Array.Clear(hand.AngularVelocity);
    }

    private bool ApplyButtonMapping(MappingEntry mapping, JoystickDeviceState sourceDevice, ref ControllerHandState hand)
    {
        if (mapping.SourceButtonIndex < 0 || mapping.SourceButtonIndex >= sourceDevice.Buttons.Count)
        {
            _logger.Debug(nameof(MappingEngine), $"Skipped button mapping: source index out of range: {mapping.SourceButtonIndex}.");
            return false;
        }

        hand.EnsureInitialized();
        if (mapping.TargetButton == VirtualButtonTarget.RecenterHand)
        {
            if (sourceDevice.Buttons[mapping.SourceButtonIndex])
            {
                ResetHandPose(ref hand);
            }

            return true;
        }

        var buttonIndex = ResolveButtonIndex(mapping.TargetButton);
        if (buttonIndex >= 0 && buttonIndex < hand.Buttons.Length)
        {
            hand.Buttons[buttonIndex] = sourceDevice.Buttons[mapping.SourceButtonIndex];
        }

        return true;
    }

    private bool TryGetAxisLikeInput(MappingEntry mapping, JoystickDeviceState sourceDevice, out double value)
    {
        value = 0;
        if (mapping.IsAxisMapping)
        {
            if (sourceDevice.Axes.TryGetValue(mapping.SourceAxis.ToUpperInvariant(), out value))
            {
                return true;
            }

            _logger.Debug(nameof(MappingEngine), $"Skipped mapping: source axis not found: {mapping.SourceAxis}.");
            return false;
        }

        if (mapping.SourceButtonIndex < 0 || mapping.SourceButtonIndex >= sourceDevice.Buttons.Count)
        {
            _logger.Debug(nameof(MappingEngine), $"Skipped mapping: source button out of range: {mapping.SourceButtonIndex}.");
            return false;
        }

        value = sourceDevice.Buttons[mapping.SourceButtonIndex] ? 1.0 : 0.0;
        return true;
    }

    private void FinalizeHandPose(ref ControllerHandState hand, HandPoseScratch scratch, double deltaSeconds)
    {
        hand.EnsureInitialized();
        hand.Position[0] += scratch.Px + (scratch.Vx * deltaSeconds);
        hand.Position[1] += scratch.Py + (scratch.Vy * deltaSeconds);
        hand.Position[2] += scratch.Pz + (scratch.Vz * deltaSeconds);
        hand.LinearVelocity[0] = scratch.Vx;
        hand.LinearVelocity[1] = scratch.Vy;
        hand.LinearVelocity[2] = scratch.Vz;
        hand.AngularVelocity[0] = scratch.Wx;
        hand.AngularVelocity[1] = scratch.Wy;
        hand.AngularVelocity[2] = scratch.Wz;
        var quat = hand.Quaternion ?? new double[VirtualControllerLayout.Quat];
        PoseMappingMath.WriteEulerDegreesToQuaternion(scratch.PitchDeg, scratch.YawDeg, scratch.RollDeg, quat);
        hand.Quaternion = quat;
    }

    private static ref ControllerHandState SelectHand(ref VirtualControllerState output, VirtualTargetHand targetHand) =>
        ref targetHand == VirtualTargetHand.Right ? ref output.Right : ref output.Left;

    private struct HandPoseScratch
    {
        public double Px, Py, Pz;
        public double PitchDeg, YawDeg, RollDeg;
        public double Vx, Vy, Vz;
        public double Wx, Wy, Wz;
        public bool ResetRequested;
    }

    private static int ResolveAxisIndex(VirtualAxisTarget axisTarget) => axisTarget switch
    {
        VirtualAxisTarget.ThumbstickX => VirtualInputLayout.ThumbstickXAxis,
        VirtualAxisTarget.ThumbstickY => VirtualInputLayout.ThumbstickYAxis,
        VirtualAxisTarget.Trigger => VirtualInputLayout.TriggerAxis,
        VirtualAxisTarget.Grip => VirtualInputLayout.GripAxis,
        _ => -1
    };

    private static int ResolveButtonIndex(VirtualButtonTarget buttonTarget) => buttonTarget switch
    {
        VirtualButtonTarget.ThumbstickClick => VirtualInputLayout.ThumbstickClickButton,
        VirtualButtonTarget.PrimaryFaceButton => VirtualInputLayout.PrimaryFaceButton,
        VirtualButtonTarget.SecondaryFaceButton => VirtualInputLayout.SecondaryFaceButton,
        VirtualButtonTarget.System => VirtualInputLayout.SystemButton,
        _ => -1
    };

    private static double CombineAxisValue(double existingValue, double incomingValue, bool isAxisSource, VirtualAxisTarget targetAxis)
    {
        if (isAxisSource)
        {
            return incomingValue;
        }

        var combined = existingValue + incomingValue;
        return targetAxis is VirtualAxisTarget.Trigger or VirtualAxisTarget.Grip
            ? Math.Clamp(combined, 0.0, 1.0)
            : Math.Clamp(combined, -1.0, 1.0);
    }

    private static void ResetHandPose(ref ControllerHandState hand)
    {
        hand.EnsureInitialized();
        Array.Clear(hand.Position);
        Array.Clear(hand.LinearVelocity);
        Array.Clear(hand.AngularVelocity);
        var quaternion = hand.Quaternion ?? new double[VirtualControllerLayout.Quat];
        Array.Clear(quaternion);
        quaternion[0] = 1.0;
        hand.Quaternion = quaternion;
    }

    private static void ResetPoseScratch(ref HandPoseScratch scratch)
    {
        scratch.Px = 0;
        scratch.Py = 0;
        scratch.Pz = 0;
        scratch.PitchDeg = 0;
        scratch.YawDeg = 0;
        scratch.RollDeg = 0;
        scratch.Vx = 0;
        scratch.Vy = 0;
        scratch.Vz = 0;
        scratch.Wx = 0;
        scratch.Wy = 0;
        scratch.Wz = 0;
    }

    private static AxisRangeKind ResolveAxisRangeForSource(MappingEntry mapping)
    {
        if (!mapping.IsAxisMapping && mapping.AxisRange == AxisRangeKind.Unidirectional)
        {
            return AxisRangeKind.Bidirectional;
        }

        return mapping.AxisRange;
    }

    private static void ApplyDerivedAxisButtons(MappingEntry mapping, double correctedAxisValue, ref ControllerHandState hand)
    {
        if (mapping.TargetAxis is not (VirtualAxisTarget.Trigger or VirtualAxisTarget.Grip))
        {
            return;
        }

        var threshold = Math.Clamp(mapping.FullPressThreshold, 0.0, 1.0);
        var fullyPressed = correctedAxisValue >= threshold;

        if (mapping.TargetAxis == VirtualAxisTarget.Trigger)
        {
            hand.Buttons[VirtualInputLayout.TriggerClickButton] = hand.Buttons[VirtualInputLayout.TriggerClickButton] || fullyPressed;
            return;
        }

        hand.Buttons[VirtualInputLayout.GripClickButton] = hand.Buttons[VirtualInputLayout.GripClickButton] || fullyPressed;
    }

    private static void ApplyDerivedAxisTouch(VirtualAxisTarget targetAxis, double correctedAxisValue, ref ControllerHandState hand)
    {
        var touched = Math.Abs(correctedAxisValue) > 0.01;

        switch (targetAxis)
        {
            case VirtualAxisTarget.ThumbstickX:
            case VirtualAxisTarget.ThumbstickY:
                hand.Buttons[VirtualInputLayout.ThumbstickTouchButton] = hand.Buttons[VirtualInputLayout.ThumbstickTouchButton] || touched;
                break;
            case VirtualAxisTarget.Trigger:
                hand.Buttons[VirtualInputLayout.TriggerTouchButton] = hand.Buttons[VirtualInputLayout.TriggerTouchButton] || touched;
                break;
            case VirtualAxisTarget.Grip:
                hand.Buttons[VirtualInputLayout.GripTouchButton] = hand.Buttons[VirtualInputLayout.GripTouchButton] || touched;
                break;
        }
    }

    public static double MapAxisValue(double value, double deadzone, double curve, double saturation, bool invert, AxisRangeKind axisRange = AxisRangeKind.Bidirectional)
    {
        value = NormalizeAxisInput(value, axisRange);
        var clampedDeadzone = Math.Clamp(deadzone, 0.0, 0.8);
        var clampedCurve = Math.Clamp(curve, -1.0, 1.0);
        var clampedSaturation = Math.Clamp(saturation, 0.0, 5.0);

        var sign = Math.Sign(value);
        var abs = Math.Abs(value);

        if (abs <= clampedDeadzone)
        {
            return 0;
        }

        var normalized = (abs - clampedDeadzone) / (1.0 - clampedDeadzone);
        var exponent = (1.0 + clampedCurve) / Math.Max(0.0001, 1.0 - clampedCurve);
        var curved = Math.Pow(Math.Clamp(normalized, 0.0, 1.0), exponent);
        var finalValue = curved * sign * clampedSaturation;
        return invert ? -finalValue : finalValue;
    }

    public static double NormalizeAxisInput(double value, AxisRangeKind axisRange)
    {
        var clamped = Math.Clamp(value, -1.0, 1.0);
        return axisRange == AxisRangeKind.Unidirectional
            ? Math.Clamp((clamped + 1.0) * 0.5, 0.0, 1.0)
            : clamped;
    }
}
