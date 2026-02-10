using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Engine;
using D2BotNG.Utilities;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace D2BotNG.Services;

public class ScheduleServiceImpl : ScheduleService.ScheduleServiceBase
{
    private readonly ScheduleRepository _scheduleRepository;
    private readonly ProfileRepository _profileRepository;
    private readonly ProfileEngine _profileEngine;
    private readonly EventBroadcaster _eventBroadcaster;

    public ScheduleServiceImpl(
        ScheduleRepository scheduleRepository,
        ProfileRepository profileRepository,
        ProfileEngine profileEngine,
        EventBroadcaster eventBroadcaster)
    {
        _scheduleRepository = scheduleRepository;
        _profileRepository = profileRepository;
        _profileEngine = profileEngine;
        _eventBroadcaster = eventBroadcaster;
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

    private async Task PropagateScheduleChangeAsync(string oldName, string? newName)
    {
        var profiles = await _profileRepository.GetAllAsync();
        var affected = profiles.Where(p => p.Schedule == oldName).ToList();
        foreach (var profile in affected)
        {
            profile.Schedule = newName ?? "";
            if (string.IsNullOrEmpty(newName))
                profile.ScheduleEnabled = false;
            await _profileRepository.UpdateAsync(profile);
        }

        if (affected.Count > 0)
            await _profileEngine.BroadcastProfilesSnapshotAsync();
    }

    public override async Task<Empty> Create(Schedule request, ServerCallContext context)
    {
        await _scheduleRepository.CreateAsync(request);
        await BroadcastSchedulesSnapshotAsync();
        return new Empty();
    }

    public override async Task<Empty> Update(UpdateScheduleRequest request, ServerCallContext context)
    {
        var schedule = request.Schedule;
        var lookupName = request.HasOriginalName ? request.OriginalName : schedule.Name;

        var existing = await _scheduleRepository.GetByKeyAsync(lookupName);
        if (existing == null)
        {
            throw RpcExceptions.NotFound("Schedule", lookupName);
        }

        if (request.HasOriginalName && request.OriginalName != schedule.Name)
        {
            await _scheduleRepository.DeleteAsync(request.OriginalName);
            await _scheduleRepository.CreateAsync(schedule);
            await PropagateScheduleChangeAsync(request.OriginalName, schedule.Name);
        }
        else
        {
            await _scheduleRepository.UpdateAsync(schedule);
        }

        await BroadcastSchedulesSnapshotAsync();
        return new Empty();
    }

    public override async Task<Empty> Delete(ScheduleName request, ServerCallContext context)
    {
        await _scheduleRepository.DeleteAsync(request.Name);
        await PropagateScheduleChangeAsync(request.Name, null);
        await BroadcastSchedulesSnapshotAsync();
        return new Empty();
    }
}
