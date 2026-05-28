using Newtonsoft.Json;

namespace VRCHOTAS.Models;

/// <summary>
/// Root object stored in preferences.json (default configuration + app preferences).
/// </summary>
public sealed class PreferencesDocument
{
    [JsonProperty("defaultConfigurationFileName", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string? DefaultConfigurationFileName { get; set; }

    [JsonProperty("hotkeys")]
    public HotkeyPreferences Hotkeys { get; set; } = new();

    [JsonProperty("eulerAngles")]
    public EulerAnglePreferences EulerAngles { get; set; } = new();

    public string GetNormalizedDefaultFileName()
    {
        var name = string.IsNullOrWhiteSpace(DefaultConfigurationFileName)
            ? "default-config.json"
            : DefaultConfigurationFileName.Trim();
        return name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.json";
    }
}
