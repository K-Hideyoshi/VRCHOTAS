using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace VRCHOTAS.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IAppLogger _logger;
    private readonly JoystickService _joystickService;
    private readonly MappingEngine _mappingEngine;
    private readonly ConfigurationService _configurationService;
    private readonly PreferencesService _preferencesService;
    private readonly HotkeyRuntime _hotkeyRuntime = new();
    private HotkeyPreferences _hotkeyPreferences = new();
    private readonly SharedMemoryStateChannel? _ipc;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _deviceRefreshTimer;
    private readonly CancellationTokenSource _frameLoopCancellation = new();
    private readonly Task _frameLoopTask;

    private RawJoystickState _latestState = new();
    private VirtualControllerState _lastMappedState = VirtualControllerState.CreateDefault();
    private MappingEntry[] _mappingSnapshot = [];
    private MappingEntry? _selectedMapping;
    private string _deviceStatusSummary = "No device discovered.";
    private string _currentConfigurationFileName = string.Empty;
    private bool _isConfigurationDirty;
    private bool _isMappingEnabled = true;
    private int _deviceShellRefreshQueued;
    private int _deviceMonitorRefreshQueued;
    private int _joystickPollCountInWindow;
    private long _rateWindowStartTicks = Environment.TickCount64;
    private string _joystickRefreshRateDisplay = "—";
    private string _driverHeartbeatStatusDisplay = "No signal";
    private MappingEntry? _lastAutoSelectedMapping;

    public ObservableCollection<MappingEntry> Mappings { get; } = new();
    public ObservableCollection<DeviceMonitorGroup> DeviceGroups { get; } = new();
    public ObservableCollection<LogEntry> LogEntries { get; } = new();
    public ObservableCollection<LogLevelFilterItem> LogLevelFilters { get; } = new();
    public ObservableCollection<string> AvailableConfigurationFiles { get; } = new();

    public ICollectionView FilteredLogs { get; }

    public ObservableCollection<ConfigurationMenuItem> ConfigurationMenuItems { get; } = new();

    public PreferencesService Preferences => _preferencesService;

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
    public IRelayCommand<MappingEntry?> ToggleMappingTempDisabledCommand { get; }

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
            _logger.Info(nameof(MainViewModel), $"Mapping master switch {(value ? "enabled" : "disabled")}.");
        }
    }

    public string MappingEnabledStatusText => IsMappingEnabled ? "Enabled" : "Disabled";

    /// <summary>Joystick poll rate averaged over the last 5 seconds (updated every 5s).</summary>
    public string JoystickRefreshRateDisplay
    {
        get => _joystickRefreshRateDisplay;
        private set => SetProperty(ref _joystickRefreshRateDisplay, value);
    }

    /// <summary>Driver shared-memory heartbeat status (OK when recent tick from OpenVR driver).</summary>
    public string DriverHeartbeatStatusDisplay
    {
        get => _driverHeartbeatStatusDisplay;
        private set => SetProperty(ref _driverHeartbeatStatusDisplay, value);
    }

    public MainViewModel()
    {
        _dispatcher = WpfApplication.Current?.Dispatcher ?? throw new InvalidOperationException("A WPF dispatcher is required.");
        _logger = LogManager.Logger;
        _logger.EntryWritten += OnLogWritten;
        _logger.Info(nameof(MainViewModel), "Application started.");

        _joystickService = new JoystickService(_logger);
        _mappingEngine = new MappingEngine(_logger);
        _configurationService = new ConfigurationService(_logger);
        _preferencesService = new PreferencesService(_logger);
        _preferencesService.EnsurePreferencesFileReady();
        _hotkeyPreferences = _preferencesService.LoadHotkeys();

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
        ToggleMappingTempDisabledCommand = new RelayCommand<MappingEntry?>(entry =>
        {
            if (entry is null)
            {
                return;
            }

            entry.IsTemporarilyDisabled = !entry.IsTemporarilyDisabled;
        });

        InitializeLogFilters();
        FilteredLogs = CollectionViewSource.GetDefaultView(LogEntries);
        FilteredLogs.Filter = FilterLogEntry;

        Mappings.CollectionChanged += OnMappingsCollectionChanged;

        InitializeConfigurationOnStartup();
        RefreshMappingSnapshot();

        _joystickService.DevicesChanged += OnDevicesChanged;
        _joystickService.RefreshDevices();

        _deviceRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _deviceRefreshTimer.Tick += (_, _) =>
        {
            _joystickService.RefreshDevices();
            _joystickService.TryAcquireDisconnectedDevices();
        };
        _deviceRefreshTimer.Start();

        _frameLoopTask = Task.Run(() => RunFrameLoopAsync(_frameLoopCancellation.Token));
    }

    public HashSet<string> GetJoystickHotkeyConflictKeysForCapture() => BuildJoystickHotkeyConflictSet();

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
        return true;
    }

    public RawJoystickState GetLatestStateSnapshot() => Volatile.Read(ref _latestState);

    public void MoveMappingUp(MappingEntry mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var index = Mappings.IndexOf(mapping);
        if (index <= 0)
        {
            SelectedMapping = mapping;
            return;
        }

        Mappings.Move(index, index - 1);
        SelectedMapping = mapping;
        MarkConfigurationDirty();
        _logger.Info(nameof(MainViewModel), $"Mapping moved up: {mapping.SourceDisplay} -> {mapping.TargetDisplay}");
    }

    public void MoveMappingDown(MappingEntry mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var index = Mappings.IndexOf(mapping);
        if (index < 0 || index >= Mappings.Count - 1)
        {
            SelectedMapping = mapping;
            return;
        }

        Mappings.Move(index, index + 1);
        SelectedMapping = mapping;
        MarkConfigurationDirty();
        _logger.Info(nameof(MainViewModel), $"Mapping moved down: {mapping.SourceDisplay} -> {mapping.TargetDisplay}");
    }

    public void MoveMappingToIndex(MappingEntry mapping, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var currentIndex = Mappings.IndexOf(mapping);
        if (currentIndex < 0)
        {
            return;
        }

        targetIndex = Math.Clamp(targetIndex, 0, Math.Max(0, Mappings.Count - 1));
        if (currentIndex == targetIndex)
        {
            SelectedMapping = mapping;
            return;
        }

        Mappings.Move(currentIndex, targetIndex);
        SelectedMapping = mapping;
        MarkConfigurationDirty();
        _logger.Info(nameof(MainViewModel), $"Mapping moved to index {targetIndex}: {mapping.SourceDisplay} -> {mapping.TargetDisplay}");
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

    private void OnMappingsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshMappingDuplicateBackgrounds();
        RefreshMappingSnapshot();
    }

    private void RefreshMappingSnapshot()
    {
        Volatile.Write(ref _mappingSnapshot, Mappings.ToArray());
    }

    private void RefreshMappingDuplicateBackgrounds()
    {
        foreach (var mapping in Mappings)
        {
            mapping.SourceDuplicateBackground = MediaBrushes.Transparent;
            mapping.TargetDuplicateBackground = MediaBrushes.Transparent;
        }

        var indexedMappings = Mappings.Select((mapping, index) => new { mapping, index }).ToArray();

        var sourceGroups = indexedMappings
            .GroupBy(item => item.mapping.SourceGroupingKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Min(item => item.index))
            .ToArray();

        for (var index = 0; index < sourceGroups.Length; index++)
        {
            var brush = CreateSourceDuplicateBrush(index);
            foreach (var item in sourceGroups[index])
            {
                item.mapping.SourceDuplicateBackground = brush;
            }
        }

        var targetGroups = indexedMappings
            .GroupBy(item => item.mapping.TargetGroupingKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Min(item => item.index))
            .ToArray();

        for (var index = 0; index < targetGroups.Length; index++)
        {
            var brush = CreateTargetDuplicateBrush(index);
            foreach (var item in targetGroups[index])
            {
                item.mapping.TargetDuplicateBackground = brush;
            }
        }
    }

    private static MediaBrush CreateSourceDuplicateBrush(int index)
    {
        var depth = (byte)Math.Max(88, 228 - ((index * 29) % 150));
        return CreateFrozenBrush(MediaColor.FromArgb(0x66, 0xFF, depth, depth));
    }

    private static MediaBrush CreateTargetDuplicateBrush(int index)
    {
        var depth = (byte)Math.Max(88, 228 - ((index * 29) % 150));
        return CreateFrozenBrush(MediaColor.FromArgb(0x66, depth, depth, 0xFF));
    }

    private static MediaBrush CreateFrozenBrush(MediaColor color)
    {
        var brush = new MediaSolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public void Dispose()
    {
        _deviceRefreshTimer.Stop();
        _frameLoopCancellation.Cancel();

        try
        {
            _frameLoopTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        _joystickService.DevicesChanged -= OnDevicesChanged;
        Mappings.CollectionChanged -= OnMappingsCollectionChanged;
        _logger.EntryWritten -= OnLogWritten;

        foreach (var filter in LogLevelFilters)
        {
            filter.PropertyChanged -= OnLogLevelFilterChanged;
        }

        _frameLoopCancellation.Dispose();
        _joystickService.Dispose();
        _ipc?.Dispose();
        _logger.Info(nameof(MainViewModel), "Application stopped.");
    }
}
