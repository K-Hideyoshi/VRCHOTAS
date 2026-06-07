using Newtonsoft.Json;
using VRCHOTAS.Models;
using Xunit;

namespace VRCHOTAS.Tests;

public sealed class PreferencesDocumentTests
{
    [Fact]
    public void Serialize_RoundTripsAllFields()
    {
        var doc = new PreferencesDocument
        {
            DefaultConfigurationFileName = "my-config.json",
            ControllerOutputMode = ControllerOutputMode.HybridKeepLeftReal,
            LocateMappingEnabled = false,
            Hotkeys = new HotkeyPreferences
            {
                PreviousConfiguration = new HotkeyBinding
                {
                    Kind = HotkeyInputKind.Keyboard,
                    Keyboard = new KeyboardChordBinding { Modifiers = 2, Key = 37 } // Ctrl+Left
                }
            },
            EulerAngles = new EulerAnglePreferences
            {
                Order = EulerAngleOrder.YawPitchRoll,
                AxisReference = EulerAngleAxisReference.World
            }
        };

        var json = JsonConvert.SerializeObject(doc, Formatting.Indented);
        var restored = JsonConvert.DeserializeObject<PreferencesDocument>(json);

        Assert.NotNull(restored);
        Assert.Equal("my-config.json", restored!.DefaultConfigurationFileName);
        Assert.Equal(ControllerOutputMode.HybridKeepLeftReal, restored.ControllerOutputMode);
        Assert.False(restored.LocateMappingEnabled);
        Assert.NotNull(restored.Hotkeys);
        Assert.NotNull(restored.EulerAngles);
    }

    [Theory]
    [InlineData(null, "default-config.json")]
    [InlineData("", "default-config.json")]
    [InlineData("  ", "default-config.json")]
    [InlineData("my-config", "my-config.json")]
    [InlineData("my-config.json", "my-config.json")]
    public void GetNormalizedDefaultFileName_ReturnsExpected(string? input, string expected)
    {
        var doc = new PreferencesDocument { DefaultConfigurationFileName = input };
        Assert.Equal(expected, doc.GetNormalizedDefaultFileName());
    }

    [Fact]
    public void NewDocument_HasSensibleDefaults()
    {
        var doc = new PreferencesDocument();
        Assert.Equal(ControllerOutputMode.FullVirtual, doc.ControllerOutputMode);
        Assert.True(doc.LocateMappingEnabled);
        Assert.NotNull(doc.Hotkeys);
        Assert.NotNull(doc.EulerAngles);
        Assert.NotNull(doc.VrOverlay);
    }
}
