using D2BotNG.Core.Protos;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace D2BotNG.Services;

/// <summary>
/// gRPC service for character analytics mutations. Character state is read-only over the
/// event stream; this exposes the resets/clears.
/// </summary>
public class CharacterServiceImpl : CharacterService.CharacterServiceBase
{
    private readonly CharacterStateService _characterState;

    public CharacterServiceImpl(CharacterStateService characterState)
    {
        _characterState = characterState;
    }

    public override Task<Empty> ResetKills(ResetCharacterRequest request, ServerCallContext context)
    {
        _characterState.ResetKills(request.Profile);
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> ResetAreaTime(ResetCharacterRequest request, ServerCallContext context)
    {
        _characterState.ResetAreaTime(request.Profile);
        return Task.FromResult(new Empty());
    }
}
