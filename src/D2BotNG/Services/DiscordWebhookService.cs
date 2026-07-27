using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Rendering;

namespace D2BotNG.Services;

/// <summary>
/// Posts profile messages and item images to per-profile and global Discord webhooks.
/// All public methods are fire-and-forget and never throw.
/// </summary>
public class DiscordWebhookService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ItemRenderer _itemRenderer;
    private readonly SettingsRepository _settingsRepository;
    private readonly ILogger<DiscordWebhookService> _logger;

    public DiscordWebhookService(
        IHttpClientFactory httpClientFactory,
        ItemRenderer itemRenderer,
        SettingsRepository settingsRepository,
        ILogger<DiscordWebhookService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _itemRenderer = itemRenderer;
        _settingsRepository = settingsRepository;
        _logger = logger;
    }

    public void PostConsole(Profile profile, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        PostText(profile, text, w => w.PostConsole);
    }

    public void PostAnnounce(Profile profile, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        PostText(profile, text, w => w.PostAnnounce);
    }

    public void PostItem(Profile profile, Item item)
    {
        var urls = SelectUrls(profile, w => w.PostItems);
        if (urls.Length == 0) return;

        var settings = _settingsRepository.Current;
        var itemFont = settings.Display?.ItemFont ?? ItemFont.Exocet;

        byte[] png;
        try
        {
            png = _itemRenderer.RenderItemTooltip(item, itemFont);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to render item PNG for {ItemName}", item.Name);
            return;
        }

        var payloadJson = JsonSerializer.Serialize(new { username = profile.Name, content = item.Name });
        var profileName = profile.Name;

        foreach (var url in urls)
        {
            _ = PostMultipartAsync(url, payloadJson, png, profileName, item.Name);
        }
    }

    private void PostText(Profile profile, string text, Func<DiscordWebhook, bool> selector)
    {
        var urls = SelectUrls(profile, selector);
        if (urls.Length == 0) return;

        var payloadJson = JsonSerializer.Serialize(new { username = profile.Name, content = text });

        foreach (var url in urls)
        {
            _ = PostJsonAsync(url, payloadJson);
        }
    }

    private string[] SelectUrls(Profile profile, Func<DiscordWebhook, bool> selector)
    {
        var settings = _settingsRepository.Current;
        IEnumerable<DiscordWebhook> globals = settings.Discord?.Webhooks ?? Enumerable.Empty<DiscordWebhook>();
        return profile.DiscordWebhooks
            .Concat(globals)
            .Where(w => selector(w) && !string.IsNullOrEmpty(w.Url))
            .Select(w => w.Url)
            .Distinct()
            .ToArray();
    }

    private async Task PostJsonAsync(string url, string payloadJson)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(url, content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discord webhook POST to {Url} failed", url);
        }
    }

    private async Task PostMultipartAsync(string url, string payloadJson, byte[] png, string profileName, string itemName)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            using var form = new MultipartFormDataContent();

            var payloadContent = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            payloadContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            form.Add(payloadContent, "payload_json");

            var fileContent = new ByteArrayContent(png);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(fileContent, "files[0]", "item.png");

            using var response = await client.PostAsync(url, form);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discord webhook item POST to {Url} for profile {Profile} item {Item} failed", url, profileName, itemName);
        }
    }
}
