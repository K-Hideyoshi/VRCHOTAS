using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json;
using Valve.VR;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;
using VRCHOTAS.Services;
using WpfColor = System.Windows.Media.Color;
using WpfFlowDirection = System.Windows.FlowDirection;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

var parentProcessId = TryGetParentProcessId(args);
var logger = new FileAppLogger(fileNameSuffix: "overlay-helper");
using var host = new OverlayHelperHost(logger, new OpenVrNativeLibraryService(logger), parentProcessId);
await host.RunAsync();

static int? TryGetParentProcessId(string[] arguments)
{
    for (var i = 0; i < arguments.Length - 1; i++)
    {
        if (string.Equals(arguments[i], "--parent-pid", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(arguments[i + 1], out var processId))
        {
            return processId;
        }
    }

    return null;
}

internal sealed class OverlayHelperHost : IDisposable
{
    private const string ToastOverlayKey = "vrchotas.overlay.toast";
    private const string StatusOverlayKey = "vrchotas.overlay.status";
    private const string ToastOverlayName = "VRCHOTAS Toast";
    private const string StatusOverlayName = "VRCHOTAS Status";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly IAppLogger _logger;
    private readonly OpenVrNativeLibraryService _openVrNativeLibraryService;
    private readonly Dispatcher _dispatcher;
    private readonly Thread _overlayThread;
    private readonly int? _parentProcessId;
    private readonly object _initializationSync = new();
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _parentWatchdogTimer;
    private readonly DispatcherTimer _initializationRetryTimer;

    private VrOverlayPreferences _preferences = new();
    private DateTime _nextInitializationAttemptUtc = DateTime.MinValue;
    private CVROverlay? _overlayApi;
    private ulong _toastOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;
    private ulong _statusOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;
    private bool _isMasterSwitchOn;
    private bool _initialized;
    private bool _initializationInProgress;
    private bool _disposed;
    private string? _pendingToastMessage;

    public OverlayHelperHost(IAppLogger logger, OpenVrNativeLibraryService openVrNativeLibraryService, int? parentProcessId)
    {
        _logger = logger;
        _openVrNativeLibraryService = openVrNativeLibraryService;
        _parentProcessId = parentProcessId;

        var dispatcherReady = new ManualResetEventSlim(false);
        Dispatcher? dispatcher = null;
        DispatcherTimer? toastTimer = null;
        DispatcherTimer? parentWatchdogTimer = null;
        DispatcherTimer? initializationRetryTimer = null;
        Exception? startupException = null;

        _overlayThread = new Thread(() =>
        {
            try
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                toastTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                toastTimer.Tick += (_, _) => StopToastInternal();
                parentWatchdogTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                parentWatchdogTimer.Tick += OnParentWatchdogTick;
                initializationRetryTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                initializationRetryTimer.Tick += OnInitializationRetryTick;
            }
            catch (Exception ex)
            {
                startupException = ex;
            }
            finally
            {
                dispatcherReady.Set();
            }

            if (startupException is null)
            {
                Dispatcher.Run();
            }
        })
        {
            IsBackground = true,
            Name = "VRCHOTAS Overlay Helper"
        };

        _overlayThread.SetApartmentState(ApartmentState.STA);
        _overlayThread.Start();
        dispatcherReady.Wait();

        if (startupException is not null)
        {
            throw new InvalidOperationException("Failed to start overlay helper thread.", startupException);
        }

        _dispatcher = dispatcher ?? throw new InvalidOperationException("Overlay helper dispatcher was not initialized.");
        _toastTimer = toastTimer ?? throw new InvalidOperationException("Overlay helper toast timer was not initialized.");
        _parentWatchdogTimer = parentWatchdogTimer ?? throw new InvalidOperationException("Overlay helper parent watchdog timer was not initialized.");
        _initializationRetryTimer = initializationRetryTimer ?? throw new InvalidOperationException("Overlay helper initialization retry timer was not initialized.");
    }

    public async Task RunAsync()
    {
        _logger.Info(nameof(OverlayHelperHost), $"Overlay helper started. Parent PID: {_parentProcessId?.ToString() ?? "<none>"}. Log file: '{_logger.CurrentLogFilePath}'.");
        _parentWatchdogTimer.Start();
        _initializationRetryTimer.Start();

        while (!_disposed)
        {
            using var server = new NamedPipeServerStream(OverlayHelperProtocol.PipeName, PipeDirection.In, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync();
            _logger.Info(nameof(OverlayHelperHost), "Overlay helper client connected.");

            using var reader = new StreamReader(server);
            while (!_disposed && server.IsConnected)
            {
                var line = await reader.ReadLineAsync();
                if (line is null)
                {
                    _logger.Info(nameof(OverlayHelperHost), "Overlay helper pipe disconnected. Exiting helper process.");
                    Dispose();
                    break;
                }

                try
                {
                    var message = JsonConvert.DeserializeObject<OverlayHelperMessage>(line);
                    if (message is null)
                    {
                        continue;
                    }

                    if (string.Equals(message.Type, OverlayHelperMessageType.Shutdown, StringComparison.Ordinal))
                    {
                        Dispose();
                        return;
                    }

                    InvokeOnDispatcher(() => HandleMessageInternal(message), synchronous: true);
                }
                catch (Exception ex)
                {
                    _logger.Warning(nameof(OverlayHelperHost), $"Failed to process helper message: {ex.Message}");
                }
            }
        }
    }

    private void HandleMessageInternal(OverlayHelperMessage message)
    {
        switch (message.Type)
        {
            case OverlayHelperMessageType.ApplyPreferences:
                ApplyPreferencesInternal(message);
                break;
            case OverlayHelperMessageType.ShowMasterSwitchToast:
                ShowToastInternal(message.IsMasterSwitchOn == true ? "Master Switch ON" : "Master Switch OFF");
                break;
            case OverlayHelperMessageType.ShowConfigurationToast:
                if (!string.IsNullOrWhiteSpace(message.ConfigurationFileName))
                {
                    ShowToastInternal($"Configuration: {message.ConfigurationFileName}");
                }
                break;
            case OverlayHelperMessageType.UpdateStatusIndicator:
                UpdateStatusIndicatorInternal(message.IsMasterSwitchOn == true);
                break;
        }
    }

    private void ApplyPreferencesInternal(OverlayHelperMessage message)
    {
        _preferences.Enabled = message.Enabled ?? _preferences.Enabled;
        _preferences.StatusIndicatorEnabled = message.StatusIndicatorEnabled ?? _preferences.StatusIndicatorEnabled;
        if (message.ToastDurationSeconds is int toastDurationSeconds)
        {
            _preferences.ToastDurationSeconds = toastDurationSeconds;
        }

        _preferences.Normalize();
        _isMasterSwitchOn = message.IsMasterSwitchOn == true;

        if (!_preferences.Enabled)
        {
            _pendingToastMessage = null;
            StopToastInternal();
            HideOverlayInternal(_statusOverlayHandle);
            return;
        }

        UpdateStatusIndicatorInternal(_isMasterSwitchOn);
    }

    private void ShowToastInternal(string message)
    {
        if (!_preferences.Enabled || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _pendingToastMessage = message;
        if (!TryEnsureInitializedInternal())
        {
            return;
        }

        if (!ShowToastOverlayInternal(message))
        {
            return;
        }

        _pendingToastMessage = null;
    }

    private bool ShowToastOverlayInternal(string message)
    {
        StopToastInternal();
        if (!UpdateOverlayTextureInternal(_toastOverlayHandle, message, OverlayVisualKind.Toast))
        {
            return false;
        }

        SetToastPlacementInternal();
        var error = GetOverlayApi().ShowOverlay(_toastOverlayHandle);
        if (error != EVROverlayError.None)
        {
            LogOverlayError("Show toast overlay", error);
            return false;
        }

        _toastTimer.Interval = TimeSpan.FromSeconds(_preferences.ToastDurationSeconds);
        _toastTimer.Start();
        return true;
    }

    private bool HasPendingOverlayWork()
    {
        if (!_preferences.Enabled)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_pendingToastMessage))
        {
            return true;
        }

        return _preferences.StatusIndicatorEnabled && _isMasterSwitchOn;
    }

    private void UpdateStatusIndicatorInternal(bool isMasterSwitchOn)
    {
        _isMasterSwitchOn = isMasterSwitchOn;
        if (!_preferences.Enabled || !_preferences.StatusIndicatorEnabled || !isMasterSwitchOn)
        {
            HideOverlayInternal(_statusOverlayHandle);
            return;
        }

        if (!TryEnsureInitializedInternal())
        {
            return;
        }

        if (!UpdateOverlayTextureInternal(_statusOverlayHandle, "MASTER ON", OverlayVisualKind.Status))
        {
            return;
        }

        SetStatusPlacementInternal();
        var error = GetOverlayApi().ShowOverlay(_statusOverlayHandle);
        if (error != EVROverlayError.None)
        {
            LogOverlayError("Show status overlay", error);
        }
    }

    private bool TryEnsureInitializedInternal()
    {
        if (_initialized)
        {
            return true;
        }

        if (!HasPendingOverlayWork())
        {
            return false;
        }

        if (DateTime.UtcNow < _nextInitializationAttemptUtc)
        {
            return false;
        }

        if (_initializationInProgress)
        {
            return false;
        }

        try
        {
            if (!_openVrNativeLibraryService.IsSteamVrRunning())
            {
                _nextInitializationAttemptUtc = DateTime.UtcNow + RetryDelay;
                return false;
            }

            if (!_openVrNativeLibraryService.TryEnsureLoaded(out var failureMessage))
            {
                _logger.Warning(nameof(OverlayHelperHost), failureMessage);
                _nextInitializationAttemptUtc = DateTime.UtcNow + RetryDelay;
                return false;
            }

            StartInitializationAttempt();
            return false;
        }
        catch (Exception ex)
        {
            _logger.Warning(nameof(OverlayHelperHost), $"Helper overlay initialization failed: {ex.Message}");
            _nextInitializationAttemptUtc = DateTime.UtcNow + RetryDelay;
            return false;
        }
    }

    private void StartInitializationAttempt()
    {
        lock (_initializationSync)
        {
            if (_disposed || _initialized || _initializationInProgress)
            {
                return;
            }

            _initializationInProgress = true;
        }

        _logger.Info(nameof(OverlayHelperHost), "Scheduling OpenVR initialization attempt on a dedicated MTA thread.");

        var initializationThread = new Thread(() =>
        {
            try
            {
                string runtimePath;
                try
                {
                    runtimePath = OpenVR.RuntimePath();
                }
                catch (Exception ex)
                {
                    runtimePath = $"<unavailable: {ex.Message}>";
                }

                _logger.Info(nameof(OverlayHelperHost), $"OpenVR runtime diagnostics: installed={OpenVR.IsRuntimeInstalled()}, hmdPresent={OpenVR.IsHmdPresent()}, runtimePath='{runtimePath}'.");

                var initError = EVRInitError.None;
                _logger.Info(nameof(OverlayHelperHost), "Calling OpenVR.Init for overlay application on MTA thread.");
                var system = OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Overlay);
                _logger.Info(nameof(OverlayHelperHost), $"OpenVR.Init returned: {OpenVR.GetStringForHmdError(initError)}");
                if (initError != EVRInitError.None || system is null)
                {
                    _logger.Warning(nameof(OverlayHelperHost), $"OpenVR init failed: {OpenVR.GetStringForHmdError(initError)}");
                    _nextInitializationAttemptUtc = DateTime.UtcNow + RetryDelay;
                    return;
                }

                InvokeOnDispatcher(CompleteInitializationOnDispatcher, synchronous: true);
            }
            catch (Exception ex)
            {
                _logger.Warning(nameof(OverlayHelperHost), $"Helper overlay initialization failed on MTA thread: {ex.Message}");
                _nextInitializationAttemptUtc = DateTime.UtcNow + RetryDelay;
            }
            finally
            {
                lock (_initializationSync)
                {
                    _initializationInProgress = false;
                }
            }
        })
        {
            IsBackground = true,
            Name = "VRCHOTAS Overlay Init"
        };

        initializationThread.SetApartmentState(ApartmentState.MTA);
        initializationThread.Start();
    }

    private void CompleteInitializationOnDispatcher()
    {
        if (_disposed)
        {
            OpenVR.Shutdown();
            return;
        }

        _logger.Info(nameof(OverlayHelperHost), "Resolving OpenVR overlay interface.");
        _overlayApi = OpenVR.Overlay;
        if (_overlayApi is null)
        {
            _logger.Warning(nameof(OverlayHelperHost), "OpenVR overlay interface is unavailable.");
            OpenVR.Shutdown();
            _nextInitializationAttemptUtc = DateTime.UtcNow + RetryDelay;
            return;
        }

        _logger.Info(nameof(OverlayHelperHost), "OpenVR overlay interface resolved.");
        _logger.Info(nameof(OverlayHelperHost), "Ensuring toast and status overlay handles.");
        if (!EnsureOverlayHandleInternal(ToastOverlayKey, ToastOverlayName, ref _toastOverlayHandle, OverlayVisualKind.Toast)
            || !EnsureOverlayHandleInternal(StatusOverlayKey, StatusOverlayName, ref _statusOverlayHandle, OverlayVisualKind.Status))
        {
            OpenVR.Shutdown();
            _overlayApi = null;
            _nextInitializationAttemptUtc = DateTime.UtcNow + RetryDelay;
            return;
        }

        _nextInitializationAttemptUtc = DateTime.MinValue;
        _initialized = true;
        _logger.Info(nameof(OverlayHelperHost), "OpenVR overlay runtime initialized in helper process.");
        RestoreOverlayStateAfterInitialization();
    }

    private void RestoreOverlayStateAfterInitialization()
    {
        _logger.Info(nameof(OverlayHelperHost), $"Restoring overlay state after initialization. MasterSwitch={_isMasterSwitchOn}, StatusEnabled={_preferences.StatusIndicatorEnabled}, PendingToast={!string.IsNullOrWhiteSpace(_pendingToastMessage)}.");

        UpdateStatusIndicatorInternal(_isMasterSwitchOn);

        if (!string.IsNullOrWhiteSpace(_pendingToastMessage) && ShowToastOverlayInternal(_pendingToastMessage))
        {
            _logger.Info(nameof(OverlayHelperHost), "Pending toast overlay restored successfully.");
            _pendingToastMessage = null;
        }
    }

    private bool EnsureOverlayHandleInternal(string key, string name, ref ulong handle, OverlayVisualKind kind)
    {
        if (handle != OpenVR.k_ulOverlayHandleInvalid)
        {
            return true;
        }

        var overlay = GetOverlayApi();
        var error = overlay.FindOverlay(key, ref handle);
        if (error == EVROverlayError.None && handle != OpenVR.k_ulOverlayHandleInvalid)
        {
            ConfigureOverlayInternal(handle, kind);
            return true;
        }

        handle = OpenVR.k_ulOverlayHandleInvalid;
        error = overlay.CreateOverlay(key, name, ref handle);
        if (error != EVROverlayError.None || handle == OpenVR.k_ulOverlayHandleInvalid)
        {
            LogOverlayError($"Create overlay '{key}'", error);
            _nextInitializationAttemptUtc = DateTime.UtcNow + RetryDelay;
            return false;
        }

        ConfigureOverlayInternal(handle, kind);
        return true;
    }

    private void ConfigureOverlayInternal(ulong handle, OverlayVisualKind kind)
    {
        var overlay = GetOverlayApi();
        overlay.SetOverlayInputMethod(handle, VROverlayInputMethod.None);
        overlay.SetOverlayTexelAspect(handle, 1f);
        overlay.SetOverlayAlpha(handle, 1f);
        overlay.SetOverlayColor(handle, 1f, 1f, 1f);
        overlay.SetOverlayWidthInMeters(handle, kind == OverlayVisualKind.Toast ? 0.58f : 0.22f);
    }

    private void SetToastPlacementInternal()
    {
        var transform = CreateTransform(0f, -0.34f, -0.86f);
        var error = GetOverlayApi().SetOverlayTransformTrackedDeviceRelative(_toastOverlayHandle, OpenVR.k_unTrackedDeviceIndex_Hmd, ref transform);
        if (error != EVROverlayError.None)
        {
            LogOverlayError("Position toast overlay", error);
        }
    }

    private void SetStatusPlacementInternal()
    {
        var transform = CreateTransform(-0.28f, -0.27f, -0.8f);
        var error = GetOverlayApi().SetOverlayTransformTrackedDeviceRelative(_statusOverlayHandle, OpenVR.k_unTrackedDeviceIndex_Hmd, ref transform);
        if (error != EVROverlayError.None)
        {
            LogOverlayError("Position status overlay", error);
        }
    }

    private bool UpdateOverlayTextureInternal(ulong handle, string text, OverlayVisualKind kind)
    {
        if (handle == OpenVR.k_ulOverlayHandleInvalid)
        {
            return false;
        }

        var bitmap = RenderOverlayBitmap(text, kind);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var gcHandle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var error = GetOverlayApi().SetOverlayRaw(handle, gcHandle.AddrOfPinnedObject(), (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 4);
            if (error != EVROverlayError.None)
            {
                LogOverlayError("Upload overlay texture", error);
                return false;
            }

            return true;
        }
        finally
        {
            gcHandle.Free();
        }
    }

    private static RenderTargetBitmap RenderOverlayBitmap(string text, OverlayVisualKind kind)
    {
        var fontFamily = new WpfFontFamily("Segoe UI");
        var typeface = new Typeface(fontFamily, System.Windows.FontStyles.Normal, System.Windows.FontWeights.SemiBold, System.Windows.FontStretches.Normal);
        var fontSize = kind == OverlayVisualKind.Toast ? 42d : 30d;
        var horizontalPadding = kind == OverlayVisualKind.Toast ? 36d : 24d;
        var verticalPadding = kind == OverlayVisualKind.Toast ? 24d : 16d;
        var maxTextWidth = kind == OverlayVisualKind.Toast ? 980d : 380d;
        var formattedText = new FormattedText(text, CultureInfo.InvariantCulture, WpfFlowDirection.LeftToRight, typeface, fontSize, System.Windows.Media.Brushes.White, 1d)
        {
            MaxTextWidth = maxTextWidth,
            Trimming = System.Windows.TextTrimming.CharacterEllipsis,
            TextAlignment = kind == OverlayVisualKind.Toast ? System.Windows.TextAlignment.Center : System.Windows.TextAlignment.Left
        };

        var width = (int)Math.Ceiling(Math.Max(formattedText.Width + (horizontalPadding * 2d), kind == OverlayVisualKind.Toast ? 420d : 200d));
        var height = (int)Math.Ceiling(Math.Max(formattedText.Height + (verticalPadding * 2d), kind == OverlayVisualKind.Toast ? 116d : 72d));
        var background = kind == OverlayVisualKind.Toast
            ? new SolidColorBrush(WpfColor.FromArgb(220, 20, 20, 24))
            : new SolidColorBrush(WpfColor.FromArgb(220, 0, 80, 0));
        var border = kind == OverlayVisualKind.Toast
            ? new WpfPen(new SolidColorBrush(WpfColor.FromArgb(200, 120, 180, 255)), 3)
            : new WpfPen(new SolidColorBrush(WpfColor.FromArgb(230, 190, 255, 190)), 3);
        background.Freeze();
        border.Freeze();

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRoundedRectangle(background, border, new System.Windows.Rect(0, 0, width, height), 20, 20);
            var textPoint = new WpfPoint(kind == OverlayVisualKind.Toast ? (width - formattedText.WidthIncludingTrailingWhitespace) / 2d : horizontalPadding, (height - formattedText.Height) / 2d);
            context.DrawText(formattedText, textPoint);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private void StopToastInternal()
    {
        _toastTimer.Stop();
        HideOverlayInternal(_toastOverlayHandle);
    }

    private void HideOverlayInternal(ulong handle)
    {
        if (!_initialized || handle == OpenVR.k_ulOverlayHandleInvalid)
        {
            return;
        }

        try
        {
            var error = GetOverlayApi().HideOverlay(handle);
            if (error is not EVROverlayError.None and not EVROverlayError.InvalidHandle)
            {
                LogOverlayError("Hide overlay", error);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(nameof(OverlayHelperHost), $"Failed to hide overlay: {ex.Message}");
        }
    }

    private void DestroyOverlayInternal(ref ulong handle)
    {
        if (!_initialized || handle == OpenVR.k_ulOverlayHandleInvalid)
        {
            handle = OpenVR.k_ulOverlayHandleInvalid;
            return;
        }

        try
        {
            var error = GetOverlayApi().DestroyOverlay(handle);
            if (error is not EVROverlayError.None and not EVROverlayError.InvalidHandle)
            {
                LogOverlayError("Destroy overlay", error);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(nameof(OverlayHelperHost), $"Failed to destroy overlay: {ex.Message}");
        }
        finally
        {
            handle = OpenVR.k_ulOverlayHandleInvalid;
        }
    }

    private void LogOverlayError(string operation, EVROverlayError error)
    {
        if (error != EVROverlayError.None)
        {
            _logger.Warning(nameof(OverlayHelperHost), $"{operation} failed: {error}");
        }
    }

    private CVROverlay GetOverlayApi()
    {
        return _overlayApi ?? throw new InvalidOperationException("OpenVR overlay interface is not initialized.");
    }

    private void InvokeOnDispatcher(Action action, bool synchronous = false)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        if (synchronous)
        {
            _dispatcher.Invoke(action);
            return;
        }

        _dispatcher.BeginInvoke(action);
    }

    private static HmdMatrix34_t CreateTransform(float x, float y, float z)
    {
        return new HmdMatrix34_t
        {
            m0 = 1f,
            m1 = 0f,
            m2 = 0f,
            m3 = x,
            m4 = 0f,
            m5 = 1f,
            m6 = 0f,
            m7 = y,
            m8 = 0f,
            m9 = 0f,
            m10 = 1f,
            m11 = z
        };
    }

    private void OnParentWatchdogTick(object? sender, EventArgs e)
    {
        if (_parentProcessId is null)
        {
            return;
        }

        try
        {
            var process = Process.GetProcessById(_parentProcessId.Value);
            if (process.HasExited)
            {
                _logger.Info(nameof(OverlayHelperHost), $"Parent process {_parentProcessId.Value} has exited. Shutting down helper.");
                Dispose();
            }
        }
        catch (ArgumentException)
        {
            _logger.Info(nameof(OverlayHelperHost), $"Parent process {_parentProcessId.Value} was not found. Shutting down helper.");
            Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning(nameof(OverlayHelperHost), $"Parent process watchdog failed: {ex.Message}");
        }
    }

    private void OnInitializationRetryTick(object? sender, EventArgs e)
    {
        if (_disposed || _initialized || !HasPendingOverlayWork())
        {
            return;
        }

        if (!TryEnsureInitializedInternal())
        {
            return;
        }

        _logger.Info(nameof(OverlayHelperHost), "Overlay helper retry initialization succeeded. Restoring overlay state.");
        RestoreOverlayStateAfterInitialization();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        InvokeOnDispatcher(() =>
        {
            _initializationRetryTimer.Stop();
            _parentWatchdogTimer.Stop();
            _toastTimer.Stop();
            DestroyOverlayInternal(ref _toastOverlayHandle);
            DestroyOverlayInternal(ref _statusOverlayHandle);
            if (_initialized)
            {
                OpenVR.Shutdown();
                _initialized = false;
                _overlayApi = null;
            }

            _openVrNativeLibraryService.Dispose();
            _dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        }, synchronous: true);

        if (Thread.CurrentThread != _overlayThread && _overlayThread.IsAlive)
        {
            _overlayThread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private enum OverlayVisualKind
    {
        Toast,
        Status
    }
}
