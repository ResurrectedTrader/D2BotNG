using System.Collections.Concurrent;
using Serilog;

namespace D2BotNG.Logging;

/// <summary>
/// Wraps ILoggerFactory to intercept CreateLogger calls and register categories
/// in the LoggerRegistry. Uses static initialization since it must be created
/// before DI is built.
/// </summary>
public class TrackingLoggerFactory : ILoggerFactory
{
    private readonly ILoggerFactory _inner;
    private static LoggerRegistry? _registry;
    private static readonly ConcurrentBag<string> PendingCategories = [];

    public TrackingLoggerFactory(ILoggerFactory inner)
    {
        _inner = inner;
    }

    public static void Initialize(LoggerRegistry registry)
    {
        _registry = registry;
        while (PendingCategories.TryTake(out var category))
            registry.Register(category);
    }

    /// <summary>
    /// Create a static Serilog logger while also tracking the category in the registry.
    /// Use instead of Log.ForContext() for static loggers that should appear in the UI.
    /// </summary>
    public static Serilog.ILogger ForContext(Type source)
    {
        var category = source.FullName ?? source.Name;
        if (category.StartsWith("D2BotNG."))

        {
            if (_registry != null)
                _registry.Register(category);
            else
                PendingCategories.Add(category);
        }
        return Log.ForContext(source);
    }

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
    {
        if (categoryName.StartsWith("D2BotNG."))
        {
            if (_registry != null)
                _registry.Register(categoryName);
            else
                PendingCategories.Add(categoryName);
        }
        return _inner.CreateLogger(categoryName);
    }

    public void AddProvider(ILoggerProvider provider) => _inner.AddProvider(provider);
    public void Dispose() => _inner.Dispose();
}
