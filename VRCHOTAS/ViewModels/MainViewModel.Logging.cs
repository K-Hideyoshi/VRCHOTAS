using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using VRCHOTAS.Logging;

namespace VRCHOTAS.ViewModels;

public sealed partial class MainViewModel
{
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

    private void OnLogWritten(LogEntry entry)
    {
        if (_dispatcher.CheckAccess())
        {
            LogEntries.Add(entry);
            return;
        }

        _dispatcher.BeginInvoke(() => LogEntries.Add(entry));
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
}
