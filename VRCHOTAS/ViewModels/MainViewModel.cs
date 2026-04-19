using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCHOTAS.Interop;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;
using VRCHOTAS.Services;
using WpfApplication = System.Windows.Application;

namespace VRCHOTAS.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IAppLogger _logger;
    private readonly JoystickService _joystickService;
    private readonly MappingEngine _mappingEngine;
    private readonly ConfigurationService _configurationService;
    private readonly SharedMemoryStateChannel? _ipc;
    private readonly DispatcherTimer _frameTimer;
    private readonly DispatcherTimer _deviceRefreshTimer;

    private RawJoystickState _latestState = new();
    private MappingEntry? _selectedMapping;
    private string _deviceStatusSummary = "No device discovered.";
    private string _currentConfigurationFileName = string.Empty;
    private bool _isConfigurationDirty;
    private bool _isMappingEnabled = true;
    private bool _suppressConfigurationChangeTracking;

    public ObservableCollection<MappingEntry> Mappings { get; } = new();
    public ObservableCollection<DeviceMonitorGroup> DeviceGroups { get; } = new();
    public ObservableCollection<LogEntry> LogEntries { get; } = new();
    public ObservableCollection<LogLevelFilterItem> LogLevelFilters { get; } = new();
    public ObservableCollection<string> AvailableConfigurationFiles { get; } = new();

    public ICollectionView FilteredLogs { get; }

    public IRelayCommand SaveConfigCommand { get; }
    public IRelayCommand SaveAsConfigCommand { get; }
    public IRelayCommand RefreshConfigListCommand { get; }
    public IRelayCommand OpenLogWindowCommand { get; }
    public IRelayCommand OpenCurrentLogFileLocationCommand { get; }
    public IRelayCommand OpenAddMappingDialogCommand { get; }
    public IRelayCommand OpenEditMappingDialogCommand { get; }
    public IRelayCommand DeleteSelectedMappingCommand { get; }
    public IRelayCommand ToggleMappingEnabledCommand { get; }
    public IRelayCommand<string> LoadConfigByNameCommand { get; }
    public IRelayCommand<string> SetDefaultConfigByNameCommand { get; }

    public event EventHandler? LogWindowRequested;
    public event EventHandler<MappingEditorRequestEventArgs>? MappingEditorRequested;
    public event EventHandler? SaveAsRequested;

    public string DeviceStatusSummary
    {
        get => _deviceStatusSummary;
        set => SetProperty(ref _deviceStatusSummary, value);
    }

    public string CurrentConfigurationFileName
    {
        get => _currentConfigurationFileName;
        private set
        {
            if (SetProperty(ref _currentConfigurationFileName, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    public bool IsConfigurationDirty
    {
        get => _isConfigurationDirty;
        private set
        {
            if (SetProperty(ref _isConfigurationDirty, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    public string WindowTitle => $"VRCHOTAS Mapper - {CurrentConfigurationFileName}{(IsConfigurationDirty ? " *" : string.Empty)}";

    public MappingEntry? SelectedMapping
    {
        get => _selectedMapping;
        set => SetProperty(ref _selectedMapping, value);
    }

    public string CurrentLogFilePath => _logger.CurrentLogFilePath;

    public bool IsMappingEnabled
    {
        get => _isMappingEnabled;
        set
        {
            if (!SetProperty(ref _isMappingEnabled, value))
            {
                return;
            }

            OnPropertyChanged(nameof(MappingEnabledStatusText));

            if (_suppressConfigurationChangeTracking)
            {
                return;
            }

            MarkConfigurationDirty();
            _logger.Info(nameof(MainViewModel), $"Mapping master switch {(value ? "enabled" : "disabled")}.");
        }
    }

    public string MappingEnabledStatusText => IsMappingEnabled ? "Enabled" : "Disabled";

    public MainViewModel()
    {
        _logger = LogManager.Logger;
        _logger.EntryWritten += OnLogWritten;
        _logger.Info(nameof(MainViewModel), "Application started.");

        _joystickService = new JoystickService(_logger);
        _mappingEngine = new MappingEngine(_logger);
        _configurationService = new ConfigurationService(_logger);

        try
        {
            _ipc = new SharedMemoryStateChannel(_logger);
        }
        catch (Exception ex)
        {
            _ipc = null;
            _logger.Error(nameof(MainViewModel), "Shared memory channel initialization failed. Driver output will be unavailable.", ex);
        }

        SaveConfigCommand = new RelayCommand(SaveCurrentConfiguration);
        SaveAsConfigCommand = new RelayCommand(() => SaveAsRequested?.Invoke(this, EventArgs.Empty));
        RefreshConfigListCommand = new RelayCommand(RefreshAvailableConfigurations);
        OpenLogWindowCommand = new RelayCommand(() => LogWindowRequested?.Invoke(this, EventArgs.Empty));
        OpenCurrentLogFileLocationCommand = new RelayCommand(OpenCurrentLogFileLocation);
        OpenAddMappingDialogCommand = new RelayCommand(() => MappingEditorRequested?.Invoke(this, new MappingEditorRequestEventArgs(null)));
        OpenEditMappingDialogCommand = new RelayCommand(OpenEditMappingDialog);
        DeleteSelectedMappingCommand = new RelayCommand(DeleteSelectedMapping);
        ToggleMappingEnabledCommand = new RelayCommand(() => IsMappingEnabled = !IsMappingEnabled);
        LoadConfigByNameCommand = new RelayCommand<string>(LoadConfigurationByName);
        SetDefaultConfigByNameCommand = new RelayCommand<string>(SetDefaultConfigurationByName);

        InitializeLogFilters();
        FilteredLogs = CollectionViewSource.GetDefaultView(LogEntries);
        FilteredLogs.Filter = FilterLogEntry;

        InitializeConfigurationOnStartup();

        _joystickService.DevicesChanged += OnDevicesChanged;
        _joystickService.RefreshDevices();

        _deviceRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _deviceRefreshTimer.Tick += (_, _) =>
        {
            _joystickService.RefreshDevices();
            _joystickService.TryAcquireDisconnectedDevices();
        };
        _deviceRefreshTimer.Start();

        _frameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _frameTimer.Tick += (_, _) => RunFrame();
        _frameTimer.Start();
    }

    private void InitializeConfigurationOnStartup()
    {
        var defaultFileName = _configurationService.EnsureDefaultConfigurationFileName();
        RefreshAvailableConfigurations();
        LoadConfigurationByName(defaultFileName);
    }

    private void InitializeLogFilters()
    {
        LogLevelFilters.Clear();
        foreach (var level in Enum.GetValues<AppLogLevel>())
        {
            var item = new LogLevelFilterItem(level)
            {
                IsSelected = true
            };

            item.PropertyChanged += OnLogLevelFilterChanged;
            LogLevelFilters.Add(item);
        }
    }

    private void OnLogLevelFilterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogLevelFilterItem.IsSelected))
        {
            FilteredLogs.Refresh();
        }
    }

    private bool FilterLogEntry(object value)
    {
        if (value is not LogEntry entry)
        {
            return false;
        }

        var selectedLevels = LogLevelFilters.Where(item => item.IsSelected).Select(item => item.Level).ToHashSet();
        if (selectedLevels.Count == 0)
        {
            return true;
        }

        return selectedLevels.Contains(entry.Level);
    }

    private void OpenEditMappingDialog()
    {
        if (SelectedMapping is null)
        {
            _logger.Warning(nameof(MainViewModel), "Edit mapping requested with no selected item.");
            return;
        }

        MappingEditorRequested?.Invoke(this, new MappingEditorRequestEventArgs(SelectedMapping));
    }

    private void DeleteSelectedMapping()
    {
        if (SelectedMapping is null)
        {
            _logger.Warning(nameof(MainViewModel), "Delete mapping requested with no selected item.");
            return;
        }

        var removed = SelectedMapping;
        Mappings.Remove(removed);
        MarkConfigurationDirty();
        _logger.Info(nameof(MainViewModel), $"Mapping deleted: {removed.SourceDisplay} -> {removed.TargetDisplay}");
    }

    public void SaveMappingFromDialog(MappingEntry mapping, MappingEntry? original)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        if (!CanUseTarget(mapping, original, out var errorMessage))
        {
            throw new InvalidOperationException(errorMessage);
        }

        if (original is null)
        {
            Mappings.Add(mapping);
            MarkConfigurationDirty();
            _logger.Info(nameof(MainViewModel), $"Mapping created: {mapping.SourceDisplay} -> {mapping.TargetDisplay}");
            return;
        }

        var index = Mappings.IndexOf(original);
        if (index < 0)
        {
            Mappings.Add(mapping);
            MarkConfigurationDirty();
            _logger.Warning(nameof(MainViewModel), "Original mapping not found during edit. New mapping was added.");
            return;
        }

        Mappings[index] = mapping;
        MarkConfigurationDirty();
        _logger.Info(nameof(MainViewModel), $"Mapping updated: {mapping.SourceDisplay} -> {mapping.TargetDisplay}");
    }

    private bool CanUseTarget(MappingEntry candidate, MappingEntry? original, out string errorMessage)
    {
        errorMessage = string.Empty;
        var kind = candidate.ResolvedTargetKind;
        if (kind != MappingTargetKind.Button && kind != MappingTargetKind.AxisInput)
        {
            return true;
        }

        foreach (var existing in Mappings)
        {
            if (ReferenceEquals(existing, original))
            {
                continue;
            }

            if (existing.TargetHand != candidate.TargetHand)
            {
                continue;
            }

            var existingKind = existing.ResolvedTargetKind;
            if (kind == MappingTargetKind.Button && existingKind == MappingTargetKind.Button && existing.TargetButtonIndex == candidate.TargetButtonIndex)
            {
                errorMessage = $"Target button {candidate.TargetButtonIndex} on {candidate.TargetHand} hand is already mapped.";
                return false;
            }

            if (kind == MappingTargetKind.AxisInput && existingKind == MappingTargetKind.AxisInput && existing.TargetAxisIndex == candidate.TargetAxisIndex)
            {
                errorMessage = $"Target axis {candidate.TargetAxisIndex} on {candidate.TargetHand} hand is already mapped.";
                return false;
            }
        }

        return true;
    }

    public RawJoystickState GetLatestStateSnapshot() => _latestState;

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

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        WpfApplication.Current.Dispatcher.Invoke(RefreshDeviceGroupShells);
    }

    private void RefreshDeviceGroupShells()
    {
        var states = _joystickService.GetDeviceStatesSnapshot();

        var knownIds = states.Select(item => item.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in DeviceGroups.Where(group => !knownIds.Contains(group.DeviceId)).ToArray())
        {
            DeviceGroups.Remove(stale);
        }

        foreach (var state in states)
        {
            var group = DeviceGroups.FirstOrDefault(item => item.DeviceId.Equals(state.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (group is null)
            {
                group = new DeviceMonitorGroup
                {
                    DeviceId = state.DeviceId,
                    DeviceName = state.DeviceName
                };
                DeviceGroups.Add(group);
            }

            group.DeviceName = state.DeviceName;
            group.IsConnected = state.IsConnected;
        }

        UpdateMappingSourceDeviceStates(states);
        UpdateDeviceStatusSummary();
    }

    private void RunFrame()
    {
        try
        {
            _latestState = _joystickService.PollStates();
            UpdateDeviceGroups(_latestState);
            UpdateDeviceStatusSummary();

            var mapped = IsMappingEnabled
                ? _mappingEngine.Map(_latestState, Mappings)
                : VirtualControllerState.CreateDefault();
            mapped.PoseSource = IsMappingEnabled
                ? VirtualPoseSource.Mapped
                : VirtualPoseSource.MirrorRealControllers;
            _ipc?.Write(mapped);
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(MainViewModel), "Frame update failed.", ex);
        }
    }

    private void UpdateDeviceGroups(RawJoystickState state)
    {
        foreach (var device in state.Devices)
        {
            var group = DeviceGroups.FirstOrDefault(item => item.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (group is null)
            {
                group = new DeviceMonitorGroup
                {
                    DeviceId = device.DeviceId,
                    DeviceName = device.DeviceName
                };
                DeviceGroups.Add(group);
            }

            group.DeviceName = device.DeviceName;
            group.IsConnected = device.IsConnected;
            group.UpdateFrom(device);
        }

        UpdateMappingSourceDeviceStates(state.Devices);
    }

    private void UpdateMappingSourceDeviceStates(IEnumerable<JoystickDeviceState> states)
    {
        var connectedDeviceIds = states
            .Where(state => state.IsConnected)
            .Select(state => state.DeviceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in Mappings)
        {
            mapping.IsSourceDeviceConnected =
                !string.IsNullOrWhiteSpace(mapping.SourceDeviceId)
                && connectedDeviceIds.Contains(mapping.SourceDeviceId);
        }
    }

    private void UpdateDeviceStatusSummary()
    {
        if (DeviceGroups.Count == 0)
        {
            DeviceStatusSummary = "No device discovered.";
            return;
        }

        DeviceStatusSummary = string.Join(Environment.NewLine,
            DeviceGroups.Select(group =>
                $"{group.DeviceName} ({group.DeviceId[..Math.Min(8, group.DeviceId.Length)]}) - {(group.IsConnected ? "Connected" : "Disconnected")}"));
    }

    private void OnLogWritten(LogEntry entry)
    {
        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            LogEntries.Add(entry);
            return;
        }

        dispatcher.Invoke(() => LogEntries.Add(entry));
    }

    private void OpenCurrentLogFileLocation()
    {
        try
        {
            var logFilePath = _logger.CurrentLogFilePath;
            if (string.IsNullOrWhiteSpace(logFilePath))
            {
                _logger.Warning(nameof(MainViewModel), "Current log file path is empty.");
                return;
            }

            if (File.Exists(logFilePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{logFilePath}\"",
                    UseShellExecute = true
                });
                return;
            }

            var logDirectory = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrWhiteSpace(logDirectory) && Directory.Exists(logDirectory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{logDirectory}\"",
                    UseShellExecute = true
                });
                return;
            }

            _logger.Warning(nameof(MainViewModel), $"Log file and directory are missing: {logFilePath}");
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(MainViewModel), "Failed to open log file location.", ex);
        }
    }

    public void Dispose()
    {
        _frameTimer.Stop();
        _deviceRefreshTimer.Stop();
        _joystickService.DevicesChanged -= OnDevicesChanged;
        _logger.EntryWritten -= OnLogWritten;

        foreach (var filter in LogLevelFilters)
        {
            filter.PropertyChanged -= OnLogLevelFilterChanged;
        }

        _joystickService.Dispose();
        _ipc?.Dispose();
        _logger.Info(nameof(MainViewModel), "Application stopped.");
    }
}

public sealed class LogLevelFilterItem : ObservableObject
{
    private bool _isSelected;

    public LogLevelFilterItem(AppLogLevel level)
    {
        Level = level;
        DisplayName = level.ToString();
    }

    public AppLogLevel Level { get; }
    public string DisplayName { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class MappingEditorRequestEventArgs : EventArgs
{
    public MappingEditorRequestEventArgs(MappingEntry? mappingToEdit)
    {
        MappingToEdit = mappingToEdit;
    }

    public MappingEntry? MappingToEdit { get; }
}

public sealed class DeviceMonitorGroup : ObservableObject
{
    private string _deviceId = string.Empty;
    private string _deviceName = string.Empty;
    private bool _isConnected;

    public string DeviceId
    {
        get => _deviceId;
        set => SetProperty(ref _deviceId, value);
    }

    public string DeviceName
    {
        get => _deviceName;
        set => SetProperty(ref _deviceName, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    public ObservableCollection<AxisMonitorItem> Axes { get; } = new();
    public ObservableCollection<ButtonMonitorItem> Buttons { get; } = new();

    public void UpdateFrom(JoystickDeviceState state)
    {
        var axisKeys = state.Axes.Keys.ToList();
        while (Axes.Count < axisKeys.Count)
        {
            Axes.Add(new AxisMonitorItem());
        }

        while (Axes.Count > axisKeys.Count)
        {
            Axes.RemoveAt(Axes.Count - 1);
        }

        for (var index = 0; index < axisKeys.Count; index++)
        {
            var key = axisKeys[index];
            Axes[index].Name = key;
            Axes[index].Value = state.Axes[key];
        }

        while (Buttons.Count < state.Buttons.Count)
        {
            Buttons.Add(new ButtonMonitorItem());
        }

        while (Buttons.Count > state.Buttons.Count)
        {
            Buttons.RemoveAt(Buttons.Count - 1);
        }

        for (var index = 0; index < state.Buttons.Count; index++)
        {
            Buttons[index].Name = index.ToString();
            Buttons[index].IsPressed = state.Buttons[index];
        }
    }
}

public sealed class AxisMonitorItem : ObservableObject
{
    private string _name = string.Empty;
    private double _value;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public double Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}

public sealed class ButtonMonitorItem : ObservableObject
{
    private string _name = string.Empty;
    private bool _isPressed;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public bool IsPressed
    {
        get => _isPressed;
        set => SetProperty(ref _isPressed, value);
    }
}
