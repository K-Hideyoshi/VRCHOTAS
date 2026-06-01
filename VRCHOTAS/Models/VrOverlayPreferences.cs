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

    public VrOverlayPreferences Clone()
    {
        return new VrOverlayPreferences
        {
            Enabled = Enabled,
            StatusIndicatorEnabled = StatusIndicatorEnabled,
            ToastDurationSeconds = ToastDurationSeconds
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
    }
}
