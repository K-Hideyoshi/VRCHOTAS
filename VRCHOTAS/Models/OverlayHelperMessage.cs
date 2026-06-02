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

    [JsonProperty("renderingMode")]
    public VrOverlayRenderingMode? RenderingMode { get; set; }

    [JsonProperty("diagnosticsEnabled")]
    public bool? DiagnosticsEnabled { get; set; }

    [JsonProperty("isMasterSwitchOn")]
    public bool? IsMasterSwitchOn { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }

    [JsonProperty("configurationFileName")]
    public string? ConfigurationFileName { get; set; }
}
