using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using D2BotNG.Capture;
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
    private readonly CaptureEngine _captureEngine;

    /// <summary>
    /// Handles we've already warned about, so a 1Hz sender doesn't flood the log.
    /// </summary>
    private readonly ConcurrentDictionary<nint, byte> _unroutedHandles = new();

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
        CharacterStateService characterStateService,
        CaptureEngine captureEngine)
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
        _captureEngine = captureEngine;
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
        // Checked before resolving the profile, which takes the repository lock and scans it.
        // A bot keeps talking through the WM_CLOSE grace after we decide to stop or kill it, and
        // the routing entry now outlives the process, so we still know exactly who it is. Most of
        // what it says is worth keeping — see IgnoredWhileTearingDown for what isn't and why.
        var sender = _profileEngine.GetInstanceByHandle(msg.SenderHandle);
        if (sender is { TearingDown: true }
            && IsIgnoredWhileTearingDown(msg.Message.Function, msg.Message.Arguments, sender))
        {
            _logger.LogDebug("Ignoring {Function} from {Profile} while its game is being killed",
                msg.Message.Function ?? "?", sender.ProfileName);
            return;
        }

        var profile = await FindProfileByHandleAsync(msg.SenderHandle);

        _logger.LogDebug("D2BS command: {Command} from {Profile}", msg.Message, profile?.Name ?? "unknown");

        if (profile == null)
        {
            // A message we cannot attribute is a message we throw away — including a heartbeat,
            // which then reads as a dead bot. This used to vanish into the Debug log above with
            // no counter, so a leaked or misrouted entry in the engine's handle map was
            // invisible. Warn once per handle rather than per message: a bot sends ~1Hz.
            if (_unroutedHandles.TryAdd(msg.SenderHandle, 0))
            {
                _logger.LogWarning(
                    "Discarding D2BS message from unknown window handle {Handle} ({Function}) — " +
                    "no running profile is registered for it",
                    msg.SenderHandle, msg.Message.Function ?? "?");
            }
            return;
        }

        _unroutedHandles.TryRemove(msg.SenderHandle, out _);

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
                await HandleRequestGameInfo(msg.SenderHandle, profile);
                break;

            case "setProfile":
                await HandleSetProfileAsync(profile, args);
                break;

            case "restartProfile":
                HandleRestartProfile(profile.Name, args.Length > 1 && args[1].Equals("true", StringComparison.OrdinalIgnoreCase));
                break;

            case "stop":
                RunDetached("Stop", profile.Name, () => _profileEngine.StopProfileAsync(profile.Name));
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

    /// <summary>
    /// Functions ignored while a profile's game is being killed.
    /// </summary>
    /// <remarks>
    /// Two groups. Lifecycle commands would fight a teardown already in flight — restarting a
    /// profile out from under a stop, which nothing downstream catches because Stopped -> Starting
    /// is a legal transition. ("start" is handled separately: it names its target, and starting
    /// some *other* profile is legitimate.) The rest are the high-frequency writes whose value
    /// dies with the run: a counter the run about to be killed would have bumped, or a status the
    /// teardown overwrites moments later. Each costs a rewrite of the whole profiles.json plus
    /// every framework's d2bs.ini, or an EnumWindows sweep of the session to build a status
    /// snapshot, so at 160 profiles keeping them would mean up to one full-file rewrite per
    /// profile inside a single Stop All's shutdown grace.
    /// <para>
    /// Everything not listed still flows: the item log, item PNGs, console output, character
    /// state, and CDKeyDisabled — a key Battle.net just disabled must still be held, or the next
    /// profile to start picks it up. stopSchedule/startSchedule persist too and are deliberately
    /// NOT listed: they are a script's explicit one-shot decision and the sanctioned way for it
    /// to opt out, so honouring them matters more than the write, and they are rare.
    /// </para>
    /// <para>
    /// Note this window is a strict subset of what was dropped before. Previously the routing
    /// entry was removed before the kill, so every message from a dying game was discarded as
    /// unattributable; these eight are what remains of that.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> IgnoredWhileTearingDown =
    [
        "restartProfile", "stop",
        "updateStatus", "updateRuns", "updateChickens", "updateDeaths", "setProfile", "setTag",
    ];

    private static bool IsIgnoredWhileTearingDown(string? function, string[] args, ProfileInstance sender)
    {
        if (function == null) return false;
        if (IgnoredWhileTearingDown.Contains(function)) return true;

        // "start" names its target. Starting another profile is legitimate even from a bot on its
        // way out; starting *itself* is the same resurrection as "restartProfile", and legal —
        // Error -> Starting is a valid transition — so it has to be caught here.
        return function == "start" && args.Length > 0 && args[0] == sender.ProfileName;
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

    private async Task HandleRequestGameInfo(nint senderHandle, Profile profile)
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
        // Through the engine rather than the process directly, so a reply that never lands is
        // attributed to the profile. The script blocks waiting for this — it carries the key and
        // game name it needs to make a game.
        _profileEngine.SendMessage(senderHandle, MessageType.GameInfo, JsonSerializer.Serialize(gameInfo));
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
            HandleRestartProfile(profile.Name, rotateKey: profile.SwitchKeysOnRestart);
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
            var settings = _settingsRepository.Current;
            var itemFont = settings.Display?.ItemFont ?? ItemFont.Exocet;
            var spriteStyle = settings.Display?.SpriteStyle ?? SpriteStyle.Classic;
            var png = _itemRenderer.RenderItemTooltip(item, itemFont, spriteStyle: spriteStyle);
            var imagesDir = Path.Combine(_paths.BasePath, "images");
            Directory.CreateDirectory(imagesDir);
            var index = Directory.GetFiles(imagesDir, item.Name + "*").Length + 1;
            var path = Path.Combine(imagesDir, $"{item.Name}{index}.png");
            AtomicFile.WriteAllBytes(path, png);
            _logger.LogDebug("Saved item screenshot to {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save item screenshot");
        }
    }

    /// <summary>
    /// Routes a character snapshot to the stack that owns its wire schema. The two are separate
    /// all the way down — engine, storage and endpoints — and a profile fills exactly one of
    /// them, decided by the engine DLL it is running. They share only this message name, because
    /// routing on the payload's own schemaVersion needs no change on the engine side.
    /// </summary>
    private void HandleCharacterState(Profile profile, string json)
    {
        try
        {
            if (CaptureEngine.PeekSchemaVersion(json) == CaptureEngine.SchemaVersion)
            {
                _captureEngine.Ingest(profile.Name, json);
                return;
            }

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

        // The launched executable is the profile's own Diablo II Path.
        var gamePath = targetProfile.D2Path;

        var export = new
        {
            targetProfile.Name,
            instance.Status,
            targetProfile.Account,
            targetProfile.Character,
            Difficulty = targetProfile.Difficulty.ToString(),
            Realm = targetProfile.Realm.ToString(),
            Game = gamePath,
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

    /// <summary>
    /// Runs profile lifecycle work off the dispatch loop.
    /// </summary>
    /// <remarks>
    /// This loop processes one message at a time for the entire fleet, so anything awaited here
    /// is time during which no other profile's messages — including its heartbeats — are read.
    /// A restart is <c>StopProfileAsync</c> (up to a 5s terminate grace) + a 1s settle + start,
    /// so awaiting it inline stalls the whole pipeline for six seconds or more; a handful of
    /// profiles rotating keys together was enough to push uninvolved bots past the missed-
    /// heartbeat threshold and recruit them into the same restart storm.
    /// <para>
    /// D2Bot# never did this inline either — <c>D2Profile.Stop()</c> queued a Worker onto one of
    /// ten shards keyed by profile name hash. The engine's own state machine already rejects
    /// invalid transitions, so concurrent lifecycle requests for one profile are safe.
    /// </para>
    /// </remarks>
    private void RunDetached(string description, string profileName, Func<Task> work)
    {
        _ = Task.Run(work).ContinueWith(
            t => _logger.LogError(t.Exception, "{Description} failed for profile {Profile}", description, profileName),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private void HandleRestartProfile(string profileName, bool rotateKey)
    {
        RunDetached("Restart", profileName,
            () => _profileEngine.RestartProfileAsync(profileName, rotateKey: rotateKey));
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
