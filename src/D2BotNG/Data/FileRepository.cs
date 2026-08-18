using D2BotNG.Logging;
using D2BotNG.Utilities;
using Google.Protobuf;
using ILogger = Serilog.ILogger;

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
    private readonly List<TItem> _data = [];
    private volatile bool _loaded;

    private readonly Paths _paths;
    private readonly DataWriteGate _writeGate;
    private ILogger? _logger;
    private ILogger Logger => _logger ??= TrackingLoggerFactory.ForContext(GetType());

    protected FileRepository(Paths paths, DataWriteGate writeGate, string fileName)
    {
        _paths = paths;
        _writeGate = writeGate;
        FilePath = fileName;
    }

    /// <summary>
    /// Whether this process still owns the data directory. False once a handoff has
    /// given it to a successor — see <see cref="DataWriteGate"/>. Overrides of
    /// <see cref="SaveAsync"/> that write files of their own must check this too.
    /// </summary>
    protected bool CanWrite => !_writeGate.IsClosed;

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

    /// <summary>
    /// The live backing list. Only valid while <see cref="Lock"/> is held — which
    /// includes <see cref="SaveAsync"/> overrides, since every save runs under the lock.
    /// Callers outside the lock must use <see cref="GetAllAsync"/> instead.
    /// </summary>
    protected IReadOnlyList<TItem> Items => _data;

    protected async Task EnsureLoadedAsync()
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

    protected Task LoadAsync() => LoadFileIntoAsync(_data);

    /// <summary>
    /// Reads and parses the backing file into <paramref name="target"/>. If the file is
    /// unreadable (corrupt / truncated / zero-filled — see <see cref="AtomicFile"/>), it is
    /// quarantined to a ".corrupt" sibling and the repository starts empty rather than
    /// throwing forever: an unparseable file would otherwise wedge <c>EnsureLoadedAsync</c>
    /// on every read and every save, so the app could never overwrite it with good data.
    /// The next save writes a fresh file.
    /// </summary>
    private async Task LoadFileIntoAsync(List<TItem> target)
    {
        if (!File.Exists(FilePath)) return;

        var json = await File.ReadAllTextAsync(FilePath);
        try
        {
            var list = ProtobufJsonConfig.Parser.Parse<TList>(json);
            target.AddRange(GetItems(list));
        }
        catch (InvalidProtocolBufferException ex)
        {
            // InvalidJsonException derives from InvalidProtocolBufferException, so this
            // catches both malformed-JSON and schema-mismatch failures.
            QuarantineCorruptFile(ex);
        }
    }

    private void QuarantineCorruptFile(Exception ex)
    {
        var quarantinePath = FilePath + ".corrupt";
        try
        {
            File.Move(FilePath, quarantinePath, overwrite: true);
            Logger.Warning(ex,
                "Data file {FilePath} is unreadable; quarantined to {QuarantinePath} and starting empty. It will be rewritten on the next save",
                FilePath, quarantinePath);
        }
        catch (Exception moveEx)
        {
            Logger.Error(moveEx,
                "Data file {FilePath} is unreadable and could not be quarantined; starting empty",
                FilePath);
        }
    }

    protected virtual async Task SaveAsync()
    {
        if (!CanWrite)
        {
            Logger.Debug("Handoff in progress; not writing {FilePath} — the successor owns it now", FilePath);
            return;
        }

        var directory = Path.GetDirectoryName(FilePath);
        if (directory != null)
            Directory.CreateDirectory(directory);

        var list = CreateList(_data);
        var json = ProtobufJsonConfig.Formatter.Format(list);

        await AtomicFile.WriteAllTextAsync(FilePath, json);
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
            await LoadFileIntoAsync(tempData);

            _data.Clear();
            _data.AddRange(tempData);
            _loaded = true;
        }
        finally
        {
            Lock.Release();
        }
    }

    // Reads take the lock: an unlocked enumeration racing a structural write
    // would throw InvalidOperationException.

    public async Task<IReadOnlyList<TItem>> GetAllAsync()
    {
        await EnsureLoadedAsync();

        await Lock.WaitAsync();
        try
        {
            return _data.ToList();
        }
        finally
        {
            Lock.Release();
        }
    }

    public async Task<TItem?> GetByKeyAsync(string key)
    {
        await EnsureLoadedAsync();

        await Lock.WaitAsync();
        try
        {
            return _data.FirstOrDefault(e => GetKey(e) == key);
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>
    /// Atomically reads, mutates, and (when <paramref name="mutate"/> returns true)
    /// saves the backing list under the repository lock — unlike a GetAll → modify →
    /// ReplaceAll sequence, this cannot clobber concurrent writes.
    /// </summary>
    /// <returns>Whether the mutation reported a change (and the list was saved).</returns>
    public async Task<bool> MutateAllAsync(Func<List<TItem>, bool> mutate)
    {
        await EnsureLoadedAsync();

        await Lock.WaitAsync();
        try
        {
            if (!mutate(_data))
                return false;

            await SaveAsync();
            return true;
        }
        finally
        {
            Lock.Release();
        }
    }

    public async Task<TItem> CreateAsync(TItem entity)
    {
        await EnsureLoadedAsync();

        await Lock.WaitAsync();
        try
        {
            var key = GetKey(entity);
            var index = _data.FindIndex(e => GetKey(e) == key);
            if (index >= 0)
                throw new InvalidOperationException($"{typeof(TItem).Name} '{key}' already exists");

            _data.Add(entity);
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
            var index = _data.FindIndex(e => GetKey(e) == key);
            if (index < 0)
                throw new KeyNotFoundException($"{typeof(TItem).Name} '{key}' not found");

            _data[index] = entity;
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
            var index = _data.FindIndex(e => GetKey(e) == key);
            if (index >= 0)
                _data.RemoveAt(index);
            await SaveAsync();
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>
    /// Moves multiple items to a target index, preserving their relative order.
    /// The target index is relative to the list with the moved items removed.
    /// </summary>
    public async Task MoveMultipleToIndexAsync(IReadOnlyList<string> keys, int newIndex)
    {
        await EnsureLoadedAsync();

        await Lock.WaitAsync();
        try
        {
            var keySet = new HashSet<string>(keys);
            var itemsToMove = new List<TItem>();
            var remaining = new List<TItem>();

            foreach (var item in _data)
            {
                if (keySet.Contains(GetKey(item)))
                    itemsToMove.Add(item);
                else
                    remaining.Add(item);
            }

            if (itemsToMove.Count != keys.Count)
            {
                var missing = keys.Where(k => !itemsToMove.Any(i => GetKey(i) == k));
                throw new KeyNotFoundException(
                    $"{typeof(TItem).Name}(s) not found: {string.Join(", ", missing)}");
            }

            newIndex = Math.Clamp(newIndex, 0, remaining.Count);

            remaining.InsertRange(newIndex, itemsToMove);

            _data.Clear();
            _data.AddRange(remaining);
            await SaveAsync();
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>
    /// Replaces the entire contents with the given items and saves. Used for bulk writes
    /// (e.g. persisting an in-memory live-state set on a debounce).
    /// </summary>
    public async Task ReplaceAllAsync(IEnumerable<TItem> items)
    {
        await EnsureLoadedAsync();

        await Lock.WaitAsync();
        try
        {
            _data.Clear();
            _data.AddRange(items);
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
