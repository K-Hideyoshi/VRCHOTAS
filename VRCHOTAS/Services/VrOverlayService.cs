using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;

namespace VRCHOTAS.Services;

public sealed class VrOverlayService : IDisposable
{
    private static readonly TimeSpan HelperReconnectDelay = TimeSpan.FromSeconds(2);
    private const int HelperConnectTimeoutMilliseconds = 10_000;

    private readonly IAppLogger _logger;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private Process? _helperProcess;
    private NamedPipeClientStream? _pipeClient;
    private StreamWriter? _writer;
    private Task? _statusReaderTask;
    private CancellationTokenSource? _statusReaderCancellation;
    private OverlayHelperMessage? _lastPreferencesMessage;
    private OverlayHelperMessage? _lastStatusMessage;
    private OverlayHelperMessage? _lastToastMessage;
    private bool _disposed;
    private DateTime _nextConnectAttemptUtc = DateTime.MinValue;

    public event Action<OverlayHelperStatusMessage>? StatusChanged;

    public VrOverlayService(IAppLogger logger, OpenVrNativeLibraryService _)
    {
        _logger = logger;
    }

    public void ApplyPreferences(VrOverlayPreferences? preferences, bool isMasterSwitchOn)
    {
        var normalized = preferences?.Clone() ?? new VrOverlayPreferences();
        normalized.Normalize();

        var message = new OverlayHelperMessage
        {
            Type = OverlayHelperMessageType.ApplyPreferences,
            Enabled = normalized.Enabled,
            StatusIndicatorEnabled = normalized.StatusIndicatorEnabled,
            ToastDurationSeconds = normalized.ToastDurationSeconds,
            MarkerImagePath = normalized.MarkerImagePath,
            MarkerSize = normalized.MarkerSize,
            MarkerPositionX = normalized.MarkerPositionX,
            MarkerPositionY = normalized.MarkerPositionY,
            MarkerOpacity = normalized.MarkerOpacity,
            ToastBackgroundColor = normalized.ToastBackgroundColor,
            ToastOpacity = normalized.ToastOpacity,
            ToastTextSize = normalized.ToastTextSize,
            IsMasterSwitchOn = isMasterSwitchOn
        };
        _lastPreferencesMessage = CloneMessage(message);
        Send(message);
    }

    public void ShowMasterSwitchToast(bool isEnabled)
    {
        var message = new OverlayHelperMessage
        {
            Type = OverlayHelperMessageType.ShowMasterSwitchToast,
            IsMasterSwitchOn = isEnabled
        };
        _lastToastMessage = CloneMessage(message);
        Send(message);
    }

    public void ShowConfigurationToast(string? configurationFileName)
    {
        if (string.IsNullOrWhiteSpace(configurationFileName))
        {
            return;
        }

        var message = new OverlayHelperMessage
        {
            Type = OverlayHelperMessageType.ShowConfigurationToast,
            ConfigurationFileName = configurationFileName
        };
        _lastToastMessage = CloneMessage(message);
        Send(message);
    }

    public void ShowTestToast()
    {
        var message = new OverlayHelperMessage
        {
            Type = OverlayHelperMessageType.ShowTestToast,
            Message = "VRCHOTAS overlay test"
        };
        _lastToastMessage = CloneMessage(message);
        Send(message);
    }

    public void UpdateStatusIndicator(bool isMasterSwitchOn)
    {
        var message = new OverlayHelperMessage
        {
            Type = OverlayHelperMessageType.UpdateStatusIndicator,
            IsMasterSwitchOn = isMasterSwitchOn
        };
        _lastStatusMessage = CloneMessage(message);
        Send(message);
    }

    private void Send(OverlayHelperMessage message)
    {
        _ = SendAsync(message);
    }

    private async Task SendAsync(OverlayHelperMessage message)
    {
        if (_disposed)
        {
            return;
        }

        await _sendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed || !EnsureConnected())
            {
                return;
            }

            try
            {
                await WriteMessageAsync(message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(nameof(VrOverlayService), $"Failed to send overlay helper message: {ex.Message}");
                Disconnect(killHelper: false);
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private bool EnsureConnected()
    {
        if (DateTime.UtcNow < _nextConnectAttemptUtc)
        {
            return false;
        }

        if (_writer is not null && _pipeClient is not null && _pipeClient.IsConnected && _helperProcess is { HasExited: false })
        {
            return true;
        }

        Disconnect(killHelper: false);

        try
        {
            var helperPath = Path.Combine(AppContext.BaseDirectory, "VRCHOTAS.OverlayHelper.exe");
            if (!File.Exists(helperPath))
            {
                _logger.Warning(nameof(VrOverlayService), $"Overlay helper executable was not found: '{helperPath}'.");
                return false;
            }

            var helperProcess = EnsureHelperProcess(helperPath);
            if (helperProcess is null)
            {
                return false;
            }

            _pipeClient = new NamedPipeClientStream(".", OverlayHelperProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            _pipeClient.Connect(HelperConnectTimeoutMilliseconds);
            _writer = new StreamWriter(_pipeClient, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true)
            {
                AutoFlush = true
            };

            StartStatusReader(_pipeClient);
            ReplayRememberedState();
            _nextConnectAttemptUtc = DateTime.MinValue;
            _logger.Info(nameof(VrOverlayService), $"Connected to overlay helper process. Helper PID: {helperProcess.Id}.");
            return true;
        }
        catch (Exception ex)
        {
            _nextConnectAttemptUtc = DateTime.UtcNow + HelperReconnectDelay;
            _logger.Warning(nameof(VrOverlayService), $"Failed to connect to overlay helper process: {ex.Message}");
            Disconnect(killHelper: false);
            return false;
        }
    }

    private Process? EnsureHelperProcess(string helperPath)
    {
        if (_helperProcess is { HasExited: false })
        {
            return _helperProcess;
        }

        _helperProcess?.Dispose();
        _helperProcess = null;
        var appDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCHOTAS");
        var logDirectory = Path.Combine(appDataDirectory, "logs");
        var startInfo = new ProcessStartInfo
        {
            FileName = helperPath,
            Arguments = $"--parent-pid {Environment.ProcessId}",
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        }.WithEnvironment(FileAppLogger.LogDirectoryEnvironmentVariable, logDirectory);

        _helperProcess = Process.Start(startInfo);
        if (_helperProcess is null)
        {
            _logger.Warning(nameof(VrOverlayService), $"Failed to start overlay helper process from '{helperPath}'.");
            return null;
        }

        _logger.Info(
            nameof(VrOverlayService),
            $"Overlay helper started. PID={_helperProcess.Id}, path='{helperPath}', cwd='{AppContext.BaseDirectory}', logDirectory='{logDirectory}'.");
        return _helperProcess;
    }

    private void ReplayRememberedState()
    {
        foreach (var message in new[] { _lastPreferencesMessage, _lastStatusMessage, _lastToastMessage })
        {
            if (message is null)
            {
                continue;
            }

            _writer!.WriteLine(JsonConvert.SerializeObject(message));
            _writer.Flush();
        }
    }

    private async Task WriteMessageAsync(OverlayHelperMessage message)
    {
        var payload = JsonConvert.SerializeObject(message);
        await _writer!.WriteLineAsync(payload).ConfigureAwait(false);
        await _writer.FlushAsync().ConfigureAwait(false);
    }

    private void StartStatusReader(NamedPipeClientStream pipeClient)
    {
        _statusReaderCancellation?.Cancel();
        _statusReaderCancellation?.Dispose();
        _statusReaderCancellation = new CancellationTokenSource();
        var token = _statusReaderCancellation.Token;
        _statusReaderTask = Task.Run(() => ReadStatusLoopAsync(pipeClient, token), token);
    }

    private async Task ReadStatusLoopAsync(NamedPipeClientStream pipeClient, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(pipeClient, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            while (!cancellationToken.IsCancellationRequested && pipeClient.IsConnected)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    return;
                }

                var status = JsonConvert.DeserializeObject<OverlayHelperStatusMessage>(line);
                if (status is null)
                {
                    continue;
                }

                _logger.Info(nameof(VrOverlayService), $"Overlay helper status: {status.Kind} - {status.Message}{(string.IsNullOrWhiteSpace(status.Detail) ? string.Empty : $" ({status.Detail})")}");
                StatusChanged?.Invoke(status);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                _logger.Warning(nameof(VrOverlayService), $"Overlay helper status reader stopped: {ex.Message}");
            }
        }
    }

    private void Disconnect(bool killHelper)
    {
        _statusReaderCancellation?.Cancel();

        try
        {
            _writer?.Dispose();
        }
        catch
        {
        }

        try
        {
            _pipeClient?.Dispose();
        }
        catch
        {
        }

        if (killHelper && _helperProcess is not null)
        {
            try
            {
                if (!_helperProcess.HasExited)
                {
                    _helperProcess.Kill(entireProcessTree: true);
                    _helperProcess.WaitForExit(2000);
                }
            }
            catch
            {
            }
        }

        if (_helperProcess is { HasExited: true })
        {
            try
            {
                _logger.Info(nameof(VrOverlayService), $"Overlay helper exited with code {_helperProcess.ExitCode}.");
            }
            catch
            {
            }

            _helperProcess.Dispose();
            _helperProcess = null;
        }

        _writer = null;
        _pipeClient = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _sendGate.Wait();
            try
            {
                if (EnsureConnected())
                {
                    var payload = JsonConvert.SerializeObject(new OverlayHelperMessage
                    {
                        Type = OverlayHelperMessageType.Shutdown
                    });
                    _writer!.WriteLine(payload);
                    _writer.Flush();
                }
            }
            catch
            {
            }
            finally
            {
                _sendGate.Release();
                _sendGate.Dispose();
                Disconnect(killHelper: true);
                _statusReaderCancellation?.Dispose();
            }
        }
    }

    private static OverlayHelperMessage CloneMessage(OverlayHelperMessage message)
    {
        return JsonConvert.DeserializeObject<OverlayHelperMessage>(JsonConvert.SerializeObject(message)) ?? message;
    }
}

internal static class ProcessStartInfoExtensions
{
    public static ProcessStartInfo WithEnvironment(this ProcessStartInfo startInfo, string key, string value)
    {
        startInfo.Environment[key] = value;
        return startInfo;
    }
}
