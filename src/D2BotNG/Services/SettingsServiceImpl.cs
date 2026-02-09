using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Data.LegacyModels;
using D2BotNG.Engine;
using Discord;
using Discord.WebSocket;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace D2BotNG.Services;

public class SettingsServiceImpl : SettingsService.SettingsServiceBase
{
    private readonly SettingsRepository _settingsRepository;
    private readonly ProfileRepository _profileRepository;
    private readonly KeyListRepository _keyListRepository;
    private readonly ScheduleRepository _scheduleRepository;
    private readonly PatchRepository _patchRepository;
    private readonly ProfileEngine _profileEngine;
    private readonly EventBroadcaster _eventBroadcaster;

    public SettingsServiceImpl(
        SettingsRepository settingsRepository,
        ProfileRepository profileRepository,
        KeyListRepository keyListRepository,
        ScheduleRepository scheduleRepository,
        PatchRepository patchRepository,
        ProfileEngine profileEngine,
        EventBroadcaster eventBroadcaster)
    {
        _settingsRepository = settingsRepository;
        _profileRepository = profileRepository;
        _keyListRepository = keyListRepository;
        _scheduleRepository = scheduleRepository;
        _patchRepository = patchRepository;
        _profileEngine = profileEngine;
        _eventBroadcaster = eventBroadcaster;
    }

    public override async Task<Empty> Update(Settings request, ServerCallContext context)
    {
        var oldSettings = await _settingsRepository.GetAsync();
        var basePathChanged = oldSettings.BasePath != request.BasePath;

        var settings = await _settingsRepository.UpdateAsync(request);

        if (basePathChanged)
        {
            // Migrate legacy data at new path if needed
            Migration.MigrateIfNeeded(request.BasePath);

            // Reload all repositories from new path
            await _profileRepository.ReloadAsync();
            await _keyListRepository.ReloadAsync();
            await _scheduleRepository.ReloadAsync();
            await _patchRepository.ReloadAsync();
        }

        // Broadcast settings event
        _eventBroadcaster.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Settings = settings
        });

        // If base path changed, broadcast fresh snapshots of all data
        if (basePathChanged)
        {
            await _profileEngine.BroadcastProfilesSnapshotAsync();
            await _profileEngine.BroadcastKeyListsSnapshotAsync();
            await BroadcastSchedulesSnapshotAsync();
        }

        return new Empty();
    }

    private async Task BroadcastSchedulesSnapshotAsync()
    {
        var snapshot = new SchedulesSnapshot();
        var schedules = await _scheduleRepository.GetAllAsync();
        foreach (var schedule in schedules)
        {
            snapshot.Schedules.Add(schedule);
        }

        _eventBroadcaster.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            SchedulesSnapshot = snapshot
        });
    }

    public override async Task<TestDiscordResponse> TestDiscord(DiscordSettings request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return new TestDiscordResponse { Success = false, Message = "Bot token is required" };
        }

        if (string.IsNullOrWhiteSpace(request.ServerId) || !ulong.TryParse(request.ServerId, out var serverId))
        {
            return new TestDiscordResponse { Success = false, Message = "Valid server ID is required" };
        }

        var client = new DiscordSocketClient(new DiscordSocketConfig
        {
            LogLevel = LogSeverity.Warning,
            GatewayIntents = GatewayIntents.Guilds
        });

        try
        {
            var readyTcs = new TaskCompletionSource<bool>();
            client.Ready += () => { readyTcs.TrySetResult(true); return Task.CompletedTask; };

            await client.LoginAsync(TokenType.Bot, request.Token);
            await client.StartAsync();

            // Wait for ready with timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10), context.CancellationToken);
            var completedTask = await Task.WhenAny(readyTcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                return new TestDiscordResponse { Success = false, Message = "Connection timed out" };
            }

            // Try to get the guild (server)
            var guild = client.GetGuild(serverId);
            if (guild == null)
            {
                return new TestDiscordResponse
                {
                    Success = false,
                    Message = $"Server not found. Make sure the bot has been added to the server with ID {request.ServerId}."
                };
            }

            return new TestDiscordResponse
            {
                Success = true,
                Message = $"Connected successfully to server: {guild.Name}"
            };
        }
        catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return new TestDiscordResponse { Success = false, Message = "Invalid bot token" };
        }
        catch (Exception ex)
        {
            return new TestDiscordResponse { Success = false, Message = $"Error: {ex.Message}" };
        }
        finally
        {
            // Discord.NET can throw during disposal if connection is still active
            // Wrap in try-catch to prevent unhandled exceptions
            try
            {
                await client.LogoutAsync();
                await client.StopAsync();
            }
            catch
            {
                // Ignore disposal errors
            }

            try
            {
                await client.DisposeAsync();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }
}
