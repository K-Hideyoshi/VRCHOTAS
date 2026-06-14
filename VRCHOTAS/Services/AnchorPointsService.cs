using System.IO;
using System.Threading;
using Timer = System.Threading.Timer;
using Newtonsoft.Json;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;

namespace VRCHOTAS.Services;

/// <summary>
/// Manages per-configuration anchor point persistence with debounced writes.
/// Anchors are saved to a single anchor_points.json file keyed by config name.
/// File writes are deferred until anchors stop changing for a configurable quiet period.
/// Uses a single reusable <see cref="System.Threading.Timer"/> to avoid per-call allocations.
/// </summary>
public sealed class AnchorPointsService : IDisposable
{
    private readonly IAppLogger _logger;
    private readonly int _debounceDelayMs;
    private readonly Timer _debounceTimer;
    private readonly object _lock = new();
    private string? _pendingConfigName;
    private HandAnchorData? _pendingLeft;
    private HandAnchorData? _pendingRight;
    private bool _disposed;

    public AnchorPointsService(IAppLogger logger, int debounceDelayMs = 2000)
    {
        _logger = logger;
        _debounceDelayMs = debounceDelayMs;
        _debounceTimer = new Timer(OnDebounceTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    public AnchorPointsPerConfig? LoadAnchorPoints(string configFileName)
    {
        try
        {
            var document = LoadDocument();
            if (document.Configs.TryGetValue(configFileName, out var entry))
            {
                return entry;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(AnchorPointsService),
                $"Failed to load anchor points for config '{configFileName}'.", ex);
        }
        return null;
    }

    public void ScheduleSave(string configFileName, HandAnchorData left, HandAnchorData right)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _pendingConfigName = configFileName;
            _pendingLeft = left.Clone();
            _pendingRight = right.Clone();
            _debounceTimer.Change(_debounceDelayMs, Timeout.Infinite);
        }
    }

    public void FlushPendingSave()
    {
        HandAnchorData? left;
        HandAnchorData? right;
        string? configName;
        lock (_lock)
        {
            if (_pendingLeft is null || _pendingRight is null || _pendingConfigName is null) return;
            _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            configName = _pendingConfigName;
            left = _pendingLeft;
            right = _pendingRight;
            _pendingConfigName = null;
            _pendingLeft = null;
            _pendingRight = null;
        }
        WriteAnchorPointsToDisk(configName, left, right);
    }

    public void DeleteAnchorPoints(string configFileName)
    {
        try
        {
            var document = LoadDocument();
            if (document.Configs.Remove(configFileName))
            {
                SaveDocument(document);
                _logger.Info(nameof(AnchorPointsService), $"Deleted anchor points for config '{configFileName}'.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(AnchorPointsService), $"Failed to delete anchor points for config '{configFileName}'.", ex);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _debounceTimer.Dispose();
        }
    }

    private void OnDebounceTimerElapsed(object? state)
    {
        HandAnchorData? left;
        HandAnchorData? right;
        string? configName;
        lock (_lock)
        {
            if (_disposed) return;
            if (_pendingLeft is null || _pendingRight is null || _pendingConfigName is null) return;
            configName = _pendingConfigName;
            left = _pendingLeft;
            right = _pendingRight;
            _pendingConfigName = null;
            _pendingLeft = null;
            _pendingRight = null;
        }
        WriteAnchorPointsToDisk(configName, left, right);
    }

    private void WriteAnchorPointsToDisk(string configFileName, HandAnchorData left, HandAnchorData right)
    {
        try
        {
            var document = LoadDocument();
            document.Configs[configFileName] = new AnchorPointsPerConfig { Left = left, Right = right };
            SaveDocument(document);
            _logger.Debug(nameof(AnchorPointsService), $"Saved anchor points for config '{configFileName}'.");
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(AnchorPointsService), $"Failed to save anchor points for config '{configFileName}'.", ex);
        }
    }

    private AnchorPointsDocument LoadDocument()
    {
        try
        {
            if (!File.Exists(AppPaths.AnchorPointsFilePath)) return new AnchorPointsDocument();
            var text = File.ReadAllText(AppPaths.AnchorPointsFilePath);
            return JsonConvert.DeserializeObject<AnchorPointsDocument>(text) ?? new AnchorPointsDocument();
        }
        catch (Exception ex)
        {
            _logger.Warning(nameof(AnchorPointsService), $"Failed to parse anchor_points.json, starting fresh. {ex.Message}");
            return new AnchorPointsDocument();
        }
    }

    private void SaveDocument(AnchorPointsDocument document)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ConfigDirectory);
            var text = JsonConvert.SerializeObject(document, Formatting.Indented);
            File.WriteAllText(AppPaths.AnchorPointsFilePath, text);
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(AnchorPointsService), "Failed to write anchor_points.json.", ex);
        }
    }
}
