using VRCHOTAS.Interop;
using VRCHOTAS.Models;

namespace VRCHOTAS.ViewModels;

public sealed partial class MainViewModel
{
    private const int FramePollIntervalMilliseconds = 5;

    private async Task RunFrameLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(FramePollIntervalMilliseconds));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            RunFrameCore();
        }
    }

    private void RunFrameCore()
    {
        try
        {
            var latestState = _joystickService.PollStates();
            Volatile.Write(ref _latestState, latestState);
            QueueDeviceMonitorRefresh();

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
}
