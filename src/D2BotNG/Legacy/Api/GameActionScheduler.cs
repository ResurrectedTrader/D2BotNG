using System.Collections.Concurrent;
using System.Text.Json;
using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Engine;
using D2BotNG.Legacy.Models;
using D2BotNG.Services;

namespace D2BotNG.Legacy.Api;

public class GameActionScheduler : BackgroundService
{
    private readonly EventBroadcaster _eventBroadcaster;
    private readonly ProfileEngine _profileEngine;
    private readonly ProfileRepository _profileRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly NotificationQueue _notificationQueue;
    private readonly ILogger<GameActionScheduler> _logger;

    private readonly ConcurrentQueue<string> _actionQueue = new();

    public GameActionScheduler(
        EventBroadcaster eventBroadcaster,
        ProfileEngine profileEngine,
        ProfileRepository profileRepository,
        SettingsRepository settingsRepository,
        NotificationQueue notificationQueue,
        ILogger<GameActionScheduler> logger)
    {
        _eventBroadcaster = eventBroadcaster;
        _profileEngine = profileEngine;
        _profileRepository = profileRepository;
        _settingsRepository = settingsRepository;
        _notificationQueue = notificationQueue;
        _logger = logger;
    }

    public void EnqueueAction(string actionJson)
    {
        _actionQueue.Enqueue(actionJson);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var clientId = _eventBroadcaster.AddClient();

        try
        {
            await foreach (var evt in _eventBroadcaster.Subscribe(clientId, stoppingToken))
            {
                if (evt.EventCase == Event.EventOneofCase.ProfileState)
                    await HandleProfileStateAsync(evt.ProfileState);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing events in GameActionScheduler");
        }
    }

    private async Task HandleProfileStateAsync(ProfileState state)
    {
        if (state.State != RunState.Stopped)
            return;

        var profileName = state.ProfileName;

        var settings = await _settingsRepository.GetAsync();
        if (!settings.LegacyApi.Profiles.Contains(profileName))
            return;

        var profile = await _profileRepository.GetByKeyAsync(profileName);
        if (profile == null)
            return;

        // Check if the profile's InfoTag contains a completed game action
        if (!string.IsNullOrEmpty(profile.InfoTag))
        {
            try
            {
                var gameAction = JsonSerializer.Deserialize<LegacyGameAction>(profile.InfoTag);
                if (gameAction is { Action: "done" })
                {
                    _notificationQueue.Enqueue(gameAction.Profile, new LegacyResponse
                    {
                        Request = "GameActionNotify",
                        Status = "success",
                        Body = profile.InfoTag
                    });

                    profile.InfoTag = "";
                    await _profileRepository.UpdateAsync(profile);
                    await _profileEngine.NotifyProfileStateChangedAsync(profileName, includeProfile: true);
                }
            }
            catch (JsonException)
            {
                // InfoTag is not a game action, ignore
            }
        }

        // If there are queued actions and the profile's InfoTag is empty, assign the next one
        if (string.IsNullOrEmpty(profile.InfoTag) && _actionQueue.TryDequeue(out var actionJson))
        {
            profile.InfoTag = actionJson;
            await _profileRepository.UpdateAsync(profile);
            await _profileEngine.NotifyProfileStateChangedAsync(profileName, includeProfile: true);
            await _profileEngine.StartProfileAsync(profileName);
        }
        // Restart profile if InfoTag is non-empty from another source
        else if (!string.IsNullOrEmpty(profile.InfoTag))
        {
            await _profileEngine.StartProfileAsync(profileName);
        }
    }
}
