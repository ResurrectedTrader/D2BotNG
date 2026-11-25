using D2BotNG.Capture;
using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Engine;
using D2BotNG.Legacy.Models;
using D2BotNG.Utilities;
using Google.Protobuf.WellKnownTypes;

namespace D2BotNG.Services;

/// <summary>
/// Picks up edits made to the manager's files from outside it, and moves the manager to a new
/// data folder when <see cref="Settings.BasePath"/> changes — both are "these files changed under
/// us": reload the repositories, redo what depends on them, and push fresh snapshots to clients.
///
/// Watches the data directory's JSON files and d2botng.json. Not characters.json (the manager
/// rewrites it constantly from live game state, so an external edit would only be overwritten)
/// nor captures.db (SQLite, with its own locking). Our own saves raise events too; the
/// repositories skip those by comparing the file with what they last wrote.
///
/// The last writer wins: a save the manager makes between an external write and its reload
/// overwrites that write. Nothing is reloaded once a handoff has closed the
/// <see cref="DataWriteGate"/>, since the successor may be migrating the files to a schema
/// this build cannot read.
/// </summary>
public sealed class DataFileWatcher : IHostedService, IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(300);

    private readonly SettingsRepository _settings;
    private readonly Paths _paths;
    private readonly DataWriteGate _writeGate;
    private readonly ProfileRepository _profiles;
    private readonly FrameworkRepository _frameworks;
    private readonly ScheduleRepository _schedules;
    private readonly FrameworkBootstrap _frameworkBootstrap;
    private readonly ItemRepository _items;
    private readonly ProfileEngine _engine;
    private readonly CaptureStore _captures;
    private readonly EventBroadcaster _events;
    private readonly ILogger<DataFileWatcher> _logger;
    private readonly Dictionary<string, IReloadableRepository> _repositories;
    private readonly DebouncedWatcher _dataWatcher;
    private readonly DebouncedWatcher _settingsWatcher;
    private readonly SemaphoreSlim _applyLock = new(1, 1);
    private string? _dataDirectory;

    public DataFileWatcher(
        SettingsRepository settings,
        Paths paths,
        DataWriteGate writeGate,
        ProfileRepository profiles,
        KeyListRepository keyLists,
        ProxyRepository proxies,
        FrameworkRepository frameworks,
        ScheduleRepository schedules,
        PatchRepository patches,
        FrameworkBootstrap frameworkBootstrap,
        ItemRepository items,
        ProfileEngine engine,
        CaptureStore captures,
        EventBroadcaster events,
        ILogger<DataFileWatcher> logger)
    {
        _settings = settings;
        _paths = paths;
        _writeGate = writeGate;
        _profiles = profiles;
        _frameworks = frameworks;
        _schedules = schedules;
        _frameworkBootstrap = frameworkBootstrap;
        _items = items;
        _engine = engine;
        _captures = captures;
        _events = events;
        _logger = logger;

        // Profiles before frameworks: the bootstrap that follows a reload reads both.
        IReloadableRepository[] repositories = [profiles, keyLists, proxies, frameworks, schedules, patches];
        _repositories = repositories.ToDictionary(r => r.FileName, StringComparer.OrdinalIgnoreCase);

        _dataWatcher = new DebouncedWatcher(SettleDelay,
            path => _repositories.ContainsKey(Path.GetFileName(path)) ? Path.GetFileName(path) : null,
            OnDataFileChangedAsync, logger);
        _settingsWatcher = new DebouncedWatcher(SettleDelay,
            path => string.Equals(path, _settings.FilePath, StringComparison.OrdinalIgnoreCase) ? path : null,
            _ => OnSettingsFileChangedAsync(), logger);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _dataDirectory = _paths.DataDirectory;
        _dataWatcher.Watch([_dataDirectory], "*.json");
        _settingsWatcher.Watch([Path.GetDirectoryName(_settings.FilePath)!], Path.GetFileName(_settings.FilePath));
        _settings.SettingsChanged += OnSettingsChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _settings.SettingsChanged -= OnSettingsChanged;
        Dispose();
        return Task.CompletedTask;
    }

    private async Task OnDataFileChangedAsync(string fileName)
    {
        if (_writeGate.IsClosed) return;

        await _applyLock.WaitAsync();
        try
        {
            var repository = _repositories[fileName];
            if (!await repository.ReloadIfChangedAsync()) return;

            _logger.LogInformation("{FileName} changed on disk; reloaded it", fileName);
            await RefreshDependentsAsync(
                profilesChanged: repository == _profiles || repository == _frameworks,
                frameworksChanged: repository == _frameworks);
        }
        finally
        {
            _applyLock.Release();
        }
    }

    private async Task OnSettingsFileChangedAsync()
    {
        if (_writeGate.IsClosed) return;

        // Raises SettingsChanged, so a new base path relocates through OnSettingsChanged.
        if (!await _settings.ReloadIfChangedAsync()) return;

        _events.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Settings = await _settings.GetAsync()
        });
    }

    /// <summary>
    /// Paths subscribed to SettingsChanged before us (it is constructed first, as a dependency
    /// of every repository), so <see cref="Paths.DataDirectory"/> already names the new folder.
    /// </summary>
    private void OnSettingsChanged(object? sender, Settings settings)
    {
        var directory = _paths.DataDirectory;
        if (string.Equals(directory, _dataDirectory, StringComparison.OrdinalIgnoreCase)) return;

        _dataDirectory = directory;
        _ = Task.Run(() => RelocateAsync(directory, settings.BasePath));
    }

    private async Task RelocateAsync(string directory, string basePath)
    {
        await _applyLock.WaitAsync();
        try
        {
            _dataWatcher.Watch([directory], "*.json");
            Migration.MigrateIfNeeded(basePath);
            foreach (var repository in _repositories.Values)
            {
                await repository.ReloadAsync();
            }

            await RefreshDependentsAsync(profilesChanged: true, frameworksChanged: true, capturesMoved: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Moving to the data folder {Directory} failed", directory);
        }
        finally
        {
            _applyLock.Release();
        }
    }

    /// <summary>Redoes what depends on reloaded files, then pushes fresh snapshots to clients.</summary>
    private async Task RefreshDependentsAsync(bool profilesChanged, bool frameworksChanged, bool capturesMoved = false)
    {
        if (profilesChanged)
        {
            // A framework-less profile or an empty framework list is repaired exactly as at
            // startup, and every d2bs.ini reflects the profiles now on disk.
            await _frameworkBootstrap.EnsureDefaultAsync();
            await _engine.SyncProfilesAsync();
            await _profiles.RewriteInisAsync();
        }

        if (frameworksChanged)
        {
            await _items.RefreshAsync();
        }

        // Every snapshot, whichever file changed: each carries usage derived from the others
        // (a key list's or proxy's users are profiles), and external edits are rare.
        await _engine.BroadcastProfilesSnapshotAsync();
        await _engine.BroadcastKeyListsSnapshotAsync();
        await _engine.BroadcastProxiesSnapshotAsync();
        await _engine.BroadcastFrameworksSnapshotAsync();
        var schedules = new SchedulesSnapshot();
        schedules.Schedules.AddRange(await _schedules.GetAllAsync());
        _events.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            SchedulesSnapshot = schedules
        });

        if (capturesMoved)
        {
            // CaptureEngine reopened the database at the new folder; without a snapshot the
            // clients keep listing the old folder's characters, and picking one answers NotFound.
            var snapshot = new CapturesSnapshot();
            snapshot.Characters.AddRange(_captures.ListCharacters());
            _events.Broadcast(new Event
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                CapturesSnapshot = snapshot
            });
        }
    }

    public void Dispose()
    {
        _dataWatcher.Dispose();
        _settingsWatcher.Dispose();
    }
}
