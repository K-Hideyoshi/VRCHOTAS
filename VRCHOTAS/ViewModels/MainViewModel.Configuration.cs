using VRCHOTAS.Models;

namespace VRCHOTAS.ViewModels;

public sealed partial class MainViewModel
{
    private void InitializeConfigurationOnStartup()
    {
        var defaultFileName = _preferencesService.GetDefaultConfigurationFileName();
        _configurationService.EnsureConfigurationFileExistsOrCreate(defaultFileName);
        RefreshAvailableConfigurations();
        LoadConfigurationByName(defaultFileName, false);
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
            Mappings = Mappings.ToList()
        });

        CurrentConfigurationFileName = normalizedFileName;
        IsConfigurationDirty = false;
        RefreshAvailableConfigurations();
    }

    public void SaveCurrentConfigurationForUi()
    {
        SaveCurrentConfiguration();
    }

    public bool ConfigurationFileExists(string fileName)
    {
        return _configurationService.ConfigurationExists(fileName);
    }

    public string GetConfigurationDirectoryPathForUi()
    {
        return _configurationService.GetConfigurationDirectoryPath();
    }

    public bool TryCreateNewConfiguration(string fileName, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            errorMessage = "Configuration file name cannot be empty.";
            return false;
        }

        var normalizedFileName = EnsureJsonFileName(fileName);
        if (_configurationService.ConfigurationExists(normalizedFileName))
        {
            errorMessage = $"Configuration already exists: {normalizedFileName}";
            return false;
        }

        _configurationService.SaveByFileName(normalizedFileName, new AppConfiguration());
        RefreshAvailableConfigurations();
        LoadConfigurationByName(normalizedFileName);
        _logger.Info(nameof(MainViewModel), $"New configuration created: {normalizedFileName}");
        return true;
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

        RebuildConfigurationMenuItems();
    }

    private void RebuildConfigurationMenuItems()
    {
        LoadConfigurationMenuItems.Clear();
        DefaultConfigurationMenuItems.Clear();
        var defaultFile = _preferencesService.GetDefaultConfigurationFileName();
        foreach (var fileName in AvailableConfigurationFiles)
        {
            var isCurrent = string.Equals(fileName, CurrentConfigurationFileName, StringComparison.OrdinalIgnoreCase);
            var isDefault = string.Equals(fileName, defaultFile, StringComparison.OrdinalIgnoreCase);
            LoadConfigurationMenuItems.Add(new ConfigurationMenuItem(fileName, isCurrent));
            DefaultConfigurationMenuItems.Add(new ConfigurationMenuItem(fileName, isDefault));
        }
    }

    private void CycleConfiguration(int delta)
    {
        if (AvailableConfigurationFiles.Count == 0)
        {
            return;
        }

        var files = AvailableConfigurationFiles.ToList();
        var idx = files.FindIndex(f => string.Equals(f, CurrentConfigurationFileName, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
        {
            idx = 0;
        }

        var next = (idx + delta + files.Count) % files.Count;
        LoadConfigurationByName(files[next]);
    }

    private void LoadConfigurationByName(string? fileName, bool showOverlayToast = true)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var normalized = EnsureJsonFileName(fileName);
        var config = _configurationService.LoadByFileName(normalized);
        Mappings.Clear();
        foreach (var mapping in config.Mappings)
        {
            Mappings.Add(mapping);
        }

        UpdateMappingSourceDeviceStates(_joystickService.GetDeviceStatesSnapshot());

        CurrentConfigurationFileName = normalized;
        IsConfigurationDirty = false;
        RebuildConfigurationMenuItems();

        if (showOverlayToast)
        {
            _vrOverlayService.ShowConfigurationToast(CurrentConfigurationFileName);
        }

        _logger.Info(nameof(MainViewModel), $"Configuration switched to: {CurrentConfigurationFileName}");
    }

    private void SetDefaultConfigurationByName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var normalized = EnsureJsonFileName(fileName);
        _preferencesService.SetDefaultConfigurationFileName(normalized);
        RebuildConfigurationMenuItems();
        _logger.Info(nameof(MainViewModel), $"Default configuration set to: {normalized}");
    }

    public void ApplyHotkeyPreferences(HotkeyPreferences preferences)
    {
        _hotkeyPreferences = preferences ?? new HotkeyPreferences();
    }

    public void ApplyEulerAnglePreferences(EulerAnglePreferences preferences)
    {
        _eulerAnglePreferences = preferences?.Clone() ?? new EulerAnglePreferences();
        _mappingEngine.ApplyEulerAnglePreferences(_eulerAnglePreferences);
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
