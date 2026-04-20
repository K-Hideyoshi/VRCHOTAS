using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;

namespace VRCHOTAS.Services;

public sealed class PreferencesService
{
    private readonly IAppLogger _logger;
    private static readonly string AppDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCHOTAS");
    private static readonly string PreferencesPath = Path.Combine(AppDataDirectory, "preferences.json");
    private const string DefaultConfigFileName = "default-config.json";

    public PreferencesService(IAppLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates preferences.json on first run with default configuration name and empty hotkey slots.
    /// </summary>
    public void EnsurePreferencesFileReady()
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            if (File.Exists(PreferencesPath))
            {
                return;
            }

            var doc = new PreferencesDocument
            {
                DefaultConfigurationFileName = DefaultConfigFileName
            };
            SaveDocument(doc);
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(PreferencesService), "Failed to ensure preferences file.", ex);
        }
    }

    public PreferencesDocument LoadDocument()
    {
        try
        {
            if (!File.Exists(PreferencesPath))
            {
                return new PreferencesDocument();
            }

            var text = File.ReadAllText(PreferencesPath);
            var root = JObject.Parse(text);
            if (root["hotkeys"] is JObject hotkeysToken)
            {
                return new PreferencesDocument
                {
                    DefaultConfigurationFileName = root["defaultConfigurationFileName"]?.Value<string>()
                        ?? root["DefaultConfigurationFileName"]?.Value<string>(),
                    Hotkeys = hotkeysToken.ToObject<HotkeyPreferences>() ?? new HotkeyPreferences()
                };
            }

            var hotkeysOnly = JsonConvert.DeserializeObject<HotkeyPreferences>(text) ?? new HotkeyPreferences();
            return new PreferencesDocument
            {
                DefaultConfigurationFileName = null,
                Hotkeys = hotkeysOnly
            };
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(PreferencesService), "Failed to load preferences.json.", ex);
            return new PreferencesDocument();
        }
    }

    public void SaveDocument(PreferencesDocument document)
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            var text = JsonConvert.SerializeObject(document, Formatting.Indented);
            File.WriteAllText(PreferencesPath, text);
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(PreferencesService), "Failed to save preferences.json.", ex);
        }
    }

    public string GetDefaultConfigurationFileName()
    {
        return LoadDocument().GetNormalizedDefaultFileName();
    }

    public void SetDefaultConfigurationFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Configuration file name cannot be empty.", nameof(fileName));
        }

        var normalized = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? fileName : $"{fileName}.json";
        var doc = LoadDocument();
        doc.DefaultConfigurationFileName = normalized;
        SaveDocument(doc);
    }

    public HotkeyPreferences LoadHotkeys()
    {
        return LoadDocument().Hotkeys;
    }

    public void SaveHotkeys(HotkeyPreferences hotkeys)
    {
        var doc = LoadDocument();
        if (string.IsNullOrWhiteSpace(doc.DefaultConfigurationFileName))
        {
            doc.DefaultConfigurationFileName = doc.GetNormalizedDefaultFileName();
        }

        doc.Hotkeys = hotkeys ?? new HotkeyPreferences();
        SaveDocument(doc);
    }
}
