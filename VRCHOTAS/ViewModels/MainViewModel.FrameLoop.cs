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
    private bool _driverHeartbeatAlive;
    private bool? _lastPublishedDriverHeartbeatAlive;

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
        _dispatcher.BeginInvoke(() => JoystickRefreshRateDisplay = text);
    }

    private void RunFrameCore()
    {
        try
        {
            var latestState = _joystickService.PollStates();
            RecordJoystickPollForRateDisplay();
            Volatile.Write(ref _latestState, latestState);
            QueueDeviceMonitorRefresh();

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
            var mappings = Volatile.Read(ref _mappingSnapshot);
            var mapped = isMappingEnabled
                ? _mappingEngine.Map(latestState, mappings)
                : VirtualControllerState.CreateDefault();
            mapped.PoseSource = isMappingEnabled
                ? VirtualPoseSource.Mapped
                : VirtualPoseSource.MirrorRealControllers;
            _ipc?.Write(mapped);
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(MainViewModel), "Frame update failed.", ex);
        }
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
}
