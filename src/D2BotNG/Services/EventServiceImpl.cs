using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Engine;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace D2BotNG.Services;

public class EventServiceImpl : EventService.EventServiceBase
{
    private readonly ILogger<EventServiceImpl> _logger;
    private readonly EventBroadcaster _eventBroadcaster;
    private readonly ScheduleRepository _scheduleRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly ProfileEngine _profileEngine;
    private readonly UpdateManager _updateManager;
    private readonly MessageService _messageService;

    public EventServiceImpl(
        ILogger<EventServiceImpl> logger,
        EventBroadcaster eventBroadcaster,
        ScheduleRepository scheduleRepository,
        SettingsRepository settingsRepository,
        ProfileEngine profileEngine,
        UpdateManager updateManager,
        MessageService messageService)
    {
        _logger = logger;
        _eventBroadcaster = eventBroadcaster;
        _scheduleRepository = scheduleRepository;
        _settingsRepository = settingsRepository;
        _profileEngine = profileEngine;
        _updateManager = updateManager;
        _messageService = messageService;
    }

    public override async Task StreamEvents(Empty request, IServerStreamWriter<Event> responseStream, ServerCallContext context)
    {
        var clientId = _eventBroadcaster.AddClient();
        _logger.LogDebug("Client {ClientId} connected to event stream", clientId);

        try
        {
            // Send snapshots first
            await SendSnapshotsAsync(responseStream, context.CancellationToken);

            // Then stream events
            await foreach (var evt in _eventBroadcaster.Subscribe(clientId, context.CancellationToken))
            {
                await responseStream.WriteAsync(evt);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Client {ClientId} disconnected from event stream", clientId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming events to client {ClientId}", clientId);
            throw;
        }
        finally
        {
            _eventBroadcaster.RemoveClient(clientId);
        }
    }

    private async Task SendSnapshotsAsync(IServerStreamWriter<Event> responseStream, CancellationToken ct)
    {
        var now = Timestamp.FromDateTime(DateTime.UtcNow);

        // 1. Profiles snapshot with status
        await responseStream.WriteAsync(new Event
        {
            Timestamp = now,
            ProfilesSnapshot = await _profileEngine.BuildProfilesSnapshotAsync()
        }, ct);

        // 2. KeyLists snapshot with usage
        var keyListsSnapshot = await _profileEngine.BuildKeyListsSnapshotAsync();
        await responseStream.WriteAsync(new Event
        {
            Timestamp = now,
            KeyListsSnapshot = keyListsSnapshot
        }, ct);

        // 3. Schedules snapshot
        var schedulesSnapshot = await BuildSchedulesSnapshotAsync();
        await responseStream.WriteAsync(new Event
        {
            Timestamp = now,
            SchedulesSnapshot = schedulesSnapshot
        }, ct);

        // 4. Settings
        var settings = await _settingsRepository.GetAsync();
        await responseStream.WriteAsync(new Event
        {
            Timestamp = now,
            Settings = settings
        }, ct);

        // 5. Update status
        var updateStatus = _updateManager.GetStatus();
        await responseStream.WriteAsync(new Event
        {
            Timestamp = now,
            UpdateStatus = updateStatus
        }, ct);

        // 6. Console message history
        await SendConsoleHistoryAsync(responseStream, now, ct);
    }

    private async Task SendConsoleHistoryAsync(IServerStreamWriter<Event> responseStream, Timestamp now, CancellationToken ct)
    {
        // Send all messages from history
        foreach (var msg in _messageService.GetHistory())
        {
            await responseStream.WriteAsync(new Event
            {
                Timestamp = now,
                Message = msg
            }, ct);
        }
    }


    private async Task<SchedulesSnapshot> BuildSchedulesSnapshotAsync()
    {
        var snapshot = new SchedulesSnapshot();
        var schedules = await _scheduleRepository.GetAllAsync();

        foreach (var schedule in schedules)
        {
            snapshot.Schedules.Add(schedule);
        }

        return snapshot;
    }

    public override Task<Empty> ClearMessages(ClearMessagesRequest request, ServerCallContext context)
    {
        _messageService.ClearMessages(request.Source);
        return Task.FromResult(new Empty());
    }
}
