using Newtonsoft.Json;
using VRCHOTAS.Models;
using Xunit;

namespace VRCHOTAS.Tests;

public sealed class OverlayHelperProtocolTests
{
    [Fact]
    public void OverlayHelperMessage_PreservesFractionalToastDuration()
    {
        var message = new OverlayHelperMessage
        {
            Type = OverlayHelperMessageType.ApplyPreferences,
            Enabled = true,
            StatusIndicatorEnabled = true,
            ToastDurationSeconds = 3.5d,
            IsMasterSwitchOn = true
        };

        var json = JsonConvert.SerializeObject(message);
        var restored = JsonConvert.DeserializeObject<OverlayHelperMessage>(json);

        Assert.NotNull(restored);
        Assert.Equal(3.5d, restored!.ToastDurationSeconds);
    }

    [Fact]
    public void OverlayHelperStatusMessage_RoundTripsStatusPayload()
    {
        var status = new OverlayHelperStatusMessage
        {
            Kind = OverlayHelperStatusKind.FallbackRaw,
            Message = "D3D11 failed; using raw overlay texture renderer.",
            Detail = "adapter unavailable",
            TimestampUtc = new DateTime(2026, 6, 2, 3, 0, 0, DateTimeKind.Utc)
        };

        var json = JsonConvert.SerializeObject(status);
        var restored = JsonConvert.DeserializeObject<OverlayHelperStatusMessage>(json);

        Assert.NotNull(restored);
        Assert.Equal(OverlayHelperStatusKind.FallbackRaw, restored!.Kind);
        Assert.Equal(status.Message, restored.Message);
        Assert.Equal(status.Detail, restored.Detail);
        Assert.Equal(status.TimestampUtc, restored.TimestampUtc);
    }
}
