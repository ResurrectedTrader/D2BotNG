using D2BotNG.Core.Protos;
using Google.Protobuf.WellKnownTypes;
using Message = D2BotNG.Core.Protos.Message;

namespace D2BotNG.Services;

/// <summary>
/// Centralized service for all console messages.
/// Maintains history and broadcasts to all connected clients.
/// </summary>
public class MessageService
{
    private readonly EventBroadcaster _eventBroadcaster;

    private const int MaxHistorySize = 100_000;
    private readonly List<Message> _history = [];
    private readonly Lock _historyLock = new();

    public MessageService(EventBroadcaster eventBroadcaster)
    {
        _eventBroadcaster = eventBroadcaster;
    }

    /// <summary>
    /// Add a message and broadcast it to all clients.
    /// </summary>
    /// <param name="source">"System" for system messages, or profile ID</param>
    /// <param name="content">The message content</param>
    /// <param name="color">Message color</param>
    /// <param name="item">Optional item attachment</param>
    public void AddMessage(string source, string content, MessageColor color = MessageColor.ColorDefault, Item? item = null)
    {
        var msg = new Message
        {
            Source = source,
            Content = content,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Color = color
        };

        if (item != null)
        {
            msg.Item = item;
        }

        // Add to history
        lock (_historyLock)
        {
            _history.Add(msg);
            while (_history.Count > MaxHistorySize)
                _history.RemoveAt(0);
        }

        // Broadcast to all clients
        _eventBroadcaster.Broadcast(new Event
        {
            Timestamp = msg.Timestamp,
            Message = msg
        });
    }

    /// <summary>
    /// Get all messages, optionally filtered by source.
    /// </summary>
    public IReadOnlyList<Message> GetHistory(string? source = null)
    {
        lock (_historyLock)
        {
            if (source == null)
            {
                return _history.ToList();
            }
            return _history.Where(m => m.Source == source).ToList();
        }
    }

    /// <summary>
    /// Clear messages for a specific source.
    /// </summary>
    public void ClearMessages(string source)
    {
        lock (_historyLock)
        {
            _history.RemoveAll(m => m.Source == source);
        }
    }
}
