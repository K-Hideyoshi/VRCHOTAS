using System.IO;
using System.Text;

namespace VRCHOTAS.Logging;

public sealed class FileAppLogger : IAppLogger, IDisposable
{
    private const int MaxRetainedLogFiles = 100;

    private readonly object _sync = new();
    private readonly StreamWriter _writer;

    public event Action<LogEntry>? EntryWritten;
    public string CurrentLogFilePath { get; }

    public FileAppLogger()
    {
        var appDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCHOTAS");
        var logDir = Path.Combine(appDataDirectory, "logs");

        // If a file exists where we expect a directory, or creating the directory fails,
        // fall back to the system temp path to avoid FileNotFoundException when opening the log file.
        try
        {
            if (File.Exists(logDir) || File.Exists(appDataDirectory))
            {
                // fallback to temp folder if a file collides with the expected directory path
                logDir = Path.Combine(Path.GetTempPath(), "VRCHOTAS", "logs");
                Directory.CreateDirectory(logDir);
            }
            else
            {
                Directory.CreateDirectory(logDir);
            }

            CurrentLogFilePath = Path.Combine(logDir, $"vrchotas-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _writer = new StreamWriter(CurrentLogFilePath, append: true, Encoding.UTF8) { AutoFlush = true };
            CleanupOldLogFiles(logDir);
        }
        catch
        {
            // Last resort: use temp path so the application can continue running even if
            // AppData folder is not writable or a file blocks the expected directory.
            logDir = Path.Combine(Path.GetTempPath(), "VRCHOTAS", "logs");
            Directory.CreateDirectory(logDir);
            CurrentLogFilePath = Path.Combine(logDir, $"vrchotas-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _writer = new StreamWriter(CurrentLogFilePath, append: true, Encoding.UTF8) { AutoFlush = true };
            CleanupOldLogFiles(logDir);
        }
    }

    private void CleanupOldLogFiles(string logDir)
    {
        try
        {
            var currentFullPath = Path.GetFullPath(CurrentLogFilePath);
            var filesToKeep = Directory
                .EnumerateFiles(logDir, "*.log", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => string.Equals(file.FullName, currentFullPath, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxRetainedLogFiles)
                .Select(file => file.FullName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var filePath in Directory.EnumerateFiles(logDir, "*.log", SearchOption.TopDirectoryOnly))
            {
                if (filesToKeep.Contains(filePath))
                {
                    continue;
                }

                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            Log(AppLogLevel.Warning, nameof(FileAppLogger), $"Failed to enforce log retention policy. {ex.Message}");
        }
    }

    public void Log(AppLogLevel level, string source, string message, Exception? ex = null)
    {
        var entry = new LogEntry
        {
            Level = level,
            Source = source,
            Message = ex is null
                ? message
                : $"{message} | Exception: {ex.Message} | {ex}"
        };

        lock (_sync)
        {
            _writer.WriteLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] [{entry.Source}] {entry.Message}");
        }

        EntryWritten?.Invoke(entry);
    }

    public void Debug(string source, string message) => Log(AppLogLevel.Debug, source, message);
    public void Info(string source, string message) => Log(AppLogLevel.Info, source, message);
    public void Warning(string source, string message) => Log(AppLogLevel.Warning, source, message);
    public void Error(string source, string message, Exception? ex = null) => Log(AppLogLevel.Error, source, message, ex);

    public void Dispose()
    {
        lock (_sync)
        {
            _writer.Dispose();
        }
    }
}
