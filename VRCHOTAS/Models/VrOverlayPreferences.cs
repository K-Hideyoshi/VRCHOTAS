using Newtonsoft.Json;

namespace VRCHOTAS.Models;

public sealed class VrOverlayPreferences
{
    private const double DefaultToastDurationSeconds = 2d;
    public const double DefaultMarkerSize = 5d;
    public const double DefaultMarkerPositionX = 0d;
    public const double DefaultMarkerPositionY = 0d;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("statusIndicatorEnabled")]
    public bool StatusIndicatorEnabled { get; set; } = true;

    [JsonProperty("hideWhenDashboardIsVisible")]
    public bool HideWhenDashboardIsVisible { get; set; } = false;

    [JsonProperty("toastDurationSeconds")]
    public double ToastDurationSeconds { get; set; } = DefaultToastDurationSeconds;

    [JsonProperty("markerImagePath")]
    public string? MarkerImagePath { get; set; }

    [JsonProperty("markerSize")]
    public double MarkerSize { get; set; } = DefaultMarkerSize;

    [JsonProperty("markerPositionX")]
    public double MarkerPositionX { get; set; } = DefaultMarkerPositionX;

    [JsonProperty("markerPositionY")]
    public double MarkerPositionY { get; set; } = DefaultMarkerPositionY;

    [JsonProperty("markerOpacity")]
    public double MarkerOpacity { get; set; } = 0.5;

    [JsonProperty("toastBackgroundColor")]
    public string ToastBackgroundColor { get; set; } = "#80000000";

    [JsonProperty("toastOpacity")]
    public double ToastOpacity { get; set; } = 0.75;

    [JsonProperty("toastTextSize")]
    public double ToastTextSize { get; set; } = 32.0;

    [JsonProperty("overlayDistanceMeters")]
    public double OverlayDistanceMeters { get; set; } = 0.8;

    [JsonProperty("overlaySizeScale")]
    public double OverlaySizeScale { get; set; } = 1.2;

    [JsonProperty("toastPositionY")]
    public double ToastPositionY { get; set; } = 0.32;

    public VrOverlayPreferences Clone()
    {
        return new VrOverlayPreferences
        {
            Enabled = Enabled,
            StatusIndicatorEnabled = StatusIndicatorEnabled,
            HideWhenDashboardIsVisible = HideWhenDashboardIsVisible,
            ToastDurationSeconds = ToastDurationSeconds,
            MarkerImagePath = MarkerImagePath,
            MarkerSize = MarkerSize,
            MarkerPositionX = MarkerPositionX,
            MarkerPositionY = MarkerPositionY,
            MarkerOpacity = MarkerOpacity,
            ToastBackgroundColor = ToastBackgroundColor,
            ToastOpacity = ToastOpacity,
            ToastTextSize = ToastTextSize,
            OverlayDistanceMeters = OverlayDistanceMeters,
            OverlaySizeScale = OverlaySizeScale,
            ToastPositionY = ToastPositionY
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

        if (double.IsNaN(MarkerSize) || double.IsInfinity(MarkerSize) || MarkerSize < 0) MarkerSize = DefaultMarkerSize;
        if (double.IsNaN(MarkerOpacity)) MarkerOpacity = 0.5;
        if (double.IsNaN(ToastOpacity)) ToastOpacity = 0.75;
        if (double.IsNaN(ToastTextSize) || ToastTextSize <= 0) ToastTextSize = 32.0;
        if (double.IsNaN(MarkerPositionX) || double.IsInfinity(MarkerPositionX)) MarkerPositionX = DefaultMarkerPositionX;
        if (double.IsNaN(MarkerPositionY) || double.IsInfinity(MarkerPositionY)) MarkerPositionY = DefaultMarkerPositionY;

        MarkerOpacity = Math.Clamp(MarkerOpacity, 0d, 1d);
        ToastOpacity = Math.Clamp(ToastOpacity, 0d, 1d);
        MarkerSize = Math.Clamp(MarkerSize, 0d, 20d);
        MarkerPositionX = Math.Clamp(MarkerPositionX, 0d, 1d);
        MarkerPositionY = Math.Clamp(MarkerPositionY, 0d, 1d);
        ToastTextSize = Math.Clamp(ToastTextSize, 15d, 100d);
        OverlayDistanceMeters = Math.Clamp(OverlayDistanceMeters, 0.2d, 2d);
        OverlaySizeScale = Math.Clamp(OverlaySizeScale, 0.5d, 3d);
        ToastPositionY = Math.Clamp(ToastPositionY, 0d, 1d);

        // Round to eliminate IEEE 754 floating-point artifacts from slider values.
        // Precision matches each slider's TickFrequency so saved JSON stays clean.
        ToastDurationSeconds = Math.Round(ToastDurationSeconds, 1);
        MarkerSize = Math.Round(MarkerSize, 0);
        MarkerPositionX = Math.Round(MarkerPositionX, 3);
        MarkerPositionY = Math.Round(MarkerPositionY, 3);
        MarkerOpacity = Math.Round(MarkerOpacity, 2);
        ToastOpacity = Math.Round(ToastOpacity, 2);
        ToastTextSize = Math.Round(ToastTextSize, 0);
        OverlayDistanceMeters = Math.Round(OverlayDistanceMeters, 1);
        OverlaySizeScale = Math.Round(OverlaySizeScale, 1);
        ToastPositionY = Math.Round(ToastPositionY, 2);
    }
}
