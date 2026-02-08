using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using D2BotNG.Core.Protos;
using D2BotNG.Data.LegacyModels;
using D2BotNG.Services;
using Google.Protobuf.WellKnownTypes;

namespace D2BotNG.Data;

/// <summary>
/// In-memory repository for character entities and their items.
/// Loads all data on startup and watches for file changes.
/// </summary>
public class ItemRepository : IDisposable
{
    private readonly string _mulesDir;
    private readonly ILogger<ItemRepository> _logger;
    private readonly EventBroadcaster _eventBroadcaster;
    private readonly ConcurrentDictionary<string, CharacterEntity> _entities = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public ItemRepository(Paths paths, ILogger<ItemRepository> logger, EventBroadcaster eventBroadcaster)
    {
        _mulesDir = paths.MulesDirectory;
        _logger = logger;
        _eventBroadcaster = eventBroadcaster;
    }

    /// <summary>
    /// Initialize the repository by loading all entities and starting file watcher.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadAllEntitiesAsync();
        StartWatcher();
    }

    /// <summary>
    /// Get all entities, optionally filtered by path prefix.
    /// </summary>
    public IReadOnlyList<Entity> GetEntities(string? pathPrefix = null)
    {
        var result = new List<Entity>();
        var directories = new HashSet<string>();

        foreach (var kvp in _entities)
        {
            var path = kvp.Key;
            var entity = kvp.Value;

            // Filter by prefix if specified
            if (!string.IsNullOrEmpty(pathPrefix) && !path.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Add directory entries for path components
            var parts = path.Split('/');
            var currentPath = "";
            for (int i = 0; i < parts.Length - 1; i++)
            {
                currentPath = i == 0 ? parts[i] : $"{currentPath}/{parts[i]}";

                if (!string.IsNullOrEmpty(pathPrefix) && !currentPath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase))
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

    /// <summary>
    /// Search items within the specified entity path.
    /// </summary>
    public IReadOnlyList<Item> Search(
        string? entityPath,
        string? query,
        ModeFilter? modeFilter)
    {
        var allItems = new List<Item>();
        Regex? queryRegex = null;

        if (!string.IsNullOrWhiteSpace(query))
        {
            try
            {
                queryRegex = new Regex(query, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
            catch (RegexParseException)
            {
                // Invalid regex, treat as literal string
                queryRegex = new Regex(Regex.Escape(query), RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
        }

        foreach (var (path, entity) in _entities)
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

            // Add matching items
            foreach (var item in entity.Items)
            {
                if (queryRegex == null || MatchesQuery(item, queryRegex))
                {
                    allItems.Add(item);
                }
            }
        }

        return allItems
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool MatchesQuery(Item item, Regex query)
    {
        return query.IsMatch(item.Name) ||
               query.IsMatch(item.Description) ||
               query.IsMatch(item.Code) ||
               query.IsMatch(item.Header);
    }

    private async Task LoadAllEntitiesAsync()
    {
        await _loadLock.WaitAsync();
        try
        {
            _entities.Clear();

            if (!Directory.Exists(_mulesDir))
            {
                _logger.LogInformation("Mules directory does not exist: {MulesDir}", _mulesDir);
                return;
            }

            var files = Directory.GetFiles(_mulesDir, "*.txt", SearchOption.AllDirectories);
            _logger.LogInformation("Loading {Count} entity files from {MulesDir}", files.Length, _mulesDir);

            foreach (var file in files)
            {
                await LoadEntityFromFileAsync(file);
            }

            _logger.LogInformation("Loaded {Count} entities with items", _entities.Count);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task LoadEntityFromFileAsync(string filePath)
    {
        try
        {
            var relativePath = Path.GetRelativePath(_mulesDir, filePath);
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

    private void StartWatcher()
    {
        if (!Directory.Exists(_mulesDir))
        {
            _logger.LogWarning("Cannot start file watcher - mules directory does not exist: {MulesDir}", _mulesDir);
            return;
        }

        _watcher = new FileSystemWatcher(_mulesDir)
        {
            Filter = "*.txt",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };

        _watcher.Created += OnFileSystemChange;
        _watcher.Changed += OnFileSystemChange;
        _watcher.Deleted += OnFileSystemChange;
        _watcher.Renamed += OnFileSystemChange;
        _watcher.EnableRaisingEvents = true;

        _logger.LogInformation("Started file watcher on {MulesDir}", _mulesDir);
    }

    private CancellationTokenSource? _reloadCts;

    private async void OnFileSystemChange(object sender, FileSystemEventArgs e)
    {
        _logger.LogDebug("File system change detected: {ChangeType} {FullPath}", e.ChangeType, e.FullPath);

        // Cancel any pending reload and start a new debounce
        _reloadCts?.Cancel();
        _reloadCts = new CancellationTokenSource();
        var token = _reloadCts.Token;

        try
        {
            // Debounce: wait for changes to settle
            await Task.Delay(200, token);

            _logger.LogInformation("Reloading all entities due to file system change");
            await LoadAllEntitiesAsync();

            // Notify UI to refresh
            _eventBroadcaster.Broadcast(new Event
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                EntitiesChanged = new EntitiesChanged()
            });
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _watcher?.Dispose();
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
