using D2BotNG.Logging;
using D2BotNG.Utilities;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using ILogger = Serilog.ILogger;

namespace D2BotNG.Data;

/// <summary>
/// Base class for protobuf JSON file-backed repositories.
/// Stores data as a single JSON document using protobuf's JsonFormatter/JsonParser.
/// </summary>
/// <typeparam name="TItem">The protobuf message type for individual entities</typeparam>
/// <typeparam name="TList">The protobuf container message type (e.g., ProfileCollection)</typeparam>
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
    protected ILogger Logger => _logger ??= TrackingLoggerFactory.ForContext(GetType());

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
            var migrated = await LoadAsync();
            _loaded = true;
            // Persist the upgraded shape immediately so the migration is one-time
            // (and, e.g., plaintext credentials leave profiles.json) rather than
            // re-running on every load until some other write happens to save.
            if (migrated) await PersistMigrationAsync();
        }
        finally
        {
            Lock.Release();
        }
    }

    private Task<bool> LoadAsync() => LoadFileIntoAsync(_data);

    private async Task PersistMigrationAsync()
    {
        try
        {
            await SaveAsync();
        }
        catch (Exception ex)
        {
            // A migration that can't persist must not break loading — it stays in memory
            // (correct) and re-runs idempotently next time.
            Logger.Warning(ex, "Could not persist migration of {FilePath}; will retry", FilePath);
        }
    }

    /// <summary>
    /// The schema version this repository writes and migrates up to. 0 = unversioned
    /// (no migration). Override, along with <see cref="MigrateAsync"/>, to opt in —
    /// the version is stamped centrally, so there is nothing else to wire up.
    /// </summary>
    protected virtual int SchemaVersion => 0;

    private const string SchemaVersionFieldName = "schema_version";

    /// <summary>
    /// The container's schema_version field, resolved once per closed generic type.
    /// Every storage container declares it (asserted by a test) so any repository can
    /// opt into versioning without a proto change.
    /// </summary>
    private static readonly FieldDescriptor? SchemaVersionField =
        new TList().Descriptor.FindFieldByName(SchemaVersionFieldName);

    /// <summary>Stamps the container's version before it is saved.</summary>
    private static void StampSchemaVersion(TList list, int version)
    {
        if (SchemaVersionField == null)
        {
            throw new InvalidOperationException(
                $"{typeof(TList).Name} declares no '{SchemaVersionFieldName}' field, so "
                + $"{typeof(TItem).Name} cannot be versioned. Add it to the container message.");
        }

        SchemaVersionField.Accessor.SetValue(list, version);
    }

    /// <summary>
    /// Upgrades a file at <paramref name="fromVersion"/> (&lt; <see cref="SchemaVersion"/>)
    /// to the current shape, given its raw JSON. Only called for versioned repositories.
    /// </summary>
    protected virtual Task<TList> MigrateAsync(string rawJson, int fromVersion) =>
        throw new NotSupportedException($"{GetType().Name} declares a schema version but no migration");

    /// <summary>
    /// Reads and parses the backing file into <paramref name="target"/>, running the
    /// versioned migration first when the file is behind. If the file is unreadable
    /// (corrupt / truncated / zero-filled — see <see cref="AtomicFile"/>), it is
    /// quarantined to a ".corrupt" sibling and the repository starts empty rather than
    /// throwing forever: an unparseable file would otherwise wedge <c>EnsureLoadedAsync</c>
    /// on every read and every save. The next save writes a fresh (upgraded) file.
    /// </summary>
    /// <returns>Whether a versioned migration ran (so the caller can persist the upgrade).</returns>
    private async Task<bool> LoadFileIntoAsync(List<TItem> target)
    {
        if (!File.Exists(FilePath)) return false;

        var json = await File.ReadAllTextAsync(FilePath);
        try
        {
            var fileVersion = SchemaVersion > 0 ? PeekSchemaVersion(json) : 0;
            var migrated = fileVersion < SchemaVersion;
            if (migrated)
            {
                await BackupBeforeMigrationAsync(json, fileVersion);
            }

            var list = migrated
                ? await MigrateAsync(json, fileVersion)
                : ProtobufJsonConfig.Parser.Parse<TList>(json);
            target.AddRange(GetItems(list));

            if (migrated)
            {
                Logger.Information("Migrated {FilePath} from schema v{From} to v{To}",
                    FilePath, fileVersion, SchemaVersion);
            }

            return migrated;
        }
        catch (InvalidProtocolBufferException ex)
        {
            // InvalidJsonException derives from InvalidProtocolBufferException, so this
            // catches both malformed-JSON and schema-mismatch failures.
            QuarantineCorruptFile(ex);
            return false;
        }
    }

    /// <summary>
    /// Preserves the file as it was before a schema migration rewrites it in place.
    /// A migration is not reversible — profiles.json v0 holds the only copy of the inline
    /// credentials that v1 moves into accounts.json, and an older build cannot read the
    /// upgraded shape — so the original is kept as a sibling ".v{n}.bak".
    ///
    /// Never overwritten: a migration re-runs when its post-migration save fails, and the
    /// second pass must not replace the pre-migration copy with an already-migrated one.
    ///
    /// Nothing reads these files; they exist for a human to restore from, and are never
    /// cleaned up automatically — the failure they insure against (wrong credentials) may
    /// only surface days later on a failed login, long after any sensible expiry.
    /// Best effort: a directory we cannot write a backup into is one we cannot persist the
    /// migration into either, so a failure here is logged rather than blocking the load.
    /// </summary>
    private async Task BackupBeforeMigrationAsync(string originalJson, int fromVersion)
    {
        var backupPath = $"{FilePath}.v{fromVersion}.bak";
        try
        {
            if (File.Exists(backupPath)) return;

            await AtomicFile.WriteAllTextAsync(backupPath, originalJson);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex,
                "Could not back up {FilePath} before migrating it to schema v{To}; continuing with the migration",
                FilePath, SchemaVersion);
        }
    }

    /// <summary>Reads just the schema_version property from raw JSON (0 when absent).</summary>
    private static int PeekSchemaVersion(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            // The formatter emits camelCase ("schemaVersion"); also accept the proto snake_case
            // so a hand-edited file isn't misread as v0 and re-migrated on every load.
            var root = doc.RootElement;
            var found = root.TryGetProperty("schemaVersion", out var v)
                        || root.TryGetProperty("schema_version", out v);
            return found && v.TryGetInt32(out var n) ? n : 0;
        }
        catch
        {
            return 0;
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
        if (SchemaVersion > 0) StampSchemaVersion(list, SchemaVersion);
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
            var migrated = await LoadFileIntoAsync(tempData);

            _data.Clear();
            _data.AddRange(tempData);
            _loaded = true;
            if (migrated) await PersistMigrationAsync();
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
