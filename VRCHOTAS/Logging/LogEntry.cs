namespace VRCHOTAS.Logging;

public sealed class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public AppLogLevel Level { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
