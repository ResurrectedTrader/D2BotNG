namespace D2BotNG.Utilities;

/// <summary>
/// Watches directories and reports a change only once it has settled. One save is several
/// events — <see cref="AtomicFile"/> writes a temp and swaps it in, editors truncate and
/// rewrite — so a handler run per event would read half-written files and do the same work
/// repeatedly. Events are coalesced per key: <c>keyOf</c> maps a changed path to one (null
/// ignores the path), and the handler runs once per key after <c>delay</c> of quiet.
/// </summary>
public sealed class DebouncedWatcher : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Func<string, string?> _keyOf;
    private readonly Func<string, Task> _onSettled;
    private readonly ILogger _logger;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Dictionary<string, CancellationTokenSource> _pending = new();
    private readonly Lock _lock = new();

    public DebouncedWatcher(TimeSpan delay, Func<string, string?> keyOf, Func<string, Task> onSettled, ILogger logger)
    {
        _delay = delay;
        _keyOf = keyOf;
        _onSettled = onSettled;
        _logger = logger;
    }

    /// <summary>Replaces whatever was watched before with <paramref name="directories"/>.</summary>
    public void Watch(IEnumerable<string> directories, string filter, bool includeSubdirectories = false)
    {
        lock (_lock)
        {
            StopLocked();
            foreach (var dir in directories)
            {
                if (!Directory.Exists(dir))
                {
                    _logger.LogWarning("Cannot watch {Directory}: it does not exist", dir);
                    continue;
                }

                var watcher = new FileSystemWatcher(dir, filter)
                {
                    IncludeSubdirectories = includeSubdirectories,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                };
                watcher.Created += OnEvent;
                watcher.Changed += OnEvent;
                watcher.Deleted += OnEvent;
                watcher.Renamed += OnEvent;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
                _logger.LogInformation("Watching {Directory} for changes to {Filter}", dir, filter);
            }
        }
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        var key = _keyOf(e.FullPath);
        if (key == null) return;

        CancellationToken token;
        lock (_lock)
        {
            if (_pending.Remove(key, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }

            var cts = new CancellationTokenSource();
            _pending[key] = cts;
            token = cts.Token;
        }

        _ = SettleAsync(key, token);
    }

    private async Task SettleAsync(string key, CancellationToken token)
    {
        try
        {
            await Task.Delay(_delay, token);
        }
        catch (OperationCanceledException)
        {
            return; // superseded by a later event for the same key
        }

        lock (_lock)
        {
            // A later event may have replaced us between the delay ending and the lock.
            if (!_pending.TryGetValue(key, out var current) || current.Token != token) return;
            _pending.Remove(key);
            current.Dispose();
        }

        try
        {
            await _onSettled(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handling a change to {Key} failed", key);
        }
    }

    private void StopLocked()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        foreach (var cts in _pending.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _pending.Clear();
    }

    public void Dispose()
    {
        lock (_lock) StopLocked();
    }
}
