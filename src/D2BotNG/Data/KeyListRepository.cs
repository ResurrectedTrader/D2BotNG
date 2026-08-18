using System.Text.Json;
using D2BotNG.Core.Protos;
using D2BotNG.Engine.Handoff;

namespace D2BotNG.Data;

public class KeyListRepository : FileRepository<KeyList, KeyListCollection>, IHandoffParticipant
{
    private readonly Dictionary<string, int> _currentIndex = new();

    public KeyListRepository(Paths paths, DataWriteGate writeGate) : base(paths, writeGate, "keylists.json") { }

    protected override string GetKey(KeyList k) => k.Name;

    protected override IList<KeyList> GetItems(KeyListCollection list) => list.KeyLists;

    protected override KeyListCollection CreateList(IEnumerable<KeyList> items)
    {
        var list = new KeyListCollection();
        list.KeyLists.AddRange(items);
        return list;
    }

    public async Task<CDKey?> GetNextAvailableKeyAsync(string keyListName, IReadOnlySet<string> usedKeyNames)
    {
        var keyList = await GetByKeyAsync(keyListName);
        if (keyList == null)
            return null;

        var keys = keyList.Keys;
        if (keys.Count == 0) return null;

        await Lock.WaitAsync();
        try
        {
            _currentIndex.TryGetValue(keyListName, out var startIndex);

            for (int i = 0; i < keys.Count; i++)
            {
                var index = (startIndex + i) % keys.Count;
                var key = keys[index];

                if (!usedKeyNames.Contains(key.Name) && !key.Held)
                {
                    _currentIndex[keyListName] = (index + 1) % keys.Count;
                    return key;
                }
            }

            return null;
        }
        finally
        {
            Lock.Release();
        }
    }

    public async Task HoldKeyAsync(string keyListName, string keyName)
    {
        var all = await GetAllAsync();
        var keyList = all.FirstOrDefault(k => GetKey(k) == keyListName);
        if (keyList == null) return;

        var key = keyList.Keys.FirstOrDefault(k => k.Name == keyName);
        if (key != null)
        {
            key.Held = true;
        }
    }

    public string HandoffKey => "keyState";

    public async Task<object?> SnapshotAsync()
    {
        Dictionary<string, int> cursors;
        await Lock.WaitAsync();
        try
        {
            cursors = new Dictionary<string, int>(_currentIndex);
        }
        finally
        {
            Lock.Release();
        }

        var held = new List<HeldKeyDto>();
        var all = await GetAllAsync();
        foreach (var list in all)
        {
            foreach (var key in list.Keys)
            {
                if (key.Held) held.Add(new HeldKeyDto { KeyList = list.Name, KeyName = key.Name });
            }
        }

        return new KeyStateDto { RotationCursors = cursors, HeldKeys = held };
    }

    public async Task RestoreAsync(JsonElement payload, JsonSerializerOptions options)
    {
        var dto = payload.Deserialize<KeyStateDto>(options);
        if (dto == null) return;

        await Lock.WaitAsync();
        try
        {
            foreach (var (k, v) in dto.RotationCursors)
                _currentIndex[k] = v;
        }
        finally
        {
            Lock.Release();
        }

        var all = await GetAllAsync();
        foreach (var held in dto.HeldKeys)
        {
            var list = all.FirstOrDefault(k => GetKey(k) == held.KeyList);
            var key = list?.Keys.FirstOrDefault(k => k.Name == held.KeyName);
            key?.Held = true;
        }
    }
}
