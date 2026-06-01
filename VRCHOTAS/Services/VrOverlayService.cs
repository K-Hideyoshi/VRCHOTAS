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
    private const int HelperConnectTimeoutMilliseconds = 1000;

    private readonly IAppLogger _logger;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private Process? _helperProcess;
    private NamedPipeClientStream? _pipeClient;
    private StreamWriter? _writer;
    private VrOverlayPreferences _preferences = new();
    private bool _isMasterSwitchOn;
    private bool _disposed;
    private DateTime _nextConnectAttemptUtc = DateTime.MinValue;

    public VrOverlayService(IAppLogger logger, OpenVrNativeLibraryService _)
    {
        _logger = logger;
    }

    public void ApplyPreferences(VrOverlayPreferences? preferences, bool isMasterSwitchOn)
    {
        _preferences = preferences?.Clone() ?? new VrOverlayPreferences();
        _preferences.Normalize();
        _isMasterSwitchOn = isMasterSwitchOn;

        Send(new OverlayHelperMessage
        {
            Type = OverlayHelperMessageType.ApplyPreferences,
            Enabled = _preferences.Enabled,
            StatusIndicatorEnabled = _preferences.StatusIndicatorEnabled,
            ToastDurationSeconds = (int)Math.Round(_preferences.ToastDurationSeconds),
            IsMasterSwitchOn = isMasterSwitchOn
        });
    }

    public void ShowMasterSwitchToast(bool isEnabled)
    {
        _isMasterSwitchOn = isEnabled;
        Send(new OverlayHelperMessage
        {
            Type = OverlayHelperMessageType.ShowMasterSwitchToast,
            IsMasterSwitchOn = isEnabled
        });
    }

    public void ShowConfigurationToast(string? configurationFileName)
    {
        if (string.IsNullOrWhiteSpace(configurationFileName))
        {
            return;
        }

        Send(new OverlayHelperMessage
        {
            Type = OverlayHelperMessageType.ShowConfigurationToast,
            ConfigurationFileName = configurationFileName
        });
    }

    public void UpdateStatusIndicator(bool isMasterSwitchOn)
    {
        _isMasterSwitchOn = isMasterSwitchOn;
        Send(new OverlayHelperMessage
        {
            Type = OverlayHelperMessageType.UpdateStatusIndicator,
            IsMasterSwitchOn = isMasterSwitchOn
        });
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
                var payload = JsonConvert.SerializeObject(message);
                await _writer!.WriteLineAsync(payload).ConfigureAwait(false);
                await _writer.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(nameof(VrOverlayService), $"Failed to send overlay helper message: {ex.Message}");
                Disconnect();
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

        Disconnect();

        try
        {
            var helperPath = Path.Combine(AppContext.BaseDirectory, "VRCHOTAS.OverlayHelper.exe");
            if (!File.Exists(helperPath))
            {
                _logger.Warning(nameof(VrOverlayService), $"Overlay helper executable was not found: '{helperPath}'.");
                return false;
            }

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

            if (_helperProcess is not null)
            {
                _logger.Info(nameof(VrOverlayService), $"Overlay helper started with parent PID {Environment.ProcessId} and log directory '{logDirectory}'.");
            }

            if (_helperProcess is null)
            {
                _logger.Warning(nameof(VrOverlayService), "Failed to start overlay helper process.");
                return false;
            }

            _pipeClient = new NamedPipeClientStream(".", OverlayHelperProtocol.PipeName, PipeDirection.Out);
            _pipeClient.Connect(HelperConnectTimeoutMilliseconds);
            _writer = new StreamWriter(_pipeClient, new UTF8Encoding(false))
            {
                AutoFlush = true
            };

            _nextConnectAttemptUtc = DateTime.MinValue;
            _logger.Info(nameof(VrOverlayService), "Connected to overlay helper process.");
            return true;
        }
        catch (Exception ex)
        {
            _nextConnectAttemptUtc = DateTime.UtcNow + HelperReconnectDelay;
            _logger.Warning(nameof(VrOverlayService), $"Failed to connect to overlay helper process: {ex.Message}");
            Disconnect();
            return false;
        }
    }
    private void Disconnect()
    {
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

        if (_helperProcess is not null)
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
            finally
            {
                _helperProcess.Dispose();
            }
        }

        _writer = null;
        _pipeClient = null;
        _helperProcess = null;
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
                Disconnect();
            }
        }
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
