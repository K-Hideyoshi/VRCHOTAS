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
    private const string DefaultConfigFileName = "default-config.json";

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
            return Directory
                .EnumerateFiles(ConfigDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
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
