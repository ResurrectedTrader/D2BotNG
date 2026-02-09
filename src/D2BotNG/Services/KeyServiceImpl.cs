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

    public KeyServiceImpl(
        KeyListRepository keyListRepository,
        ProfileRepository profileRepository,
        ProfileEngine profileEngine)
    {
        _keyListRepository = keyListRepository;
        _profileRepository = profileRepository;
        _profileEngine = profileEngine;
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
                if (instance != null) instance.KeyName = null;
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
            if (instance?.KeyName == null)
                continue;

            if (renames.TryGetValue(instance.KeyName, out var renamedTo))
            {
                instance.KeyName = renamedTo;
                affected = true;
            }
            else if (deletes.Contains(instance.KeyName))
            {
                instance.KeyName = null;
                affected = true;
            }
        }

        if (affected)
            await _profileEngine.BroadcastProfilesSnapshotAsync();
    }

    public override async Task<Empty> CreateKeyList(KeyList request, ServerCallContext context)
    {
        await _keyListRepository.CreateAsync(request);
        await _profileEngine.BroadcastKeyListsSnapshotAsync();
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

        await _profileEngine.BroadcastKeyListsSnapshotAsync();
        return new Empty();
    }

    public override async Task<Empty> DeleteKeyList(KeyListName request, ServerCallContext context)
    {
        await _keyListRepository.DeleteAsync(request.Name);
        await PropagateKeyListChangeAsync(request.Name, null);
        await _profileEngine.BroadcastKeyListsSnapshotAsync();
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
        await _profileEngine.BroadcastKeyListsSnapshotAsync();
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
        await _profileEngine.BroadcastKeyListsSnapshotAsync();
        return new Empty();
    }
}
