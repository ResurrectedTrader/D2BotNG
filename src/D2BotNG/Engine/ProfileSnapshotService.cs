using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Services;
using Google.Protobuf.WellKnownTypes;

namespace D2BotNG.Engine;

/// <summary>
/// Builds and broadcasts profile and key list snapshots.
/// </summary>
public class ProfileSnapshotService
{
    private readonly ProfileRepository _profileRepository;
    private readonly KeyListRepository _keyListRepository;
    private readonly EventBroadcaster _eventBroadcaster;

    public ProfileSnapshotService(
        ProfileRepository profileRepository,
        KeyListRepository keyListRepository,
        EventBroadcaster eventBroadcaster)
    {
        _profileRepository = profileRepository;
        _keyListRepository = keyListRepository;
        _eventBroadcaster = eventBroadcaster;
    }

    public async Task<ProfilesSnapshot> BuildProfilesSnapshotAsync(Func<string, ProfileInstance?> getInstance)
    {
        var snapshot = new ProfilesSnapshot();
        var profiles = await _profileRepository.GetAllAsync();

        foreach (var profile in profiles)
        {
            var instance = getInstance(profile.Name);
            var status = instance?.GetState() ?? new ProfileState
            {
                ProfileName = profile.Name,
                State = RunState.Stopped,
                Status = ""
            };

            status.Profile = profile;
            snapshot.Profiles.Add(status);
        }

        return snapshot;
    }

    public async Task BroadcastProfilesSnapshotAsync(Func<string, ProfileInstance?> getInstance)
    {
        _eventBroadcaster.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ProfilesSnapshot = await BuildProfilesSnapshotAsync(getInstance)
        });
    }

    public async Task<KeyListsSnapshot> BuildKeyListsSnapshotAsync(Func<string, ProfileInstance?> getInstance)
    {
        var snapshot = new KeyListsSnapshot();
        var keyLists = await _keyListRepository.GetAllAsync();
        var profiles = await _profileRepository.GetAllAsync();

        foreach (var keyList in keyLists)
        {
            var keyListWithUsage = new KeyListWithUsage { KeyList = keyList };

            foreach (var key in keyList.Keys)
            {
                var profileUsingKey = profiles.FirstOrDefault(p =>
                {
                    if (p.KeyList != keyList.Name) return false;
                    var instance = getInstance(p.Name);
                    return instance?.KeyName == key.Name;
                });

                keyListWithUsage.Usage.Add(new KeyUsage
                {
                    KeyName = key.Name,
                    ProfileName = profileUsingKey?.Name ?? ""
                });
            }

            snapshot.KeyLists.Add(keyListWithUsage);
        }

        return snapshot;
    }

    public async Task BroadcastKeyListsSnapshotAsync(Func<string, ProfileInstance?> getInstance)
    {
        _eventBroadcaster.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            KeyListsSnapshot = await BuildKeyListsSnapshotAsync(getInstance)
        });
    }

    public async Task NotifyProfileStateChangedAsync(
        string profileName,
        ProfileInstance instance,
        bool includeProfile = false)
    {
        var state = instance.GetState();
        if (includeProfile)
            state.Profile = await _profileRepository.GetByKeyAsync(profileName);

        _eventBroadcaster.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ProfileState = state
        });
    }
}
