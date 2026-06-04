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
    private OverlayHelperStatusKind? _lastStatusKind;

    // OpenVR.Init persistent background thread state
    // Once an Init thread is launched, we never launch another until it returns.
    // The thread may block indefinitely inside OpenVR IPC �?that is expected and safe.
    private Thread? _initThread;
    private volatile InitState _initState = InitState.Idle;
    private CVRSystem? _initResultSystem;
    private EVRInitError _initResultError = EVRInitError.None;
    private EVRApplicationType _initResultAppType = EVRApplicationType.VRApplication_Overlay;

    private enum InitState
    {
        Idle,       // No init in progress, not yet started
        Running,    // Init thread launched, waiting for OpenVR.Init to return
        Succeeded,  // OpenVR.Init returned with no error
        Failed,     // OpenVR.Init returned with an error
    }

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

    private RuntimeSnapshot? _lastSnapshot;

    private void Tick()
    {
        var snapshot = CreateSnapshot();
        _lastSnapshot = snapshot;
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
        // Already fully initialized
        if (_system is not null && _overlay is not null && _renderer is not null)
        {
            return true;
        }

        // An Init thread is already running �?check if it has finished
        if (_initState == InitState.Running)
        {
            PublishStatus(OverlayHelperStatusKind.WaitingForSteamVR, "Waiting for OpenVR.Init()...");
            return false;
        }

        // Init returned with a result �?process it
        if (_initState == InitState.Succeeded || _initState == InitState.Failed)
        {
            var resultSystem = _initResultSystem;
            var resultError  = _initResultError;
            _initState       = InitState.Idle;
            _initThread      = null;
            _initResultSystem = null;
            _initResultError  = EVRInitError.None;

            var errorCode = (int)resultError;
            var errorName = OpenVR.GetStringForHmdError(resultError);

            if (resultError != EVRInitError.None || resultSystem is null)
            {
                _logger.Warning(nameof(OpenVrOverlayRuntime),
                    $"�?OpenVR.Init failed: {errorName} (code {errorCode}). Will retry next tick.");
                PublishStatus(OverlayHelperStatusKind.LastError,
                    "OpenVR initialization failed.",
                    $"{errorName} (error {errorCode})",
                    force: true);
                return false;
            }

            // Init succeeded �?finish the rest of initialization synchronously
            _logger.Info(nameof(OpenVrOverlayRuntime),
                $"✓✓�?OpenVR.Init succeeded! (appType={_initResultAppType})");
            _system = resultSystem;

            _logger.Info(nameof(OpenVrOverlayRuntime), "Retrieving OpenVR.Overlay interface...");
            _overlay = OpenVR.Overlay;
            if (_overlay is null)
            {
                _logger.Warning(nameof(OpenVrOverlayRuntime), "�?Failed: OpenVR.Overlay interface is null");
                ResetOpenVr(RetryDelay);
                PublishStatus(OverlayHelperStatusKind.LastError, "OpenVR overlay interface is unavailable.", force: true);
                return false;
            }
            _logger.Info(nameof(OpenVrOverlayRuntime), "�?OpenVR.Overlay interface obtained");

            _logger.Info(nameof(OpenVrOverlayRuntime),
                "Creating texture renderer...");
            try
            {
                _renderer = CreateRenderer();
                _logger.Info(nameof(OpenVrOverlayRuntime), $"�?Renderer created: {_renderer.Name}");
            }
            catch (Exception ex)
            {
                _logger.Warning(nameof(OpenVrOverlayRuntime),
                    $"�?Failed to create renderer: {ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}");
                ResetOpenVr(RetryDelay);
                PublishStatus(OverlayHelperStatusKind.LastError,
                    "Failed to create texture renderer.", ex.Message, force: true);
                return false;
            }

            _logger.Info(nameof(OpenVrOverlayRuntime),
                $"=== Initialization Complete: Overlay runtime ready with {_renderer.Name} ===");
            PublishStatus(OverlayHelperStatusKind.OpenVrReady,
                "OpenVR overlay runtime initialized.",
                $"renderer={_renderer.Name}",
                force: true);
            MarkAllDirty();
            return true;
        }

        // State is Idle �?check prerequisites then launch the Init thread
        if (!_openVrNativeLibraryService.IsSteamVrRunning())
        {
            PublishStatus(OverlayHelperStatusKind.WaitingForSteamVR, "Waiting for SteamVR.");
            return false;
        }

        if (!_openVrNativeLibraryService.TryEnsureLoaded(out var failureMessage))
        {
            _logger.Warning(nameof(OpenVrOverlayRuntime),
                $"Init failed at DLL loading stage: {failureMessage}");
            PublishStatus(OverlayHelperStatusKind.LastError, "openvr_api.dll could not be loaded.", failureMessage, force: true);
            return false;
        }

        // Launch the Init thread.
        // Strategy: first try VRApplication_Background which connects immediately without
        // waiting for the IPC namespace slot. If that succeeds and the Overlay interface is
        // available we use it. Only if Background itself fails do we fall back to
        // VRApplication_Overlay (which can block for minutes while waiting for the IPC slot).
        _logger.Info(nameof(OpenVrOverlayRuntime), ">>> Launching OpenVR.Init background thread (trying Background first, then Overlay)...");
        _initState = InitState.Running;
        _initThread = new Thread(() =>
        {
            int tid = Environment.CurrentManagedThreadId;
            var err = EVRInitError.None;
            CVRSystem? sys = null;
            EVRApplicationType usedType = EVRApplicationType.VRApplication_Background;

            // --- Attempt 1: VRApplication_Background (non-blocking) ---
            _logger.Info(nameof(OpenVrOverlayRuntime),
                $"Init thread {tid}: trying OpenVR.Init(VRApplication_Background)...");
            try
            {
                sys = OpenVR.Init(ref err, EVRApplicationType.VRApplication_Background);
                _logger.Info(nameof(OpenVrOverlayRuntime),
                    $"Init thread {tid}: VRApplication_Background returned error={err} ({OpenVR.GetStringForHmdError(err)})");
            }
            catch (Exception ex)
            {
                _logger.Warning(nameof(OpenVrOverlayRuntime),
                    $"Init thread {tid}: VRApplication_Background threw {ex.GetType().Name}: {ex.Message}");
                err = EVRInitError.Init_Internal;
                sys = null;
            }

            if (err == EVRInitError.None && sys is not null)
            {
                // Verify that the Overlay interface is accessible with Background type.
                // Background mode always supports IVROverlay; if for some reason it doesn't,
                // fall through to the Overlay type below.
                var overlayCheck = OpenVR.Overlay;
                if (overlayCheck is not null)
                {
                    _logger.Info(nameof(OpenVrOverlayRuntime),
                        $"Init thread {tid}: VRApplication_Background succeeded and Overlay interface is available. Using Background type.");
                    usedType = EVRApplicationType.VRApplication_Background;
                    _initResultSystem  = sys;
                    _initResultError   = EVRInitError.None;
                    _initResultAppType = usedType;
                    _initState         = InitState.Succeeded;
                    Wake();
                    return;
                }

                _logger.Warning(nameof(OpenVrOverlayRuntime),
                    $"Init thread {tid}: VRApplication_Background init succeeded but Overlay interface is null. Shutting down and retrying as VRApplication_Overlay.");
                try { OpenVR.Shutdown(); } catch { /* ignore */ }
                sys = null;
                err = EVRInitError.Init_Internal;
            }
            else
            {
                _logger.Warning(nameof(OpenVrOverlayRuntime),
                    $"Init thread {tid}: VRApplication_Background failed (error={err}). Falling back to VRApplication_Overlay (may block).");
                try { OpenVR.Shutdown(); } catch { /* ignore */ }
                sys = null;
                System.Threading.Thread.Sleep(500); // Give OS/OpenVR time to clean up IPC mutexes
            }

            // --- Attempt 2: VRApplication_Overlay (may block waiting for IPC namespace) ---
            usedType = EVRApplicationType.VRApplication_Overlay;
            err = EVRInitError.None;
            _logger.Info(nameof(OpenVrOverlayRuntime),
                $"Init thread {tid}: trying OpenVR.Init(VRApplication_Overlay) [may block]...");
            try
            {
                sys = OpenVR.Init(ref err, EVRApplicationType.VRApplication_Overlay);
                _logger.Info(nameof(OpenVrOverlayRuntime),
                    $"Init thread {tid}: VRApplication_Overlay returned error={err} ({OpenVR.GetStringForHmdError(err)})");
            }
            catch (Exception ex)
            {
                _logger.Warning(nameof(OpenVrOverlayRuntime),
                    $"Init thread {tid}: VRApplication_Overlay threw {ex.GetType().Name}: {ex.Message}");
                err = EVRInitError.Init_Internal;
                sys = null;
            }

            _initResultSystem  = sys;
            _initResultError   = err;
            _initResultAppType = usedType;
            _initState         = sys is not null && err == EVRInitError.None
                ? InitState.Succeeded
                : InitState.Failed;
            Wake();
        })
        {
            IsBackground = true,
            Name = "VRCHOTAS OpenVR Init"
        };
        _initThread.SetApartmentState(ApartmentState.MTA);
        _initThread.Start();

        PublishStatus(OverlayHelperStatusKind.WaitingForSteamVR, "Connecting to SteamVR OpenVR runtime...");
        return false;
    }


    private IOverlayTextureRenderer CreateRenderer()
    {
        _logger.Info(nameof(OpenVrOverlayRuntime), 
            "CreateRenderer called");

        Func<string, OverlayVisualKind, OverlayBitmapFrame> renderBitmap = RenderBitmapOnSta;

        _logger.Info(nameof(OpenVrOverlayRuntime), 
            "Attempting to create D3D11OverlayTextureRenderer...");
        try
        {
            var renderer = new D3D11OverlayTextureRenderer(renderBitmap);
            _logger.Info(nameof(OpenVrOverlayRuntime), 
                "D3D11OverlayTextureRenderer created successfully");
            PublishStatus(OverlayHelperStatusKind.D3DReady, "D3D11 overlay texture renderer initialized.");
            return renderer;
        }
        catch (Exception ex)
        {
            _logger.Warning(nameof(OpenVrOverlayRuntime), 
                $"D3D11 renderer creation failed, falling back to raw: {ex.GetType().Name} - {ex.Message}");
            _logger.Info(nameof(OpenVrOverlayRuntime), 
                $"Exception details:\n{ex.StackTrace}");
            PublishStatus(OverlayHelperStatusKind.FallbackRaw, "D3D11 failed; using raw overlay texture renderer.", ex.Message, force: true);
            return new RawOverlayTextureRenderer(renderBitmap);
        }

        _logger.Info(nameof(OpenVrOverlayRuntime), 
            "Attempting to create D3D11OverlayTextureRenderer...");
        try
        {
            var renderer = new D3D11OverlayTextureRenderer(renderBitmap);
            _logger.Info(nameof(OpenVrOverlayRuntime), 
                "�?D3D11OverlayTextureRenderer created successfully");
            PublishStatus(OverlayHelperStatusKind.D3DReady, "D3D11 overlay texture renderer initialized.");
            return renderer;
        }
        catch (Exception ex)
        {
            _logger.Warning(nameof(OpenVrOverlayRuntime), 
                $"D3D11 renderer creation failed (Auto mode), falling back to raw: {ex.GetType().Name} - {ex.Message}");
            _logger.Info(nameof(OpenVrOverlayRuntime), 
                $"Exception details:\n{ex.StackTrace}");
            PublishStatus(OverlayHelperStatusKind.FallbackRaw, "D3D11 failed; using raw overlay texture renderer.", ex.Message, force: true);
            return new RawOverlayTextureRenderer(renderBitmap);
        }
    }

    private OverlayBitmapFrame RenderBitmapOnSta(string text, OverlayVisualKind kind)
    {
        if (_renderDispatcher.CheckAccess())
        {
            var prefs = _lastSnapshot?.Preferences;
            return _bitmapFactory.Render(text, kind, prefs);
        }

        return _renderDispatcher.Invoke(() => _bitmapFactory.Render(text, kind, _lastSnapshot?.Preferences));
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

        _logger.Info(nameof(OpenVrOverlayRuntime), 
            $"Toast: Rendering \"{snapshot.PendingToast}\" (dirty={snapshot.ToastDirty}, visible={_toastVisible})");

        if (!_handleManager.Ensure(_overlay!, OverlayVisualKind.Toast, out var handle, snapshot.Preferences) || _renderer is null)
        {
            _logger.Warning(nameof(OpenVrOverlayRuntime), 
                "Toast: Failed to ensure overlay handle or renderer is null");
            return;
        }

        if (snapshot.ToastDirty || !_toastVisible)
        {
            _logger.Info(nameof(OpenVrOverlayRuntime), 
                $"Toast: Uploading texture via {_renderer.Name}...");
            var result = _renderer.Upload(_overlay!, handle, snapshot.PendingToast, OverlayVisualKind.Toast);
            if (!result.Success)
            {
                _logger.Warning(nameof(OpenVrOverlayRuntime), 
                    $"Toast: Upload failed: {result.Error}");
                HandleOverlayError(OverlayVisualKind.Toast, "Upload toast overlay texture", result.Error);
                return;
            }
            _logger.Info(nameof(OpenVrOverlayRuntime), 
                "Toast: �?Texture uploaded");
        }

        _logger.Info(nameof(OpenVrOverlayRuntime), 
            "Toast: Showing overlay...");
        if (!_handleManager.Show(_overlay!, OverlayVisualKind.Toast, _lastSnapshot?.Preferences))
        {
            _logger.Warning(nameof(OpenVrOverlayRuntime), 
                "Toast: Failed to show overlay");
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
        _logger.Info(nameof(OpenVrOverlayRuntime), 
            $"Toast: �?Shown successfully, expires at {_toastVisibleUntilUtc:HH:mm:ss}");
        PublishStatus(OverlayHelperStatusKind.OverlayShown, "Toast overlay shown.", $"renderer={_renderer.Name}");
    }

    private void RenderStatusIfNeeded(RuntimeSnapshot snapshot)
    {
        if (!snapshot.StatusWanted)
        {
            if (_statusVisible)
            {
                _logger.Info(nameof(OpenVrOverlayRuntime), 
                    "Status: Hiding (not wanted)");
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

        _logger.Info(nameof(OpenVrOverlayRuntime), 
            $"Status: Rendering (dirty={snapshot.StatusDirty}, visible={_statusVisible})");

        if (!_handleManager.Ensure(_overlay!, OverlayVisualKind.Status, out var handle, snapshot.Preferences) || _renderer is null)
        {
            _logger.Warning(nameof(OpenVrOverlayRuntime), 
                "Status: Failed to ensure overlay handle or renderer is null");
            return;
        }

        if (snapshot.StatusDirty || !_statusVisible)
        {
            _logger.Info(nameof(OpenVrOverlayRuntime), 
                $"Status: Uploading texture via {_renderer.Name}...");
            var result = _renderer.Upload(_overlay!, handle, "MASTER ON", OverlayVisualKind.Status);
            if (!result.Success)
            {
                _logger.Warning(nameof(OpenVrOverlayRuntime), 
                    $"Status: Upload failed: {result.Error}");
                HandleOverlayError(OverlayVisualKind.Status, "Upload status overlay texture", result.Error);
                return;
            }
            _logger.Info(nameof(OpenVrOverlayRuntime), 
                "Status: �?Texture uploaded");
        }

        _logger.Info(nameof(OpenVrOverlayRuntime), 
            "Status: Showing overlay...");
        if (!_handleManager.Show(_overlay!, OverlayVisualKind.Status, snapshot.Preferences))
        {
            _logger.Warning(nameof(OpenVrOverlayRuntime), 
                "Status: Failed to show overlay");
            MarkStatusDirty();
            return;
        }

        lock (_stateSync)
        {
            _statusDirty = false;
        }

        _statusVisible = true;
        _logger.Info(nameof(OpenVrOverlayRuntime), 
            "Status: �?Shown successfully");
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
            // If an init thread is still running it will complete and set its result.
            // Don't touch _initState here so the state machine stays consistent;
            // the thread will wake us when it finishes.
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


