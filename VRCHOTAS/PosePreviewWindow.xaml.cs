using System.Numerics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Shapes;
using VRCHOTAS.Interop;
using VRCHOTAS.Models;
using VRCHOTAS.ViewModels;

namespace VRCHOTAS;

public partial class PosePreviewWindow : Window
{
    private const double CanvasCenterX = 230;
    private const double CanvasCenterY = 115;
    private const double AxisScale = 72;

    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _timer;

    public PosePreviewWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _timer.Tick += OnTimerTick;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshPose();
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        RefreshPose();
    }

    private void RefreshPose()
    {
        var state = _viewModel.GetLatestMappedStateSnapshot();
        var preferences = _viewModel.GetEulerAnglePreferencesSnapshot();
        PoseSourceText.Text = $"Pose source: {state.PoseSource}";
        EulerOrderText.Text = $"Euler order: {FormatEulerOrder(preferences.Order)}";
        AxisReferenceText.Text = $"Axis reference: {FormatAxisReference(preferences.AxisReference)}";
        LeftPoseText.Text = FormatHandState(state.Left);
        RightPoseText.Text = FormatHandState(state.Right);
        UpdateAxesPreview(
            state.Left,
            LeftWorldXAxis,
            LeftWorldYAxis,
            LeftWorldZAxis,
            LeftLocalXAxis,
            LeftLocalYAxis,
            LeftLocalZAxis,
            LeftForwardMarker,
            LeftOrientationHint);
        UpdateAxesPreview(
            state.Right,
            RightWorldXAxis,
            RightWorldYAxis,
            RightWorldZAxis,
            RightLocalXAxis,
            RightLocalYAxis,
            RightLocalZAxis,
            RightForwardMarker,
            RightOrientationHint);
    }

    private static string FormatHandState(ControllerHandState hand)
    {
        hand.EnsureInitialized();
        var quaternion = GetQuaternion(hand.Quaternion);
        var euler = ToYawPitchRollDegrees(quaternion);
        var builder = new StringBuilder();
        builder.AppendLine($"Position         X {FormatValue(hand.Position, 0)}   Y {FormatValue(hand.Position, 1)}   Z {FormatValue(hand.Position, 2)}");
        builder.AppendLine($"Quaternion (wxyz)  {FormatQuatComponent(hand.Quaternion, 0)}  {FormatQuatComponent(hand.Quaternion, 1)}  {FormatQuatComponent(hand.Quaternion, 2)}  {FormatQuatComponent(hand.Quaternion, 3)}");
        builder.AppendLine($"Euler Y/P/R (deg) Y {euler.Yaw:F2}   P {euler.Pitch:F2}   R {euler.Roll:F2}");
        builder.AppendLine();
        builder.AppendLine($"Linear Vel       X {FormatValue(hand.LinearVelocity, 0)}   Y {FormatValue(hand.LinearVelocity, 1)}   Z {FormatValue(hand.LinearVelocity, 2)}");
        builder.AppendLine($"Angular Vel      X {FormatValue(hand.AngularVelocity, 0)}   Y {FormatValue(hand.AngularVelocity, 1)}   Z {FormatValue(hand.AngularVelocity, 2)}");
        return builder.ToString();
    }

    private static Quaternion GetQuaternion(double[]? values)
    {
        var w = values is { Length: > 0 } ? (float)values[0] : 1f;
        var x = values is { Length: > 1 } ? (float)values[1] : 0f;
        var y = values is { Length: > 2 } ? (float)values[2] : 0f;
        var z = values is { Length: > 3 } ? (float)values[3] : 0f;
        return Quaternion.Normalize(new Quaternion(x, y, z, w));
    }

    private static (double Yaw, double Pitch, double Roll) ToYawPitchRollDegrees(Quaternion quaternion)
    {
        var sinPitch = 2.0 * (quaternion.W * quaternion.X - quaternion.Z * quaternion.Y);
        sinPitch = Math.Clamp(sinPitch, -1.0, 1.0);

        var yaw = Math.Atan2(
            2.0 * (quaternion.W * quaternion.Y + quaternion.X * quaternion.Z),
            1.0 - 2.0 * (quaternion.X * quaternion.X + quaternion.Y * quaternion.Y));
        var pitch = Math.Asin(sinPitch);
        var roll = Math.Atan2(
            2.0 * (quaternion.W * quaternion.Z + quaternion.X * quaternion.Y),
            1.0 - 2.0 * (quaternion.X * quaternion.X + quaternion.Z * quaternion.Z));

        const double toDeg = 180.0 / Math.PI;
        return (yaw * toDeg, pitch * toDeg, roll * toDeg);
    }

    private static string FormatValue(double[]? values, int index)
    {
        if (values is null || index < 0 || index >= values.Length)
        {
            return "0.0000";
        }

        return values[index].ToString("F4");
    }

    private static string FormatQuatComponent(double[]? values, int index)
    {
        if (values is null || index < 0 || index >= values.Length)
        {
            return "0.000000";
        }

        return values[index].ToString("F6");
    }

    private static string FormatEulerOrder(EulerAngleOrder order)
    {
        return order switch
        {
            EulerAngleOrder.PitchYawRoll => "Pitch → Yaw → Roll (X → Y → Z)",
            EulerAngleOrder.PitchRollYaw => "Pitch → Roll → Yaw (X → Z → Y)",
            EulerAngleOrder.YawPitchRoll => "Yaw → Pitch → Roll (Y → X → Z)",
            EulerAngleOrder.YawRollPitch => "Yaw → Roll → Pitch (Y → Z → X)",
            EulerAngleOrder.RollPitchYaw => "Roll → Pitch → Yaw (Z → X → Y)",
            EulerAngleOrder.RollYawPitch => "Roll → Yaw → Pitch (Z → Y → X)",
            _ => order.ToString()
        };
    }

    private static string FormatAxisReference(EulerAngleAxisReference axisReference)
    {
        return axisReference == EulerAngleAxisReference.World
            ? "World axes (extrinsic rotations)"
            : "Local axes (intrinsic rotations)";
    }

    private static void UpdateAxesPreview(
        ControllerHandState hand,
        Line worldX,
        Line worldY,
        Line worldZ,
        Line localX,
        Line localY,
        Line localZ,
        Ellipse forwardMarker,
        TextBlock orientationHint)
    {
        hand.EnsureInitialized();
        var quaternion = GetQuaternion(hand.Quaternion);

        SetAxisLine(worldX, Vector3.UnitX);
        SetAxisLine(worldY, Vector3.UnitY);
        SetAxisLine(worldZ, Vector3.UnitZ);

        var rotatedX = Vector3.Transform(Vector3.UnitX, quaternion);
        var rotatedY = Vector3.Transform(Vector3.UnitY, quaternion);
        var rotatedZ = Vector3.Transform(Vector3.UnitZ, quaternion);

        SetAxisLine(localX, rotatedX);
        SetAxisLine(localY, rotatedY);
        SetAxisLine(localZ, rotatedZ);

        var markerPoint = Project(rotatedZ);
        Canvas.SetLeft(forwardMarker, markerPoint.X - (forwardMarker.Width / 2.0));
        Canvas.SetTop(forwardMarker, markerPoint.Y - (forwardMarker.Height / 2.0));
        Canvas.SetLeft(orientationHint, markerPoint.X + 8);
        Canvas.SetTop(orientationHint, markerPoint.Y - 10);
        orientationHint.Text = "+Z";
    }

    private static void SetAxisLine(Line line, Vector3 direction)
    {
        var end = Project(direction);
        line.X1 = CanvasCenterX;
        line.Y1 = CanvasCenterY;
        line.X2 = end.X;
        line.Y2 = end.Y;
    }

    private static System.Windows.Point Project(Vector3 direction)
    {
        var x = CanvasCenterX + (direction.X * 1.0 + direction.Z * 0.55) * AxisScale;
        var y = CanvasCenterY - (direction.Y * 1.0 - direction.Z * 0.35) * AxisScale;
        return new System.Windows.Point(x, y);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        Loaded -= OnLoaded;
        Closed -= OnClosed;
    }
}