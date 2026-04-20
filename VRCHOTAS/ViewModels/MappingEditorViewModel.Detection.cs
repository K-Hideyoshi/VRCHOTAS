using VRCHOTAS.Models;

namespace VRCHOTAS.ViewModels;

public sealed partial class MappingEditorViewModel
{
    private sealed class DetectionSnapshot
    {
        public required bool[] Buttons { get; init; }
        public required Dictionary<string, double> Axes { get; init; }
    }

    public void StartAutoDetect(bool clearDetectedSource)
    {
        if (clearDetectedSource)
        {
            HasDetectedSource = false;
            IsSourceButtonDetected = false;
            SourceDeviceId = string.Empty;
            SourceDeviceName = string.Empty;
            SourceAxis = "X";
            SourceButtonIndex = 0;
        }

        CaptureDetectionBaseline();
        IsListening = true;
        UpdateLivePreview();
    }

    public bool TryAutoDetectSource()
    {
        if (!IsListening)
        {
            return false;
        }

        var state = _stateProvider();
        var now = DateTime.UtcNow;
        var elapsedSeconds = (now - _lastDetectionSampleUtc).TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            RefreshDetectionSnapshots(state);
            _lastDetectionSampleUtc = now;
            return false;
        }

        foreach (var device in state.Devices.Where(item => item.IsConnected))
        {
            if (!_detectionSnapshots.TryGetValue(device.DeviceId, out var previous))
            {
                continue;
            }

            var buttonCount = Math.Min(previous.Buttons.Length, device.Buttons.Count);
            for (var index = 0; index < buttonCount; index++)
            {
                if (previous.Buttons[index] == device.Buttons[index])
                {
                    continue;
                }

                SourceDeviceId = device.DeviceId;
                SourceDeviceName = device.DeviceName;
                HasDetectedSource = true;
                IsSourceButtonDetected = true;
                SourceButtonIndex = index;
                UpdateLivePreview();
                return true;
            }

            foreach (var axis in device.Axes)
            {
                if (!previous.Axes.TryGetValue(axis.Key, out var previousValue))
                {
                    continue;
                }

                var speed = Math.Abs(axis.Value - previousValue) / elapsedSeconds;
                if (speed <= AxisDetectSpeedThreshold)
                {
                    continue;
                }

                SourceDeviceId = device.DeviceId;
                SourceDeviceName = device.DeviceName;
                HasDetectedSource = true;
                IsSourceButtonDetected = false;
                if (SelectedTargetKind == MappingTargetKind.Button)
                {
                    SelectedTargetKind = MappingTargetKind.AxisInput;
                }

                SourceAxis = axis.Key;
                UpdateLivePreview();
                return true;
            }
        }

        RefreshDetectionSnapshots(state);
        _lastDetectionSampleUtc = now;

        return false;
    }

    private void CaptureDetectionBaseline()
    {
        var state = _stateProvider();
        _detectionSnapshots.Clear();
        RefreshDetectionSnapshots(state);
        _lastDetectionSampleUtc = DateTime.UtcNow;
    }

    private void RefreshDetectionSnapshots(RawJoystickState state)
    {
        var connectedIds = state.Devices.Where(item => item.IsConnected).Select(item => item.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var removedId in _detectionSnapshots.Keys.Where(id => !connectedIds.Contains(id)).ToList())
        {
            _detectionSnapshots.Remove(removedId);
        }

        foreach (var device in state.Devices.Where(item => item.IsConnected))
        {
            _detectionSnapshots[device.DeviceId] = new DetectionSnapshot
            {
                Buttons = device.Buttons.ToArray(),
                Axes = device.Axes.ToDictionary(axis => axis.Key, axis => axis.Value, StringComparer.OrdinalIgnoreCase)
            };
        }
    }
}
