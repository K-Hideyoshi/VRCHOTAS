using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows.Threading;
using Newtonsoft.Json;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;
using VRCHOTAS.Services;

internal sealed class OverlayHelperHost : IDisposable
{
    private readonly IAppLogger _logger;
    private readonly int? _parentProcessId;
    private readonly Dispatcher _renderDispatcher;
    private readonly Thread _renderThread;
    private readonly OpenVrOverlayRuntime _runtime;
    private readonly object _statusWriterSync = new();
    private readonly System.Threading.Timer _parentWatchdogTimer;

    private StreamWriter? _statusWriter;
    private bool _disposed;

    public OverlayHelperHost(IAppLogger logger, OpenVrNativeLibraryService openVrNativeLibraryService, int? parentProcessId)
    {
        _logger = logger;
        _parentProcessId = parentProcessId;
        _renderDispatcher = StartRenderDispatcher(out _renderThread);
        _runtime = new OpenVrOverlayRuntime(logger, openVrNativeLibraryService, _renderDispatcher, PublishStatus);
        _parentWatchdogTimer = new System.Threading.Timer(CheckParentProcess, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public async Task RunAsync()
    {
        _logger.Info(nameof(OverlayHelperHost), $"Overlay helper started. Parent PID: {_parentProcessId?.ToString() ?? "<none>"}. Log file: '{_logger.CurrentLogFilePath}'.");
        PublishStatus(OverlayHelperStatusKind.HelperStarted, "Overlay helper started.", _logger.CurrentLogFilePath, force: true);

        while (!_disposed)
        {
            using var server = new NamedPipeServerStream(
                OverlayHelperProtocol.PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Message,
                PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync().ConfigureAwait(false);
            _logger.Info(nameof(OverlayHelperHost), "Overlay helper client connected.");

            using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            using var writer = new StreamWriter(server, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true)
            {
                AutoFlush = true
            };

            lock (_statusWriterSync)
            {
                _statusWriter = writer;
            }

            PublishStatus(OverlayHelperStatusKind.HelperConnected, "Overlay helper client connected.", force: true);

            try
            {
                await ReadCommandsAsync(reader).ConfigureAwait(false);
            }
            finally
            {
                lock (_statusWriterSync)
                {
                    if (ReferenceEquals(_statusWriter, writer))
                    {
                        _statusWriter = null;
                    }
                }
            }
        }
    }

    private async Task ReadCommandsAsync(StreamReader reader)
    {
        while (!_disposed)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                _logger.Info(nameof(OverlayHelperHost), "Overlay helper pipe disconnected. Waiting for the next client.");
                return;
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

                HandleMessage(message);
            }
            catch (Exception ex)
            {
                _logger.Warning(nameof(OverlayHelperHost), $"Failed to process helper message: {ex.Message}");
                PublishStatus(OverlayHelperStatusKind.LastError, "Failed to process helper message.", ex.Message, force: true);
            }
        }
    }

    private void HandleMessage(OverlayHelperMessage message)
    {
        switch (message.Type)
        {
            case OverlayHelperMessageType.ApplyPreferences:
                _runtime.ApplyPreferences(CreatePreferences(message), message.IsMasterSwitchOn == true);
                break;
            case OverlayHelperMessageType.ShowMasterSwitchToast:
                _runtime.ShowToast(message.IsMasterSwitchOn == true ? "Master Switch ON" : "Master Switch OFF");
                break;
            case OverlayHelperMessageType.ShowConfigurationToast:
                if (!string.IsNullOrWhiteSpace(message.ConfigurationFileName))
                {
                    _runtime.ShowToast($"Configuration: {message.ConfigurationFileName}");
                }

                break;
            case OverlayHelperMessageType.ShowTestToast:
                _runtime.ShowToast(string.IsNullOrWhiteSpace(message.Message) ? "VRCHOTAS overlay test" : message.Message);
                break;
            case OverlayHelperMessageType.UpdateStatusIndicator:
                _runtime.UpdateStatusIndicator(message.IsMasterSwitchOn == true);
                break;
        }
    }

    private static VrOverlayPreferences CreatePreferences(OverlayHelperMessage message)
    {
        var preferences = new VrOverlayPreferences
        {
            Enabled = message.Enabled ?? true,
            StatusIndicatorEnabled = message.StatusIndicatorEnabled ?? true,
            ToastDurationSeconds = message.ToastDurationSeconds ?? 2d,
            MarkerImagePath = message.MarkerImagePath,
            MarkerSize = message.MarkerSize ?? 32.0,
            MarkerPositionX = message.MarkerPositionX ?? 0.0,
            MarkerPositionY = message.MarkerPositionY ?? 0.0,
            MarkerOpacity = message.MarkerOpacity ?? 0.8,
            ToastBackgroundColor = message.ToastBackgroundColor ?? "#80000000",
            ToastOpacity = message.ToastOpacity ?? 0.8,
            ToastTextSize = message.ToastTextSize ?? 24.0
        };
        preferences.Normalize();
        return preferences;
    }

    private static Dispatcher StartRenderDispatcher(out Thread renderThread)
    {
        var dispatcherReady = new ManualResetEventSlim(false);
        Dispatcher? dispatcher = null;
        Exception? startupException = null;

        renderThread = new Thread(() =>
        {
            try
            {
                dispatcher = Dispatcher.CurrentDispatcher;
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
            Name = "VRCHOTAS Overlay Render Dispatcher"
        };
        renderThread.SetApartmentState(ApartmentState.STA);
        renderThread.Start();
        dispatcherReady.Wait();

        if (startupException is not null)
        {
            throw new InvalidOperationException("Failed to start overlay render dispatcher.", startupException);
        }

        return dispatcher ?? throw new InvalidOperationException("Overlay render dispatcher was not initialized.");
    }

    private void PublishStatus(OverlayHelperStatusMessage message)
    {
        _logger.Info(nameof(OverlayHelperHost), $"Overlay status: {message.Kind} - {message.Message}{(string.IsNullOrWhiteSpace(message.Detail) ? string.Empty : $" ({message.Detail})")}");
        lock (_statusWriterSync)
        {
            if (_statusWriter is null)
            {
                return;
            }

            try
            {
                _statusWriter.WriteLine(JsonConvert.SerializeObject(message));
                _statusWriter.Flush();
            }
            catch (Exception ex)
            {
                _logger.Warning(nameof(OverlayHelperHost), $"Failed to write overlay status: {ex.Message}");
                _statusWriter = null;
            }
        }
    }

    private void PublishStatus(OverlayHelperStatusKind kind, string message, string? detail = null, bool force = false)
    {
        _ = force;
        PublishStatus(new OverlayHelperStatusMessage
        {
            Kind = kind,
            Message = message,
            Detail = detail
        });
    }

    private void CheckParentProcess(object? state)
    {
        if (_parentProcessId is null || _disposed)
        {
            return;
        }

        try
        {
            var process = Process.GetProcessById(_parentProcessId.Value);
            process.Refresh();
            if (process.HasExited)
            {
                _logger.Info(nameof(OverlayHelperHost), $"Parent process {_parentProcessId.Value} has exited. Shutting down helper.");
                Dispose();
                Environment.Exit(0);
            }
        }
        catch (ArgumentException)
        {
            _logger.Info(nameof(OverlayHelperHost), $"Parent process {_parentProcessId.Value} was not found. Shutting down helper.");
            Dispose();
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _logger.Warning(nameof(OverlayHelperHost), $"Parent process watchdog failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        PublishStatus(OverlayHelperStatusKind.HelperStopped, "Overlay helper stopped.", force: true);
        _parentWatchdogTimer.Dispose();
        _runtime.Dispose();
        lock (_statusWriterSync)
        {
            _statusWriter = null;
        }

        _renderDispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        if (Thread.CurrentThread != _renderThread && _renderThread.IsAlive)
        {
            _renderThread.Join(TimeSpan.FromSeconds(2));
        }
    }
}
