using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Engine;
using D2BotNG.Utilities;
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
            throw RpcExceptions.AlreadyExists("Profile", request.Name);
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
            throw RpcExceptions.NotFound("Profile", lookupName);
        }

        profile.PreserveStatsFrom(existing);

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

    public override async Task<Empty> Delete(ProfileNames request, ServerCallContext context)
    {
        foreach (var name in request.Names)
        {
            await _profileEngine.StopProfileAsync(name, force: true);
            await _profileRepository.DeleteAsync(name);
            _profileEngine.RemoveProfile(name);
        }
        await _profileEngine.BroadcastProfilesSnapshotAsync();

        return new Empty();
    }

    public override async Task<Empty> Start(ProfileNames request, ServerCallContext context)
    {
        return await ForEachProfileAsync(request, name => _profileEngine.StartProfileAsync(name));
    }

    public override async Task<Empty> Stop(ProfileNames request, ServerCallContext context)
    {
        return await ForEachProfileAsync(request, name => _profileEngine.StopProfileAsync(name));
    }

    public override async Task<Empty> ShowWindow(ProfileNames request, ServerCallContext context)
    {
        RequireLocalhost(context.Peer);
        return await ForEachProfileAsync(request, name => _profileEngine.ShowWindowAsync(name));
    }

    public override async Task<Empty> HideWindow(ProfileNames request, ServerCallContext context)
    {
        RequireLocalhost(context.Peer);
        return await ForEachProfileAsync(request, name => _profileEngine.HideWindowAsync(name));
    }

    public override async Task<Empty> ResetStats(ProfileNames request, ServerCallContext context)
    {
        List<string>? notFound = null;
        foreach (var name in request.Names)
        {
            if (!await _profileEngine.ResetStatsAsync(name))
                (notFound ??= []).Add(name);
        }
        if (notFound != null)
            throw RpcExceptions.NotFound("Profile(s)", string.Join(", ", notFound));
        return new Empty();
    }

    public override Task<Empty> TriggerMule(ProfileNames request, ServerCallContext context)
    {
        foreach (var name in request.Names)
        {
            _profileEngine.SendMessage(name, MessageType.Mule, "mule");
        }
        return Task.FromResult(new Empty());
    }

    public override async Task<Empty> RotateKey(ProfileNames request, ServerCallContext context)
    {
        if (!await _profileEngine.RotateKeysAsync(request.Names))
        {
            throw RpcExceptions.FailedPrecondition("Failed to rotate key");
        }
        return new Empty();
    }

    public override async Task<Empty> ReleaseKey(ProfileNames request, ServerCallContext context)
    {
        await _profileEngine.ReleaseKeysAsync(request.Names);
        return new Empty();
    }

    public override async Task<Empty> EnableSchedule(ProfileNames request, ServerCallContext context)
    {
        List<string>? notFound = null;
        foreach (var name in request.Names)
        {
            var profile = await _profileRepository.GetByKeyAsync(name);
            if (profile == null)
            {
                (notFound ??= []).Add(name);
                continue;
            }

            profile.ScheduleEnabled = true;
            await _profileRepository.UpdateAsync(profile);
            await _profileEngine.NotifyProfileStateChangedAsync(name, includeProfile: true);
        }
        if (notFound != null)
            throw RpcExceptions.NotFound("Profile(s)", string.Join(", ", notFound));

        return new Empty();
    }

    public override async Task<Empty> DisableSchedule(ProfileNames request, ServerCallContext context)
    {
        List<string>? notFound = null;
        foreach (var name in request.Names)
        {
            var profile = await _profileRepository.GetByKeyAsync(name);
            if (profile == null)
            {
                (notFound ??= []).Add(name);
                continue;
            }

            profile.ScheduleEnabled = false;
            await _profileRepository.UpdateAsync(profile);
            await _profileEngine.NotifyProfileStateChangedAsync(name, includeProfile: true);
        }
        if (notFound != null)
            throw RpcExceptions.NotFound("Profile(s)", string.Join(", ", notFound));

        return new Empty();
    }

    public override async Task<Empty> Reorder(ReorderProfileRequest request, ServerCallContext context)
    {
        if (request.ProfileNames.Count == 0)
            return new Empty();

        var movingNames = new HashSet<string>(request.ProfileNames);

        // Validate all profiles exist
        var allProfiles = (await _profileRepository.GetAllAsync()).ToList();
        var notFound = request.ProfileNames.Where(n => !allProfiles.Any(p => p.Name == n)).ToList();
        if (notFound.Count > 0)
            throw RpcExceptions.NotFound("Profile(s)", string.Join(", ", notFound));

        // Determine target group - use group of first profile if not specified
        var firstProfile = allProfiles.First(p => p.Name == request.ProfileNames[0]);
        var targetGroup = request.HasNewGroup ? request.NewGroup : firstProfile.Group;

        // Update group for any profiles that need it
        if (request.HasNewGroup)
        {
            foreach (var name in request.ProfileNames)
            {
                var profile = allProfiles.First(p => p.Name == name);
                if (profile.Group != request.NewGroup)
                {
                    profile.Group = request.NewGroup;
                    await _profileRepository.UpdateAsync(profile);
                }
            }
            // Refresh the list after updates
            allProfiles = (await _profileRepository.GetAllAsync()).ToList();
        }

        // Find profiles in target group (excluding the ones being moved)
        var groupProfiles = allProfiles
            .Select((p, index) => (Profile: p, Index: index))
            .Where(x => x.Profile.Group == targetGroup && !movingNames.Contains(x.Profile.Name))
            .ToList();

        // Calculate global index (in the list with moved items removed)
        // We need to compute the index in the "remaining" list (all profiles minus moved ones)
        int globalIndex;
        if (groupProfiles.Count == 0)
        {
            // No other profiles in target group - find where this group should be
            if (string.IsNullOrEmpty(targetGroup))
            {
                globalIndex = 0;
            }
            else
            {
                var afterGroup = allProfiles
                    .Where(x => !movingNames.Contains(x.Name))
                    .Select((p, index) => (Profile: p, Index: index))
                    .Where(x => !string.IsNullOrEmpty(x.Profile.Group) &&
                                string.Compare(x.Profile.Group, targetGroup, StringComparison.Ordinal) > 0)
                    .OrderBy(x => x.Index)
                    .FirstOrDefault();

                var remaining = allProfiles.Where(p => !movingNames.Contains(p.Name)).ToList();
                globalIndex = afterGroup.Profile != null ? afterGroup.Index : remaining.Count;
            }
        }
        else if (request.NewIndex <= 0)
        {
            // Insert at beginning of group - convert group-local index to global remaining index
            var targetName = groupProfiles[0].Profile.Name;
            var remaining = allProfiles.Where(p => !movingNames.Contains(p.Name)).ToList();
            globalIndex = remaining.FindIndex(p => p.Name == targetName);
        }
        else if (request.NewIndex >= groupProfiles.Count)
        {
            // Insert at end of group
            var targetName = groupProfiles[^1].Profile.Name;
            var remaining = allProfiles.Where(p => !movingNames.Contains(p.Name)).ToList();
            globalIndex = remaining.FindIndex(p => p.Name == targetName) + 1;
        }
        else
        {
            // Insert at specific position within group
            var targetName = groupProfiles[request.NewIndex].Profile.Name;
            var remaining = allProfiles.Where(p => !movingNames.Contains(p.Name)).ToList();
            globalIndex = remaining.FindIndex(p => p.Name == targetName);
        }

        globalIndex = Math.Max(globalIndex, 0);

        await _profileRepository.MoveMultipleToIndexAsync(request.ProfileNames.ToList(), globalIndex);

        await _profileEngine.BroadcastProfilesSnapshotAsync();

        return new Empty();
    }

    private static async Task<Empty> ForEachProfileAsync(ProfileNames request, Func<string, Task> action)
    {
        await Task.WhenAll(request.Names.Select(action));
        return new Empty();
    }

    private static void RequireLocalhost(string peer)
    {
        if (!peer.Contains("127.0.0.1") && !peer.Contains("::1") && !peer.Contains("localhost"))
        {
            throw RpcExceptions.PermissionDenied("Window control only allowed from localhost");
        }
    }
}
