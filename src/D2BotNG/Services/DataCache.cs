using System.Collections.Concurrent;

namespace D2BotNG.Services;

/// <summary>
/// In-memory cache for D2BS store/retrieve/delete operations.
/// </summary>
public class DataCache
{
    private readonly ConcurrentDictionary<string, string> _cache = new();

    public void Store(string key, string value)
    {
        _cache[key] = value;
    }

    public string? Retrieve(string key)
    {
        return _cache.TryGetValue(key, out var value) ? value : null;
    }

    public bool Delete(string key)
    {
        return _cache.TryRemove(key, out _);
    }
}
