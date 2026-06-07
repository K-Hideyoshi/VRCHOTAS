using Newtonsoft.Json;

namespace VRCHOTAS.Models;

public sealed class OverlayHelperMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("enabled")]
    public bool? Enabled { get; set; }

    [JsonProperty("statusIndicatorEnabled")]
    public bool? StatusIndicatorEnabled { get; set; }

    [JsonProperty("toastDurationSeconds")]
    public double? ToastDurationSeconds { get; set; }

    [JsonProperty("markerImagePath")]
    public string? MarkerImagePath { get; set; }

    [JsonProperty("markerSize")]
    public double? MarkerSize { get; set; }

    [JsonProperty("markerPositionX")]
    public double? MarkerPositionX { get; set; }

    [JsonProperty("markerPositionY")]
    public double? MarkerPositionY { get; set; }

    [JsonProperty("markerOpacity")]
    public double? MarkerOpacity { get; set; }

    [JsonProperty("toastBackgroundColor")]
    public string? ToastBackgroundColor { get; set; }

    [JsonProperty("toastOpacity")]
    public double? ToastOpacity { get; set; }

    [JsonProperty("toastTextSize")]
    public double? ToastTextSize { get; set; }

    [JsonProperty("hideWhenDashboardIsVisible")]
    public bool? HideWhenDashboardIsVisible { get; set; }

    [JsonProperty("overlayDistanceMeters")]
    public double? OverlayDistanceMeters { get; set; }

    [JsonProperty("overlaySizeScale")]
    public double? OverlaySizeScale { get; set; }

    [JsonProperty("toastPositionY")]
    public double? ToastPositionY { get; set; }

    [JsonProperty("isMasterSwitchOn")]
    public bool? IsMasterSwitchOn { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }

    [JsonProperty("configurationFileName")]
    public string? ConfigurationFileName { get; set; }

    public static OverlayHelperMessage CreateApplyPreferences(VrOverlayPreferences preferences, bool isMasterSwitchOn)
    {
        var normalized = preferences.Clone();
        normalized.Normalize();
        return new OverlayHelperMessage
        {
            Type = OverlayHelperMessageType.ApplyPreferences,
            Enabled = normalized.Enabled,
            StatusIndicatorEnabled = normalized.StatusIndicatorEnabled,
            HideWhenDashboardIsVisible = normalized.HideWhenDashboardIsVisible,
            OverlayDistanceMeters = normalized.OverlayDistanceMeters,
            OverlaySizeScale = normalized.OverlaySizeScale,
            ToastPositionY = normalized.ToastPositionY,
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
    }

    public static OverlayHelperMessage CreateMasterSwitchToast(bool isEnabled) => new()
    {
        Type = OverlayHelperMessageType.ShowMasterSwitchToast,
        IsMasterSwitchOn = isEnabled
    };

    public static OverlayHelperMessage CreateConfigurationToast(string configurationFileName) => new()
    {
        Type = OverlayHelperMessageType.ShowConfigurationToast,
        ConfigurationFileName = configurationFileName
    };

    public static OverlayHelperMessage CreateTestToast(string? message = null) => new()
    {
        Type = OverlayHelperMessageType.ShowTestToast,
        Message = message ?? "VRCHOTAS overlay test"
    };

    public static OverlayHelperMessage CreateStatusIndicator(bool isMasterSwitchOn) => new()
    {
        Type = OverlayHelperMessageType.UpdateStatusIndicator,
        IsMasterSwitchOn = isMasterSwitchOn
    };
}
