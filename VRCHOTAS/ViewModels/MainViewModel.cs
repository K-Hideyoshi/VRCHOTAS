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
    private readonly OpenVrNativeLibraryService _openVrNativeLibraryService;
    private readonly VrOverlayService _vrOverlayService;
    private readonly SteamVrDriverDeploymentService _steamVrDriverDeploymentService;
    private readonly AnchorPointsService _anchorPointsService;
    private readonly HotkeyRuntime _hotkeyRuntime = new();
    private HotkeyPreferences _hotkeyPreferences = new();
    private EulerAnglePreferences _eulerAnglePreferences = new();
    private VrOverlayPreferences _vrOverlayPreferences = new();
    private ControllerOutputMode _controllerOutputMode = ControllerOutputMode.FullVirtual;
    private readonly SharedMemoryStateChannel? _ipc;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _deviceRefreshTimer;
    private readonly CancellationTokenSource _deviceRefreshCancellation = new();
    private readonly CancellationTokenSource _frameLoopCancellation = new();
    private readonly Task _frameLoopTask;
    private Task? _deviceRefreshTask;

    private RawJoystickState _latestState = new();
    private VirtualControllerState _lastMappedState = VirtualControllerState.CreateDefault();
    private MappingEntry[] _mappingSnapshot = [];
    private MappingEntry? _selectedMapping;
    private string _deviceStatusSummary = "No device discovered.";
    private string _currentConfigurationFileName = string.Empty;
    private bool _isConfigurationDirty;
    private bool _isMappingEnabled;
    private int _deviceShellRefreshQueued;
    private int _deviceMonitorRefreshQueued;
    private int _deviceRefreshInProgress;
    private int _joystickPollCountInWindow;
    private long _rateWindowStartTicks = Environment.TickCount64;
    private string _driverSyncRateDisplay = "—";
    private string _driverHeartbeatStatusDisplay = "No signal";
    private string _vrOverlayStatusDisplay = "Not connected";
    private string _vrOverlayLastErrorDisplay = string.Empty;
    private MappingEntry? _lastAutoSelectedMapping;
    private bool _isLocateMappingEnabled = true;
    private HandAnchorData _lastSavedAnchorLeft = new();
    private HandAnchorData _lastSavedAnchorRight = new();

    public ObservableCollection<MappingEntry> Mappings { get; } = new();
    public ObservableCollection<DeviceMonitorGroup> DeviceGroups { get; } = new();
    public ObservableCollection<LogEntry> LogEntries { get; } = new();
    public ObservableCollection<LogLevelFilterItem> LogLevelFilters { get; } = new();
    public ObservableCollection<string> AvailableConfigurationFiles { get; } = new();

    public ICollectionView FilteredLogs { get; }

    public ObservableCollection<ConfigurationMenuItem> LoadConfigurationMenuItems { get; } = new();
    public ObservableCollection<ConfigurationMenuItem> DefaultConfigurationMenuItems { get; } = new();
    public IReadOnlyList<ControllerOutputModeOption> ControllerOutputModeOptions { get; } =
    [
        new ControllerOutputModeOption("Full Virtual", ControllerOutputMode.FullVirtual),
        new ControllerOutputModeOption("Left Real + Right Virtual", ControllerOutputMode.HybridKeepLeftReal),
        new ControllerOutputModeOption("Left Virtual + Right Real", ControllerOutputMode.HybridKeepRightReal)
    ];

    public PreferencesService Preferences => _preferencesService;

    public IRelayCommand SaveConfigCommand { get; }
    public IRelayCommand SaveAsConfigCommand { get; }
    public IRelayCommand RefreshConfigListCommand { get; }
    public IRelayCommand OpenLogWindowCommand { get; }
    public IRelayCommand OpenCurrentLogFileLocationCommand { get; }
    public IRelayCommand OpenAddMappingDialogCommand { get; }
    public IRelayCommand OpenEditMappingDialogCommand { get; }
    public IRelayCommand DeleteSelectedMappingCommand { get; }
    public IRelayCommand ToggleLocateMappingCommand { get; }
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

    public string WindowTitle => $"VRCHOTAS - {CurrentConfigurationFileName}{(IsConfigurationDirty ? " *" : string.Empty)}";

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

            _vrOverlayService.ShowMasterSwitchToast(value);

            _vrOverlayService.UpdateStatusIndicator(value);
            _logger.Info(nameof(MainViewModel), $"Mapping master switch {(value ? "enabled" : "disabled")}.");
        }
    }

    public string MappingEnabledStatusText => IsMappingEnabled ? "Master ON" : "Master OFF";

    public bool IsLocateMappingEnabled
    {
        get => _isLocateMappingEnabled;
        set
        {
            if (!SetProperty(ref _isLocateMappingEnabled, value))
            {
                return;
            }

            if (!value)
            {
                _lastAutoSelectedMapping = null;
            }

            _preferencesService.SaveLocateMappingEnabled(value);
            _logger.Info(nameof(MainViewModel), $"Locate Mapping {(value ? "enabled" : "disabled")}.");
        }
    }

    public ControllerOutputMode ControllerOutputMode
    {
        get => _controllerOutputMode;
        set => SetControllerOutputMode(value, true);
    }

    public bool IsHybridControllerOutputMode => ControllerOutputMode is ControllerOutputMode.HybridKeepLeftReal or ControllerOutputMode.HybridKeepRightReal;

    public string ControllerOutputModeDisplay => ControllerOutputMode switch
    {
        ControllerOutputMode.HybridKeepLeftReal => "Left Real + Right Virtual",
        ControllerOutputMode.HybridKeepRightReal => "Left Virtual + Right Real",
        _ => "Full Virtual"
    };

    /// <summary>Driver sync rate averaged over the last 5 seconds (updated every 5s).</summary>
    public string DriverSyncRateDisplay
    {
        get => _driverSyncRateDisplay;
        private set => SetProperty(ref _driverSyncRateDisplay, value);
    }

    /// <summary>Driver shared-memory heartbeat status (OK when recent tick from OpenVR driver).</summary>
    public string DriverHeartbeatStatusDisplay
    {
        get => _driverHeartbeatStatusDisplay;
        private set
        {
            if (SetProperty(ref _driverHeartbeatStatusDisplay, value))
            {
                OnPropertyChanged(nameof(DriverHeartbeatStatusBrush));
            }
        }
    }

    public MediaBrush DriverHeartbeatStatusBrush => string.Equals(DriverHeartbeatStatusDisplay, "OK", StringComparison.OrdinalIgnoreCase)
        ? MediaBrushes.ForestGreen
        : MediaBrushes.Firebrick;

    public string VrOverlayStatusDisplay
    {
        get => _vrOverlayStatusDisplay;
        private set => SetProperty(ref _vrOverlayStatusDisplay, value);
    }

    public string VrOverlayLastErrorDisplay
    {
        get => _vrOverlayLastErrorDisplay;
        private set => SetProperty(ref _vrOverlayLastErrorDisplay, value);
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
        _openVrNativeLibraryService = new OpenVrNativeLibraryService(_logger);
        _vrOverlayService = new VrOverlayService(_logger, _openVrNativeLibraryService);
        _vrOverlayService.StatusChanged += OnVrOverlayStatusChanged;
        _anchorPointsService = new AnchorPointsService(_logger);
        _steamVrDriverDeploymentService = new SteamVrDriverDeploymentService(_logger);
        _preferencesService.EnsurePreferencesFileReady();
        _hotkeyPreferences = _preferencesService.LoadHotkeys();
        _eulerAnglePreferences = _preferencesService.LoadEulerAngles();
        _vrOverlayPreferences = _preferencesService.LoadVrOverlay();
        _controllerOutputMode = _preferencesService.LoadControllerOutputMode();
        _isLocateMappingEnabled = _preferencesService.LoadLocateMappingEnabled();
        _mappingEngine.ApplyEulerAnglePreferences(_eulerAnglePreferences);
        _vrOverlayService.ApplyPreferences(_vrOverlayPreferences, _isMappingEnabled);
        _steamVrDriverDeploymentService.TryDeployOnStartup();

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
        ToggleLocateMappingCommand = new RelayCommand(() => IsLocateMappingEnabled = !IsLocateMappingEnabled);
        ToggleMappingEnabledCommand = new RelayCommand(() => IsMappingEnabled = !IsMappingEnabled);
        LoadConfigByNameCommand = new RelayCommand<string>(fileName => LoadConfigurationByName(fileName));
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
        _deviceRefreshTimer.Tick += (_, _) => _deviceRefreshTask = RefreshDevicesInBackgroundAsync();
        _deviceRefreshTimer.Start();

        _frameLoopTask = Task.Run(() => RunFrameLoopAsync(_frameLoopCancellation.Token));
    }

    public HashSet<string> GetJoystickHotkeyConflictKeysForCapture() => BuildJoystickHotkeyConflictSet();

    private async Task RefreshDevicesInBackgroundAsync()
    {
        if (_deviceRefreshCancellation.IsCancellationRequested)
        {
            return;
        }

        if (Interlocked.Exchange(ref _deviceRefreshInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                _joystickService.RefreshDevices();
                _joystickService.TryAcquireDisconnectedDevices();
            }, _deviceRefreshCancellation.Token);
        }
        catch (OperationCanceledException) when (_deviceRefreshCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(MainViewModel), "Background device refresh failed.", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _deviceRefreshInProgress, 0);
        }
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
        return true;
    }

    public RawJoystickState GetLatestStateSnapshot() => Volatile.Read(ref _latestState);

    public VirtualControllerState GetLatestMappedStateSnapshot()
    {
        var state = _lastMappedState;
        state.EnsureInitialized();
        return state;
    }

    public EulerAnglePreferences GetEulerAnglePreferencesSnapshot()
    {
        return _eulerAnglePreferences.Clone();
    }

    public VrOverlayPreferences GetVrOverlayPreferencesSnapshot()
    {
        return _vrOverlayPreferences.Clone();
    }

    public ControllerOutputMode GetControllerOutputModeSnapshot() => _controllerOutputMode;

    public void ApplyControllerOutputMode(ControllerOutputMode mode)
    {
        SetControllerOutputMode(mode, false);
    }

    public void ApplyVrOverlayPreferences(VrOverlayPreferences preferences)
    {
        _vrOverlayPreferences = preferences?.Clone() ?? new VrOverlayPreferences();
        _vrOverlayPreferences.Normalize();
        _vrOverlayService.ApplyPreferences(_vrOverlayPreferences, _isMappingEnabled);
    }

    public void ShowVrOverlayTestToast()
    {
        _vrOverlayService.ShowTestToast();
    }

    private void OnVrOverlayStatusChanged(OverlayHelperStatusMessage status)
    {
        _dispatcher.BeginInvoke(() =>
        {
            VrOverlayStatusDisplay = status.Kind.ToString();
            if (status.Kind == OverlayHelperStatusKind.LastError)
            {
                VrOverlayLastErrorDisplay = string.IsNullOrWhiteSpace(status.Detail)
                    ? status.Message
                    : $"{status.Message} {status.Detail}";
            }
            else if (status.Kind is OverlayHelperStatusKind.OpenVrReady or OverlayHelperStatusKind.D3DReady or OverlayHelperStatusKind.FallbackRaw)
            {
                VrOverlayLastErrorDisplay = string.Empty;
            }
        });
    }

    private void SetControllerOutputMode(ControllerOutputMode mode, bool markDirty)
    {
        if (_controllerOutputMode == mode)
        {
            return;
        }

        _controllerOutputMode = mode;
        OnPropertyChanged(nameof(ControllerOutputMode));
        OnPropertyChanged(nameof(IsHybridControllerOutputMode));
        OnPropertyChanged(nameof(ControllerOutputModeDisplay));

        if (markDirty)
        {
            MarkConfigurationDirty();
            _logger.Info(nameof(MainViewModel), $"Controller output mode changed to: {mode}");
        }
    }

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

    public void DuplicateMapping(MappingEntry mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var index = Mappings.IndexOf(mapping);
        if (index < 0)
        {
            return;
        }

        var duplicate = CloneMapping(mapping);
        Mappings.Insert(index + 1, duplicate);
        SelectedMapping = duplicate;
        MarkConfigurationDirty();
        _logger.Info(nameof(MainViewModel), $"Mapping duplicated: {mapping.SourceDisplay} -> {mapping.TargetDisplay}");
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
        return CreateDuplicateBrush(index, 0.26);
    }

    private static MediaBrush CreateTargetDuplicateBrush(int index)
    {
        return CreateDuplicateBrush(index, 0.0);
    }

    private static MediaBrush CreateDuplicateBrush(int index, double hueOffset)
    {
        const int hueCount = 8;
        var brightnessLevels = new[] { 0.82, 0.68, 0.54 };
        var hueIndex = ((index % hueCount) + hueCount) % hueCount;
        var brightnessIndex = (index / hueCount) % brightnessLevels.Length;
        var hue = (hueIndex * (360.0 / hueCount) + (hueOffset * 360.0)) % 360.0;
        var color = CreateColorFromHsv(hue, 0.45, brightnessLevels[brightnessIndex]);
        return CreateFrozenBrush(MediaColor.FromArgb(0x66, color.R, color.G, color.B));
    }

    private static MediaColor CreateColorFromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360.0) + 360.0) % 360.0;
        saturation = Math.Clamp(saturation, 0.0, 1.0);
        value = Math.Clamp(value, 0.0, 1.0);

        if (saturation <= 0.0)
        {
            var channel = (byte)Math.Round(value * 255.0);
            return MediaColor.FromRgb(channel, channel, channel);
        }

        var sector = hue / 60.0;
        var sectorIndex = (int)Math.Floor(sector);
        var fraction = sector - sectorIndex;
        var p = value * (1.0 - saturation);
        var q = value * (1.0 - saturation * fraction);
        var t = value * (1.0 - saturation * (1.0 - fraction));

        var (red, green, blue) = sectorIndex switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q)
        };

        return MediaColor.FromRgb(
            (byte)Math.Round(red * 255.0),
            (byte)Math.Round(green * 255.0),
            (byte)Math.Round(blue * 255.0));
    }

    private static MediaBrush CreateFrozenBrush(MediaColor color)
    {
        var brush = new MediaSolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static MappingEntry CloneMapping(MappingEntry mapping)
    {
        return new MappingEntry
        {
            TargetKind = mapping.TargetKind,
            IsAxisMapping = mapping.IsAxisMapping,
            TargetHand = mapping.TargetHand,
            SourceDeviceId = mapping.SourceDeviceId,
            SourceDeviceName = mapping.SourceDeviceName,
            SourceAxis = mapping.SourceAxis,
            SourceButtonIndex = mapping.SourceButtonIndex,
            AxisRange = mapping.AxisRange,
            TargetAxis = mapping.TargetAxis,
            TargetButton = mapping.TargetButton,
            TargetControllerPose = mapping.TargetControllerPose,
            TargetControllerPoseAction = mapping.TargetControllerPoseAction,
            FullPressThreshold = mapping.FullPressThreshold,
            ToggleMode = mapping.ToggleMode,
            Deadzone = mapping.Deadzone,
            Curve = mapping.Curve,
            Saturation = mapping.Saturation,
            InputInvert = mapping.InputInvert,
            Invert = mapping.Invert,
            Description = mapping.Description,
            IsSourceDeviceConnected = mapping.IsSourceDeviceConnected,
            IsTemporarilyDisabled = mapping.IsTemporarilyDisabled
        };
    }

    public void Dispose()
    {
        _deviceRefreshTimer.Stop();
        _deviceRefreshCancellation.Cancel();
        _frameLoopCancellation.Cancel();

        // Wait for background tasks with a hard timeout so we never block
        // process exit indefinitely.
        const int taskTimeoutMs = 3000;

        try
        {
            if (_deviceRefreshTask is not null)
            {
                _deviceRefreshTask.Wait(TimeSpan.FromMilliseconds(taskTimeoutMs));
            }
        }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
        {
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            _frameLoopTask.Wait(TimeSpan.FromMilliseconds(taskTimeoutMs));
        }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
        {
        }
        catch (OperationCanceledException)
        {
        }

        _joystickService.DevicesChanged -= OnDevicesChanged;
        Mappings.CollectionChanged -= OnMappingsCollectionChanged;
        _logger.EntryWritten -= OnLogWritten;
        _vrOverlayService.StatusChanged -= OnVrOverlayStatusChanged;

        foreach (var filter in LogLevelFilters)
        {
            filter.PropertyChanged -= OnLogLevelFilterChanged;
        }

        _deviceRefreshCancellation.Dispose();
        _frameLoopCancellation.Dispose();
        _joystickService.Dispose();
        _ipc?.Dispose();
        _vrOverlayService.Dispose();
        _anchorPointsService.FlushPendingSave();
        _logger.Info(nameof(MainViewModel), "Application stopped.");
    }
}
