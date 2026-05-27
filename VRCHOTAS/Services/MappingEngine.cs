using System.Diagnostics;
using VRCHOTAS.Interop;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;

namespace VRCHOTAS.Services;

public sealed class MappingEngine
{
    private const double DegreesPerRotation = 360.0;
    private const double RadiansToDegrees = 180.0 / Math.PI;

    private readonly IAppLogger _logger;
    private long _lastMapTimestamp;
    private readonly HandPoseAnchorState _leftAnchor = new();
    private readonly HandPoseAnchorState _rightAnchor = new();
    private readonly Dictionary<MappingEntry, ToggleState> _toggleStates = new();

    public MappingEngine(IAppLogger logger)
    {
        _logger = logger;
        _lastMapTimestamp = Stopwatch.GetTimestamp();
    }

    public VirtualControllerState Map(RawJoystickState rawState, IEnumerable<MappingEntry> mappings, VirtualControllerState? previousState = null)
    {
        var mappingList = mappings as IList<MappingEntry> ?? mappings.ToList();
        CleanupToggleStates(mappingList);
        var session = CreateSession(previousState);

        if (!rawState.HasConnectedDevice)
        {
            return session.Output;
        }

        foreach (var mapping in mappingList)
        {
            ProcessMapping(rawState, mapping, session);
        }

        FinalizeSession(session);
        return session.Output;
    }

    private MappingSession CreateSession(VirtualControllerState? previousState)
    {
        var output = previousState ?? VirtualControllerState.CreateDefault();
        output.EnsureInitialized();
        var session = new MappingSession(output, GetFrameDeltaSeconds());
        ResetTransientInputs(ref session.LeftHand);
        ResetTransientInputs(ref session.RightHand);
        return session;
    }

    private void ProcessMapping(RawJoystickState raw, MappingEntry mapping, MappingSession session)
    {
        if (!TryCreateMappingContext(raw, mapping, session, out var context))
        {
            return;
        }

        if (context.TargetKind == MappingTargetKind.Button)
        {
            ProcessButtonTarget(context);
            return;
        }

        if (context.TargetKind == MappingTargetKind.ControllerPoseAction)
        {
            ProcessAxisActionTarget(context);
            return;
        }

        ProcessNonButtonTarget(context);
    }

    private bool TryCreateMappingContext(RawJoystickState raw, MappingEntry mapping, MappingSession session, out ActiveMappingContext context)
    {
        context = default;
        if (mapping.IsTemporarilyDisabled)
        {
            return false;
        }

        var sourceDevice = FindConnectedSourceDevice(raw, mapping);
        if (sourceDevice is null)
        {
            return false;
        }

        context = new ActiveMappingContext(mapping, sourceDevice, session);
        return true;
    }

    private static JoystickDeviceState? FindConnectedSourceDevice(RawJoystickState raw, MappingEntry mapping)
    {
        return raw.Devices.FirstOrDefault(device =>
            device.IsConnected && device.DeviceId.Equals(mapping.SourceDeviceId, StringComparison.OrdinalIgnoreCase));
    }

    private void ProcessButtonTarget(ActiveMappingContext context)
    {
        if (context.Mapping.IsAxisMapping)
        {
            _logger.Debug(nameof(MappingEngine), "Skipped mapping: axis source cannot drive a button target.");
            return;
        }

        if (context.Mapping.TargetButton == VirtualButtonTarget.RecenterHand)
        {
            ProcessLegacyRecenterButton(context);
            return;
        }

        ApplyButtonMapping(context);
    }

    private void ProcessAxisActionTarget(ActiveMappingContext context)
    {
        if (context.Mapping.IsAxisMapping)
        {
            _logger.Debug(nameof(MappingEngine), "Skipped mapping: axis source cannot drive a controller pose action target.");
            return;
        }

        if (!TryGetButtonActivation(context.Mapping, context.SourceDevice, out var isActive, out var justPressed))
        {
            return;
        }

        var shouldTrigger = context.Mapping.ToggleMode ? justPressed && isActive : justPressed;
        if (!shouldTrigger)
        {
            return;
        }

        ApplyControllerPoseAction(context.Mapping.TargetControllerPoseAction, ref context.Hand, GetAnchorState(context.Mapping.TargetHand), ref context.PoseScratch);
    }

    private void ProcessLegacyRecenterButton(ActiveMappingContext context)
    {
        if (!TryGetButtonActivation(context.Mapping, context.SourceDevice, out var isActive, out var justPressed))
        {
            return;
        }

        var shouldReset = context.Mapping.ToggleMode ? justPressed && isActive : isActive;
        if (!shouldReset)
        {
            return;
        }

        ApplyControllerPoseAction(ControllerPoseActionTarget.ResetHand, ref context.Hand, GetAnchorState(context.Mapping.TargetHand), ref context.PoseScratch);
        _logger.Debug(nameof(MappingEngine),
            $"Recenter hand requested: hand={context.Mapping.TargetHand} sourceDevice={context.Mapping.SourceDeviceId} button={context.Mapping.SourceButtonIndex}");
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

    private void ApplyButtonMapping(ActiveMappingContext context)
    {
        if (!TryGetButtonActivation(context.Mapping, context.SourceDevice, out var isActive, out _))
        {
            return;
        }

        context.Hand.EnsureInitialized();
        var buttonIndex = ResolveButtonIndex(context.Mapping.TargetButton);
        if (buttonIndex >= 0 && buttonIndex < context.Hand.Buttons.Length)
        {
            context.Hand.Buttons[buttonIndex] = isActive;
        }
    }

    private void ProcessNonButtonTarget(ActiveMappingContext context)
    {
        if (!TryGetAxisLikeInput(context, out var axisValue))
        {
            return;
        }

        if (context.TargetKind == MappingTargetKind.AxisInput)
        {
            ApplyAxisTargetMapping(context, axisValue);
            return;
        }

        ApplyControllerPoseTargetMapping(context, axisValue);
    }

    private bool TryGetAxisLikeInput(ActiveMappingContext context, out double value)
    {
        value = 0;
        if (context.Mapping.IsAxisMapping)
        {
            if (context.SourceDevice.Axes.TryGetValue(context.Mapping.SourceAxis.ToUpperInvariant(), out value))
            {
                return true;
            }

            _logger.Debug(nameof(MappingEngine), $"Skipped mapping: source axis not found: {context.Mapping.SourceAxis}.");
            return false;
        }

        if (!TryGetButtonActivation(context.Mapping, context.SourceDevice, out var isActive, out _))
        {
            return false;
        }

        value = isActive ? 1.0 : 0.0;
        return true;
    }

    private void ApplyAxisTargetMapping(ActiveMappingContext context, double axisValue)
    {
        var corrected = MapAxisValue(
            axisValue,
            context.Mapping.Deadzone,
            context.Mapping.Curve,
            context.Mapping.Saturation,
            context.Mapping.Invert,
            ResolveAxisRangeForSource(context.Mapping));

        context.Hand.EnsureInitialized();
        var axisIndex = ResolveAxisIndex(context.Mapping.TargetAxis);
        if (axisIndex >= 0 && axisIndex < context.Hand.Axes.Length)
        {
            context.Hand.Axes[axisIndex] = CombineAxisValue(
                context.Hand.Axes[axisIndex],
                corrected,
                context.Mapping.IsAxisMapping);
        }

        ApplyDerivedAxisTouch(context.Mapping.TargetAxis, corrected, ref context.Hand);
        ApplyDerivedAxisButtons(context.Mapping, corrected, ref context.Hand);
    }

    private void ApplyControllerPoseTargetMapping(ActiveMappingContext context, double axisValue)
    {
        if (context.PoseScratch.ResetRequested)
        {
            return;
        }

        var shaped = MapAxisValue(
            axisValue,
            context.Mapping.Deadzone,
            context.Mapping.Curve,
            1.0,
            context.Mapping.Invert,
            ResolveAxisRangeForSource(context.Mapping));

        ApplyControllerPoseContribution(context.ControllerPoseTarget, shaped * context.Mapping.Saturation, ref context.PoseScratch);
    }

    private void ApplyControllerPoseContribution(ControllerPoseTarget targetKind, double scaledValue, ref HandPoseScratch poseScratch)
    {
        switch (targetKind)
        {
            case ControllerPoseTarget.PositionX:
                poseScratch.Px += scaledValue;
                break;
            case ControllerPoseTarget.PositionY:
                poseScratch.Py += scaledValue;
                break;
            case ControllerPoseTarget.PositionZ:
                poseScratch.Pz += scaledValue;
                break;
            case ControllerPoseTarget.OrientationPitch:
                if (!poseScratch.ResetOrientPitchRequested)
                {
                    poseScratch.PitchDeg += scaledValue * DegreesPerRotation;
                }
                break;
            case ControllerPoseTarget.OrientationYaw:
                if (!poseScratch.ResetOrientYawRequested)
                {
                    poseScratch.YawDeg += scaledValue * DegreesPerRotation;
                }
                break;
            case ControllerPoseTarget.OrientationRoll:
                if (!poseScratch.ResetOrientRollRequested)
                {
                    poseScratch.RollDeg += scaledValue * DegreesPerRotation;
                }
                break;
            case ControllerPoseTarget.LinearVelocityX:
                poseScratch.Vx += scaledValue;
                break;
            case ControllerPoseTarget.LinearVelocityY:
                poseScratch.Vy += scaledValue;
                break;
            case ControllerPoseTarget.LinearVelocityZ:
                poseScratch.Vz += scaledValue;
                break;
            case ControllerPoseTarget.AngularVelocityX:
                if (!poseScratch.ResetOrientPitchRequested)
                {
                    poseScratch.Wx += scaledValue;
                }
                break;
            case ControllerPoseTarget.AngularVelocityY:
                if (!poseScratch.ResetOrientYawRequested)
                {
                    poseScratch.Wy += scaledValue;
                }
                break;
            case ControllerPoseTarget.AngularVelocityZ:
                if (!poseScratch.ResetOrientRollRequested)
                {
                    poseScratch.Wz += scaledValue;
                }
                break;
            default:
                _logger.Debug(nameof(MappingEngine), $"Unhandled target kind: {targetKind}.");
                break;
        }
    }

    private void CleanupToggleStates(IList<MappingEntry> mappings)
    {
        var activeMappings = mappings.ToHashSet();
        foreach (var staleMapping in _toggleStates.Keys.Where(existing => !activeMappings.Contains(existing)).ToArray())
        {
            _toggleStates.Remove(staleMapping);
        }
    }

    private bool TryGetButtonActivation(MappingEntry mapping, JoystickDeviceState sourceDevice, out bool isActive, out bool justPressed)
    {
        isActive = false;
        justPressed = false;

        if (mapping.SourceButtonIndex < 0 || mapping.SourceButtonIndex >= sourceDevice.Buttons.Count)
        {
            _logger.Warning(nameof(MappingEngine), $"Skipped mapping: source button out of range: {mapping.SourceButtonIndex}.");
            _toggleStates.Remove(mapping);
            return false;
        }

        var isPressed = sourceDevice.Buttons[mapping.SourceButtonIndex];

        if (!_toggleStates.TryGetValue(mapping, out var state))
        {
            state = new ToggleState();
            _toggleStates[mapping] = state;
        }

        justPressed = isPressed && !state.WasPressed;

        if (!mapping.ToggleMode)
        {
            isActive = isPressed;
            state.WasPressed = isPressed;
            return true;
        }

        if (justPressed)
        {
            state.IsActive = !state.IsActive;
        }

        state.WasPressed = isPressed;
        isActive = state.IsActive;
        return true;
    }

    private void FinalizeHandPose(ref ControllerHandState hand, HandPoseAnchorState anchor, HandPoseScratch scratch, double deltaSeconds)
    {
        hand.EnsureInitialized();
        anchor.X += scratch.Vx * deltaSeconds;
        anchor.Y += scratch.Vy * deltaSeconds;
        anchor.Z += scratch.Vz * deltaSeconds;
        anchor.PitchDeg += scratch.Wx * deltaSeconds * RadiansToDegrees;
        anchor.YawDeg += scratch.Wy * deltaSeconds * RadiansToDegrees;
        anchor.RollDeg += scratch.Wz * deltaSeconds * RadiansToDegrees;

        hand.Position[0] = anchor.X + scratch.Px;
        hand.Position[1] = anchor.Y + scratch.Py;
        hand.Position[2] = anchor.Z + scratch.Pz;
        hand.LinearVelocity[0] = scratch.Vx;
        hand.LinearVelocity[1] = scratch.Vy;
        hand.LinearVelocity[2] = scratch.Vz;
        hand.AngularVelocity[0] = scratch.Wx;
        hand.AngularVelocity[1] = scratch.Wy;
        hand.AngularVelocity[2] = scratch.Wz;
        var quat = hand.Quaternion ?? new double[VirtualControllerLayout.Quat];
        PoseMappingMath.WriteEulerDegreesToQuaternion(
            anchor.PitchDeg + scratch.PitchDeg,
            anchor.YawDeg + scratch.YawDeg,
            anchor.RollDeg + scratch.RollDeg,
            quat);
        hand.Quaternion = quat;
    }

    private void FinalizeSession(MappingSession session)
    {
        ClampCombinedAxes(ref session.LeftHand);
        ClampCombinedAxes(ref session.RightHand);
        FinalizeHandPose(ref session.LeftHand, _leftAnchor, session.LeftPose, session.DeltaSeconds);
        FinalizeHandPose(ref session.RightHand, _rightAnchor, session.RightPose, session.DeltaSeconds);
        session.Output.Left = session.LeftHand;
        session.Output.Right = session.RightHand;
    }

    private HandPoseAnchorState GetAnchorState(VirtualTargetHand targetHand) =>
        targetHand == VirtualTargetHand.Right ? _rightAnchor : _leftAnchor;

    private struct HandPoseScratch
    {
        public double Px, Py, Pz;
        public double PitchDeg, YawDeg, RollDeg;
        public double Vx, Vy, Vz;
        public double Wx, Wy, Wz;
        public bool ResetRequested;
        public bool ResetOrientPitchRequested;
        public bool ResetOrientYawRequested;
        public bool ResetOrientRollRequested;
    }

    private sealed class MappingSession
    {
        public MappingSession(VirtualControllerState output, double deltaSeconds)
        {
            Output = output;
            DeltaSeconds = deltaSeconds;
            LeftHand = output.Left;
            RightHand = output.Right;
        }

        public VirtualControllerState Output;
        public readonly double DeltaSeconds;
        public ControllerHandState LeftHand;
        public ControllerHandState RightHand;
        public HandPoseScratch LeftPose;
        public HandPoseScratch RightPose;

        public ref ControllerHandState GetHand(VirtualTargetHand targetHand)
        {
            if (targetHand == VirtualTargetHand.Right)
            {
                return ref RightHand;
            }

            return ref LeftHand;
        }

        public ref HandPoseScratch GetPoseScratch(VirtualTargetHand targetHand)
        {
            if (targetHand == VirtualTargetHand.Right)
            {
                return ref RightPose;
            }

            return ref LeftPose;
        }
    }

    private readonly ref struct ActiveMappingContext
    {
        public ActiveMappingContext(MappingEntry mapping, JoystickDeviceState sourceDevice, MappingSession session)
        {
            Mapping = mapping;
            SourceDevice = sourceDevice;
            Session = session;
        }

        public MappingEntry Mapping { get; }
        public JoystickDeviceState SourceDevice { get; }
        private MappingSession Session { get; }
        public MappingTargetKind TargetKind => Mapping.NormalizedTargetKind;
        public ControllerPoseTarget ControllerPoseTarget => Mapping.ResolvedControllerPoseTarget;
        public ref ControllerHandState Hand => ref Session.GetHand(Mapping.TargetHand);
        public ref HandPoseScratch PoseScratch => ref Session.GetPoseScratch(Mapping.TargetHand);
    }

    private sealed class HandPoseAnchorState
    {
        public double X;
        public double Y;
        public double Z;
        public double PitchDeg;
        public double YawDeg;
        public double RollDeg;

        public void Reset()
        {
            X = 0;
            Y = 0;
            Z = 0;
            PitchDeg = 0;
            YawDeg = 0;
            RollDeg = 0;
        }

        public void ResetPositionAxis(ControllerPoseActionTarget axisAction)
        {
            switch (axisAction)
            {
                case ControllerPoseActionTarget.ResetPositionX:
                    X = 0;
                    break;
                case ControllerPoseActionTarget.ResetPositionY:
                    Y = 0;
                    break;
                case ControllerPoseActionTarget.ResetPositionZ:
                    Z = 0;
                    break;
            }
        }

        public void ResetOrientationAxis(ControllerPoseActionTarget axisAction)
        {
            switch (axisAction)
            {
                case ControllerPoseActionTarget.ResetOrientPitch:
                    PitchDeg = 0;
                    break;
                case ControllerPoseActionTarget.ResetOrientRoll:
                    RollDeg = 0;
                    break;
                case ControllerPoseActionTarget.ResetOrientYaw:
                    YawDeg = 0;
                    break;
            }
        }
    }

    private sealed class ToggleState
    {
        public bool IsActive;
        public bool WasPressed;
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

    private static double CombineAxisValue(double existingValue, double incomingValue, bool isAxisSource)
    {
        if (isAxisSource)
        {
            return incomingValue;
        }

        return existingValue + incomingValue;
    }

    private static void ClampCombinedAxes(ref ControllerHandState hand)
    {
        hand.EnsureInitialized();

        ClampAxisIfPresent(ref hand, VirtualInputLayout.ThumbstickXAxis, -1.0, 1.0);
        ClampAxisIfPresent(ref hand, VirtualInputLayout.ThumbstickYAxis, -1.0, 1.0);
        ClampAxisIfPresent(ref hand, VirtualInputLayout.TriggerAxis, 0.0, 1.0);
        ClampAxisIfPresent(ref hand, VirtualInputLayout.GripAxis, 0.0, 1.0);
    }

    private static void ClampAxisIfPresent(ref ControllerHandState hand, int axisIndex, double min, double max)
    {
        if (axisIndex < 0 || axisIndex >= hand.Axes.Length)
        {
            return;
        }

        hand.Axes[axisIndex] = Math.Clamp(hand.Axes[axisIndex], min, max);
    }

    private static void ResetHandPose(ref ControllerHandState hand, HandPoseAnchorState anchor)
    {
        hand.EnsureInitialized();
        anchor.Reset();
        Array.Clear(hand.Position);
        Array.Clear(hand.LinearVelocity);
        Array.Clear(hand.AngularVelocity);
        var quaternion = hand.Quaternion ?? new double[VirtualControllerLayout.Quat];
        Array.Clear(quaternion);
        quaternion[0] = 1.0;
        hand.Quaternion = quaternion;
    }

    private void ApplyControllerPoseAction(ControllerPoseActionTarget axisAction, ref ControllerHandState hand, HandPoseAnchorState anchor, ref HandPoseScratch scratch)
    {
        hand.EnsureInitialized();

        switch (axisAction)
        {
            case ControllerPoseActionTarget.ResetPositionX:
            case ControllerPoseActionTarget.ResetPositionY:
            case ControllerPoseActionTarget.ResetPositionZ:
                anchor.ResetPositionAxis(axisAction);
                ResetPositionScratchAxis(axisAction, ref scratch);
                ResetHandPositionAxis(axisAction, ref hand);
                _logger.Debug(nameof(MappingEngine), $"Axis action applied: {axisAction}.");
                return;
            case ControllerPoseActionTarget.ResetOrientPitch:
            case ControllerPoseActionTarget.ResetOrientRoll:
            case ControllerPoseActionTarget.ResetOrientYaw:
                anchor.ResetOrientationAxis(axisAction);
                ResetOrientationScratchAxis(axisAction, ref scratch);
                _logger.Debug(nameof(MappingEngine), $"Axis action applied: {axisAction}.");
                return;
            case ControllerPoseActionTarget.ResetHand:
                ResetHandPose(ref hand, anchor);
                ResetPoseScratch(ref scratch);
                scratch.ResetRequested = true;
                _logger.Debug(nameof(MappingEngine), $"Axis action applied: {axisAction}.");
                return;
            default:
                _logger.Debug(nameof(MappingEngine), $"Unhandled axis action target: {axisAction}.");
                return;
        }
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

    private static void ResetPositionScratchAxis(ControllerPoseActionTarget axisAction, ref HandPoseScratch scratch)
    {
        switch (axisAction)
        {
            case ControllerPoseActionTarget.ResetPositionX:
                scratch.Px = 0;
                scratch.Vx = 0;
                break;
            case ControllerPoseActionTarget.ResetPositionY:
                scratch.Py = 0;
                scratch.Vy = 0;
                break;
            case ControllerPoseActionTarget.ResetPositionZ:
                scratch.Pz = 0;
                scratch.Vz = 0;
                break;
        }
    }

    private static void ResetOrientationScratchAxis(ControllerPoseActionTarget axisAction, ref HandPoseScratch scratch)
    {
        switch (axisAction)
        {
            case ControllerPoseActionTarget.ResetOrientPitch:
                scratch.PitchDeg = 0;
                scratch.Wx = 0;
                scratch.ResetOrientPitchRequested = true;
                break;
            case ControllerPoseActionTarget.ResetOrientRoll:
                scratch.RollDeg = 0;
                scratch.Wz = 0;
                scratch.ResetOrientRollRequested = true;
                break;
            case ControllerPoseActionTarget.ResetOrientYaw:
                scratch.YawDeg = 0;
                scratch.Wy = 0;
                scratch.ResetOrientYawRequested = true;
                break;
        }
    }

    private static void ResetHandPositionAxis(ControllerPoseActionTarget axisAction, ref ControllerHandState hand)
    {
        if (hand.Position is null || hand.LinearVelocity is null)
        {
            return;
        }

        switch (axisAction)
        {
            case ControllerPoseActionTarget.ResetPositionX:
                hand.Position[0] = 0;
                hand.LinearVelocity[0] = 0;
                break;
            case ControllerPoseActionTarget.ResetPositionY:
                hand.Position[1] = 0;
                hand.LinearVelocity[1] = 0;
                break;
            case ControllerPoseActionTarget.ResetPositionZ:
                hand.Position[2] = 0;
                hand.LinearVelocity[2] = 0;
                break;
        }
    }

    private static void ResetHandOrientation(ref ControllerHandState hand)
    {
        if (hand.AngularVelocity is not null)
        {
            Array.Clear(hand.AngularVelocity);
        }

        var quaternion = hand.Quaternion ?? new double[VirtualControllerLayout.Quat];
        Array.Clear(quaternion);
        quaternion[0] = 1.0;
        hand.Quaternion = quaternion;
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
        var clampedSaturation = Math.Max(0.0, saturation);

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
