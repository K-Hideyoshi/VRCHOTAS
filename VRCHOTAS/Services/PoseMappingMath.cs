using System.Numerics;

namespace VRCHOTAS.Services;

/// <summary>
/// Converts mapped Euler angles (degrees) to the quaternion layout shared with the OpenVR driver (w,x,y,z).
/// Uses System.Numerics order: yaw about Y, pitch about X, roll about Z (radians).
/// </summary>
internal static class PoseMappingMath
{
    public static void WriteEulerDegreesToQuaternion(double pitchXDeg, double yawYDeg, double rollZDeg, double[] q)
    {
        const double toRad = Math.PI / 180.0;
        var quat = Quaternion.CreateFromYawPitchRoll(
            (float)(yawYDeg * toRad),
            (float)(pitchXDeg * toRad),
            (float)(rollZDeg * toRad));
        quat = Quaternion.Normalize(quat);
        q[0] = quat.W;
        q[1] = quat.X;
        q[2] = quat.Y;
        q[3] = quat.Z;
    }
}
