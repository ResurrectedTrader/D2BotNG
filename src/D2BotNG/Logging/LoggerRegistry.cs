using System.Collections.Concurrent;
using D2BotNG.Core.Protos;
using D2BotNG.Services;
using Google.Protobuf.WellKnownTypes;
using Serilog.Core;
using Serilog.Events;

namespace D2BotNG.Logging;

/// <summary>
/// Tracks all logger categories and their per-logger minimum levels.
/// Also serves as a global Serilog ILogEventFilter so all sinks respect the configured levels.
/// Session-only — levels reset to defaults on restart.
/// </summary>
public class LoggerRegistry : ILogEventFilter
{
    private const string Prefix = "D2BotNG.";
    private const LogEventLevel DefaultLevel = LogEventLevel.Information;

    private static LoggerRegistry? _instance;

    private readonly ConcurrentDictionary<string, LogEventLevel> _levels = new();
    private readonly EventBroadcaster? _eventBroadcaster;

    /// <summary>
    /// Used by Serilog's Filter.With&lt;T&gt;() — delegates to the DI instance via _instance.
    /// </summary>
    public LoggerRegistry() { }

    /// <summary>
    /// Used by DI.
    /// </summary>
    public LoggerRegistry(EventBroadcaster eventBroadcaster)
    {
        _eventBroadcaster = eventBroadcaster;
        _instance = this;
    }

    private static string Shorten(string category) =>
        category.StartsWith(Prefix) ? category[Prefix.Length..] : category;

    /// <summary>
    /// Register a logger category. If already registered, does nothing.
    /// </summary>
    public void Register(string category)
    {
        if (_levels.TryAdd(Shorten(category), DefaultLevel))
        {
            BroadcastSnapshot();
        }
    }

    /// <summary>
    /// Set the minimum log level for a category's messages.
    /// </summary>
    public void SetLevel(string category, LogEventLevel level)
    {
        _levels[category] = level;
        BroadcastSnapshot();
    }

    /// <summary>
    /// Check if a log event from the given source context should be logged.
    /// </summary>
    public bool ShouldLog(string? sourceContext, LogEventLevel level)
    {
        if (sourceContext == null)
            return level >= DefaultLevel;

        if (_levels.TryGetValue(Shorten(sourceContext), out var minLevel))
            return level >= minLevel;

        return level >= DefaultLevel;
    }

    /// <summary>
    /// ILogEventFilter implementation — Serilog calls this for every log event.
    /// Delegates to the DI instance's ShouldLog.
    /// </summary>
    public bool IsEnabled(LogEvent logEvent)
    {
        if (_instance == null)
            return true;

        var sourceContext = logEvent.Properties.TryGetValue("SourceContext", out var value)
            ? value.ToString().Trim('"')
            : null;

        return _instance.ShouldLog(sourceContext, logEvent.Level);
    }

    /// <summary>
    /// Get all registered categories and their current levels.
    /// </summary>
    public LogLevelsSnapshot GetSnapshot()
    {
        var snapshot = new LogLevelsSnapshot();
        foreach (var (category, level) in _levels)
        {
            snapshot.Levels.Add(new LogLevelEntry
            {
                Category = category,
                Level = MapToProtoLevel(level)
            });
        }
        return snapshot;
    }

    private void BroadcastSnapshot()
    {
        _eventBroadcaster?.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            LogLevelsSnapshot = GetSnapshot()
        });
    }

    public static LogEventLevel MapFromProtoLevel(SinkLogLevel level) => level switch
    {
        SinkLogLevel.Verbose => LogEventLevel.Verbose,
        SinkLogLevel.Debug => LogEventLevel.Debug,
        SinkLogLevel.Information => LogEventLevel.Information,
        SinkLogLevel.Warning => LogEventLevel.Warning,
        SinkLogLevel.Error => LogEventLevel.Error,
        SinkLogLevel.Fatal => LogEventLevel.Fatal,
        _ => LogEventLevel.Information
    };

    private static SinkLogLevel MapToProtoLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => SinkLogLevel.Verbose,
        LogEventLevel.Debug => SinkLogLevel.Debug,
        LogEventLevel.Information => SinkLogLevel.Information,
        LogEventLevel.Warning => SinkLogLevel.Warning,
        LogEventLevel.Error => SinkLogLevel.Error,
        LogEventLevel.Fatal => SinkLogLevel.Fatal,
        _ => SinkLogLevel.Information
    };
}
