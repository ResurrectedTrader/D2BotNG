using D2BotNG.Core.Protos;

namespace D2BotNG.Data;

public class KeyListRepository : FileRepository<KeyList, KeyListCollection>
{
    private readonly Dictionary<string, int> _currentIndex = new();

    public KeyListRepository(Paths paths) : base(paths, "keylists.json") { }

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
}
