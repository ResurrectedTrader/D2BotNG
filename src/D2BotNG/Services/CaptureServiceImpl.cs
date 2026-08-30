using D2BotNG.Capture;
using D2BotNG.Core.Protos.Captures;
// Aliased rather than importing the v1 namespace: it declares its own Character and SearchItems
// messages, so pulling it in whole makes every one of them ambiguous here.
using Event = D2BotNG.Core.Protos.Event;
using CaptureChanged = D2BotNG.Core.Protos.CaptureChanged;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace D2BotNG.Services;

/// <summary>
/// Read endpoints for captured character state. Its own service rather than methods on
/// <see cref="CharacterServiceImpl" />: captures are a separate stack with separate storage, and
/// unlike v1 character state they are not streamed — an inventory carries every item's stat
/// lists, which is far too much to push through the event stream on every change. Clients pull
/// instead.
/// </summary>
public class CaptureServiceImpl : CaptureService.CaptureServiceBase
{
    private readonly CaptureStore _store;
    private readonly EventBroadcaster _events;

    public CaptureServiceImpl(CaptureStore store, EventBroadcaster events)
    {
        _store = store;
        _events = events;
    }

    public override Task<Character> GetCharacter(ProfileRequest request,
        ServerCallContext context)
    {
        var character = _store.GetCharacter(RequireProfile(request));
        if (character == null)
            throw new RpcException(new Status(StatusCode.NotFound, $"No capture for profile '{request.Profile}'"));

        return Task.FromResult(character);
    }

    public override Task<SearchItemsResponse> SearchItems(SearchItemsRequest request,
        ServerCallContext context)
    {
        try
        {
            return Task.FromResult(_store.SearchItems(request));
        }
        catch (InvalidSearchRequestException ex)
        {
            // The store rejects a request it cannot answer literally rather than repairing it,
            // so this is a caller error with a message naming the offending field.
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }

    public override Task<Empty> ResetKills(ProfileRequest request, ServerCallContext context)
    {
        var profile = RequireProfile(request);
        Announce(profile, _store.ResetKills(profile));
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> ResetAreaTime(ProfileRequest request, ServerCallContext context)
    {
        var profile = RequireProfile(request);
        Announce(profile, _store.ResetAreaTime(profile));
        return Task.FromResult(new Empty());
    }

    /// <summary>
    /// Tells every client the capture changed, the same way an ingested snapshot does.
    ///
    /// A reset is the one change to a capture that does not come from a bot reporting, so it is
    /// also the only one nothing else would announce. The client that issued it invalidates its own
    /// query, but a second window — or the same one after the profile has stopped, where no further
    /// snapshot is ever coming — would keep showing the totals that were just cleared.
    /// </summary>
    private void Announce(string profile, CharacterSummary? summary)
    {
        // Null means the store is disabled, so nothing was deleted and there is nothing to say.
        if (summary == null) return;

        _events.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            CaptureChanged = new CaptureChanged { Profile = profile, Summary = summary },
        });
    }

    /// <summary>Every endpoint here is keyed by profile; an empty one would silently do nothing.</summary>
    private static string RequireProfile(ProfileRequest request) =>
        string.IsNullOrEmpty(request.Profile)
            ? throw new RpcException(new Status(StatusCode.InvalidArgument, "profile is required"))
            : request.Profile;
}
