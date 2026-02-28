using System.Text.Json;
using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Engine;
using D2BotNG.Legacy.Models;
using D2BotNG.Rendering;
using D2BotNG.Services;
using D2BotNG.Windows;

namespace D2BotNG.Legacy.Api;

public class LegacyApiHandler
{
    private readonly ProfileEngine _profileEngine;
    private readonly ProfileRepository _profileRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly DataCache _dataCache;
    private readonly ItemRepository _itemRepository;
    private readonly ItemRenderer _itemRenderer;
    private readonly Paths _paths;
    private readonly NotificationQueue _notificationQueue;
    private readonly GameActionScheduler _gameActionScheduler;
    private readonly WebhookService _webhookService;
    private readonly ILogger<LegacyApiHandler> _logger;

    public LegacyApiHandler(
        ProfileEngine profileEngine,
        ProfileRepository profileRepository,
        SettingsRepository settingsRepository,
        DataCache dataCache,
        ItemRepository itemRepository,
        ItemRenderer itemRenderer,
        Paths paths,
        NotificationQueue notificationQueue,
        GameActionScheduler gameActionScheduler,
        WebhookService webhookService,
        ILogger<LegacyApiHandler> logger)
    {
        _profileEngine = profileEngine;
        _profileRepository = profileRepository;
        _settingsRepository = settingsRepository;
        _dataCache = dataCache;
        _itemRepository = itemRepository;
        _itemRenderer = itemRenderer;
        _paths = paths;
        _notificationQueue = notificationQueue;
        _gameActionScheduler = gameActionScheduler;
        _webhookService = webhookService;
        _logger = logger;
    }

    public async Task<LegacyResponse> HandleAsync(LegacyRequest request, string sessionKey)
    {
        var response = new LegacyResponse { Request = request.Func };

        try
        {
            // Look up user
            var settings = await _settingsRepository.GetAsync();
            var user = settings.LegacyApi.Users
                .FirstOrDefault(u => u.Name == request.Profile);

            if (user == null || user.Permissions == LegacyApiPermissions.Undefined)
            {
                response.Body = "invalid user";
                return response;
            }

            // Validate session
            if (!string.IsNullOrEmpty(user.ApiKey)
                && !request.Func.Equals("challenge", StringComparison.OrdinalIgnoreCase))
            {
                var decrypted = request.Session == "null"
                    ? null
                    : AesEncryption.Decrypt(request.Session, user.ApiKey);
                if (decrypted != sessionKey)
                {
                    response.Body = "invalid session";
                    return response;
                }
            }

            var func = request.Func.ToLowerInvariant();

            // Auth commands
            if (func == "challenge")
            {
                response.Status = "success";
                response.Body = sessionKey;
                return response;
            }

            if (user.Permissions == LegacyApiPermissions.Admin)
            {
                switch (func)
                {
                    case "registerevent":
                        return await HandleRegisterEvent(request, response);
                    case "poll":
                        return HandlePoll(request, response);
                    case "ping":
                        return HandlePing(response);
                    case "get":
                        return await HandleGet(request, user, response);
                    case "put":
                        return await HandlePut(request, user, response);
                    case "store":
                        return HandleStore(request, response);
                    case "retrieve":
                        return HandleRetrieve(request, response);
                    case "delete":
                        return HandleDelete(request, response);
                    case "profiles":
                        return await HandleProfiles(response);
                    case "settag":
                        return await HandleSetTag(request, response);
                    case "start":
                        return await HandleStart(request, response);
                    case "stop":
                        return await HandleStop(request, response);
                    case "emit":
                        return await HandleEmit(request, response);
                    case "gameaction":
                        return HandleGameAction(request, response);
                }
            }

            if (user.Permissions > LegacyApiPermissions.Undefined)
            {
                switch (func)
                {
                    case "validate":
                        response.Status = "success";
                        response.Body = "apikey is valid";
                        return response;
                    case "accounts":
                        return HandleAccounts(request, response);
                    case "query":
                        return await HandleQuery(request, response, generateImages: true);
                    case "fastquery":
                        return await HandleQuery(request, response, generateImages: false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling legacy API func={Func}", request.Func);
            response.Status = "failed";
            response.Body = ex.Message;
        }

        return response;
    }

    private Task<LegacyResponse> HandleRegisterEvent(LegacyRequest request, LegacyResponse response)
    {
        if (request.Args.Length > 1
            && !string.IsNullOrEmpty(request.Args[0])
            && _webhookService.RegisterEvent(request.Args[0], request.Args[1]))
        {
            response.Status = "success";
            response.Body = "event has been registered";
        }
        else
        {
            response.Body = "event does not exist";
        }
        return Task.FromResult(response);
    }

    private LegacyResponse HandlePoll(LegacyRequest request, LegacyResponse response)
    {
        var items = _notificationQueue.DequeueAll(request.Profile);
        response.Status = "success";
        response.Body = items.Count > 0
            ? JsonSerializer.Serialize(items)
            : "empty";
        return response;
    }

    private static LegacyResponse HandlePing(LegacyResponse response)
    {
        response.Status = "success";
        response.Body = $"{{\"pid\":{Environment.ProcessId}}}";
        return response;
    }

    private async Task<LegacyResponse> HandleGet(LegacyRequest request, LegacyApiUser user, LegacyResponse response)
    {
        if (request.Args.Length == 0)
        {
            response.Body = "incorrect arguments";
            return response;
        }

        var filePath = ResolveSafePath(Path.Combine(_paths.LegacyDataDirectory, "web"), request.Args[0]);
        if (filePath == null || !File.Exists(filePath))
        {
            response.Body = "incorrect arguments";
            return response;
        }

        var content = await File.ReadAllTextAsync(filePath);
        if (!string.IsNullOrEmpty(user.ApiKey))
        {
            content = AesEncryption.Encrypt(content, user.ApiKey);
        }

        response.Status = "success";
        response.Body = content;
        return response;
    }

    private async Task<LegacyResponse> HandlePut(LegacyRequest request, LegacyApiUser user, LegacyResponse response)
    {
        if (request.Args.Length < 3
            || (!request.Args[0].Equals("web", StringComparison.OrdinalIgnoreCase)
                && !request.Args[0].Equals("secure", StringComparison.OrdinalIgnoreCase)))
        {
            response.Body = "incorrect arguments";
            return response;
        }

        var fileName = Path.GetFileName(request.Args[1]);
        var value = !string.IsNullOrEmpty(user.ApiKey)
            ? AesEncryption.Decrypt(request.Args[2], user.ApiKey)
            : request.Args[2];

        var dir = Path.Combine(_paths.LegacyDataDirectory, request.Args[0]);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, fileName), value);

        response.Status = "success";
        response.Body = request.Args[1];
        return response;
    }

    private LegacyResponse HandleStore(LegacyRequest request, LegacyResponse response)
    {
        if (request.Args.Length == 2)
        {
            _dataCache.Store(request.Args[0], request.Args[1]);
            response.Status = "success";
            response.Body = request.Args[1];
        }
        else
        {
            response.Body = "incorrect arguments";
        }
        return response;
    }

    private LegacyResponse HandleRetrieve(LegacyRequest request, LegacyResponse response)
    {
        if (request.Args.Length == 1)
        {
            var value = _dataCache.Retrieve(request.Args[0]);
            if (value != null)
            {
                response.Status = "success";
                response.Body = value;
                return response;
            }
        }
        response.Body = "content does not exist";
        return response;
    }

    private LegacyResponse HandleDelete(LegacyRequest request, LegacyResponse response)
    {
        if (request.Args.Length == 1)
        {
            var value = _dataCache.Retrieve(request.Args[0]);
            if (value != null && _dataCache.Delete(request.Args[0]))
            {
                response.Status = "success";
                response.Body = value;
                return response;
            }
        }
        response.Body = "incorrect arguments";
        return response;
    }

    private async Task<LegacyResponse> HandleProfiles(LegacyResponse response)
    {
        var profiles = await _profileRepository.GetAllAsync();
        var exports = new List<LegacyProfileExport>();

        foreach (var profile in profiles)
        {
            var instance = _profileEngine.GetInstance(profile.Name);
            exports.Add(LegacyProfileExport.FromProfile(profile, instance));
        }

        response.Status = "success";
        response.Body = JsonSerializer.Serialize(exports);
        return response;
    }

    private async Task<LegacyResponse> HandleSetTag(LegacyRequest request, LegacyResponse response)
    {
        if (request.Args.Length < 2)
        {
            response.Body = "incorrect arguments";
            return response;
        }

        var profile = await GetProfileByName(request.Args[0]);
        if (profile == null)
        {
            response.Body = "incorrect arguments";
            return response;
        }

        // Set tag in memory only — web API tags are temporary (e.g. game action payloads)
        // and should not be persisted to disk. The D2BS setTag handler persists separately.
        profile.InfoTag = request.Args[1];

        response.Status = "success";
        response.Body = JsonSerializer.Serialize(LegacyProfileExport.FromProfile(profile, _profileEngine.GetInstance(profile.Name)));
        _webhookService.EmitEventAsync("setTag", response.Body);
        return response;
    }

    private async Task<LegacyResponse> HandleStart(LegacyRequest request, LegacyResponse response)
    {
        if (request.Args.Length == 0)
        {
            response.Body = "incorrect arguments";
            return response;
        }

        var profile = await GetProfileByName(request.Args[0]);
        if (profile == null)
        {
            response.Body = "incorrect arguments";
            return response;
        }

        if (request.Args.Length > 1)
        {
            profile.InfoTag = request.Args[1];
            await _profileRepository.UpdateAsync(profile);
        }

        await _profileEngine.StartProfileAsync(profile.Name);
        response.Status = "success";
        response.Body = request.Args.Length > 1 ? request.Args[1] : null;
        return response;
    }

    private async Task<LegacyResponse> HandleStop(LegacyRequest request, LegacyResponse response)
    {
        if (request.Args.Length == 0)
        {
            response.Body = "incorrect arguments";
            return response;
        }

        var profile = await GetProfileByName(request.Args[0]);
        if (profile == null)
        {
            response.Body = "incorrect arguments";
            return response;
        }

        await _profileEngine.StopProfileAsync(profile.Name);

        if (request.Args.Length > 1 && request.Args[1].Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            await _profileEngine.ReleaseKeysAsync([profile.Name]);
        }

        response.Status = "success";
        response.Body = request.Args.Length > 1 ? request.Args[1] : null;
        return response;
    }

    private async Task<LegacyResponse> HandleEmit(LegacyRequest request, LegacyResponse response)
    {
        if (request.Args.Length < 2)
        {
            response.Body = "incorrect arguments";
            return response;
        }

        var profile = await GetProfileByName(request.Args[0]);
        if (profile == null)
        {
            response.Body = "incorrect arguments";
            return response;
        }

        _profileEngine.SendMessage(profile.Name, MessageType.Emit, JsonSerializer.Serialize(request.Args[1]));

        response.Status = "success";
        response.Body = JsonSerializer.Serialize(LegacyProfileExport.FromProfile(profile, _profileEngine.GetInstance(profile.Name)));
        _webhookService.EmitEventAsync("emit", response.Body);
        return response;
    }

    private LegacyResponse HandleGameAction(LegacyRequest request, LegacyResponse response)
    {
        if (request.Args.Length < 2
            || string.IsNullOrEmpty(request.Args[0])
            || string.IsNullOrEmpty(request.Args[1]))
        {
            response.Body = "incorrect arguments";
            return response;
        }

        _gameActionScheduler.EnqueueAction(request.Args[1]);
        response.Status = "success";
        response.Body = request.Args[1];
        return response;
    }

    private LegacyResponse HandleAccounts(LegacyRequest request, LegacyResponse response)
    {
        var baseMulesDir = Path.GetFullPath(_paths.MulesDirectory);
        var mulesDir = baseMulesDir;
        if (request.Args.Length == 1)
        {
            mulesDir = ResolveSafePath(baseMulesDir, request.Args[0]);
            if (mulesDir == null)
            {
                response.Body = "incorrect arguments";
                return response;
            }
        }

        if (!Directory.Exists(mulesDir))
        {
            response.Body = "incorrect arguments";
            return response;
        }

        var files = Directory.GetFiles(mulesDir, "*.txt", SearchOption.AllDirectories);
        var baseMulesDirPrefix = baseMulesDir + Path.DirectorySeparatorChar;
        var result = files.Select(f =>
        {
            var relative = f.StartsWith(baseMulesDirPrefix, StringComparison.OrdinalIgnoreCase)
                ? f[baseMulesDirPrefix.Length..]
                : Path.GetRelativePath(baseMulesDir, f);
            // Remove .txt extension
            if (relative.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                relative = relative[..^4];
            return relative;
        }).ToArray();

        response.Status = "success";
        response.Body = JsonSerializer.Serialize(result);
        return response;
    }

    private async Task<LegacyResponse> HandleQuery(LegacyRequest request, LegacyResponse response, bool generateImages)
    {
        if (request.Args.Length < 2)
        {
            response.Body = "invalid argument count";
            return response;
        }

        var query = request.Args[0];

        // Build entity paths from the folder arguments, handling comma-separated values
        var entityPaths = new List<string>();
        if (request.Args.Length > 3)
        {
            // args[1]/args[2]/part for each comma-separated part in args[3]
            var prefix = request.Args[1] + "/" + request.Args[2];
            foreach (var part in request.Args[3].Split(','))
                entityPaths.Add(prefix + "/" + part);
        }
        else if (request.Args.Length == 3)
        {
            // args[1]/part for each comma-separated part in args[2]
            foreach (var part in request.Args[2].Split(','))
                entityPaths.Add(request.Args[1] + "/" + part);
        }
        else
        {
            // part for each comma-separated part in args[1]
            foreach (var part in request.Args[1].Split(','))
                entityPaths.Add(part);
        }

        var results = entityPaths
            .SelectMany(path => _itemRepository.SearchWithContext(path, query, null))
            .ToList();

        var settings = await _settingsRepository.GetAsync();
        var itemFont = settings.Display.ItemFont;

        response.Status = "success";
        response.Body = JsonSerializer.Serialize(results.Select(r => new
        {
            lod = r.Mode.Expansion,
            sc = !r.Mode.Hardcore,
            ladder = r.Mode.Ladder,
            account = r.Account,
            character = r.Character,
            description = r.Item.Description,
            image = generateImages ? GenerateItemImage(r.Item, itemFont) : null
        }));
        return response;
    }

    private string? GenerateItemImage(Item item, ItemFont itemFont)
    {
        try
        {
            var png = _itemRenderer.RenderItemTooltip(item, itemFont);
            return Convert.ToBase64String(png);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to render item image for {Code}", item.Code);
            return null;
        }
    }

    private async Task<Profile?> GetProfileByName(string name)
    {
        return await _profileRepository.GetByKeyAsync(name);
    }

    /// <summary>
    /// Resolves a user-supplied path relative to a base directory and verifies
    /// the result is still within that directory. Returns null if the path escapes.
    /// </summary>
    private static string? ResolveSafePath(string baseDir, string userPath)
    {
        var fullBase = Path.GetFullPath(baseDir);
        var fullPath = Path.GetFullPath(Path.Combine(fullBase, userPath));
        if (fullPath.StartsWith(fullBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullPath.Equals(fullBase, StringComparison.OrdinalIgnoreCase))
            return fullPath;
        return null;
    }

}
