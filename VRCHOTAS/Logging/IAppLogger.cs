namespace VRCHOTAS.Logging;

public interface IAppLogger
{
    event Action<LogEntry>? EntryWritten;
    string CurrentLogFilePath { get; }

    void Log(AppLogLevel level, string source, string message, Exception? ex = null);
    void Debug(string source, string message);
    void Info(string source, string message);
    void Warning(string source, string message);
    void Error(string source, string message, Exception? ex = null);
}
