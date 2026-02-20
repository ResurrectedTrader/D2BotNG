using D2BotNG.Core.Protos;
using D2BotNG.Services;
using Serilog.Core;
using Serilog.Events;

namespace D2BotNG.Logging;

/// <summary>
/// Serilog sink that sends log messages to the MessageService.
/// Uses static references that must be set after DI is configured.
/// </summary>
public class MessageServiceSink : ILogEventSink
{
    private static MessageService? _messageService;
    private static LoggerRegistry? _loggerRegistry;

    /// <summary>
    /// Set the MessageService and LoggerRegistry instances. Call this after the DI container is built.
    /// </summary>
    public static void Initialize(MessageService messageService, LoggerRegistry loggerRegistry)
    {
        _messageService = messageService;
        _loggerRegistry = loggerRegistry;
    }

    public void Emit(LogEvent logEvent)
    {
        if (_messageService == null)
        {
            return; // Not initialized yet, skip
        }

        // Extract source context for level filtering
        var sourceContext = logEvent.Properties.TryGetValue("SourceContext", out var value)
            ? value.ToString().Trim('"')
            : null;

        // Check per-logger level filtering
        if (_loggerRegistry != null && !_loggerRegistry.ShouldLog(sourceContext, logEvent.Level))
        {
            return;
        }

        var message = logEvent.RenderMessage();
        var color = GetColorForLevel(logEvent.Level);

        // Include exception details if present
        if (logEvent.Exception != null)
        {
            message = $"{message}\n{logEvent.Exception}";
        }

        _messageService.AddMessage("Service", message, color);
    }

    private static MessageColor GetColorForLevel(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose => MessageColor.ColorGray,
            LogEventLevel.Debug => MessageColor.ColorGray,
            LogEventLevel.Information => MessageColor.ColorDefault,
            LogEventLevel.Warning => MessageColor.ColorOrange,
            LogEventLevel.Error => MessageColor.ColorRed,
            LogEventLevel.Fatal => MessageColor.ColorRed,
            _ => MessageColor.ColorDefault
        };
    }
}
