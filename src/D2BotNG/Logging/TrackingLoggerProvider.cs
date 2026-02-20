using Microsoft.Extensions.Logging;

namespace D2BotNG.Logging;

/// <summary>
/// An ILoggerProvider that registers logger categories in the LoggerRegistry.
/// Does not produce loggers that emit — actual logging is handled by Serilog's provider.
/// </summary>
public class TrackingLoggerProvider : ILoggerProvider
{
    private readonly LoggerRegistry _registry;

    public TrackingLoggerProvider(LoggerRegistry registry)
    {
        _registry = registry;
    }

    public ILogger CreateLogger(string categoryName)
    {
        _registry.Register(categoryName);
        return NullLogger.Instance;
    }

    public void Dispose() { }

    private sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
