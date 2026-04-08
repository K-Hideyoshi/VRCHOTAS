namespace VRCHOTAS.Logging;

public static class LogManager
{
    private static readonly Lazy<IAppLogger> _logger = new(() => new FileAppLogger());

    public static IAppLogger Logger => _logger.Value;
}
