using D2BotNG.Core.Protos;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace D2BotNG.Services;

public class UpdateServiceImpl : UpdateService.UpdateServiceBase
{
    private readonly UpdateManager _updateManager;

    public UpdateServiceImpl(UpdateManager updateManager)
    {
        _updateManager = updateManager;
    }

    public override async Task<Empty> CheckForUpdate(Empty request, ServerCallContext context)
    {
        await _updateManager.CheckForUpdateAsync(context.CancellationToken);
        return new Empty();
    }

    public override async Task<Empty> StartUpdate(Empty request, ServerCallContext context)
    {
        await _updateManager.StartUpdateAsync(context.CancellationToken);
        return new Empty();
    }
}
