using System.IO;

namespace VRCHOTAS.Models;

/// <summary>
/// Centralized application path definitions shared across services.
/// </summary>
public static class AppPaths
{
    public static readonly string AppDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCHOTAS");

    public static readonly string ConfigDirectory = Path.Combine(AppDataDirectory, "configs");
    public static readonly string PreferencesFilePath = Path.Combine(AppDataDirectory, "preferences.json");
    public const string DefaultConfigFileName = "default-config.json";

    public static string EnsureJsonExtension(string fileName)
    {
        return fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.json";
    }

    public static string GetConfigurationPath(string fileName)
    {
        return Path.Combine(ConfigDirectory, Path.GetFileName(fileName));
    }
}
