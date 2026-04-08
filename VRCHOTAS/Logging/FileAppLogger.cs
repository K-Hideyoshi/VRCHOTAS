using System.IO;
using System.Text;

namespace VRCHOTAS.Logging;

public sealed class FileAppLogger : IAppLogger, IDisposable
{
    private readonly object _sync = new();
    private readonly StreamWriter _writer;

    public event Action<LogEntry>? EntryWritten;
    public string CurrentLogFilePath { get; }

    public FileAppLogger()
    {
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);

        CurrentLogFilePath = Path.Combine(logDir, $"vrchotas-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _writer = new StreamWriter(CurrentLogFilePath, append: true, Encoding.UTF8) { AutoFlush = true };
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
