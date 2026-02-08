// TODO: Re-enable Limedrop API later
#if false
using D2BotNG.Core.Protos;
using D2BotNG.Data;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace D2BotNG.Controllers;

// Request/Response classes for Limedrop API
public class WebItemPostRequest
{
    [JsonPropertyName("profile")]
    public string Profile { get; set; } = "";

    [JsonPropertyName("item")]
    public WebItemData Item { get; set; } = new();
}

public class WebItemData
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("color")]
    public uint Color { get; set; }

    [JsonPropertyName("textColor")]
    public uint TextColor { get; set; }

    [JsonPropertyName("header")]
    public string Header { get; set; } = "";

    [JsonPropertyName("sockets")]
    public List<WebItemData> Sockets { get; set; } = [];
}

public class WebItemResponse
{
    [JsonPropertyName("account")]
    public string Account { get; set; } = "";

    [JsonPropertyName("character")]
    public string Character { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("lod")]
    public bool Lod { get; set; } = true;

    [JsonPropertyName("softcore")]
    public bool Softcore { get; set; } = true;

    [JsonPropertyName("ladder")]
    public bool Ladder { get; set; }

    [JsonPropertyName("image")]
    public WebItemImage Image { get; set; } = new();
}

public class WebItemImage
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("color")]
    public uint Color { get; set; }

    [JsonPropertyName("sockets")]
    public string[] Sockets { get; set; } = [];
}

/// <summary>
/// Legacy HTTP API for Limedrop compatibility
/// Maintains the same endpoints and response format as the original D2Bot
/// Uses API key authentication from limedrop.json
/// </summary>
[ApiController]
[Route("api")]
public class LimedropController : ControllerBase
{
    private readonly ItemRepository _itemRepository;
    private readonly ProfileRepository _profileRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly ILogger<LimedropController> _logger;

    public LimedropController(
        ItemRepository itemRepository,
        ProfileRepository profileRepository,
        SettingsRepository settingsRepository,
        ILogger<LimedropController> logger)
    {
        _itemRepository = itemRepository;
        _profileRepository = profileRepository;
        _settingsRepository = settingsRepository;
        _logger = logger;
    }

    /// <summary>
    /// Validate API key from request
    /// The original D2Bot used base64-encoded requests with an API key in the request body
    /// </summary>
    private async Task<(bool IsValid, LimedropUser? User)> ValidateApiKeyAsync(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return (false, null);

        var settings = await _settingsRepository.GetAsync();
        var user = settings.Limedrop.Users.FirstOrDefault(u =>
            u.ApiKey.Equals(apiKey, StringComparison.Ordinal));

        return (user != null, user);
    }

    /// <summary>
    /// Check if profile is in allowed list
    /// </summary>
    private async Task<bool> IsProfileAllowedAsync(string profileName)
    {
        var settings = await _settingsRepository.GetAsync();
        if (settings.Limedrop.Profiles.Count == 0)
            return true; // No restrictions

        return settings.Limedrop.Profiles.Contains(profileName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// GET /api/webitem - Get items in Limedrop format
    /// </summary>
    [HttpGet("webitem")]
    public async Task<IActionResult> GetWebItems(
        [FromQuery] string? profile = null,
        [FromQuery] string? account = null,
        [FromQuery] string? apikey = null,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0)
    {
        var (isValid, _) = await ValidateApiKeyAsync(apikey);
        if (!isValid)
            return Unauthorized(new { error = "Invalid API key" });

        var items = await _itemRepository.GetHistoryAsync(profile, limit, offset);
        var profiles = await _profileRepository.GetAllAsync();

        var result = new List<WebItemResponse>();

        foreach (var item in items)
        {
            var profileData = profiles.FirstOrDefault(p => p.Name == item.ProfileName);

            if (account != null && profileData?.Account != account)
                continue;

            if (profileData != null && !await IsProfileAllowedAsync(profileData.Name))
                continue;

            result.Add(new WebItemResponse
            {
                Account = profileData?.Account ?? "",
                Character = profileData?.Character ?? "",
                Description = item.Description,
                Lod = true,
                Softcore = true,
                Ladder = false,
                Image = new WebItemImage
                {
                    Code = item.Image,
                    Color = item.ItemColor >= 0 ? (uint)item.ItemColor : 0,
                    Sockets = item.Sockets.ToArray()
                }
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// POST /api/webitem - Receive item from D2BS
    /// The original D2Bot used base64-encoded JSON in the request body
    /// </summary>
    [HttpPost("webitem")]
    public async Task<IActionResult> PostWebItem()
    {
        try
        {
            // Read raw request body (base64 encoded in original format)
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            var rawBody = await reader.ReadToEndAsync();

            WebItemPostRequest? request;
            try
            {
                // Try to decode as base64 first (original format)
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(rawBody));
                request = JsonSerializer.Deserialize<WebItemPostRequest>(decoded);
            }
            catch
            {
                // Fall back to direct JSON
                request = JsonSerializer.Deserialize<WebItemPostRequest>(rawBody);
            }

            if (request == null)
                throw new InvalidOperationException("Failed to parse request");

            var item = ConvertToItemData(request.Item);
            await _itemRepository.LogItemAsync(request.Profile, item);

            // Return base64-encoded response (original format)
            var response = JsonSerializer.Serialize(new { success = true });
            var encodedResponse = Convert.ToBase64String(Encoding.UTF8.GetBytes(response));

            return Content(encodedResponse, "text/plain");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process webitem POST");
            var response = JsonSerializer.Serialize(new { success = false, error = ex.Message });
            var encodedResponse = Convert.ToBase64String(Encoding.UTF8.GetBytes(response));
            return Content(encodedResponse, "text/plain");
        }
    }

    /// <summary>
    /// GET /api/mule - Get mule inventory
    /// </summary>
    [HttpGet("mule")]
    public async Task<IActionResult> GetMuleInventory(
        [FromQuery] string? profile = null,
        [FromQuery] string? account = null,
        [FromQuery] string? apikey = null)
    {
        var (isValid, _) = await ValidateApiKeyAsync(apikey);
        if (!isValid)
            return Unauthorized(new { error = "Invalid API key" });

        // TODO: Implement mule inventory tracking
        return Ok(new { accounts = new Dictionary<string, object>() });
    }

    /// <summary>
    /// GET /api/search - Search items
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchItems(
        [FromQuery] string q = "",
        [FromQuery] string? profile = null,
        [FromQuery] string? apikey = null,
        [FromQuery] int limit = 100)
    {
        var (isValid, _) = await ValidateApiKeyAsync(apikey);
        if (!isValid)
            return Unauthorized(new { error = "Invalid API key" });

        var items = await _itemRepository.GetHistoryAsync(profile, limit * 10, 0);
        var profiles = await _profileRepository.GetAllAsync();

        var result = items
            .Where(i => MatchesSearch(i, q))
            .Take(limit)
            .Select(item =>
            {
                var profileData = profiles.FirstOrDefault(p => p.Name == item.ProfileName);
                return new WebItemResponse
                {
                    Account = profileData?.Account ?? "",
                    Character = profileData?.Character ?? "",
                    Description = item.Description,
                    Lod = true,
                    Softcore = true,
                    Ladder = false,
                    Image = new WebItemImage
                    {
                        Code = item.Image,
                        Color = item.ItemColor >= 0 ? (uint)item.ItemColor : 0,
                        Sockets = item.Sockets.ToArray()
                    }
                };
            })
            .ToList();

        return Ok(result);
    }

    private static bool MatchesSearch(ItemData item, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        return item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.Image.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static ItemData ConvertToItemData(WebItemData webItem)
    {
        var item = new ItemData
        {
            Image = webItem.Code,
            Title = webItem.Name,
            Description = webItem.Description,
            ItemColor = (int)webItem.Color,
            Header = webItem.Header
        };

        foreach (var socket in webItem.Sockets)
        {
            item.Sockets.Add(socket.Code);
        }

        return item;
    }
}
#endif
