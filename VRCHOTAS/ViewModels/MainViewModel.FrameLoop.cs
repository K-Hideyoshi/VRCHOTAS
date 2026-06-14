using System.Windows.Threading;
using VRCHOTAS.Interop;
using VRCHOTAS.Models;
using VRCHOTAS.Services;

namespace VRCHOTAS.ViewModels;

public sealed partial class MainViewModel
{
    private const int HighFrequencyPollIntervalMilliseconds = 5;
    private const int LowFrequencyPollIntervalMilliseconds = 20;
    private const int DriverHeartbeatMaxAgeMs = 5000;
    private const int JoystickRateWindowMs = 5000;
    private const double ActiveMappingAxisSpeedThreshold = 2.5;
    private const int LocateAxisSelectionMinimumIntervalMilliseconds = 500;
    private static readonly TimeSpan LocateAxisSelectionMinimumInterval = TimeSpan.FromMilliseconds(LocateAxisSelectionMinimumIntervalMilliseconds);

    /// <summary>
    /// Number of frames to suppress mapping output after Master transitions from OFF to ON.
    /// During this window the virtual controllers stay at neutral pose, giving them time
    /// to visually reset before any held inputs (e.g. grip axis) take effect.
    /// At 20 ms/frame (low-frequency idle), 5 frames ≈ 100 ms.
    /// </summary>
    private const int MasterOnSuppressFrameCount = 5;

    private bool _driverHeartbeatAlive;
    private bool _prevFrameMappingEnabled;
    private int _masterOnSuppressFramesRemaining;
    private bool? _lastPublishedDriverHeartbeatAlive;
    private RawJoystickState? _lastSelectionDetectionState;
    private DateTime _lastSelectionDetectionUtc;
    private string? _lastActivatedLocateSourceKey;
    private string? _lastAxisLocateSourceKey;
    private DateTime _lastAxisLocateSelectionUtc;

    private async Task RunFrameLoopAsync(CancellationToken cancellationToken)
    {
        var currentInterval = TimeSpan.FromMilliseconds(LowFrequencyPollIntervalMilliseconds);
        using var timer = new PeriodicTimer(currentInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            RunFrameCore();

            RefreshDriverHeartbeatPresenceIfDue();
            PublishDriverHeartbeatDisplayIfChanged();

            var isMappingEnabled = Volatile.Read(ref _isMappingEnabled);
            var ipcReady = _ipc != null;
            var shouldUseHighFrequency = isMappingEnabled && ipcReady && _driverHeartbeatAlive;
            var newInterval = TimeSpan.FromMilliseconds(
                shouldUseHighFrequency
                    ? HighFrequencyPollIntervalMilliseconds
                    : LowFrequencyPollIntervalMilliseconds);

            if (newInterval != currentInterval)
            {
                currentInterval = newInterval;
                timer.Period = currentInterval;
            }
        }
    }

    private void RefreshDriverHeartbeatPresenceIfDue()
    {
        if (_ipc is null)
        {
            _driverHeartbeatAlive = false;
            return;
        }

        var now = Environment.TickCount64;
        if (!_ipc.TryReadDriverHeartbeat(out var tickMs))
        {
            _driverHeartbeatAlive = false;
            return;
        }

        if (tickMs == 0)
        {
            _driverHeartbeatAlive = false;
            return;
        }

        var ageMs = now - (long)tickMs;
        _driverHeartbeatAlive = ageMs >= 0 && ageMs <= DriverHeartbeatMaxAgeMs;
    }

    private void PublishDriverHeartbeatDisplayIfChanged()
    {
        if (_lastPublishedDriverHeartbeatAlive == _driverHeartbeatAlive)
        {
            return;
        }

        _lastPublishedDriverHeartbeatAlive = _driverHeartbeatAlive;
        var text = _driverHeartbeatAlive ? "OK" : "No signal";
        _dispatcher.BeginInvoke(() => DriverHeartbeatStatusDisplay = text);
    }

    private void RecordJoystickPollForRateDisplay()
    {
        _joystickPollCountInWindow++;
        var now = Environment.TickCount64;
        if (now - _rateWindowStartTicks < JoystickRateWindowMs)
        {
            return;
        }

        var hz = _joystickPollCountInWindow / (JoystickRateWindowMs / 1000.0);
        _joystickPollCountInWindow = 0;
        _rateWindowStartTicks = now;
        var text = $"{hz:F1} Hz";
        _dispatcher.BeginInvoke(() => DriverSyncRateDisplay = text);
    }

    private void RunFrameCore()
    {
        try
        {
            var latestState = _joystickService.PollStates();
            RecordJoystickPollForRateDisplay();
            Volatile.Write(ref _latestState, latestState);
            QueueDeviceMonitorRefresh();
            UpdateSelectedMappingFromActiveInput(latestState);

            // Hotkeys are evaluated here (not on the UI thread) so global shortcuts still work when the main window is hidden in the tray.
            var conflictKeys = BuildJoystickHotkeyConflictSet();
            _hotkeyRuntime.ProcessFrame(
                latestState,
                _hotkeyPreferences,
                conflictKeys,
                () => _dispatcher.BeginInvoke(() => CycleConfiguration(-1)),
                () => _dispatcher.BeginInvoke(() => CycleConfiguration(1)),
                () => _dispatcher.BeginInvoke(() => IsMappingEnabled = !IsMappingEnabled));

            var isMappingEnabled = Volatile.Read(ref _isMappingEnabled);

            // Detect Master OFF→ON transition and start a suppression window.
            // This prevents held inputs (e.g. grip axis) from taking effect
            // before the virtual controllers have had time to present a neutral pose.
            if (isMappingEnabled && !_prevFrameMappingEnabled)
            {
                _masterOnSuppressFramesRemaining = MasterOnSuppressFrameCount;
            }

            _prevFrameMappingEnabled = isMappingEnabled;

            VirtualControllerState mapped;
            if (!isMappingEnabled)
            {
                // Master OFF: always send neutral state and clear any in-progress suppression.
                _masterOnSuppressFramesRemaining = 0;
                mapped = VirtualControllerState.CreateDefault();
            }
            else if (_masterOnSuppressFramesRemaining > 0)
            {
                // Suppression window active: send neutral state so the virtual
                // controllers appear at rest before any mappings take effect.
                _masterOnSuppressFramesRemaining--;
                mapped = VirtualControllerState.CreateDefault();
            }
            else
            {
                var mappings = Volatile.Read(ref _mappingSnapshot);
                mapped = _mappingEngine.Map(latestState, mappings, _lastMappedState);

                // Detect anchor state changes and schedule a debounced save.
                // This avoids writing to disk on every frame while anchors are continuously changing.
                var currentLeft = _mappingEngine.GetAnchorSnapshot(VirtualTargetHand.Left);
                var currentRight = _mappingEngine.GetAnchorSnapshot(VirtualTargetHand.Right);
                if (!currentLeft.EqualsAnchor(_lastSavedAnchorLeft) || !currentRight.EqualsAnchor(_lastSavedAnchorRight))
                {
                    _lastSavedAnchorLeft = currentLeft;
                    _lastSavedAnchorRight = currentRight;
                    if (!string.IsNullOrWhiteSpace(CurrentConfigurationFileName))
                    {
                        _anchorPointsService.ScheduleSave(CurrentConfigurationFileName, currentLeft, currentRight);
                    }
                }
            }

            mapped.PoseSource = ResolveVirtualPoseSource(isMappingEnabled);
            _lastMappedState = mapped;
            _ipc?.Write(mapped);
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(MainViewModel), "Frame update failed.", ex);
        }
    }

    private VirtualPoseSource ResolveVirtualPoseSource(bool isMappingEnabled)
    {
        if (!isMappingEnabled)
        {
            return VirtualPoseSource.MirrorRealControllers;
        }

        return _controllerOutputMode switch
        {
            ControllerOutputMode.HybridKeepLeftReal => VirtualPoseSource.HybridKeepLeftReal,
            ControllerOutputMode.HybridKeepRightReal => VirtualPoseSource.HybridKeepRightReal,
            _ => VirtualPoseSource.Mapped
        };
    }

    private HashSet<string> BuildJoystickHotkeyConflictSet()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in Mappings)
        {
            if (mapping.IsTemporarilyDisabled)
            {
                continue;
            }

            if (mapping.IsAxisMapping)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(mapping.SourceDeviceId))
            {
                continue;
            }

            set.Add(HotkeyRuntime.ConflictKey(mapping.SourceDeviceId, mapping.SourceButtonIndex));
        }

        return set;
    }

    private void UpdateSelectedMappingFromActiveInput(RawJoystickState latestState)
    {
        var previousState = _lastSelectionDetectionState;
        var now = DateTime.UtcNow;

        if (!IsLocateMappingEnabled || HotkeyRuntime.IsSuspended)
        {
            _lastSelectionDetectionState = latestState;
            _lastSelectionDetectionUtc = now;
            _lastActivatedLocateSourceKey = null;
            _lastAutoSelectedMapping = null;
            return;
        }

        string? activeSourceKey = null;
        MappingEntry? candidate = null;
        if (previousState is not null)
        {
            var elapsedSeconds = (now - _lastSelectionDetectionUtc).TotalSeconds;
            if (elapsedSeconds > 0)
            {
                activeSourceKey = FindActiveSourceKey(latestState, previousState, elapsedSeconds);
                if (!string.IsNullOrWhiteSpace(activeSourceKey)
                    && !string.Equals(activeSourceKey, _lastActivatedLocateSourceKey, StringComparison.Ordinal)
                    && !ShouldThrottleAxisLocate(activeSourceKey, now))
                {
                    candidate = FindNextMappingForSource(activeSourceKey);
                }
            }
        }

        _lastSelectionDetectionState = latestState;
        _lastSelectionDetectionUtc = now;
        _lastActivatedLocateSourceKey = activeSourceKey;

        if (candidate is null)
        {
            return;
        }

        if (candidate.IsAxisMapping)
        {
            _lastAxisLocateSourceKey = candidate.SourceGroupingKey;
            _lastAxisLocateSelectionUtc = now;
        }

        _lastAutoSelectedMapping = candidate;
        _dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(SelectedMapping, candidate))
            {
                SelectedMapping = candidate;
            }
        }, DispatcherPriority.Input);
    }

    private string? FindActiveSourceKey(RawJoystickState currentState, RawJoystickState previousState, double elapsedSeconds)
    {
        foreach (var mapping in Mappings.ToArray())
        {
            if (string.IsNullOrWhiteSpace(mapping.SourceDeviceId))
            {
                continue;
            }

            var currentDevice = currentState.Devices.FirstOrDefault(device =>
                device.IsConnected && device.DeviceId.Equals(mapping.SourceDeviceId, StringComparison.OrdinalIgnoreCase));
            if (currentDevice is null)
            {
                continue;
            }

            var previousDevice = previousState.Devices.FirstOrDefault(device =>
                device.DeviceId.Equals(mapping.SourceDeviceId, StringComparison.OrdinalIgnoreCase));
            if (previousDevice is null)
            {
                continue;
            }

            if (!mapping.IsAxisMapping)
            {
                if (mapping.SourceButtonIndex < 0
                    || mapping.SourceButtonIndex >= currentDevice.Buttons.Count
                    || mapping.SourceButtonIndex >= previousDevice.Buttons.Count)
                {
                    continue;
                }

                if (!previousDevice.Buttons[mapping.SourceButtonIndex] && currentDevice.Buttons[mapping.SourceButtonIndex])
                {
                    return mapping.SourceGroupingKey;
                }

                continue;
            }

            if (!currentDevice.Axes.TryGetValue(mapping.SourceAxis, out var currentAxisValue)
                || !previousDevice.Axes.TryGetValue(mapping.SourceAxis, out var previousAxisValue))
            {
                continue;
            }

            var axisSpeed = Math.Abs(currentAxisValue - previousAxisValue) / elapsedSeconds;
            if (axisSpeed > ActiveMappingAxisSpeedThreshold)
            {
                return mapping.SourceGroupingKey;
            }
        }

        return null;
    }

    private bool ShouldThrottleAxisLocate(string sourceGroupingKey, DateTime now)
    {
        if (!IsAxisSourceGroupingKey(sourceGroupingKey))
        {
            return false;
        }

        if (!string.Equals(sourceGroupingKey, _lastAxisLocateSourceKey, StringComparison.Ordinal))
        {
            return false;
        }

        return now - _lastAxisLocateSelectionUtc < LocateAxisSelectionMinimumInterval;
    }

    private bool IsAxisSourceGroupingKey(string sourceGroupingKey)
    {
        return Mappings.Any(mapping => mapping.IsAxisMapping
            && string.Equals(mapping.SourceGroupingKey, sourceGroupingKey, StringComparison.Ordinal));
    }

    private MappingEntry? FindNextMappingForSource(string sourceGroupingKey)
    {
        var matchingMappings = Mappings
            .Where(mapping => string.Equals(mapping.SourceGroupingKey, sourceGroupingKey, StringComparison.Ordinal))
            .ToArray();
        if (matchingMappings.Length == 0)
        {
            return null;
        }

        if (_lastAutoSelectedMapping is null
            || !string.Equals(_lastAutoSelectedMapping.SourceGroupingKey, sourceGroupingKey, StringComparison.Ordinal))
        {
            return matchingMappings[0];
        }

        var currentIndex = Array.IndexOf(matchingMappings, _lastAutoSelectedMapping);
        if (currentIndex < 0)
        {
            return matchingMappings[0];
        }

        return matchingMappings[(currentIndex + 1) % matchingMappings.Length];
    }
}
