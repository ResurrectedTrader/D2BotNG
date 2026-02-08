using Serilog;
using Serilog.Configuration;

namespace D2BotNG.Logging;

/// <summary>
/// Extension methods for configuring the MessageService sink.
/// </summary>
public static class MessageServiceSinkExtensions
{
    /// <summary>
    /// Write log events to the MessageService as "Service" source.
    /// Call MessageServiceSink.Initialize() after the DI container is built.
    /// </summary>
    public static LoggerConfiguration MessageService(
        this LoggerSinkConfiguration sinkConfiguration)
    {
        return sinkConfiguration.Sink(new MessageServiceSink());
    }
}
