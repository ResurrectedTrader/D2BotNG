using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using D2BotNG.Core.Protos;

namespace D2BotNG.Services;

/// <summary>
/// Manages event broadcasting to all connected clients.
/// Each client gets its own channel for receiving events.
/// </summary>
public class EventBroadcaster
{
    private readonly ConcurrentDictionary<string, Channel<Event>> _clients = new();
    private readonly ILogger<EventBroadcaster> _logger;

    public EventBroadcaster(ILogger<EventBroadcaster> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Register a new client and return its unique ID.
    /// </summary>
    public string AddClient()
    {
        var clientId = Guid.NewGuid().ToString();
        var channel = Channel.CreateUnbounded<Event>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _clients.TryAdd(clientId, channel);
        _logger.LogDebug("Client {ClientId} connected. Total clients: {Count}", clientId, _clients.Count);
        return clientId;
    }

    /// <summary>
    /// Remove a client and complete its channel.
    /// </summary>
    public void RemoveClient(string clientId)
    {
        if (_clients.TryRemove(clientId, out var channel))
        {
            channel.Writer.Complete();
            _logger.LogDebug("Client {ClientId} disconnected. Total clients: {Count}", clientId, _clients.Count);
        }
    }

    /// <summary>
    /// Broadcast an event to all connected clients.
    /// </summary>
    public void Broadcast(Event evt)
    {
        foreach (var channel in _clients.Values)
        {
            // TryWrite on unbounded channel should always succeed unless completed
            channel.Writer.TryWrite(evt);
        }
    }

    /// <summary>
    /// Get the channel reader for a specific client.
    /// </summary>
    public ChannelReader<Event>? GetReader(string clientId)
    {
        return _clients.TryGetValue(clientId, out var channel) ? channel.Reader : null;
    }

    /// <summary>
    /// Subscribe to events for a specific client.
    /// Call AddClient first to get the clientId.
    /// </summary>
    public async IAsyncEnumerable<Event> Subscribe(
        string clientId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = GetReader(clientId);
        if (reader == null)
        {
            _logger.LogWarning("Attempted to subscribe with unknown client ID: {ClientId}", clientId);
            yield break;
        }

        try
        {
            await foreach (var evt in reader.ReadAllAsync(cancellationToken))
            {
                yield return evt;
            }
        }
        finally
        {
            RemoveClient(clientId);
        }
    }

}
