using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;
using VRCHOTAS.Services;
using Valve.VR;

internal sealed class OpenVrOverlayRuntime : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan QuitRetryDelay = TimeSpan.FromSeconds(10);

    private readonly IAppLogger _logger;
    private readonly OpenVrNativeLibraryService _openVrNativeLibraryService;
    private readonly Dispatcher _renderDispatcher;
    private readonly OverlayBitmapFactory _bitmapFactory = new();
    private readonly OverlayHandleManager _handleManager;
    private readonly Action<OverlayHelperStatusMessage> _publishStatus;
    private readonly object _stateSync = new();
    private readonly AutoResetEvent _wakeSignal = new(false);
    private readonly Thread _thread;

    private VrOverlayPreferences _preferences = new();
    private bool _isMasterSwitchOn;
    private string? _pendingToastMessage;
    private DateTime _toastVisibleUntilUtc = DateTime.MinValue;
    private bool _toastDirty;
    private bool _statusDirty = true;
    private bool _toastVisible;
    private bool _statusVisible;
    private bool _disposed;

    private CVRSystem? _system;
    private CVROverlay? _overlay;
    private IOverlayTextureRenderer? _renderer;
    private DateTime _nextInitializationAttemptUtc = DateTime.MinValue;
    private OverlayHelperStatusKind? _lastStatusKind;

    public OpenVrOverlayRuntime(
        IAppLogger logger,
        OpenVrNativeLibraryService openVrNativeLibraryService,
        Dispatcher renderDispatcher,
        Action<OverlayHelperStatusMessage> publishStatus)
    {
        _logger = logger;
        _openVrNativeLibraryService = openVrNativeLibraryService;
        _renderDispatcher = renderDispatcher;
        _publishStatus = publishStatus;
        _handleManager = new OverlayHandleManager(logger);
        _thread = new Thread(ThreadLoop)
        {
            IsBackground = true,
            Name = "VRCHOTAS OpenVR Overlay Runtime"
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void ApplyPreferences(VrOverlayPreferences preferences, bool isMasterSwitchOn)
    {
        lock (_stateSync)
        {
            _preferences = preferences.Clone();
            _preferences.Normalize();
            _isMasterSwitchOn = isMasterSwitchOn;
            _statusDirty = true;

            if (!_preferences.Enabled)
            {
                _pendingToastMessage = null;
                _toastVisibleUntilUtc = DateTime.MinValue;
                _toastDirty = true;
            }
        }

        Wake();
    }

    public void ShowToast(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (_stateSync)
        {
            if (!_preferences.Enabled)
            {
                return;
            }

            _pendingToastMessage = message;
            _toastVisibleUntilUtc = DateTime.MinValue;
            _toastDirty = true;
        }

        Wake();
    }

    public void UpdateStatusIndicator(bool isMasterSwitchOn)
    {
        lock (_stateSync)
        {
            _isMasterSwitchOn = isMasterSwitchOn;
            _statusDirty = true;
        }

        Wake();
    }

    private void ThreadLoop()
    {
        while (!_disposed)
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                _logger.Warning(nameof(OpenVrOverlayRuntime), $"Overlay runtime tick failed: {ex.Message}");
                PublishStatus(OverlayHelperStatusKind.LastError, "Overlay runtime tick failed.", ex.Message, force: true);
                ResetOpenVr(QuitRetryDelay);
            }

            _wakeSignal.WaitOne(TimeSpan.FromMilliseconds(32));
        }
    }

    private void Tick()
    {
        var snapshot = CreateSnapshot();
        if (!snapshot.Enabled)
        {
            HideAll();
            PublishStatus(OverlayHelperStatusKind.Disabled, "VR overlay disabled.");
            return;
        }

        if (!snapshot.HasWork)
        {
            HideExpiredToast(snapshot.NowUtc);
            return;
        }

        if (!EnsureInitialized(snapshot))
        {
            return;
        }

        PollOpenVrEvents();
        if (_system is null || _overlay is null)
        {
            return;
        }

        if (_overlay.IsDashboardVisible())
        {
            HideAll();
            return;
        }

        RenderToastIfNeeded(snapshot);
        RenderStatusIfNeeded(snapshot);
        HideExpiredToast(snapshot.NowUtc);
    }

    private RuntimeSnapshot CreateSnapshot()
    {
        lock (_stateSync)
        {
            var statusWanted = _preferences.StatusIndicatorEnabled && _isMasterSwitchOn;
            return new RuntimeSnapshot(
                _preferences.Clone(),
                _pendingToastMessage,
                _toastDirty,
                statusWanted,
                _statusDirty,
                DateTime.UtcNow);
        }
    }

    private bool EnsureInitialized(RuntimeSnapshot snapshot)
    {
        if (_system is not null && _overlay is not null && _renderer is not null)
        {
            return true;
        }

        if (DateTime.UtcNow < _nextInitializationAttemptUtc)
        {
            return false;
        }

        if (!_openVrNativeLibraryService.IsSteamVrRunning())
        {
            _nextInitializationAttemptUtc = DateTime.UtcNow + RetryDelay;
            PublishStatus(OverlayHelperStatusKind.WaitingForSteamVR, "Waiting for SteamVR.");
            return false;
        }

        if (!_openVrNativeLibraryService.TryEnsureLoaded(out var failureMessage))
        {
            _nextInitializationAttemptUtc = DateTime.UtcNow + RetryDelay;
            _logger.Warning(nameof(OpenVrOverlayRuntime), failureMessage);
            PublishStatus(OverlayHelperStatusKind.LastError, "openvr_api.dll could not be loaded.", failureMessage, force: true);
            return false;
        }

        string runtimePath;
        try
        {
            runtimePath = OpenVR.RuntimePath();
        }
        catch (Exception ex)
        {
            runtimePath = $"<unavailable: {ex.Message}>";
        }

        _logger.Info(
            nameof(OpenVrOverlayRuntime),
            $"OpenVR runtime diagnostics: installed={OpenVR.IsRuntimeInstalled()}, hmdPresent={OpenVR.IsHmdPresent()}, runtimePath='{runtimePath}'.");

        var initError = EVRInitError.None;
        _system = OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Overlay);
        _logger.Info(nameof(OpenVrOverlayRuntime), $"OpenVR.Init returned: {OpenVR.GetStringForHmdError(initError)}");
        if (initError != EVRInitError.None || _system is null)
        {
            _system = null;
            _nextInitializationAttemptUtc = DateTime.UtcNow + RetryDelay;
            var detail = OpenVR.GetStringForHmdError(initError);
            _logger.Warning(nameof(OpenVrOverlayRuntime), $"OpenVR init failed: {detail}");
            PublishStatus(OverlayHelperStatusKind.LastError, "OpenVR initialization failed.", detail, force: true);
            return false;
        }

        _overlay = OpenVR.Overlay;
        if (_overlay is null)
        {
            ResetOpenVr(RetryDelay);
            PublishStatus(OverlayHelperStatusKind.LastError, "OpenVR overlay interface is unavailable.", force: true);
            return false;
        }

        _renderer = CreateRenderer(snapshot.Preferences.RenderingMode);
        _nextInitializationAttemptUtc = DateTime.MinValue;
        PublishStatus(OverlayHelperStatusKind.OpenVrReady, "OpenVR overlay runtime initialized.", $"renderer={_renderer.Name}", force: true);
        MarkAllDirty();
        return true;
    }

    private IOverlayTextureRenderer CreateRenderer(VrOverlayRenderingMode mode)
    {
        Func<string, OverlayVisualKind, OverlayBitmapFrame> renderBitmap = RenderBitmapOnSta;
        if (mode == VrOverlayRenderingMode.RawCompatibility)
        {
            PublishStatus(OverlayHelperStatusKind.FallbackRaw, "Using raw overlay texture renderer.");
            return new RawOverlayTextureRenderer(renderBitmap);
        }

        try
        {
            var renderer = new D3D11OverlayTextureRenderer(renderBitmap);
            PublishStatus(OverlayHelperStatusKind.D3DReady, "D3D11 overlay texture renderer initialized.");
            return renderer;
        }
        catch (Exception ex) when (mode == VrOverlayRenderingMode.Auto)
        {
            _logger.Warning(nameof(OpenVrOverlayRuntime), $"D3D11 overlay renderer failed, falling back to raw texture upload: {ex.Message}");
            PublishStatus(OverlayHelperStatusKind.FallbackRaw, "D3D11 failed; using raw overlay texture renderer.", ex.Message, force: true);
            return new RawOverlayTextureRenderer(renderBitmap);
        }
    }

    private OverlayBitmapFrame RenderBitmapOnSta(string text, OverlayVisualKind kind)
    {
        if (_renderDispatcher.CheckAccess())
        {
            return _bitmapFactory.Render(text, kind);
        }

        return _renderDispatcher.Invoke(() => _bitmapFactory.Render(text, kind));
    }

    private void PollOpenVrEvents()
    {
        if (_system is null)
        {
            return;
        }

        var vrEvent = new VREvent_t();
        while (_system.PollNextEvent(ref vrEvent, (uint)Marshal.SizeOf(vrEvent)))
        {
            if ((EVREventType)vrEvent.eventType != EVREventType.VREvent_Quit)
            {
                continue;
            }

            _logger.Info(nameof(OpenVrOverlayRuntime), "SteamVR quit event received. Resetting overlay runtime.");
            PublishStatus(OverlayHelperStatusKind.SteamVrQuit, "SteamVR quit event received.", force: true);
            ResetOpenVr(QuitRetryDelay);
            return;
        }
    }

    private void RenderToastIfNeeded(RuntimeSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.PendingToast))
        {
            return;
        }

        if (!snapshot.ToastDirty && _toastVisible)
        {
            return;
        }

        if (!_handleManager.Ensure(_overlay!, OverlayVisualKind.Toast, out var handle) || _renderer is null)
        {
            return;
        }

        if (snapshot.ToastDirty || !_toastVisible)
        {
            var result = _renderer.Upload(_overlay!, handle, snapshot.PendingToast, OverlayVisualKind.Toast);
            if (!result.Success)
            {
                HandleOverlayError(OverlayVisualKind.Toast, "Upload toast overlay texture", result.Error);
                return;
            }
        }

        if (!_handleManager.Show(_overlay!, OverlayVisualKind.Toast))
        {
            MarkToastDirty();
            return;
        }

        lock (_stateSync)
        {
            _toastDirty = false;
            _pendingToastMessage = snapshot.PendingToast;
            _toastVisibleUntilUtc = snapshot.NowUtc + TimeSpan.FromSeconds(snapshot.Preferences.ToastDurationSeconds);
        }

        _toastVisible = true;
        PublishStatus(OverlayHelperStatusKind.OverlayShown, "Toast overlay shown.", $"renderer={_renderer.Name}");
    }

    private void RenderStatusIfNeeded(RuntimeSnapshot snapshot)
    {
        if (!snapshot.StatusWanted)
        {
            if (_statusVisible)
            {
                _handleManager.Hide(_overlay, OverlayVisualKind.Status);
                _statusVisible = false;
                PublishStatus(OverlayHelperStatusKind.OverlayHidden, "Status overlay hidden.");
            }

            return;
        }

        if (!snapshot.StatusDirty && _statusVisible)
        {
            return;
        }

        if (!_handleManager.Ensure(_overlay!, OverlayVisualKind.Status, out var handle) || _renderer is null)
        {
            return;
        }

        if (snapshot.StatusDirty || !_statusVisible)
        {
            var result = _renderer.Upload(_overlay!, handle, "MASTER ON", OverlayVisualKind.Status);
            if (!result.Success)
            {
                HandleOverlayError(OverlayVisualKind.Status, "Upload status overlay texture", result.Error);
                return;
            }
        }

        if (!_handleManager.Show(_overlay!, OverlayVisualKind.Status))
        {
            MarkStatusDirty();
            return;
        }

        lock (_stateSync)
        {
            _statusDirty = false;
        }

        _statusVisible = true;
        PublishStatus(OverlayHelperStatusKind.OverlayShown, "Status overlay shown.", $"renderer={_renderer.Name}");
    }

    private void HideExpiredToast(DateTime nowUtc)
    {
        if (!_toastVisible || _toastVisibleUntilUtc == DateTime.MinValue || nowUtc < _toastVisibleUntilUtc)
        {
            return;
        }

        _handleManager.Hide(_overlay, OverlayVisualKind.Toast);
        lock (_stateSync)
        {
            _pendingToastMessage = null;
            _toastDirty = false;
            _toastVisibleUntilUtc = DateTime.MinValue;
        }

        _toastVisible = false;
        PublishStatus(OverlayHelperStatusKind.OverlayHidden, "Toast overlay hidden.");
    }

    private void HideAll()
    {
        _handleManager.HideAll(_overlay);
        _toastVisible = false;
        _statusVisible = false;
    }

    private void HandleOverlayError(OverlayVisualKind kind, string operation, EVROverlayError error)
    {
        _logger.Warning(nameof(OpenVrOverlayRuntime), $"{operation} failed: {error}");
        PublishStatus(OverlayHelperStatusKind.LastError, operation, error.ToString(), force: true);
        _handleManager.Destroy(_overlay, kind);
        if (kind == OverlayVisualKind.Toast)
        {
            MarkToastDirty();
            return;
        }

        MarkStatusDirty();
    }

    private void ResetOpenVr(TimeSpan retryDelay)
    {
        try
        {
            _handleManager.DestroyAll(_overlay);
            _renderer?.Dispose();
            if (_system is not null || _overlay is not null)
            {
                OpenVR.Shutdown();
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(nameof(OpenVrOverlayRuntime), $"OpenVR shutdown failed: {ex.Message}");
        }
        finally
        {
            _system = null;
            _overlay = null;
            _renderer = null;
            _toastVisible = false;
            _statusVisible = false;
            _nextInitializationAttemptUtc = DateTime.UtcNow + retryDelay;
            MarkAllDirty();
        }
    }

    private void MarkAllDirty()
    {
        lock (_stateSync)
        {
            _toastDirty = !string.IsNullOrWhiteSpace(_pendingToastMessage);
            _statusDirty = true;
        }
    }

    private void MarkToastDirty()
    {
        lock (_stateSync)
        {
            _toastDirty = true;
        }
    }

    private void MarkStatusDirty()
    {
        lock (_stateSync)
        {
            _statusDirty = true;
        }
    }

    private void PublishStatus(OverlayHelperStatusKind kind, string message, string? detail = null, bool force = false)
    {
        if (!force && _lastStatusKind == kind)
        {
            return;
        }

        _lastStatusKind = kind;
        _publishStatus(new OverlayHelperStatusMessage
        {
            Kind = kind,
            Message = message,
            Detail = detail
        });
    }

    private void Wake()
    {
        _wakeSignal.Set();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Wake();
        if (Thread.CurrentThread != _thread && _thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        ResetOpenVr(TimeSpan.Zero);
        _openVrNativeLibraryService.Dispose();
        _wakeSignal.Dispose();
    }

    private readonly record struct RuntimeSnapshot(
        VrOverlayPreferences Preferences,
        string? PendingToast,
        bool ToastDirty,
        bool StatusWanted,
        bool StatusDirty,
        DateTime NowUtc)
    {
        public bool Enabled => Preferences.Enabled;
        public bool HasWork => !string.IsNullOrWhiteSpace(PendingToast) || StatusWanted;
    }
}
