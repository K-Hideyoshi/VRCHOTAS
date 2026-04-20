using System.Windows.Threading;
using VRCHOTAS.Models;

namespace VRCHOTAS.ViewModels;

public sealed partial class MainViewModel
{
    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        QueueDeviceShellRefresh();
    }

    private void QueueDeviceShellRefresh()
    {
        if (Interlocked.Exchange(ref _deviceShellRefreshQueued, 1) == 1)
        {
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            try
            {
                RefreshDeviceGroupShells();
            }
            finally
            {
                Interlocked.Exchange(ref _deviceShellRefreshQueued, 0);
            }
        }, DispatcherPriority.Background);
    }

    private void RefreshDeviceGroupShells()
    {
        var states = _joystickService.GetDeviceStatesSnapshot();

        var knownIds = states.Select(item => item.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in DeviceGroups.Where(group => !knownIds.Contains(group.DeviceId)).ToArray())
        {
            DeviceGroups.Remove(stale);
        }

        foreach (var state in states)
        {
            var group = DeviceGroups.FirstOrDefault(item => item.DeviceId.Equals(state.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (group is null)
            {
                group = new DeviceMonitorGroup
                {
                    DeviceId = state.DeviceId,
                    DeviceName = state.DeviceName
                };
                DeviceGroups.Add(group);
            }

            group.DeviceName = state.DeviceName;
            group.IsConnected = state.IsConnected;
        }

        UpdateMappingSourceDeviceStates(states);
        UpdateDeviceStatusSummary();
    }

    private void QueueDeviceMonitorRefresh()
    {
        if (Interlocked.Exchange(ref _deviceMonitorRefreshQueued, 1) == 1)
        {
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            try
            {
                UpdateDeviceGroups(GetLatestStateSnapshot());
                UpdateDeviceStatusSummary();
            }
            finally
            {
                Interlocked.Exchange(ref _deviceMonitorRefreshQueued, 0);
            }
        }, DispatcherPriority.Background);
    }

    private void UpdateDeviceGroups(RawJoystickState state)
    {
        foreach (var device in state.Devices)
        {
            var group = DeviceGroups.FirstOrDefault(item => item.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (group is null)
            {
                group = new DeviceMonitorGroup
                {
                    DeviceId = device.DeviceId,
                    DeviceName = device.DeviceName
                };
                DeviceGroups.Add(group);
            }

            group.DeviceName = device.DeviceName;
            group.IsConnected = device.IsConnected;
            group.UpdateFrom(device);
        }

        UpdateMappingSourceDeviceStates(state.Devices);
    }

    private void UpdateMappingSourceDeviceStates(IEnumerable<JoystickDeviceState> states)
    {
        var connectedDeviceIds = states
            .Where(state => state.IsConnected)
            .Select(state => state.DeviceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in Mappings)
        {
            mapping.IsSourceDeviceConnected =
                !string.IsNullOrWhiteSpace(mapping.SourceDeviceId)
                && connectedDeviceIds.Contains(mapping.SourceDeviceId);
        }
    }

    private void UpdateDeviceStatusSummary()
    {
        if (DeviceGroups.Count == 0)
        {
            DeviceStatusSummary = "No device discovered.";
            return;
        }

        DeviceStatusSummary = string.Join(Environment.NewLine,
            DeviceGroups.Select(group =>
                $"{group.DeviceName} ({group.DeviceId[..Math.Min(8, group.DeviceId.Length)]}) - {(group.IsConnected ? "Connected" : "Disconnected")}"));
    }
}
