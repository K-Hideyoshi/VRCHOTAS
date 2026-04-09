using VRCHOTAS.Interop;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;

namespace VRCHOTAS.Services;

public sealed class MappingEngine
{
    private const double DegreesPerRotation = 360.0;

    private readonly IAppLogger _logger;

    public MappingEngine(IAppLogger logger)
    {
        _logger = logger;
    }

    public VirtualControllerState Map(RawJoystickState raw, IEnumerable<MappingEntry> mappings)
    {
        var output = VirtualControllerState.CreateDefault();

        if (!raw.HasConnectedDevice)
        {
            return output;
        }

        var leftPose = new HandPoseScratch();
        var rightPose = new HandPoseScratch();

        foreach (var mapping in mappings)
        {
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
                        var corrected = MapAxisValue(axisValue, mapping.Deadzone, mapping.Curve, mapping.Saturation, mapping.Invert);
                        hand.EnsureInitialized();
                        if (mapping.TargetAxisIndex >= 0 && mapping.TargetAxisIndex < hand.Axes.Length)
                        {
                            hand.Axes[mapping.TargetAxisIndex] = corrected;
                        }

                        break;
                    }
                    default:
                    {
                        // Saturation scales world units (meters, rotation, rad/s) after normalized shaping.
                        var shaped = MapAxisValue(axisValue, mapping.Deadzone, mapping.Curve, 1.0, mapping.Invert);
                        var scaled = shaped * mapping.Saturation;
                        switch (kind)
                        {
                            case MappingTargetKind.PosePositionX:
                                poseScratch.Px = scaled;
                                break;
                            case MappingTargetKind.PosePositionY:
                                poseScratch.Py = scaled;
                                break;
                            case MappingTargetKind.PosePositionZ:
                                poseScratch.Pz = scaled;
                                break;
                            case MappingTargetKind.PoseOrientationX:
                                poseScratch.PitchDeg = scaled * DegreesPerRotation;
                                break;
                            case MappingTargetKind.PoseOrientationY:
                                poseScratch.YawDeg = scaled * DegreesPerRotation;
                                break;
                            case MappingTargetKind.PoseOrientationZ:
                                poseScratch.RollDeg = scaled * DegreesPerRotation;
                                break;
                            case MappingTargetKind.LinearVelocityX:
                                poseScratch.Vx = scaled;
                                break;
                            case MappingTargetKind.LinearVelocityY:
                                poseScratch.Vy = scaled;
                                break;
                            case MappingTargetKind.LinearVelocityZ:
                                poseScratch.Vz = scaled;
                                break;
                            case MappingTargetKind.AngularVelocityX:
                                poseScratch.Wx = scaled;
                                break;
                            case MappingTargetKind.AngularVelocityY:
                                poseScratch.Wy = scaled;
                                break;
                            case MappingTargetKind.AngularVelocityZ:
                                poseScratch.Wz = scaled;
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

        FinalizeHandPose(ref output.Left, leftPose);
        FinalizeHandPose(ref output.Right, rightPose);
        return output;
    }

    private bool ApplyButtonMapping(MappingEntry mapping, JoystickDeviceState sourceDevice, ref ControllerHandState hand)
    {
        if (mapping.SourceButtonIndex < 0 || mapping.SourceButtonIndex >= sourceDevice.Buttons.Count)
        {
            _logger.Debug(nameof(MappingEngine), $"Skipped button mapping: source index out of range: {mapping.SourceButtonIndex}.");
            return false;
        }

        hand.EnsureInitialized();
        if (mapping.TargetButtonIndex >= 0 && mapping.TargetButtonIndex < hand.Buttons.Length)
        {
            hand.Buttons[mapping.TargetButtonIndex] = sourceDevice.Buttons[mapping.SourceButtonIndex];
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

    private static void FinalizeHandPose(ref ControllerHandState hand, HandPoseScratch scratch)
    {
        hand.EnsureInitialized();
        hand.Position[0] = scratch.Px;
        hand.Position[1] = scratch.Py;
        hand.Position[2] = scratch.Pz;
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
    }

    public static double MapAxisValue(double value, double deadzone, double curve, double saturation, bool invert)
    {
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
}
