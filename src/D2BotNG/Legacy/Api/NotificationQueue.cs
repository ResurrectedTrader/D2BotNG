using System.Collections.Concurrent;
using D2BotNG.Legacy.Models;

namespace D2BotNG.Legacy.Api;

public class NotificationQueue
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<LegacyResponse>> _queues = new();

    public void Enqueue(string username, LegacyResponse response)
    {
        _queues.GetOrAdd(username, _ => new ConcurrentQueue<LegacyResponse>()).Enqueue(response);
    }

    public List<LegacyResponse> DequeueAll(string username)
    {
        var results = new List<LegacyResponse>();
        if (_queues.TryGetValue(username, out var queue))
        {
            while (queue.TryDequeue(out var item))
            {
                results.Add(item);
            }
        }
        return results;
    }
}
