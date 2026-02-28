using System.Collections.Concurrent;

namespace D2BotNG.Legacy.Api;

public class SessionManager
{
    private readonly ConcurrentDictionary<string, string> _sessions = new();

    public string GetOrCreateSession(string clientIp, string userAgent)
    {
        var key = clientIp + "|" + userAgent;
        return _sessions.GetOrAdd(key, _ => AesEncryption.GenerateKey(32));
    }
}
