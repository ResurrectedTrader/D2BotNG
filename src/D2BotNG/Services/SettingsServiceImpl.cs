using System.Net;
using D2BotNG.Core.Protos;
using D2BotNG.Data;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace D2BotNG.Services;

public class SettingsServiceImpl : SettingsService.SettingsServiceBase
{
    private readonly SettingsRepository _settingsRepository;
    private readonly EventBroadcaster _eventBroadcaster;

    public SettingsServiceImpl(SettingsRepository settingsRepository, EventBroadcaster eventBroadcaster)
    {
        _settingsRepository = settingsRepository;
        _eventBroadcaster = eventBroadcaster;
    }

    /// <summary>
    /// Saves the settings. A changed base path moves the manager to the new data folder through
    /// <see cref="DataFileWatcher"/>, which reacts to SettingsChanged and re-broadcasts every
    /// snapshot once the repositories have reloaded.
    /// </summary>
    public override async Task<Empty> Update(Settings request, ServerCallContext context)
    {
        var settings = await _settingsRepository.UpdateAsync(request);

        _eventBroadcaster.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Settings = settings
        });

        return new Empty();
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
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Unauthorized)
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
