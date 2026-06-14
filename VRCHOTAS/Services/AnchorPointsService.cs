using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;

namespace VRCHOTAS.Services;

/// <summary>
/// Manages per-configuration anchor point persistence with debounced writes.
/// Anchors are saved to a single anchor_points.json file keyed by config name.
/// File writes are deferred until anchors stop changing for a configurable quiet period.
/// </summary>
public sealed class AnchorPointsService
{
    private readonly IAppLogger _logger;
    private readonly TimeSpan _debounceDelay;
    private CancellationTokenSource? _pendingSaveCts;
    private readonly object _lock = new();
    private string? _pendingConfigName;
    private HandAnchorData? _pendingLeft;
    private HandAnchorData? _pendingRight;

    public AnchorPointsService(IAppLogger logger, TimeSpan? debounceDelay = null)
    {
        _logger = logger;
        _debounceDelay = debounceDelay ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>
    /// Loads saved anchor points for a specific configuration.
    /// Returns null if no anchors have been saved for this config.
    /// </summary>
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

    /// <summary>
    /// Schedules a debounced save. Each call resets the quiet timer.
    /// The actual file write only occurs after <see cref="_debounceDelay"/> has passed
    /// without any new call to this method.
    /// </summary>
    public void ScheduleSave(string configFileName, HandAnchorData left, HandAnchorData right)
    {
        lock (_lock)
        {
            _pendingConfigName = configFileName;
            _pendingLeft = left.Clone();
            _pendingRight = right.Clone();

            _pendingSaveCts?.Cancel();
            _pendingSaveCts?.Dispose();
            _pendingSaveCts = new CancellationTokenSource();
            var token = _pendingSaveCts.Token;

            _ = DebounceAndSaveAsync(configFileName, left.Clone(), right.Clone(), token);
        }
    }

    /// <summary>
    /// Immediately flushes any pending anchor save to disk.
    /// Call on application shutdown or before switching configurations.
    /// </summary>
    public void FlushPendingSave()
    {
        HandAnchorData? left;
        HandAnchorData? right;
        string? configName;

        lock (_lock)
        {
            if (_pendingLeft is null || _pendingRight is null || _pendingConfigName is null)
            {
                return;
            }

            _pendingSaveCts?.Cancel();
            _pendingSaveCts?.Dispose();
            _pendingSaveCts = null;

            configName = _pendingConfigName;
            left = _pendingLeft;
            right = _pendingRight;
            _pendingConfigName = null;
            _pendingLeft = null;
            _pendingRight = null;
        }

        WriteAnchorPointsToDisk(configName, left, right);
    }

    /// <summary>
    /// Deletes saved anchor points for a configuration.
    /// </summary>
    public void DeleteAnchorPoints(string configFileName)
    {
        try
        {
            var document = LoadDocument();
            if (document.Configs.Remove(configFileName))
            {
                SaveDocument(document);
                _logger.Info(nameof(AnchorPointsService),
                    $"Deleted anchor points for config '{configFileName}'.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(AnchorPointsService),
                $"Failed to delete anchor points for config '{configFileName}'.", ex);
        }
    }

    private async Task DebounceAndSaveAsync(string configFileName, HandAnchorData left, HandAnchorData right, CancellationToken token)
    {
        try
        {
            await Task.Delay(_debounceDelay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        WriteAnchorPointsToDisk(configFileName, left, right);
    }

    private void WriteAnchorPointsToDisk(string configFileName, HandAnchorData left, HandAnchorData right)
    {
        try
        {
            var document = LoadDocument();
            document.Configs[configFileName] = new AnchorPointsPerConfig
            {
                Left = left,
                Right = right
            };
            SaveDocument(document);
            _logger.Debug(nameof(AnchorPointsService),
                $"Saved anchor points for config '{configFileName}'.");
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(AnchorPointsService),
                $"Failed to save anchor points for config '{configFileName}'.", ex);
        }
    }

    private AnchorPointsDocument LoadDocument()
    {
        try
        {
            if (!File.Exists(AppPaths.AnchorPointsFilePath))
            {
                return new AnchorPointsDocument();
            }

            var text = File.ReadAllText(AppPaths.AnchorPointsFilePath);
            return JsonConvert.DeserializeObject<AnchorPointsDocument>(text)
                   ?? new AnchorPointsDocument();
        }
        catch (Exception ex)
        {
            _logger.Warning(nameof(AnchorPointsService),
                $"Failed to parse anchor_points.json, starting fresh. {ex.Message}");
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
            _logger.Error(nameof(AnchorPointsService),
                "Failed to write anchor_points.json.", ex);
        }
    }
}
