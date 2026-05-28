using System.Numerics;
using VRCHOTAS.Models;

namespace VRCHOTAS.Services;

/// <summary>
/// Converts mapped Euler angles (degrees) to the quaternion layout shared with the OpenVR driver (w,x,y,z).
/// </summary>
internal static class PoseMappingMath
{
    public static void WriteEulerDegreesToQuaternion(
        double pitchXDeg,
        double yawYDeg,
        double rollZDeg,
        EulerAnglePreferences preferences,
        double[] q)
    {
        const double toRad = Math.PI / 180.0;

        var rotation = Quaternion.Identity;
        foreach (var step in GetSteps(preferences?.Order ?? EulerAngleOrder.YawPitchRoll, pitchXDeg, yawYDeg, rollZDeg))
        {
            var angleRadians = (float)(step.AngleDegrees * toRad);
            if (Math.Abs(angleRadians) < float.Epsilon)
            {
                continue;
            }

            var delta = Quaternion.CreateFromAxisAngle(step.Axis, angleRadians);
            rotation = (preferences?.AxisReference ?? EulerAngleAxisReference.Local) == EulerAngleAxisReference.Local
                ? Quaternion.Normalize(rotation * delta)
                : Quaternion.Normalize(delta * rotation);
        }

        var quat = Quaternion.Normalize(rotation);
        quat = Quaternion.Normalize(quat);
        q[0] = quat.W;
        q[1] = quat.X;
        q[2] = quat.Y;
        q[3] = quat.Z;
    }

    private static RotationStep[] GetSteps(EulerAngleOrder order, double pitchXDeg, double yawYDeg, double rollZDeg)
    {
        return order switch
        {
            EulerAngleOrder.PitchYawRoll => [
                new RotationStep(Vector3.UnitX, pitchXDeg),
                new RotationStep(Vector3.UnitY, yawYDeg),
                new RotationStep(Vector3.UnitZ, rollZDeg)
            ],
            EulerAngleOrder.PitchRollYaw => [
                new RotationStep(Vector3.UnitX, pitchXDeg),
                new RotationStep(Vector3.UnitZ, rollZDeg),
                new RotationStep(Vector3.UnitY, yawYDeg)
            ],
            EulerAngleOrder.YawPitchRoll => [
                new RotationStep(Vector3.UnitY, yawYDeg),
                new RotationStep(Vector3.UnitX, pitchXDeg),
                new RotationStep(Vector3.UnitZ, rollZDeg)
            ],
            EulerAngleOrder.YawRollPitch => [
                new RotationStep(Vector3.UnitY, yawYDeg),
                new RotationStep(Vector3.UnitZ, rollZDeg),
                new RotationStep(Vector3.UnitX, pitchXDeg)
            ],
            EulerAngleOrder.RollPitchYaw => [
                new RotationStep(Vector3.UnitZ, rollZDeg),
                new RotationStep(Vector3.UnitX, pitchXDeg),
                new RotationStep(Vector3.UnitY, yawYDeg)
            ],
            EulerAngleOrder.RollYawPitch => [
                new RotationStep(Vector3.UnitZ, rollZDeg),
                new RotationStep(Vector3.UnitY, yawYDeg),
                new RotationStep(Vector3.UnitX, pitchXDeg)
            ],
            _ => [
                new RotationStep(Vector3.UnitY, yawYDeg),
                new RotationStep(Vector3.UnitX, pitchXDeg),
                new RotationStep(Vector3.UnitZ, rollZDeg)
            ]
        };
    }

    private readonly record struct RotationStep(Vector3 Axis, double AngleDegrees);
}
