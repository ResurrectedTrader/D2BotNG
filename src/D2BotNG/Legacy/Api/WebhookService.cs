using System.Collections.Concurrent;
using System.Text;

namespace D2BotNG.Legacy.Api;

public class WebhookService
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _events = new();
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookService> _logger;

    public WebhookService(IHttpClientFactory httpClientFactory, ILogger<WebhookService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _events["setTag"] = [];
        _events["emit"] = [];
    }

    public bool RegisterEvent(string eventType, string callbackUrl)
    {
        if (!_events.TryGetValue(eventType, out var urls))
            return false;

        lock (urls)
        {
            urls.Add(callbackUrl);
        }
        return true;
    }

    public void EmitEventAsync(string eventType, string json)
    {
        if (!_events.TryGetValue(eventType, out var urls))
            return;

        string[] snapshot;
        lock (urls)
        {
            snapshot = [.. urls];
        }

        foreach (var url in snapshot)
        {
            _ = PostAsync(url, json);
        }
    }

    private async Task PostAsync(string url, string json)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            await client.PostAsync(url, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook POST to {Url} failed", url);
        }
    }
}
