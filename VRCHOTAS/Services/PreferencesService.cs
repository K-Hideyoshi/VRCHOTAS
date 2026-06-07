using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;

namespace VRCHOTAS.Services;

public sealed class PreferencesService
{
    private readonly IAppLogger _logger;

    public PreferencesService(IAppLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates preferences.json on first run with default configuration name and default app preferences.
    /// </summary>
    public void EnsurePreferencesFileReady()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataDirectory);
            if (File.Exists(AppPaths.PreferencesFilePath))
            {
                return;
            }

            var doc = new PreferencesDocument
            {
                DefaultConfigurationFileName = AppPaths.DefaultConfigFileName
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
            if (!File.Exists(AppPaths.PreferencesFilePath))
            {
                return new PreferencesDocument();
            }

            var text = File.ReadAllText(AppPaths.PreferencesFilePath);
            var root = JObject.Parse(text);
            if (root["hotkeys"] is JObject hotkeysToken)
            {
                return new PreferencesDocument
                {
                    DefaultConfigurationFileName = root["defaultConfigurationFileName"]?.Value<string>()
                        ?? root["DefaultConfigurationFileName"]?.Value<string>(),
                    Hotkeys = hotkeysToken.ToObject<HotkeyPreferences>() ?? new HotkeyPreferences(),
                    EulerAngles = root["eulerAngles"]?.ToObject<EulerAnglePreferences>() ?? new EulerAnglePreferences(),
                    ControllerOutputMode = root["controllerOutputMode"]?.ToObject<ControllerOutputMode?>() ?? ControllerOutputMode.FullVirtual,
                    LocateMappingEnabled = root["locateMappingEnabled"]?.ToObject<bool?>() ?? true,
                    VrOverlay = LoadVrOverlayPreferences(root)
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
            Directory.CreateDirectory(AppPaths.AppDataDirectory);
            document.VrOverlay ??= new VrOverlayPreferences();
            document.VrOverlay.Normalize();
            var text = JsonConvert.SerializeObject(document, Formatting.Indented);
            File.WriteAllText(AppPaths.PreferencesFilePath, text);
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

        var normalized = AppPaths.EnsureJsonExtension(fileName);
        ModifyAndSave(doc => doc.DefaultConfigurationFileName = normalized);
    }

    /// <summary>
    /// Loads the preferences document, applies a mutation, and saves it back.
    /// Ensures DefaultConfigurationFileName is never left null on save.
    /// </summary>
    private void ModifyAndSave(Action<PreferencesDocument> mutate)
    {
        var doc = LoadDocument();
        if (string.IsNullOrWhiteSpace(doc.DefaultConfigurationFileName))
        {
            doc.DefaultConfigurationFileName = doc.GetNormalizedDefaultFileName();
        }

        mutate(doc);
        SaveDocument(doc);
    }

    public HotkeyPreferences LoadHotkeys()
    {
        return LoadDocument().Hotkeys;
    }

    public void SaveHotkeys(HotkeyPreferences hotkeys)
    {
        ModifyAndSave(doc => doc.Hotkeys = hotkeys ?? new HotkeyPreferences());
    }

    public EulerAnglePreferences LoadEulerAngles()
    {
        return LoadDocument().EulerAngles ?? new EulerAnglePreferences();
    }

    public void SaveEulerAngles(EulerAnglePreferences preferences)
    {
        ModifyAndSave(doc => doc.EulerAngles = preferences?.Clone() ?? new EulerAnglePreferences());
    }

    public ControllerOutputMode LoadControllerOutputMode()
    {
        return LoadDocument().ControllerOutputMode;
    }

    public void SaveControllerOutputMode(ControllerOutputMode mode)
    {
        ModifyAndSave(doc => doc.ControllerOutputMode = mode);
    }

    public bool LoadLocateMappingEnabled()
    {
        return LoadDocument().LocateMappingEnabled;
    }

    public void SaveLocateMappingEnabled(bool enabled)
    {
        ModifyAndSave(doc => doc.LocateMappingEnabled = enabled);
    }

    public VrOverlayPreferences LoadVrOverlay()
    {
        var preferences = LoadDocument().VrOverlay ?? new VrOverlayPreferences();
        preferences.Normalize();
        return preferences;
    }

    public void SaveVrOverlay(VrOverlayPreferences preferences)
    {
        ModifyAndSave(doc => doc.VrOverlay = preferences?.Clone() ?? new VrOverlayPreferences());
    }

    private static VrOverlayPreferences LoadVrOverlayPreferences(JObject root)
    {
        var preferences = root["vrOverlay"]?.ToObject<VrOverlayPreferences>() ?? new VrOverlayPreferences();
        preferences.Normalize();
        return preferences;
    }
}
