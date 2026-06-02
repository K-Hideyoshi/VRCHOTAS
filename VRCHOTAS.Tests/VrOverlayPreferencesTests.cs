using VRCHOTAS.Models;
using Newtonsoft.Json;
using Xunit;

namespace VRCHOTAS.Tests;

public sealed class VrOverlayPreferencesTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Normalize_ResetsInvalidToastDuration(double duration)
    {
        var preferences = new VrOverlayPreferences
        {
            ToastDurationSeconds = duration
        };

        preferences.Normalize();

        Assert.Equal(5d, preferences.ToastDurationSeconds);
    }

    [Theory]
    [InlineData(-1d, 1d)]
    [InlineData(0.5d, 1d)]
    [InlineData(12.5d, 12.5d)]
    [InlineData(60d, 30d)]
    public void Normalize_ClampsToastDuration(double duration, double expected)
    {
        var preferences = new VrOverlayPreferences
        {
            ToastDurationSeconds = duration
        };

        preferences.Normalize();

        Assert.Equal(expected, preferences.ToastDurationSeconds);
    }

    [Fact]
    public void Clone_CopiesOverlayRenderingSettings()
    {
        var preferences = new VrOverlayPreferences
        {
            Enabled = false,
            StatusIndicatorEnabled = false,
            ToastDurationSeconds = 7.5d,
            RenderingMode = VrOverlayRenderingMode.RawCompatibility,
            DiagnosticsEnabled = true
        };

        var clone = preferences.Clone();

        Assert.False(clone.Enabled);
        Assert.False(clone.StatusIndicatorEnabled);
        Assert.Equal(7.5d, clone.ToastDurationSeconds);
        Assert.Equal(VrOverlayRenderingMode.RawCompatibility, clone.RenderingMode);
        Assert.True(clone.DiagnosticsEnabled);
    }

    [Fact]
    public void Deserialize_UsesCompatibleDefaultsForNewOverlayFields()
    {
        const string json = """{"enabled":true,"statusIndicatorEnabled":true,"toastDurationSeconds":5}""";

        var preferences = JsonConvert.DeserializeObject<VrOverlayPreferences>(json);

        Assert.NotNull(preferences);
        Assert.Equal(VrOverlayRenderingMode.Auto, preferences!.RenderingMode);
        Assert.False(preferences.DiagnosticsEnabled);
    }
}
