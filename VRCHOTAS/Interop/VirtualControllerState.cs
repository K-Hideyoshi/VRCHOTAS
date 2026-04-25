using System.Runtime.InteropServices;

namespace VRCHOTAS.Interop;

public enum VirtualPoseSource : byte
{
    Mapped = 0,
    MirrorRealControllers = 1
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ControllerHandState
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = VirtualControllerLayout.ButtonCount, ArraySubType = UnmanagedType.I1)]
    public bool[] Buttons;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = VirtualControllerLayout.AxisCount)]
    public double[] Axes;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = VirtualControllerLayout.Vec3)]
    public double[] Position;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = VirtualControllerLayout.Quat)]
    public double[] Quaternion;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = VirtualControllerLayout.Vec3)]
    public double[] LinearVelocity;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = VirtualControllerLayout.Vec3)]
    public double[] AngularVelocity;

    public void EnsureInitialized()
    {
        Buttons ??= new bool[VirtualControllerLayout.ButtonCount];
        Axes ??= new double[VirtualControllerLayout.AxisCount];
        Position ??= new double[VirtualControllerLayout.Vec3];
        Quaternion ??= new double[VirtualControllerLayout.Quat];
        LinearVelocity ??= new double[VirtualControllerLayout.Vec3];
        AngularVelocity ??= new double[VirtualControllerLayout.Vec3];

        if (Buttons.Length != VirtualControllerLayout.ButtonCount)
        {
            Array.Resize(ref Buttons, VirtualControllerLayout.ButtonCount);
        }

        if (Axes.Length != VirtualControllerLayout.AxisCount)
        {
            Array.Resize(ref Axes, VirtualControllerLayout.AxisCount);
        }

        Position = EnsureVecLength(Position, VirtualControllerLayout.Vec3);
        Quaternion = EnsureVecLength(Quaternion, VirtualControllerLayout.Quat);
        LinearVelocity = EnsureVecLength(LinearVelocity, VirtualControllerLayout.Vec3);
        AngularVelocity = EnsureVecLength(AngularVelocity, VirtualControllerLayout.Vec3);
    }

    private static double[] EnsureVecLength(double[]? arr, int len)
    {
        if (arr is null || arr.Length != len)
        {
            return new double[len];
        }

        return arr;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VirtualControllerState
{
    public VirtualPoseSource PoseSource;
    public ControllerHandState Left;
    public ControllerHandState Right;
    /// <summary>Driver-written heartbeat (Windows GetTickCount64 ms). Preserved by app writes.</summary>
    public ulong DriverHeartbeatTickMs;

    public static VirtualControllerState CreateDefault()
    {
        var state = new VirtualControllerState
        {
            PoseSource = VirtualPoseSource.Mapped,
            Left = CreateDefaultHand(),
            Right = CreateDefaultHand(),
            DriverHeartbeatTickMs = 0
        };
        return state;
    }

    private static ControllerHandState CreateDefaultHand()
    {
        return new ControllerHandState
        {
            Buttons = new bool[VirtualControllerLayout.ButtonCount],
            Axes = new double[VirtualControllerLayout.AxisCount],
            Position = new double[VirtualControllerLayout.Vec3],
            Quaternion = new double[] { 1, 0, 0, 0 },
            LinearVelocity = new double[VirtualControllerLayout.Vec3],
            AngularVelocity = new double[VirtualControllerLayout.Vec3]
        };
    }

    public void EnsureInitialized()
    {
        Left.EnsureInitialized();
        Right.EnsureInitialized();
    }
}

public static class VirtualControllerLayout
{
    public const int ButtonCount = 32;
    public const int AxisCount = 16;
    public const int Vec3 = 3;
    public const int Quat = 4;
    public const string SharedMemoryName = "Local\\VRCHOTAS.VirtualController.State";
    public const string SharedMemoryMutexName = "Local\\VRCHOTAS.VirtualController.State.Mutex";
}

public static class VirtualInputLayout
{
    public const int ThumbstickClickButton = 0;
    public const int PrimaryFaceButton = 1;
    public const int SecondaryFaceButton = 2;
    public const int SystemButton = 3;
    public const int ThumbstickTouchButton = 4;
    public const int TriggerTouchButton = 5;
    public const int TriggerClickButton = 6;
    public const int GripTouchButton = 7;
    public const int GripClickButton = 8;

    public const int ThumbstickXAxis = 0;
    public const int ThumbstickYAxis = 1;
    public const int TriggerAxis = 2;
    public const int GripAxis = 3;
}
