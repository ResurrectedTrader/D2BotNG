using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Engine;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace D2BotNG.Services;

public class KeyServiceImpl : KeyService.KeyServiceBase
{
    private readonly KeyListRepository _keyListRepository;
    private readonly ProfileRepository _profileRepository;
    private readonly ProfileEngine _profileEngine;
    private readonly EventBroadcaster _eventBroadcaster;

    public KeyServiceImpl(
        KeyListRepository keyListRepository,
        ProfileRepository profileRepository,
        ProfileEngine profileEngine,
        EventBroadcaster eventBroadcaster)
    {
        _keyListRepository = keyListRepository;
        _profileRepository = profileRepository;
        _profileEngine = profileEngine;
        _eventBroadcaster = eventBroadcaster;
    }

    private async Task BroadcastKeyListsSnapshotAsync()
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
                    var instance = _profileEngine.GetInstance(p.Name);
                    return instance?.CurrentKeyName == key.Name;
                });

                keyListWithUsage.Usage.Add(new KeyUsage
                {
                    KeyName = key.Name,
                    ProfileName = profileUsingKey?.Name ?? ""
                });
            }

            snapshot.KeyLists.Add(keyListWithUsage);
        }

        _eventBroadcaster.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            KeyListsSnapshot = snapshot
        });
    }

    private async Task PropagateKeyListChangeAsync(string oldName, string? newName)
    {
        var profiles = await _profileRepository.GetAllAsync();
        var affected = profiles.Where(p => p.KeyList == oldName).ToList();
        foreach (var profile in affected)
        {
            profile.KeyList = newName ?? "";
            await _profileRepository.UpdateAsync(profile);

            if (string.IsNullOrEmpty(newName))
            {
                var instance = _profileEngine.GetInstance(profile.Name);
                instance?.ClearKey();
            }
        }

        if (affected.Count > 0)
            await _profileEngine.BroadcastProfilesSnapshotAsync();
    }

    private async Task PropagateCDKeyChangesAsync(KeyList oldKeyList, KeyList newKeyList)
    {
        var oldByIdentity = oldKeyList.Keys.ToDictionary(
            k => (k.Classic, k.Expansion),
            k => k.Name);
        var newByIdentity = newKeyList.Keys.ToDictionary(
            k => (k.Classic, k.Expansion),
            k => k.Name);

        var renames = new Dictionary<string, string>();
        var deletes = new HashSet<string>();

        foreach (var (identity, oldName) in oldByIdentity)
        {
            if (newByIdentity.TryGetValue(identity, out var newName))
            {
                if (oldName != newName)
                    renames[oldName] = newName;
            }
            else
            {
                deletes.Add(oldName);
            }
        }

        if (renames.Count == 0 && deletes.Count == 0)
            return;

        var profiles = await _profileRepository.GetAllAsync();
        var affected = false;
        foreach (var profile in profiles.Where(p => p.KeyList == newKeyList.Name))
        {
            var instance = _profileEngine.GetInstance(profile.Name);
            if (instance?.CurrentKeyName == null)
                continue;

            if (renames.TryGetValue(instance.CurrentKeyName, out var renamedTo))
            {
                instance.SetKey(renamedTo);
                affected = true;
            }
            else if (deletes.Contains(instance.CurrentKeyName))
            {
                instance.ClearKey();
                affected = true;
            }
        }

        if (affected)
            await _profileEngine.BroadcastProfilesSnapshotAsync();
    }

    public override async Task<Empty> CreateKeyList(KeyList request, ServerCallContext context)
    {
        await _keyListRepository.CreateAsync(request);
        await BroadcastKeyListsSnapshotAsync();
        return new Empty();
    }

    public override async Task<Empty> UpdateKeyList(UpdateKeyListRequest request, ServerCallContext context)
    {
        var keyList = request.KeyList;
        var lookupName = request.HasOriginalName ? request.OriginalName : keyList.Name;

        var existing = await _keyListRepository.GetByKeyAsync(lookupName);
        if (existing == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"KeyList '{lookupName}' not found"));
        }

        if (request.HasOriginalName && request.OriginalName != keyList.Name)
        {
            await _keyListRepository.DeleteAsync(request.OriginalName);
            await _keyListRepository.CreateAsync(keyList);
            await PropagateKeyListChangeAsync(request.OriginalName, keyList.Name);
        }
        else
        {
            await _keyListRepository.UpdateAsync(keyList);
        }

        await PropagateCDKeyChangesAsync(existing, keyList);

        await BroadcastKeyListsSnapshotAsync();
        return new Empty();
    }

    public override async Task<Empty> DeleteKeyList(KeyListName request, ServerCallContext context)
    {
        await _keyListRepository.DeleteAsync(request.Name);
        await PropagateKeyListChangeAsync(request.Name, null);
        await BroadcastKeyListsSnapshotAsync();
        return new Empty();
    }

    public override async Task<Empty> HoldKey(KeyIdentity request, ServerCallContext context)
    {
        var keyList = await _keyListRepository.GetByKeyAsync(request.KeyListName);
        if (keyList == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"KeyList {request.KeyListName} not found"));
        }

        var key = keyList.Keys.FirstOrDefault(k => k.Name == request.KeyName);
        if (key == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Key {request.KeyName} not found"));
        }

        key.Held = true;
        await _keyListRepository.UpdateAsync(keyList);
        await BroadcastKeyListsSnapshotAsync();
        return new Empty();
    }

    public override async Task<Empty> ReleaseHeldKey(KeyIdentity request, ServerCallContext context)
    {
        var keyList = await _keyListRepository.GetByKeyAsync(request.KeyListName);
        if (keyList == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"KeyList {request.KeyListName} not found"));
        }

        var key = keyList.Keys.FirstOrDefault(k => k.Name == request.KeyName);
        if (key == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Key {request.KeyName} not found"));
        }

        key.Held = false;
        await _keyListRepository.UpdateAsync(keyList);
        await BroadcastKeyListsSnapshotAsync();
        return new Empty();
    }
}
