using System.IO;
using Newtonsoft.Json;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;

namespace VRCHOTAS.Services;

public sealed class ConfigurationService
{
    private readonly IAppLogger _logger;
    private static readonly string AppDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VRCHOTAS");
    private static readonly string ConfigDirectory = Path.Combine(AppDataDirectory, "configs");
    private static readonly string AppStatePath = Path.Combine(ConfigDirectory, "app-state.json");
    private const string DefaultConfigFileName = "default-config.json";

    public ConfigurationService(IAppLogger logger)
    {
        _logger = logger;
    }

    public string EnsureDefaultConfigurationFileName()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);

            var state = LoadAppState();
            var selectedFileName = string.IsNullOrWhiteSpace(state.DefaultConfigurationFileName)
                ? DefaultConfigFileName
                : state.DefaultConfigurationFileName;

            selectedFileName = EnsureJsonExtension(selectedFileName);
            var selectedPath = GetConfigurationPath(selectedFileName);

            if (!File.Exists(selectedPath))
            {
                if (!selectedFileName.Equals(DefaultConfigFileName, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Warning(nameof(ConfigurationService), $"Selected default configuration is missing: {selectedFileName}. Falling back to {DefaultConfigFileName}.");
                }

                state.DefaultConfigurationFileName = DefaultConfigFileName;
                SaveAppState(state);

                var defaultPath = GetConfigurationPath(DefaultConfigFileName);
                if (!File.Exists(defaultPath))
                {
                    SaveByFileName(DefaultConfigFileName, new AppConfiguration());
                    _logger.Warning(nameof(ConfigurationService), $"Default configuration was missing and has been created: {DefaultConfigFileName}");
                }

                return DefaultConfigFileName;
            }

            state.DefaultConfigurationFileName = selectedFileName;
            SaveAppState(state);
            return selectedFileName;
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(ConfigurationService), "Failed to ensure default configuration.", ex);
            return DefaultConfigFileName;
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
                .Where(name => !string.IsNullOrWhiteSpace(name) && !name.Equals(Path.GetFileName(AppStatePath), StringComparison.OrdinalIgnoreCase))
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

    public void SetDefaultConfigurationFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Configuration file name cannot be empty.", nameof(fileName));
        }

        var state = LoadAppState();
        state.DefaultConfigurationFileName = EnsureJsonExtension(fileName);
        SaveAppState(state);
    }

    private static string EnsureJsonExtension(string fileName)
    {
        return fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? fileName : $"{fileName}.json";
    }

    private static string GetConfigurationPath(string fileName)
    {
        return Path.Combine(ConfigDirectory, Path.GetFileName(fileName));
    }

    private AppStateModel LoadAppState()
    {
        try
        {
            if (!File.Exists(AppStatePath))
            {
                return new AppStateModel
                {
                    DefaultConfigurationFileName = DefaultConfigFileName
                };
            }

            var content = File.ReadAllText(AppStatePath);
            return JsonConvert.DeserializeObject<AppStateModel>(content) ?? new AppStateModel
            {
                DefaultConfigurationFileName = DefaultConfigFileName
            };
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(ConfigurationService), "Failed to read app state file.", ex);
            return new AppStateModel
            {
                DefaultConfigurationFileName = DefaultConfigFileName
            };
        }
    }

    private void SaveAppState(AppStateModel state)
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            var content = JsonConvert.SerializeObject(state, Formatting.Indented);
            File.WriteAllText(AppStatePath, content);
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(ConfigurationService), "Failed to write app state file.", ex);
        }
    }

    private sealed class AppStateModel
    {
        public string DefaultConfigurationFileName { get; set; } = DefaultConfigFileName;
    }
}
