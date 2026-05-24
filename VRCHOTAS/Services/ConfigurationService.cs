using System.IO;
using Newtonsoft.Json;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;

namespace VRCHOTAS.Services;

public sealed class ConfigurationService
{
    private readonly IAppLogger _logger;
    private static readonly string AppDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCHOTAS");
    private static readonly string ConfigDirectory = Path.Combine(AppDataDirectory, "configs");
    private static readonly string LegacyConfigDirectory = AppDataDirectory;
    private const string DefaultConfigFileName = "default-config.json";
    private const string PreferencesFileName = "preferences.json";

    public ConfigurationService(IAppLogger logger)
    {
        _logger = logger;
    }

    public void EnsureConfigurationFileExistsOrCreate(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            var normalized = EnsureJsonExtension(fileName);
            var path = GetConfigurationPath(normalized);
            if (File.Exists(path))
            {
                return;
            }

            SaveByFileName(normalized, new AppConfiguration());
            _logger.Info(nameof(ConfigurationService), $"Created missing configuration file: {normalized}");
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(ConfigurationService), "Failed to ensure configuration file exists.", ex);
        }
    }

    public IReadOnlyList<string> GetConfigurationFileNames()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            return EnumerateConfigurationFilePaths()
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(ConfigurationService), "Failed to enumerate configuration files.", ex);
            return Array.Empty<string>();
        }
    }

    public AppConfiguration LoadByFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Configuration file name cannot be empty.", nameof(fileName));
        }

        var normalized = EnsureJsonExtension(fileName);
        var configPath = GetConfigurationPath(normalized);

        try
        {
            if (!File.Exists(configPath))
            {
                _logger.Warning(nameof(ConfigurationService), $"Configuration file does not exist: {normalized}");
                return new AppConfiguration();
            }

            var text = File.ReadAllText(configPath);
            var config = JsonConvert.DeserializeObject<AppConfiguration>(text) ?? new AppConfiguration();
            _logger.Info(nameof(ConfigurationService), $"Configuration loaded: {normalized}, mappings: {config.Mappings.Count}");
            return config;
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(ConfigurationService), $"Configuration load failed: {normalized}", ex);
            return new AppConfiguration();
        }
    }

    public bool ConfigurationExists(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        try
        {
            var normalized = EnsureJsonExtension(fileName);
            return EnumerateConfigurationFilePaths()
                .Any(path => Path.GetFileName(path).Equals(normalized, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(ConfigurationService), $"Failed to check configuration existence: {fileName}", ex);
            return false;
        }
    }

    public string GetConfigurationDirectoryPath()
    {
        Directory.CreateDirectory(ConfigDirectory);
        return ConfigDirectory;
    }

    private static IEnumerable<string> EnumerateConfigurationFilePaths()
    {
        if (Directory.Exists(ConfigDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(ConfigDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                yield return path;
            }
        }

        if (!Directory.Exists(LegacyConfigDirectory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(LegacyConfigDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(path).Equals(PreferencesFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return path;
        }
    }

    public void SaveByFileName(string fileName, AppConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Configuration file name cannot be empty.", nameof(fileName));
        }

        var normalized = EnsureJsonExtension(fileName);
        var configPath = GetConfigurationPath(normalized);

        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            var text = JsonConvert.SerializeObject(configuration, Formatting.Indented);
            File.WriteAllText(configPath, text);
            _logger.Info(nameof(ConfigurationService), $"Configuration saved: {normalized}, mappings: {configuration.Mappings.Count}");
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(ConfigurationService), $"Configuration save failed: {normalized}", ex);
        }
    }

    private static string EnsureJsonExtension(string fileName)
    {
        return fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? fileName : $"{fileName}.json";
    }

    private static string GetConfigurationPath(string fileName)
    {
        return Path.Combine(ConfigDirectory, Path.GetFileName(fileName));
    }
}
