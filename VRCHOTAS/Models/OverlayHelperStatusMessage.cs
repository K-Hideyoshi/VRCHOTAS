using Newtonsoft.Json;

namespace VRCHOTAS.Models;

public sealed class OverlayHelperStatusMessage
{
    [JsonProperty("kind")]
    public OverlayHelperStatusKind Kind { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;

    [JsonProperty("detail")]
    public string? Detail { get; set; }

    [JsonProperty("timestampUtc")]
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
