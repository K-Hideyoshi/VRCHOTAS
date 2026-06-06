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
}
