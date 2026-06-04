using Newtonsoft.Json;

namespace VRCHOTAS.Models;

public sealed class VrOverlayPreferences
{
    private const double DefaultToastDurationSeconds = 2d;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("statusIndicatorEnabled")]
    public bool StatusIndicatorEnabled { get; set; } = true;

    [JsonProperty("toastDurationSeconds")]
    public double ToastDurationSeconds { get; set; } = DefaultToastDurationSeconds;

    [JsonProperty("markerImagePath")]
    public string? MarkerImagePath { get; set; }

    [JsonProperty("markerSize")]
    public double MarkerSize { get; set; } = 32.0;

    [JsonProperty("markerPositionX")]
    public double MarkerPositionX { get; set; }

    [JsonProperty("markerPositionY")]
    public double MarkerPositionY { get; set; }

    [JsonProperty("markerOpacity")]
    public double MarkerOpacity { get; set; } = 0.8;

    [JsonProperty("toastBackgroundColor")]
    public string ToastBackgroundColor { get; set; } = "#80000000";

    [JsonProperty("toastOpacity")]
    public double ToastOpacity { get; set; } = 0.8;

    [JsonProperty("toastTextSize")]
    public double ToastTextSize { get; set; } = 24.0;

    public VrOverlayPreferences Clone()
    {
        return new VrOverlayPreferences
        {
            Enabled = Enabled,
            StatusIndicatorEnabled = StatusIndicatorEnabled,
            ToastDurationSeconds = ToastDurationSeconds,
            MarkerImagePath = MarkerImagePath,
            MarkerSize = MarkerSize,
            MarkerPositionX = MarkerPositionX,
            MarkerPositionY = MarkerPositionY,
            MarkerOpacity = MarkerOpacity,
            ToastBackgroundColor = ToastBackgroundColor,
            ToastOpacity = ToastOpacity,
            ToastTextSize = ToastTextSize
        };
    }

    public void Normalize()
    {
        if (double.IsNaN(ToastDurationSeconds) || double.IsInfinity(ToastDurationSeconds))
        {
            ToastDurationSeconds = DefaultToastDurationSeconds;
            return;
        }

        ToastDurationSeconds = Math.Clamp(ToastDurationSeconds, 1d, 30d);

        if (double.IsNaN(MarkerSize) || MarkerSize <= 0) MarkerSize = 32.0;
        if (double.IsNaN(MarkerOpacity)) MarkerOpacity = 0.8;
        if (double.IsNaN(ToastOpacity)) ToastOpacity = 0.8;
        if (double.IsNaN(ToastTextSize) || ToastTextSize <= 0) ToastTextSize = 24.0;

        MarkerOpacity = Math.Clamp(MarkerOpacity, 0d, 1d);
        ToastOpacity = Math.Clamp(ToastOpacity, 0d, 1d);
    }
}
