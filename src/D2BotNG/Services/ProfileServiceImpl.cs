using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Engine;
using D2BotNG.Windows;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace D2BotNG.Services;

public class ProfileServiceImpl : ProfileService.ProfileServiceBase
{
    private readonly ProfileRepository _profileRepository;
    private readonly ProfileEngine _profileEngine;

    public ProfileServiceImpl(
        ProfileRepository profileRepository,
        ProfileEngine profileEngine)
    {
        _profileRepository = profileRepository;
        _profileEngine = profileEngine;
    }

    public override async Task<Empty> Create(Profile request, ServerCallContext context)
    {
        var existingProfile = await _profileRepository.GetByKeyAsync(request.Name);
        if (existingProfile != null)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, $"Profile '{request.Name}' already exists"));
        }

        var profile = await _profileRepository.CreateAsync(request);
        _profileEngine.AddProfile(profile.Name);
        await _profileEngine.BroadcastProfilesSnapshotAsync();

        return new Empty();
    }

    public override async Task<Empty> Update(UpdateProfileRequest request, ServerCallContext context)
    {
        var profile = request.Profile;
        var lookupName = request.HasOriginalName ? request.OriginalName : profile.Name;

        var existing = await _profileRepository.GetByKeyAsync(lookupName);
        if (existing == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Profile '{lookupName}' not found"));
        }

        // Preserve stats from existing profile
        profile.Runs = existing.Runs;
        profile.Chickens = existing.Chickens;
        profile.Deaths = existing.Deaths;
        profile.Crashes = existing.Crashes;
        profile.Restarts = existing.Restarts;
        profile.KeyRuns = existing.KeyRuns;

        // Handle rename
        if (request.HasOriginalName && request.OriginalName != profile.Name)
        {
            await _profileRepository.DeleteAsync(request.OriginalName);
            await _profileRepository.CreateAsync(profile);
            _profileEngine.RenameProfile(request.OriginalName, profile.Name);
            // Rename changes the profile set — need full snapshot
            await _profileEngine.BroadcastProfilesSnapshotAsync();
        }
        else
        {
            await _profileRepository.UpdateAsync(profile);
            await _profileEngine.NotifyProfileStateChangedAsync(profile.Name, includeProfile: true);
        }

        return new Empty();
    }

    public override async Task<Empty> Delete(ProfileName request, ServerCallContext context)
    {
        await _profileEngine.StopProfileAsync(request.Name, force: true);

        await _profileRepository.DeleteAsync(request.Name);
        _profileEngine.RemoveProfile(request.Name);
        await _profileEngine.BroadcastProfilesSnapshotAsync();

        return new Empty();
    }

    public override async Task<Empty> Start(ProfileNames request, ServerCallContext context)
    {
        foreach (var name in request.Names)
        {
            await _profileEngine.StartProfileAsync(name);
        }
        return new Empty();
    }

    public override async Task<Empty> Stop(ProfileNames request, ServerCallContext context)
    {
        foreach (var name in request.Names)
        {
            await _profileEngine.StopProfileAsync(name);
        }
        return new Empty();
    }

    public override async Task<Empty> Restart(RestartRequest request, ServerCallContext context)
    {
        await _profileEngine.StopProfileAsync(request.ProfileName);
        await Task.Delay(1000);
        await _profileEngine.StartProfileAsync(request.ProfileName);
        return new Empty();
    }

    public override async Task<Empty> ShowWindow(ProfileNames request, ServerCallContext context)
    {
        var peer = context.Peer;
        if (!IsLocalhost(peer))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Window control only allowed from localhost"));
        }

        foreach (var name in request.Names)
        {
            await _profileEngine.ShowWindowAsync(name);
        }
        return new Empty();
    }

    public override async Task<Empty> HideWindow(ProfileNames request, ServerCallContext context)
    {
        var peer = context.Peer;
        if (!IsLocalhost(peer))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Window control only allowed from localhost"));
        }

        foreach (var name in request.Names)
        {
            await _profileEngine.HideWindowAsync(name);
        }
        return new Empty();
    }

    public override async Task<Empty> ResetStats(ProfileName request, ServerCallContext context)
    {
        if (!await _profileEngine.ResetStatsAsync(request.Name))
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Profile '{request.Name}' not found"));
        }
        return new Empty();
    }

    public override Task<Empty> TriggerMule(ProfileName request, ServerCallContext context)
    {
        _profileEngine.SendMessage(request.Name, MessageType.Mule, "mule");
        return Task.FromResult(new Empty());
    }

    public override async Task<Empty> RotateKey(ProfileName request, ServerCallContext context)
    {
        if (!await _profileEngine.RotateKeyAsync(request.Name))
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Failed to rotate key"));
        }
        return new Empty();
    }

    public override async Task<Empty> ReleaseKey(ProfileName request, ServerCallContext context)
    {
        await _profileEngine.ReleaseKeyAsync(request.Name);
        return new Empty();
    }

    public override async Task<Empty> SetScheduleEnabled(SetScheduleEnabledRequest request, ServerCallContext context)
    {
        var profile = await _profileRepository.GetByKeyAsync(request.ProfileName);
        if (profile == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Profile '{request.ProfileName}' not found"));
        }

        profile.ScheduleEnabled = request.Enabled;
        await _profileRepository.UpdateAsync(profile);
        await _profileEngine.NotifyProfileStateChangedAsync(request.ProfileName, includeProfile: true);

        return new Empty();
    }

    public override async Task<Empty> Reorder(ReorderProfileRequest request, ServerCallContext context)
    {
        var profile = await _profileRepository.GetByKeyAsync(request.ProfileName);
        if (profile == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Profile '{request.ProfileName}' not found"));
        }

        var allProfiles = (await _profileRepository.GetAllAsync()).ToList();

        // Determine target group
        var targetGroup = request.HasNewGroup ? request.NewGroup : profile.Group;

        // Update group if changed
        if (request.HasNewGroup && request.NewGroup != profile.Group)
        {
            profile.Group = request.NewGroup;
            await _profileRepository.UpdateAsync(profile);
            // Refresh the list after update
            allProfiles = (await _profileRepository.GetAllAsync()).ToList();
        }

        // Find profiles in target group (excluding the one being moved)
        var groupProfiles = allProfiles
            .Select((p, index) => (Profile: p, Index: index))
            .Where(x => x.Profile.Group == targetGroup && x.Profile.Name != request.ProfileName)
            .ToList();

        // Calculate global index
        int globalIndex;
        if (groupProfiles.Count == 0)
        {
            // No other profiles in target group - find where this group should be
            // Groups are ordered: ungrouped first, then alphabetically by group name
            if (string.IsNullOrEmpty(targetGroup))
            {
                // Ungrouped goes at the beginning
                globalIndex = 0;
            }
            else
            {
                // Find the first profile of a group that comes after targetGroup alphabetically
                var afterGroup = allProfiles
                    .Select((p, index) => (Profile: p, Index: index))
                    .Where(x => !string.IsNullOrEmpty(x.Profile.Group) &&
                                string.Compare(x.Profile.Group, targetGroup, StringComparison.Ordinal) > 0 &&
                                x.Profile.Name != request.ProfileName)
                    .OrderBy(x => x.Index)
                    .FirstOrDefault();

                globalIndex = afterGroup.Profile != null ? afterGroup.Index : allProfiles.Count - 1;
            }
        }
        else if (request.NewIndex <= 0)
        {
            // Insert at beginning of group
            globalIndex = groupProfiles[0].Index;
        }
        else if (request.NewIndex >= groupProfiles.Count)
        {
            // Insert at end of group - after the last profile in the group
            globalIndex = groupProfiles[^1].Index + 1;
            // Adjust if moving from before to after (index will shift)
            var currentIndex = allProfiles.FindIndex(p => p.Name == request.ProfileName);
            if (currentIndex < groupProfiles[^1].Index)
                globalIndex--;
        }
        else
        {
            // Insert at specific position within group
            globalIndex = groupProfiles[request.NewIndex].Index;
        }

        // Clamp to valid range
        globalIndex = Math.Clamp(globalIndex, 0, allProfiles.Count - 1);

        await _profileRepository.MoveToIndexAsync(request.ProfileName, globalIndex);

        await _profileEngine.BroadcastProfilesSnapshotAsync();

        return new Empty();
    }

    private static bool IsLocalhost(string peer)
    {
        return peer.Contains("127.0.0.1") || peer.Contains("::1") || peer.Contains("localhost");
    }
}
