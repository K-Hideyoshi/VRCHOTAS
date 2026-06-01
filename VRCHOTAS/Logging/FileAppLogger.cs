using System.IO;
using System.Text;

namespace VRCHOTAS.Logging;

public sealed class FileAppLogger : IAppLogger, IDisposable
{
    public const string LogDirectoryEnvironmentVariable = "VRCHOTAS_LOG_DIR";

    private const int MaxRetainedLogFiles = 30;
    private const long MaxLogDirectorySizeBytes = 20L * 1024 * 1024;

    private readonly object _sync = new();
    private readonly StreamWriter _writer;
    private readonly string _logDirectory;

    public event Action<LogEntry>? EntryWritten;
    public string CurrentLogFilePath { get; }

    public FileAppLogger(string? logDirectory = null, string? fileNameSuffix = null)
    {
        var appDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCHOTAS");
        var configuredLogDirectory = logDirectory;
        if (string.IsNullOrWhiteSpace(configuredLogDirectory))
        {
            configuredLogDirectory = Environment.GetEnvironmentVariable(LogDirectoryEnvironmentVariable);
        }

        var logDir = string.IsNullOrWhiteSpace(configuredLogDirectory)
            ? Path.Combine(appDataDirectory, "logs")
            : configuredLogDirectory;

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

            _logDirectory = logDir;
            CurrentLogFilePath = Path.Combine(logDir, BuildLogFileName(fileNameSuffix));
            _writer = new StreamWriter(CurrentLogFilePath, append: true, Encoding.UTF8) { AutoFlush = true };
            CleanupOldLogFiles();
        }
        catch
        {
            // Last resort: use temp path so the application can continue running even if
            // AppData folder is not writable or a file blocks the expected directory.
            logDir = Path.Combine(Path.GetTempPath(), "VRCHOTAS", "logs");
            Directory.CreateDirectory(logDir);
            _logDirectory = logDir;
            CurrentLogFilePath = Path.Combine(logDir, BuildLogFileName(fileNameSuffix));
            _writer = new StreamWriter(CurrentLogFilePath, append: true, Encoding.UTF8) { AutoFlush = true };
            CleanupOldLogFiles();
        }
    }

    private static string BuildLogFileName(string? fileNameSuffix)
    {
        var suffix = string.IsNullOrWhiteSpace(fileNameSuffix)
            ? string.Empty
            : $"-{fileNameSuffix.Trim()}";
        return $"vrchotas-{DateTime.Now:yyyyMMdd-HHmmss}{suffix}.log";
    }

    private void CleanupOldLogFiles()
    {
        try
        {
            var currentFullPath = Path.GetFullPath(CurrentLogFilePath);
            var files = Directory
                .EnumerateFiles(_logDirectory, "*.log", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => string.Equals(file.FullName, currentFullPath, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var index = MaxRetainedLogFiles; index < files.Count; index++)
            {
                var file = files[index];
                if (string.Equals(file.FullName, currentFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TryDeleteFile(file.FullName);
            }

            var retainedFiles = Directory
                .EnumerateFiles(_logDirectory, "*.log", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderBy(file => string.Equals(file.FullName, currentFullPath, StringComparison.OrdinalIgnoreCase))
                .ThenBy(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            long totalSize = retainedFiles.Sum(file => file.Exists ? file.Length : 0L);
            foreach (var file in retainedFiles)
            {
                if (totalSize <= MaxLogDirectorySizeBytes)
                {
                    break;
                }

                if (string.Equals(file.FullName, currentFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fileSize = file.Exists ? file.Length : 0L;
                if (!TryDeleteFile(file.FullName))
                {
                    continue;
                }

                totalSize -= fileSize;
            }
        }
        catch
        {
        }
    }

    private static bool TryDeleteFile(string filePath)
    {
        try
        {
            File.Delete(filePath);
            return true;
        }
        catch
        {
            return false;
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
            CleanupOldLogFiles();
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
