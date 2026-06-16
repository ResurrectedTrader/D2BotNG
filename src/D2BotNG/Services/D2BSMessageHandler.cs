using System.Text.Json;
using System.Text.Json.Nodes;
using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Engine;
using D2BotNG.Legacy.Api;
using D2BotNG.Legacy.Models;
using D2BotNG.Rendering;
using D2BotNG.Utilities;
using D2BotNG.Windows;

namespace D2BotNG.Services;

/// <summary>
/// Processes incoming D2BS messages and dispatches to appropriate handlers.
/// </summary>
public class D2BSMessageHandler : BackgroundService
{
    private readonly ILogger<D2BSMessageHandler> _logger;
    private readonly MessageWindow _messageWindow;
    private readonly ProfileEngine _profileEngine;
    private readonly ProfileRepository _profileRepository;
    private readonly KeyListRepository _keyListRepository;
    private readonly MessageService _messageService;
    private readonly DataCache _dataCache;
    private readonly WebhookService _webhookService;
    private readonly NotificationQueue _notificationQueue;
    private readonly DiscordWebhookService _discordWebhookService;
    private readonly ItemRenderer _itemRenderer;
    private readonly Paths _paths;
    private readonly SettingsRepository _settingsRepository;
    private readonly CharacterStateService _characterStateService;

    public D2BSMessageHandler(
        ILogger<D2BSMessageHandler> logger,
        MessageWindow messageWindow,
        ProfileEngine profileEngine,
        ProfileRepository profileRepository,
        KeyListRepository keyListRepository,
        MessageService messageService,
        DataCache dataCache,
        WebhookService webhookService,
        NotificationQueue notificationQueue,
        DiscordWebhookService discordWebhookService,
        ItemRenderer itemRenderer,
        Paths paths,
        SettingsRepository settingsRepository,
        CharacterStateService characterStateService)
    {
        _logger = logger;
        _messageWindow = messageWindow;
        _profileEngine = profileEngine;
        _profileRepository = profileRepository;
        _keyListRepository = keyListRepository;
        _messageService = messageService;
        _dataCache = dataCache;
        _webhookService = webhookService;
        _notificationQueue = notificationQueue;
        _discordWebhookService = discordWebhookService;
        _itemRenderer = itemRenderer;
        _paths = paths;
        _settingsRepository = settingsRepository;
        _characterStateService = characterStateService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("D2BS message handler started");

        await foreach (var msg in _messageWindow.Messages.ReadAllAsync(stoppingToken))
        {
            try
            {
                await HandleMessageAsync(msg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling D2BS message: {Message}", msg.Message);
            }
        }

        _logger.LogInformation("D2BS message handler stopped");
    }

    private async Task HandleMessageAsync(D2BSMessage msg)
    {
        var profile = await FindProfileByHandleAsync(msg.SenderHandle);

        _logger.LogDebug("D2BS command: {Command} from {Profile}", msg.Message, profile?.Name ?? "unknown");

        if (profile == null)
            return;

        var args = msg.Message.Arguments;

        switch (msg.Message.Function)
        {
            case "heartBeat":
                HandleHeartBeat(msg.SenderHandle);
                break;

            case "updateStatus":
                if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
                    await HandleUpdateStatusAsync(msg.SenderHandle, args[0]);
                break;

            case "updateRuns":
                await HandleUpdateRunsAsync(profile);
                break;

            case "updateChickens":
                await HandleUpdateChickensAsync(profile);
                break;

            case "updateDeaths":
                await HandleUpdateDeathsAsync(profile);
                break;

            case "printToConsole":
                if (args.Length > 0)
                    HandlePrintToConsole(profile, args);
                break;

            case "printToItemLog":
                if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
                    HandlePrintToItemLog(profile, args[0]);
                break;

            case "saveItem":
                if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
                    HandleSaveItem(args[0]);
                break;

            case "characterState":
                if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
                    HandleCharacterState(profile, args[0]);
                break;

            case "postToIRC":
                if (args.Length >= 3 && !string.IsNullOrEmpty(args[0]) && !string.IsNullOrEmpty(args[1]))
                    HandlePostToIRC(profile, args);
                break;

            case "getProfile":
                await HandleGetProfileAsync(msg.SenderHandle, profile, args);
                break;

            case "requestGameInfo":
                await HandleRequestGameInfo(profile);
                break;

            case "setProfile":
                await HandleSetProfileAsync(profile, args);
                break;

            case "restartProfile":
                await HandleRestartProfileAsync(profile.Name, args.Length > 1 && args[1].Equals("true", StringComparison.OrdinalIgnoreCase));
                break;

            case "stop":
                await _profileEngine.StopProfileAsync(profile.Name);
                break;

            case "start":
                if (args.Length > 0)
                    await HandleStartAsync(args);
                break;

            case "setTag":
                if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
                    await HandleSetTagAsync(profile, args[0]);
                break;

            case "setNotify":
                if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
                    HandleSetNotify(args);
                break;

            case "CDKeyInUse":
                if (args.Length > 0)
                    _messageService.AddMessage(profile.Name, $"Key in use: {args[0]}", MessageColor.ColorGold);
                break;

            case "CDKeyDisabled":
                if (args.Length > 0)
                    await HandleCDKeyDisabledAsync(profile, args[0]);
                break;

            case "CDKeyRD":
                if (args.Length > 0)
                    _messageService.AddMessage(profile.Name, $"Realm down on key: {args[0]}", MessageColor.ColorRed);
                break;

            case "store":
                if (args.Length >= 2 && !string.IsNullOrEmpty(args[0]))
                    _dataCache.Store(args[0], args[1]);
                break;

            case "retrieve":
                if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
                    _profileEngine.SendMessage(msg.SenderHandle, MessageType.DataRetrieve, _dataCache.Retrieve(args[0]) ?? "null");
                break;

            case "delete":
                if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
                    _dataCache.Delete(args[0]);
                break;

            case "shoutGlobal":
                if (msg.Message.Arguments.Length > 1 && !string.IsNullOrEmpty(msg.Message.Arguments[0]) && !string.IsNullOrEmpty(msg.Message.Arguments[1]))
                    _profileEngine.BroadcastToAll((MessageType)uint.Parse(msg.Message.Arguments[1]), msg.Message.Arguments[0]);
                break;

            case "stopSchedule":
                await HandleStopScheduleAsync(profile.Name);
                break;

            case "startSchedule":
                await HandleStartScheduleAsync(profile.Name);
                break;

            case "winmsg":
                if (!HandleWinMsg(profile, msg.Message.Arguments))
                {
                    _logger.LogDebug("Invalid winmsg command: {Message}", msg.Message);
                }
                break;

            default:
                _logger.LogError("Unhandled D2BS command: {Message}", msg.Message);
                break;
        }
    }

    private bool HandleWinMsg(Profile profile, string[] args)
    {
        if (args.Length < 2) return false;
        var instance = _profileEngine.GetInstance(profile.Name);
        if (instance?.Process == null) return false;
        var hwnd = instance.Process.GameWindow;
        if (hwnd == 0) return false;
        if (!uint.TryParse(args[0], out var msgId)) return false;
        if (!int.TryParse(args[1], out var wParam)) return false;
        NativeMethods.SendMessageTimeout(hwnd, msgId, wParam, 0,
            NativeTypes.SMTO_ABORTIFHUNG, 250u, out _);
        return true;
    }

    private async Task HandleRequestGameInfo(Profile profile)
    {
        var instance = _profileEngine.GetInstance(profile.Name);
        if (instance == null) return;

        var gameInfo = new
        {
            handle = (ulong)_messageWindow.Handle,
            profile = profile.Name,
            mpq = instance.KeyName ?? "",
            gameName = profile.GameName,
            gamePass = profile.GamePass,
            difficulty = profile.Difficulty.ToString(),
            error = false, // TODO: Should we track this?
            stopTime = "", // TODO: Should we track this?
            switchKeys = !string.IsNullOrEmpty(profile.KeyList) && ((await _keyListRepository.GetByKeyAsync(profile.KeyList))?.Keys.Count ?? 0) > 1 && profile.SwitchKeysOnRestart,
            rdBlocker = false,
        };
        instance.Process?.SendMessage(MessageType.GameInfo, JsonSerializer.Serialize(gameInfo));
    }

    private async Task<Profile?> FindProfileByHandleAsync(nint handle)
    {
        var instance = _profileEngine.GetInstanceByHandle(handle);
        if (instance == null) return null;
        return await _profileRepository.GetByKeyAsync(instance.ProfileName);
    }

    private void HandleHeartBeat(nint senderHandle)
    {
        var instance = _profileEngine.GetInstanceByHandle(senderHandle);
        instance?.UpdateHeartbeat();
    }

    private async Task HandleUpdateStatusAsync(nint senderHandle, string status)
    {
        var instance = _profileEngine.GetInstanceByHandle(senderHandle);
        if (instance == null) return;
        instance.Status = status;
        await _profileEngine.NotifyProfileStateChangedAsync(instance.ProfileName);
    }

    private async Task HandleUpdateRunsAsync(Profile profile)
    {
        profile.Runs++;
        var rollover = false;
        if (profile.RunsPerKey > 0)
        {
            profile.KeyRuns++;
            if (profile.KeyRuns >= profile.RunsPerKey)
            {
                profile.KeyRuns = 0;
                rollover = true;
            }
        }
        await _profileEngine.UpdateProfileAndNotifyAsync(profile);

        if (rollover)
            await _profileEngine.RestartProfileAsync(profile.Name, rotateKey: profile.SwitchKeysOnRestart);
    }

    private async Task HandleUpdateChickensAsync(Profile profile)
    {
        profile.Chickens++;
        await _profileEngine.UpdateProfileAndNotifyAsync(profile);
    }

    private async Task HandleUpdateDeathsAsync(Profile profile)
    {
        profile.Deaths++;
        await _profileEngine.UpdateProfileAndNotifyAsync(profile);
    }

    private void HandlePrintToItemLog(Profile profile, string itemJson)
    {
        var legacyItem = JsonSerializer.Deserialize<LegacyItem>(itemJson)!;
        var item = legacyItem.ToModern();
        _messageService.AddMessage(profile.Name, legacyItem.Title, MessageColor.ColorDefault, item);
        _discordWebhookService.PostItem(profile, item);
    }

    private void HandlePrintToConsole(Profile profile, string[] args)
    {
        if (args.Length < 1 || string.IsNullOrEmpty(args[0])) return;
        var message = JsonSerializer.Deserialize<JsonNode>(args[0])!;
        var text = message["msg"]!.GetValue<string>();
        _messageService.AddMessage(profile.Name, text, (MessageColor?)message["color"]?.GetValue<int>() ?? MessageColor.ColorDefault);
        _discordWebhookService.PostConsole(profile, text);
    }

    private void HandleSaveItem(string itemJson)
    {
        try
        {
            var legacyItem = JsonSerializer.Deserialize<LegacyItem>(itemJson)!;
            var item = legacyItem.ToModern();
            var settings = _settingsRepository.GetAsync().GetAwaiter().GetResult();
            var itemFont = settings.Display?.ItemFont ?? ItemFont.Exocet;
            var png = _itemRenderer.RenderItemTooltip(item, itemFont);
            var imagesDir = Path.Combine(_paths.BasePath, "images");
            Directory.CreateDirectory(imagesDir);
            var index = Directory.GetFiles(imagesDir, item.Name + "*").Length + 1;
            var path = Path.Combine(imagesDir, $"{item.Name}{index}.png");
            File.WriteAllBytes(path, png);
            _logger.LogDebug("Saved item screenshot to {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save item screenshot");
        }
    }

    private void HandleCharacterState(Profile profile, string json)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<CharacterStateDto>(json);
            if (dto == null) return;
            _characterStateService.Ingest(profile.Name, dto);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle characterState for {Profile}", profile.Name);
        }
    }

    private void HandlePostToIRC(Profile profile, string[] args)
    {
        var combined = $"{args[2]} {args[1]}";
        switch (args[0])
        {
            case "console":
                _discordWebhookService.PostConsole(profile, combined);
                break;
            case "announce":
                _discordWebhookService.PostAnnounce(profile, combined);
                break;
        }
    }

    private async Task HandleGetProfileAsync(nint senderHandle, Profile? profile, string[] args)
    {
        // If args[0] specified, get that profile instead
        var targetProfile = profile;
        if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
        {
            targetProfile = await _profileRepository.GetByKeyAsync(args[0]);
        }

        if (targetProfile == null) return;

        var instance = _profileEngine.GetInstance(targetProfile.Name);
        if (instance == null) return;

        var export = new
        {
            targetProfile.Name,
            instance.Status,
            targetProfile.Account,
            targetProfile.Character,
            Difficulty = targetProfile.Difficulty.ToString(),
            Realm = targetProfile.Realm.ToString(),
            Game = targetProfile.D2Path,
            Entry = Path.GetFileName(targetProfile.EntryScript),
            Tag = targetProfile.InfoTag
        };

        var json = JsonSerializer.Serialize(export);

        _profileEngine.SendMessage(senderHandle, MessageType.Profile, json);
    }

    private async Task HandleSetProfileAsync(Profile profile, string[] args)
    {
        // Args: account, password, character, difficulty, realm, infoTag, d2path
        if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
            profile.Account = args[0];
        if (args.Length > 1 && !string.IsNullOrEmpty(args[1]))
            profile.Password = args[1];
        if (args.Length > 2 && !string.IsNullOrEmpty(args[2]))
            profile.Character = args[2];
        if (args.Length > 3 && !string.IsNullOrEmpty(args[3]))
            profile.Difficulty = EnumConverters.ParseDifficulty(args[3]);
        if (args.Length > 4 && !string.IsNullOrEmpty(args[4]))
            profile.Realm = EnumConverters.ParseRealm(args[4]);
        if (args.Length > 5 && !string.IsNullOrEmpty(args[5]))
            profile.InfoTag = args[5];
        if (args.Length > 6 && !string.IsNullOrEmpty(args[6]))
            profile.D2Path = args[6];

        await _profileEngine.UpdateProfileAndNotifyAsync(profile);
    }

    private async Task HandleRestartProfileAsync(string profileName, bool rotateKey)
    {
        await _profileEngine.RestartProfileAsync(profileName, rotateKey: rotateKey);
    }

    private async Task HandleCDKeyDisabledAsync(Profile profile, string keyName)
    {
        if (!string.IsNullOrEmpty(profile.KeyList))
        {
            await _keyListRepository.HoldKeyAsync(profile.KeyList, keyName);
        }
        _messageService.AddMessage(profile.Name, $"Key disabled: {keyName}", MessageColor.ColorRed);
    }

    private async Task HandleStartAsync(string[] args)
    {
        var targetProfile = await _profileRepository.GetByKeyAsync(args[0]);
        if (targetProfile == null) return;

        if (args.Length > 1 && !string.IsNullOrEmpty(args[1]))
        {
            targetProfile.InfoTag = args[1];
            await _profileEngine.UpdateProfileAndNotifyAsync(targetProfile);
        }

        await _profileEngine.StartProfileAsync(targetProfile.Name);
    }

    private async Task HandleSetTagAsync(Profile profile, string tag)
    {
        profile.InfoTag = tag;
        await _profileEngine.UpdateProfileAndNotifyAsync(profile);

        var instance = _profileEngine.GetInstance(profile.Name);
        var export = LegacyProfileExport.FromProfile(profile, instance);
        _webhookService.EmitEventAsync("setTag", JsonSerializer.Serialize(export));
    }

    private void HandleSetNotify(string[] args)
    {
        try
        {
            var gameAction = JsonSerializer.Deserialize<LegacyGameAction>(args[0]);
            if (gameAction == null) return;

            var status = args.Length > 1 && !string.IsNullOrEmpty(args[1]) ? args[1] : "success";
            _notificationQueue.Enqueue(gameAction.Profile, new LegacyResponse
            {
                Request = "GameActionNotify",
                Status = status,
                Body = args[0]
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize setNotify game action");
        }
    }

    private async Task HandleStopScheduleAsync(string profileName)
    {
        var profile = await _profileRepository.GetByKeyAsync(profileName);
        if (profile != null)
        {
            profile.ScheduleEnabled = false;
            _messageService.AddMessage(profileName, "Schedule disabled", MessageColor.ColorGold);
            await _profileEngine.UpdateProfileAndNotifyAsync(profile);
        }
    }

    private async Task HandleStartScheduleAsync(string profileName)
    {
        var profile = await _profileRepository.GetByKeyAsync(profileName);
        if (profile != null)
        {
            profile.ScheduleEnabled = true;
            _messageService.AddMessage(profileName, "Schedule enabled", MessageColor.ColorGreen);
            await _profileEngine.UpdateProfileAndNotifyAsync(profile);
        }
    }
}
