using D2BotNG.Core.Protos;
using D2BotNG.Data;

namespace D2BotNG.Engine;

/// <summary>
/// Manages CD key acquisition, rotation, release, and hold tracking for profiles.
/// </summary>
public class KeyManager
{
    private readonly ProfileRepository _profileRepository;
    private readonly KeyListRepository _keyListRepository;

    public KeyManager(
        ProfileRepository profileRepository,
        KeyListRepository keyListRepository)
    {
        _profileRepository = profileRepository;
        _keyListRepository = keyListRepository;
    }

    public async Task<HashSet<string>> GetUsedKeyNamesAsync(
        string keyListName,
        Func<string, ProfileInstance?> getInstance)
    {
        var profiles = await _profileRepository.GetAllAsync();
        var used = new HashSet<string>();
        foreach (var p in profiles.Where(p => p.KeyList == keyListName))
        {
            var inst = getInstance(p.Name);
            if (inst?.KeyName != null)
                used.Add(inst.KeyName);
        }
        return used;
    }

    public async Task<CDKey?> AcquireKeyAsync(
        string keyListName,
        Func<string, ProfileInstance?> getInstance)
    {
        var usedKeys = await GetUsedKeyNamesAsync(keyListName, getInstance);
        return await _keyListRepository.GetNextAvailableKeyAsync(keyListName, usedKeys);
    }

    public async Task<bool> RotateKeyAsync(
        string profileName,
        ProfileInstance instance,
        Func<string, ProfileInstance?> getInstance)
    {
        var profile = await _profileRepository.GetByKeyAsync(profileName);
        if (profile == null || string.IsNullOrEmpty(profile.KeyList))
        {
            return false;
        }

        // Clear current key first (frees it in runtime state)
        instance.KeyName = null;

        // Get next available key
        var key = await AcquireKeyAsync(profile.KeyList, getInstance);
        if (key == null)
        {
            return false;
        }

        instance.KeyName = key.Name;
        return true;
    }

    public void ReleaseKey(ProfileInstance instance)
    {
        instance.KeyName = null;
    }
}
