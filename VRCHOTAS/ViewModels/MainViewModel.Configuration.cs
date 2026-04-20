using VRCHOTAS.Models;

namespace VRCHOTAS.ViewModels;

public sealed partial class MainViewModel
{
    private void InitializeConfigurationOnStartup()
    {
        var defaultFileName = _configurationService.EnsureDefaultConfigurationFileName();
        RefreshAvailableConfigurations();
        LoadConfigurationByName(defaultFileName);
    }

    public void SaveAsConfiguration(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            _logger.Warning(nameof(MainViewModel), "Save As canceled because file name is empty.");
            return;
        }

        var normalizedFileName = EnsureJsonFileName(fileName);
        _configurationService.SaveByFileName(normalizedFileName, new AppConfiguration
        {
            IsMappingEnabled = IsMappingEnabled,
            Mappings = Mappings.ToList()
        });

        CurrentConfigurationFileName = normalizedFileName;
        IsConfigurationDirty = false;
        RefreshAvailableConfigurations();
    }

    private void SaveCurrentConfiguration()
    {
        if (string.IsNullOrWhiteSpace(CurrentConfigurationFileName))
        {
            _logger.Warning(nameof(MainViewModel), "Save requested without an active configuration. Falling back to Save As.");
            SaveAsRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _configurationService.SaveByFileName(CurrentConfigurationFileName, new AppConfiguration
        {
            IsMappingEnabled = IsMappingEnabled,
            Mappings = Mappings.ToList()
        });

        IsConfigurationDirty = false;
    }

    private void RefreshAvailableConfigurations()
    {
        AvailableConfigurationFiles.Clear();
        foreach (var fileName in _configurationService.GetConfigurationFileNames())
        {
            AvailableConfigurationFiles.Add(fileName);
        }
    }

    private void LoadConfigurationByName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var normalized = EnsureJsonFileName(fileName);
        var config = _configurationService.LoadByFileName(normalized);
        _suppressConfigurationChangeTracking = true;
        try
        {
            IsMappingEnabled = config.IsMappingEnabled;
            Mappings.Clear();
            foreach (var mapping in config.Mappings)
            {
                Mappings.Add(mapping);
            }

            UpdateMappingSourceDeviceStates(_joystickService.GetDeviceStatesSnapshot());
        }
        finally
        {
            _suppressConfigurationChangeTracking = false;
        }

        CurrentConfigurationFileName = normalized;
        IsConfigurationDirty = false;
        _logger.Info(nameof(MainViewModel), $"Configuration switched to: {CurrentConfigurationFileName}");
    }

    private void SetDefaultConfigurationByName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var normalized = EnsureJsonFileName(fileName);
        _configurationService.SetDefaultConfigurationFileName(normalized);
        _logger.Info(nameof(MainViewModel), $"Default configuration set to: {normalized}");
    }

    private static string EnsureJsonFileName(string fileName)
    {
        return fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? fileName : $"{fileName}.json";
    }

    private void MarkConfigurationDirty()
    {
        IsConfigurationDirty = true;
    }
}
