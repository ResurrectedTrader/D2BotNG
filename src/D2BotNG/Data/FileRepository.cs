using D2BotNG.Utilities;
using Google.Protobuf;

namespace D2BotNG.Data;

/// <summary>
/// Base class for protobuf JSON file-backed repositories.
/// Stores data as a single JSON document using protobuf's JsonFormatter/JsonParser.
/// </summary>
/// <typeparam name="TItem">The protobuf message type for individual entities</typeparam>
/// <typeparam name="TList">The protobuf list-wrapper message type (e.g., ProfileList)</typeparam>
public abstract class FileRepository<TItem, TList> : IDisposable
    where TItem : IMessage<TItem>
    where TList : IMessage<TList>, new()
{
    protected readonly SemaphoreSlim Lock = new(1, 1);
    protected readonly List<TItem> Data = [];
    private volatile bool _loaded;

    private readonly Paths _paths;

    protected FileRepository(Paths paths, string fileName)
    {
        _paths = paths;
        FilePath = fileName;
    }

    private string FilePath => Path.Combine(_paths.DataDirectory, field);

    /// <summary>
    /// Returns the unique key for the given entity.
    /// </summary>
    protected abstract string GetKey(TItem entity);

    /// <summary>
    /// Extracts the repeated item list from the list-wrapper message.
    /// </summary>
    protected abstract IList<TItem> GetItems(TList list);

    /// <summary>
    /// Creates a new list-wrapper message and populates it with the given items.
    /// </summary>
    protected abstract TList CreateList(IEnumerable<TItem> items);

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        await Lock.WaitAsync();
        try
        {
            if (_loaded) return;
            await LoadAsync();
            _loaded = true;
        }
        finally
        {
            Lock.Release();
        }
    }

    protected async Task LoadAsync()
    {
        if (!File.Exists(FilePath)) return;

        var json = await File.ReadAllTextAsync(FilePath);
        var list = ProtobufJsonConfig.Parser.Parse<TList>(json);
        Data.AddRange(GetItems(list));
    }

    protected virtual async Task SaveAsync()
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (directory != null)
            Directory.CreateDirectory(directory);

        var list = CreateList(Data);
        var json = ProtobufJsonConfig.Formatter.Format(list);

        var tempPath = FilePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    /// <summary>
    /// Forces a reload from disk. Used after migration or path change.
    /// </summary>
    public async Task ReloadAsync()
    {
        await Lock.WaitAsync();
        try
        {
            var tempData = new List<TItem>();
            if (File.Exists(FilePath))
            {
                var json = await File.ReadAllTextAsync(FilePath);
                var list = ProtobufJsonConfig.Parser.Parse<TList>(json);
                tempData.AddRange(GetItems(list));
            }

            Data.Clear();
            Data.AddRange(tempData);
            _loaded = true;
        }
        finally
        {
            Lock.Release();
        }
    }

    public async Task<IReadOnlyList<TItem>> GetAllAsync()
    {
        await EnsureLoadedAsync();
        return Data.ToList();
    }

    public async Task<TItem?> GetByKeyAsync(string key)
    {
        await EnsureLoadedAsync();
        return Data.FirstOrDefault(e => GetKey(e) == key);
    }

    public async Task<TItem> CreateAsync(TItem entity)
    {
        await EnsureLoadedAsync();

        await Lock.WaitAsync();
        try
        {
            var key = GetKey(entity);
            var index = Data.FindIndex(e => GetKey(e) == key);
            if (index >= 0)
                throw new InvalidOperationException($"{typeof(TItem).Name} '{key}' already exists");

            Data.Add(entity);
            await SaveAsync();
            return entity;
        }
        finally
        {
            Lock.Release();
        }
    }

    public async Task<TItem> UpdateAsync(TItem entity)
    {
        await EnsureLoadedAsync();

        var key = GetKey(entity);
        await Lock.WaitAsync();
        try
        {
            var index = Data.FindIndex(e => GetKey(e) == key);
            if (index < 0)
                throw new KeyNotFoundException($"{typeof(TItem).Name} '{key}' not found");

            Data[index] = entity;
            await SaveAsync();
            return entity;
        }
        finally
        {
            Lock.Release();
        }
    }

    public async Task DeleteAsync(string key)
    {
        await EnsureLoadedAsync();

        await Lock.WaitAsync();
        try
        {
            var index = Data.FindIndex(e => GetKey(e) == key);
            if (index >= 0)
                Data.RemoveAt(index);
            await SaveAsync();
        }
        finally
        {
            Lock.Release();
        }
    }

    public async Task MoveToIndexAsync(string key, int newIndex)
    {
        await EnsureLoadedAsync();

        await Lock.WaitAsync();
        try
        {
            var currentIndex = Data.FindIndex(e => GetKey(e) == key);
            if (currentIndex < 0)
                throw new KeyNotFoundException($"{typeof(TItem).Name} '{key}' not found");

            if (newIndex < 0 || newIndex >= Data.Count)
                throw new ArgumentOutOfRangeException(nameof(newIndex), $"Index must be between 0 and {Data.Count - 1}");

            var entity = Data[currentIndex];
            Data.RemoveAt(currentIndex);
            Data.Insert(newIndex, entity);
            await SaveAsync();
        }
        finally
        {
            Lock.Release();
        }
    }

    public virtual void Dispose()
    {
        Lock.Dispose();
        GC.SuppressFinalize(this);
    }
}
