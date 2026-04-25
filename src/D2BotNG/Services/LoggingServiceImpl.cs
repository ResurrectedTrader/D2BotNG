using D2BotNG.Core.Protos;
using D2BotNG.Logging;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace D2BotNG.Services;

public class LoggingServiceImpl : LoggingService.LoggingServiceBase
{
    private readonly LoggerRegistry _loggerRegistry;

    public LoggingServiceImpl(LoggerRegistry loggerRegistry)
    {
        _loggerRegistry = loggerRegistry;
    }

    public override Task<Empty> SetLogLevel(SetLogLevelRequest request, ServerCallContext context)
    {
        var serilogLevel = LoggerRegistry.MapFromProtoLevel(request.Level);
        _loggerRegistry.SetLevel(request.Category, serilogLevel);
        return Task.FromResult(new Empty());
    }
}
