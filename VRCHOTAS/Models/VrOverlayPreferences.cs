using Newtonsoft.Json;

namespace VRCHOTAS.Models;

public sealed class VrOverlayPreferences
{
    private const double DefaultToastDurationSeconds = 5d;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("statusIndicatorEnabled")]
    public bool StatusIndicatorEnabled { get; set; } = true;

    [JsonProperty("toastDurationSeconds")]
    public double ToastDurationSeconds { get; set; } = DefaultToastDurationSeconds;

    [JsonProperty("renderingMode")]
    public VrOverlayRenderingMode RenderingMode { get; set; } = VrOverlayRenderingMode.Auto;

    [JsonProperty("diagnosticsEnabled")]
    public bool DiagnosticsEnabled { get; set; }

    public VrOverlayPreferences Clone()
    {
        return new VrOverlayPreferences
        {
            Enabled = Enabled,
            StatusIndicatorEnabled = StatusIndicatorEnabled,
            ToastDurationSeconds = ToastDurationSeconds,
            RenderingMode = RenderingMode,
            DiagnosticsEnabled = DiagnosticsEnabled
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
        if (!Enum.IsDefined(RenderingMode))
        {
            RenderingMode = VrOverlayRenderingMode.Auto;
        }
    }
}
