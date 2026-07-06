using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using D2BotNG.Core.Protos;
using D2BotNG.Legacy.Models;
using D2BotNG.Services;
using D2BotNG.Utilities;
using Google.Protobuf.WellKnownTypes;
using JetBrains.Annotations;

namespace D2BotNG.Data;

/// <summary>
/// In-memory repository for character entities and their items. Aggregates item logs
/// from every framework's kolbot/mules directory, loading all on startup and watching
/// each for changes. Call <see cref="RefreshAsync"/> when the set of frameworks changes.
/// </summary>
public class ItemRepository : IDisposable
{
    private readonly FrameworkRepository _frameworkRepository;
    private readonly ILogger<ItemRepository> _logger;
    private readonly EventBroadcaster _eventBroadcaster;
    private readonly ConcurrentDictionary<string, CharacterEntity> _entities = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly List<FileSystemWatcher> _watchers = [];
    private volatile string[] _mulesDirs = [];
    private bool _disposed;

    public ItemRepository(FrameworkRepository frameworkRepository, ILogger<ItemRepository> logger, EventBroadcaster eventBroadcaster)
    {
        _frameworkRepository = frameworkRepository;
        _logger = logger;
        _eventBroadcaster = eventBroadcaster;
    }

    /// <summary>
    /// Initialize the repository by loading all entities and starting file watchers.
    /// </summary>
    public Task InitializeAsync() => RefreshAsync();

    /// <summary>
    /// Recompute the set of mules directories from the current frameworks, reload all
    /// entities, and (re)start the file watchers. Notifies clients to refresh.
    /// </summary>
    public async Task RefreshAsync()
    {
        var dirs = await ComputeMulesDirsAsync();

        await _loadLock.WaitAsync();
        try
        {
            _mulesDirs = dirs;
            await LoadAllEntitiesLockedAsync();
            StartWatchers(dirs);
        }
        finally
        {
            _loadLock.Release();
        }

        NotifyEntitiesChanged();
    }

    private async Task<string[]> ComputeMulesDirsAsync()
    {
        var frameworks = await _frameworkRepository.GetAllAsync();
        return frameworks
            .Where(f => !string.IsNullOrWhiteSpace(f.D2BsPath))
            .Select(f => f.MulesDirectory())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Get all entities, optionally filtered by path prefix.
    /// </summary>
    /// <summary>Whether <paramref name="path"/> is <paramref name="prefix"/> itself or lies under it.</summary>
    private static bool MatchesPrefix(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || (path.Length > prefix.Length
            && path[prefix.Length] == '/'
            && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<Entity> GetEntities(string? pathPrefix = null)
    {
        var result = new List<Entity>();
        var directories = new HashSet<string>();

        // Anchor the prefix at a path-component boundary: "useast" must match
        // "useast/acct" but not the sibling directory "useast2/acct".
        var prefix = string.IsNullOrEmpty(pathPrefix) ? null : pathPrefix.TrimEnd('/');

        foreach (var kvp in _entities)
        {
            var path = kvp.Key;
            var entity = kvp.Value;

            // Filter by prefix if specified
            if (prefix != null && !MatchesPrefix(path, prefix))
            {
                continue;
            }

            // Add directory entries for path components
            var parts = path.Split('/');
            var currentPath = "";
            for (int i = 0; i < parts.Length - 1; i++)
            {
                currentPath = i == 0 ? parts[i] : $"{currentPath}/{parts[i]}";

                if (prefix != null && !MatchesPrefix(currentPath, prefix))
                {
                    continue;
                }

                if (directories.Add(currentPath))
                {
                    result.Add(new Entity
                    {
                        Path = currentPath,
                        DisplayName = parts[i],
                        IsLeaf = false
                    });
                }
            }

            // Add the leaf entity
            result.Add(new Entity
            {
                Path = path,
                DisplayName = entity.DisplayName,
                IsLeaf = true,
                Mode = entity.Mode
            });
        }

        return result.OrderBy(e => e.Path).ToList();
    }

    public record PagedSearchResult(IReadOnlyList<ItemSearchResult> Results, int Total);

    /// <summary>
    /// Search items with pagination. Total reflects the full match count
    /// before applying offset/limit.
    /// </summary>
    public PagedSearchResult SearchPaged(
        string? entityPath,
        string? query,
        ModeFilter? modeFilter,
        int offset,
        int limit)
    {
        var clampedOffset = Math.Max(0, offset);
        var clampedLimit = Math.Max(0, limit);
        var all = SearchWithContext(entityPath, query, modeFilter);
        var total = all.Count;
        var skipped = clampedOffset > 0 ? all.Skip(clampedOffset) : all;
        var taken = clampedLimit > 0 ? skipped.Take(clampedLimit) : skipped;
        return new PagedSearchResult(taken.ToList(), total);
    }

    /// <summary>
    /// Search items with entity context (account, character, mode).
    /// </summary>
    public IReadOnlyList<ItemSearchResult> SearchWithContext(
        string? entityPath,
        string? query,
        ModeFilter? modeFilter)
    {
        var results = new List<ItemSearchResult>();
        Regex? queryRegex = null;

        if (!string.IsNullOrWhiteSpace(query))
        {
            try
            {
                queryRegex = new Regex(query, RegexOptions.IgnoreCase);
            }
            catch (RegexParseException)
            {
                // Invalid regex, treat as literal string
                queryRegex = new Regex(Regex.Escape(query), RegexOptions.IgnoreCase);
            }
        }

        // Iterate in path order: ConcurrentDictionary enumeration is not
        // order-stable, and pagination requires reproducible ordering so that
        // sort ties resolve to the same items across page fetches.
        foreach (var (path, entity) in _entities.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            // Filter by entity path prefix
            if (!string.IsNullOrEmpty(entityPath) && !path.StartsWith(entityPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Filter by game mode
            if (modeFilter != null)
            {
                if (modeFilter.HasHardcore && entity.Mode.Hardcore != modeFilter.Hardcore)
                    continue;
                if (modeFilter.HasExpansion && entity.Mode.Expansion != modeFilter.Expansion)
                    continue;
                if (modeFilter.HasLadder && entity.Mode.Ladder != modeFilter.Ladder)
                    continue;
            }

            // Extract account from path (second component: realm/account/character)
            var parts = path.Split('/');
            var account = parts.Length > 1 ? parts[1] : "";

            // Add matching items
            foreach (var item in entity.Items)
            {
                if (queryRegex == null || MatchesQuery(item, queryRegex))
                {
                    results.Add(new ItemSearchResult(item, path, account, entity.DisplayName, entity.Mode));
                }
            }
        }

        return results
            .OrderBy(r => r.Item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public record ItemSearchResult(Item Item, string EntityPath, string Account, string Character, [UsedImplicitly] EntityMode Mode);

    private static bool MatchesQuery(Item item, Regex query)
    {
        return query.IsMatch(item.Name) ||
               query.IsMatch(item.Description) ||
               query.IsMatch(item.Code) ||
               query.IsMatch(item.Header);
    }

    /// <summary>
    /// Remove a single item line from the mule .txt file backing the given entity.
    /// Searches every framework's mules directory for the entity's file. Matches the
    /// first line whose parsed item's full post-$ identifier starts with
    /// <paramref name="descriptionId"/>. The post-$ chunk encodes
    /// gid:classid:loc:x:y:base64info and is per-game-session unique, so prefix
    /// equality is at least as strict as the legacy bot's substring match.
    /// </summary>
    /// <returns>true if a matching line was removed and the file rewritten; false if no match.</returns>
    public async Task<bool> RemoveItemAsync(string entityPath, string descriptionId)
    {
        // Reject obvious path-traversal segments and Windows alternate-stream
        // syntax. The full-path containment check below catches everything else
        // (rooted paths, UNC, drive-relative paths) — Path.Combine silently
        // discards the first argument when the second is rooted.
        var segments = entityPath.Split('/', '\\');
        if (segments.Any(s => s == ".." || s.Contains(':')))
        {
            throw new ArgumentException("entityPath must be a relative path under a mules directory", nameof(entityPath));
        }

        var platformRelative = entityPath.Replace('/', Path.DirectorySeparatorChar);
        var sep = Path.DirectorySeparatorChar;

        // Locate the backing file across all framework mules directories. When the same
        // relative path exists under several frameworks, the LAST match wins — mirroring
        // load order (LoadAllEntitiesLockedAsync lets a later directory overwrite the
        // entity), so we delete the file that's actually displayed. Defense in depth: skip
        // any directory the resolved path escapes.
        string? filePath = null;
        foreach (var dir in _mulesDirs)
        {
            var candidate = Path.Combine(dir, platformRelative + ".txt");
            var fullDir = Path.GetFullPath(dir);
            var fullTarget = Path.GetFullPath(candidate);
            var fullDirPrefix = fullDir.TrimEnd(sep) + sep;
            if (!fullTarget.StartsWith(fullDirPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                filePath = candidate;
            }
        }

        if (filePath == null)
        {
            return false;
        }

        await _loadLock.WaitAsync();
        try
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            var lines = await File.ReadAllLinesAsync(filePath);
            var kept = new List<string>(lines.Length);
            var matched = false;

            foreach (var line in lines)
            {
                if (matched || string.IsNullOrWhiteSpace(line))
                {
                    kept.Add(line);
                    continue;
                }

                LegacyItem? legacyItem = null;
                try
                {
                    legacyItem = JsonSerializer.Deserialize<LegacyItem>(line);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse item line in {FilePath} during RemoveItemAsync", filePath);
                }

                if (legacyItem != null)
                {
                    var parts = legacyItem.Description.Split('$', 2);
                    if (parts.Length > 1 && parts[1].StartsWith(descriptionId, StringComparison.Ordinal))
                    {
                        matched = true;
                        continue;
                    }
                }

                kept.Add(line);
            }

            if (!matched)
            {
                return false;
            }

            // Durable atomic write (flush to disk, then rename — see AtomicFile).
            // Use LF line endings to match what D2BS writes — mixing CRLF (Windows
            // default) with LF would make the file appear malformed if D2BS later
            // appends to it.
            await AtomicFile.WriteAllTextAsync(filePath, string.Join('\n', kept) + '\n');

            return true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task ReloadAllAsync()
    {
        await _loadLock.WaitAsync();
        try
        {
            await LoadAllEntitiesLockedAsync();
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task LoadAllEntitiesLockedAsync()
    {
        _entities.Clear();

        foreach (var mulesDir in _mulesDirs)
        {
            if (!Directory.Exists(mulesDir))
            {
                _logger.LogInformation("Mules directory does not exist: {MulesDir}", mulesDir);
                continue;
            }

            var files = Directory.GetFiles(mulesDir, "*.txt", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                await LoadEntityFromFileAsync(mulesDir, file);
            }
        }

        _logger.LogInformation(
            "Loaded {Count} entities with items from {DirCount} mules director(ies)",
            _entities.Count, _mulesDirs.Length);
    }

    private async Task LoadEntityFromFileAsync(string mulesDir, string filePath)
    {
        try
        {
            var relativePath = Path.GetRelativePath(mulesDir, filePath);
            // Convert to forward slashes and remove .txt extension
            var entityPath = relativePath
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');

            if (entityPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                entityPath = entityPath[..^4];
            }

            var (displayName, mode) = ParseFileName(Path.GetFileNameWithoutExtension(filePath));
            var items = await LoadItemsFromFileAsync(filePath);

            var entity = new CharacterEntity
            {
                DisplayName = displayName,
                Mode = mode,
                Items = items
            };

            _entities[entityPath] = entity;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load entity from {FilePath}", filePath);
        }
    }

    private async Task<List<Item>> LoadItemsFromFileAsync(string filePath)
    {
        var items = new List<Item>();
        var lines = await File.ReadAllLinesAsync(filePath);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var legacyItem = JsonSerializer.Deserialize<LegacyItem>(line);
                if (legacyItem != null)
                {
                    items.Add(legacyItem.ToModern());
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to load items from {FilePath}, line: {line}", filePath, line);
                // Skip malformed lines
            }
        }

        return items;
    }

    // Known class prefixes from SoloPlay (note: they include trailing dash)
    private static readonly string[] ClassPrefixes =
    [
        "amazon-", "sorceress-", "necromancer-", "paladin-",
        "barbarian-", "druid-", "assassin-"
    ];

    /// <summary>
    /// Parse filename like "Sorc.sel" into display name and mode.
    /// Supports formats:
    /// - Legacy: {charName}.{h|s}{e|c}{l|n} (e.g., "Sorc.sel")
    /// - SoloPlay: {class}-{profile}-{charName}.{hc|sc}{c?}{l|nl} (e.g., "sorceress--SCL-SOR-002AN-Anna.scl")
    /// </summary>
    private static (string DisplayName, EntityMode Mode) ParseFileName(string fileName)
    {
        var mode = new EntityMode();
        var displayName = fileName;

        // Check for suffix pattern after last dot
        var lastDot = fileName.LastIndexOf('.');
        if (lastDot <= 0 || lastDot >= fileName.Length - 1)
        {
            return (displayName, mode);
        }

        var suffix = fileName[(lastDot + 1)..].ToLowerInvariant();
        var namePart = fileName[..lastDot];

        // Detect format by checking if name part starts with a class prefix (SoloPlay)
        var lowerName = namePart.ToLowerInvariant();
        var isSoloPlay = ClassPrefixes.Any(prefix => lowerName.StartsWith(prefix));

        if (isSoloPlay)
        {
            // SoloPlay suffix: {hc|sc}{c?}{l|nl}
            if (suffix.StartsWith("hc") || suffix.StartsWith("sc"))
            {
                mode.Hardcore = suffix.StartsWith("hc");
                suffix = suffix[2..];

                // Parse classic (c) - if present, not expansion
                if (suffix.StartsWith('c'))
                {
                    mode.Expansion = false;
                    suffix = suffix[1..];
                }
                else
                {
                    mode.Expansion = true;
                }

                // Parse ladder (l vs nl)
                mode.Ladder = suffix == "l";
            }

            // Extract character name: it's after the last dash
            var lastDash = namePart.LastIndexOf('-');
            if (lastDash > 0)
            {
                displayName = namePart[(lastDash + 1)..];
            }
            else
            {
                displayName = namePart;
            }
        }
        else
        {
            // Legacy suffix: {h|s}{e|c}{l|n} (positional, exactly 3 chars)
            if (suffix.Length == 3)
            {
                mode.Hardcore = suffix[0] == 'h';
                mode.Expansion = suffix[1] == 'e';
                mode.Ladder = suffix[2] == 'l';
            }
            displayName = namePart;
        }

        return (displayName, mode);
    }

    private void StartWatchers(string[] dirs)
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir))
            {
                _logger.LogWarning("Cannot watch - mules directory does not exist: {MulesDir}", dir);
                continue;
            }

            var watcher = new FileSystemWatcher(dir)
            {
                Filter = "*.txt",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };

            watcher.Created += OnFileSystemChange;
            watcher.Changed += OnFileSystemChange;
            watcher.Deleted += OnFileSystemChange;
            watcher.Renamed += OnFileSystemChange;
            watcher.EnableRaisingEvents = true;

            _watchers.Add(watcher);
            _logger.LogInformation("Started file watcher on {MulesDir}", dir);
        }
    }

    private CancellationTokenSource? _reloadCts;
    private readonly Lock _reloadCtsLock = new();

    private async void OnFileSystemChange(object sender, FileSystemEventArgs e)
    {
        _logger.LogDebug("File system change detected: {ChangeType} {FullPath}", e.ChangeType, e.FullPath);

        // Cancel any pending reload and start a new debounce. The swap must be
        // locked: each watcher raises this handler on its own thread-pool thread,
        // and a Cancel/Dispose race would throw unhandled out of this async void.
        CancellationToken token;
        lock (_reloadCtsLock)
        {
            _reloadCts?.Cancel();
            _reloadCts?.Dispose();
            _reloadCts = new CancellationTokenSource();
            token = _reloadCts.Token;
        }

        try
        {
            // Debounce: wait for changes to settle
            await Task.Delay(200, token);

            _logger.LogInformation("Reloading all entities due to file system change");
            await ReloadAllAsync();

            NotifyEntitiesChanged();
        }
        catch (TaskCanceledException)
        {
            // Another change came in, this reload was superseded
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured while reloading entities");
        }
    }

    private void NotifyEntitiesChanged()
    {
        _eventBroadcaster.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            EntitiesChanged = new EntitiesChanged()
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_reloadCtsLock)
        {
            _reloadCts?.Dispose();
            _reloadCts = null;
        }
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
        _watchers.Clear();
        _loadLock.Dispose();
    }

    /// <summary>
    /// Internal entity representation.
    /// </summary>
    private class CharacterEntity
    {
        public required string DisplayName { get; init; }
        public required EntityMode Mode { get; init; }
        public required List<Item> Items { get; init; }
    }

}
