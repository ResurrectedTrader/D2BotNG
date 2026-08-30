using System.Collections.Concurrent;
using System.Diagnostics;
using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Engine.Handoff;
using D2BotNG.Services;
using D2BotNG.Windows;
using Google.Protobuf.WellKnownTypes;

namespace D2BotNG.Engine;

/// <summary>
/// Main engine for managing profile lifecycles, key management, and snapshot broadcasting.
/// </summary>
public class ProfileEngine
{
    private readonly ILogger<ProfileEngine> _logger;
    private readonly ProfileRepository _profileRepository;
    private readonly KeyListRepository _keyListRepository;
    private readonly ProxyRepository _proxyRepository;
    private readonly FrameworkRepository _frameworkRepository;
    private readonly EventBroadcaster _eventBroadcaster;
    private readonly GameLauncher _gameLauncher;
    private readonly ProcessManager _processManager;
    private readonly MessageWindow _messageWindow;

    private readonly ConcurrentDictionary<string, ProfileInstance> _instances = new();
    private readonly ConcurrentDictionary<nint, string> _handleToProfile = new();

    // Startup pacing. Profiles entering RunProfileAsync wait their turn on
    // _startupSemaphore (if set), wait _startupDelayMs (with a 1Hz countdown),
    // then launch. Both are mutated from the SettingsChanged callback.
    private SemaphoreSlim? _startupSemaphore;
    private volatile int _startupDelayMs;

    /// <summary>
    /// Set when the engine is being torn down for handoff to a successor process.
    /// In this mode, <see cref="StopAllAsync"/> skips game termination so the children
    /// survive the predecessor's shutdown and stay assigned to the now-successor-owned job.
    /// </summary>
    private volatile bool _handoffInProgress;

    /// <summary>
    /// Cached from settings; passed to each launched game as -noanalytics. Volatile like
    /// <see cref="_startupDelayMs"/>: same pattern of a SettingsChanged write read by launch threads.
    /// </summary>
    private volatile bool _analyticsDisabled;

    public ProfileEngine(
        ILogger<ProfileEngine> logger,
        ProfileRepository profileRepository,
        KeyListRepository keyListRepository,
        ProxyRepository proxyRepository,
        FrameworkRepository frameworkRepository,
        EventBroadcaster eventBroadcaster,
        GameLauncher gameLauncher,
        ProcessManager processManager,
        MessageWindow messageWindow,
        SettingsRepository settingsRepository)
    {
        _logger = logger;
        _profileRepository = profileRepository;
        _keyListRepository = keyListRepository;
        _proxyRepository = proxyRepository;
        _frameworkRepository = frameworkRepository;
        _eventBroadcaster = eventBroadcaster;
        _gameLauncher = gameLauncher;
        _processManager = processManager;
        _messageWindow = messageWindow;

        ApplySettings(settingsRepository.Current);
        settingsRepository.SettingsChanged += (_, s) => ApplySettings(s);
    }

    private void ApplySettings(Settings settings)
    {
        var concurrency = Math.Max(0, settings.Startup?.Concurrency ?? 0);
        _startupDelayMs = Math.Max(0, settings.Startup?.DelayMs ?? 0);
        // Read here rather than at launch so a toggle applies to the next game started,
        // without the engine holding the settings repository.
        _analyticsDisabled = settings.AnalyticsDisabled;

        // Replace the semaphore; in-flight starts already hold a reference to the previous
        // instance and will release it correctly. New starts use the fresh one.
        _startupSemaphore = concurrency > 0 ? new SemaphoreSlim(concurrency, concurrency) : null;
    }

    private Task RunProfileBackgroundAsync(ProfileInstance instance)
    {
        return Task.Run(() => RunProfileAsync(instance)).ContinueWith(t =>
        {
            if (t.Exception != null)
            {
                _logger.LogError(t.Exception, "Unhandled error in RunProfileAsync for {ProfileName}", instance.ProfileName);
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public async Task InitializeAsync()
    {
        var profiles = await _profileRepository.GetAllAsync();
        foreach (var profile in profiles)
        {
            _instances.TryAdd(profile.Name, new ProfileInstance(profile.Name));
        }
        _logger.LogInformation("Loaded {Count} profiles", profiles.Count);
    }

    public ProfileInstance? GetInstance(string profileName)
    {
        return _instances.TryGetValue(profileName, out var instance) ? instance : null;
    }

    public ProfileInstance? GetInstanceByHandle(nint handle)
    {
        if (_handleToProfile.TryGetValue(handle, out var profileName))
        {
            return GetInstance(profileName);
        }
        return null;
    }

    /// <summary>
    /// Registers the game window a profile's D2BS will send WM_COPYDATA from, and remembers it
    /// on the instance so every later removal keys off the value we actually registered.
    /// </summary>
    private void RegisterHandle(ProfileInstance instance, nint handle)
    {
        if (handle == 0) return;

        if (_handleToProfile.TryGetValue(handle, out var existing) && existing != instance.ProfileName)
        {
            // USER handle values are recycled. If this ever fires, messages for one of the two
            // profiles were about to be routed to the other — which presents as a perfectly
            // healthy bot that never heartbeats.
            _logger.LogError(
                "Window handle {Handle} was still mapped to profile {Existing} while registering {New} — " +
                "stale routing entry; messages for one of them may have been misrouted",
                handle, existing, instance.ProfileName);
        }

        instance.GameWindowHandle = handle;
        _handleToProfile[handle] = instance.ProfileName;
    }

    /// <summary>
    /// Re-points a profile's routing entry at the window its game actually owns, if the one we
    /// registered has gone stale. Returns true when something was repaired.
    /// </summary>
    /// <remarks>
    /// A wrong routing entry and a dead bot look identical from the watchdog's side: no
    /// heartbeats arrive either way. The difference is that a wrong entry is ours to fix, and
    /// killing a healthy game over it is the worst possible response — at fleet scale it is a
    /// mass restart a minute after an update.
    /// <para>
    /// The case this exists for is adoption. A successor restores the routing entry from the
    /// predecessor's manifest, so it inherits whatever the predecessor believed — and a
    /// predecessor built before the handle was tracked on the instance reverse-looked it up out
    /// of a map that leaked a dead row per game exit, returning an arbitrary one. Every update
    /// from such a build hands its successor a handle that may name a window that no longer
    /// exists, which is not something the successor can fix by being correct itself.
    /// </para>
    /// </remarks>
    private bool RepairRoutingIfStale(ProfileInstance instance, Process process)
    {
        nint liveWindow;
        try
        {
            liveWindow = process.GameWindow;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read game window for {Name} while checking routing", instance.ProfileName);
            return false;
        }

        // No game window means there is nothing to route to — that is a real fault, not a
        // routing problem, so let the watchdog handle it.
        if (liveWindow == 0) return false;

        if (instance.GameWindowHandle == liveWindow
            && _handleToProfile.TryGetValue(liveWindow, out var mapped)
            && mapped == instance.ProfileName)
        {
            return false;
        }

        _logger.LogWarning(
            "Profile {Name} went quiet while routed to window {Registered}, but its game owns {Live} — " +
            "re-registering; messages from it were being discarded",
            instance.ProfileName, instance.GameWindowHandle, liveWindow);

        UnregisterHandles(instance);
        RegisterHandle(instance, liveWindow);
        return true;
    }

    /// <summary>
    /// Removes every routing entry for a profile. Uses the stored handle rather than re-reading
    /// <c>Process.GameWindow</c>: that enumerates windows owned by the pid, so once the process
    /// has exited it returns 0 and the removal silently no-ops, leaking the entry for the life
    /// of the manager. The sweep by name is belt-and-braces for an entry registered under a
    /// different handle (e.g. restored from a handoff manifest recording a drifted top-level).
    /// </summary>
    private void UnregisterHandles(ProfileInstance instance)
    {
        if (instance.GameWindowHandle != 0)
        {
            _handleToProfile.TryRemove(instance.GameWindowHandle, out _);
            _messageWindow.ForgetHandle(instance.GameWindowHandle);
            instance.GameWindowHandle = 0;
        }

        foreach (var kvp in _handleToProfile)
        {
            if (kvp.Value != instance.ProfileName) continue;
            _handleToProfile.TryRemove(kvp.Key, out _);
            _messageWindow.ForgetHandle(kvp.Key);
        }
    }

    public void BroadcastToAll(MessageType messageType, string message)
    {
        foreach (var instance in _instances.Values)
        {
            if (instance is { State: RunState.Running, Process: not null })
            {
                instance.Process.SendMessage(messageType, message);
            }
        }
    }

    public async Task UpdateProfileAndNotifyAsync(Profile profile)
    {
        await _profileRepository.UpdateAsync(profile);
        await NotifyProfileStateChangedAsync(profile.Name, includeProfile: true);
    }

    public async Task NotifyProfileStateChangedAsync(string profileName, bool includeProfile = false)
    {
        if (!_instances.TryGetValue(profileName, out var instance)) return;

        var state = instance.GetState();
        if (includeProfile)
            state.Profile = await _profileRepository.GetByKeyAsync(profileName);

        _eventBroadcaster.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ProfileState = state
        });
    }

    public async Task<bool> StartProfileAsync(string profileName, [System.Runtime.CompilerServices.CallerMemberName] string? caller = null)
    {
        if (!_instances.TryGetValue(profileName, out var instance))
        {
            _logger.LogWarning("Profile {Name} not found", profileName);
            return false;
        }

        if (!await instance.TransitionToAsync(RunState.Starting))
        {
            _logger.LogWarning("Cannot start profile {Name} in state {State}", profileName, instance.State);
            return false;
        }

        _logger.LogDebug("Starting profile {Name} (caller: {Caller})", profileName, caller);

        instance.LaunchFailureCount = 0;
        instance.RuntimeRestartCount = 0;
        await NotifyProfileStateChangedAsync(profileName);

        _ = RunProfileBackgroundAsync(instance);
        return true;
    }

    public async Task<bool> StopProfileAsync(string profileName, bool force = false, bool preserveKey = false, CancellationToken cancellationToken = default, [System.Runtime.CompilerServices.CallerMemberName] string? caller = null)
    {
        if (!_instances.TryGetValue(profileName, out var instance))
        {
            return false;
        }

        if (instance.State == RunState.Stopped)
        {
            return true;
        }

        if (!await instance.TransitionToAsync(RunState.Stopping))
        {
            if (!force) return false;
        }

        _logger.LogDebug("Stopping profile {Name} (caller: {Caller})", profileName, caller);

        // Marked as soon as the decision is made, not just around the kill: the notify below
        // builds a state snapshot via Process.GameWindow, an EnumWindows sweep of the whole
        // session, and at fleet scale 160 of those are long enough for a dying bot's messages to
        // slip past the guard.
        instance.BeginTeardown();
        try
        {
            await NotifyProfileStateChangedAsync(profileName);

            instance.CancelRun();

            try
            {
                if (instance.Process != null)
                {
                    await _processManager.TerminateAsync(
                        instance.Process,
                        TimeSpan.FromSeconds(5),
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // TerminateAsync can throw (access denied, or a concurrent SetGameProcess
                // disposing the object mid-kill). Reaching Stopped matters more than the kill
                // succeeding: Stopping only ever transitions to Stopped, so letting this escape
                // would strand the profile in a state nothing can leave, holding its key, with
                // the routing row leaked and every later message from that handle ignored.
                _logger.LogError(ex, "Failed to terminate the game for {Name}; stopping anyway",
                    profileName);
            }

            // Drop the routing entry only once the game is gone. Doing it first left the bot
            // talking for the whole WM_CLOSE grace with nowhere to route to, so an ordinary Stop
            // All produced a burst of "unknown window handle" warnings about messages we had
            // stopped listening for on purpose.
            UnregisterHandles(instance);
        }
        finally
        {
            // Paired here rather than inside UnregisterHandles: that is called from five places,
            // only three of which are teardowns, so clearing it there let an unrelated caller (a
            // routing repair, or the other of two overlapping teardowns) drop the guard early.
            instance.EndTeardown();
        }

        await instance.TransitionToAsync(RunState.Stopped);
        instance.Status = "";
        if (!preserveKey)
            instance.KeyName = null;
        instance.ProxyName = null;
        await NotifyProfileStateChangedAsync(profileName);
        await BroadcastKeyListsSnapshotAsync();
        await BroadcastProxiesSnapshotAsync();
        await BroadcastFrameworksSnapshotAsync();

        return true;
    }

    public async Task RestartProfileAsync(string profileName, bool rotateKey = false, [System.Runtime.CompilerServices.CallerMemberName] string? caller = null)
    {
        _logger.LogDebug("Restarting profile {Name} (caller: {Caller})", profileName, caller);
        await StopProfileAsync(profileName, preserveKey: !rotateKey);
        if (rotateKey)
            await RotateKeyAsync(profileName);
        await Task.Delay(1000);
        await StartProfileAsync(profileName);
    }

    public async Task StartAllAsync()
    {
        foreach (var instance in _instances.Values)
        {
            if (instance.State == RunState.Stopped)
            {
                await StartProfileAsync(instance.ProfileName);
            }
        }
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        // QuiesceForHandoff already cancelled monitor tokens; bail before we'd try to
        // terminate the games (which the successor is about to adopt). Defensive: also
        // covers callers that somehow set _handoffInProgress without calling Quiesce.
        if (_handoffInProgress) return;

        var tasks = _instances.Values
            .Where(i => i.State != RunState.Stopped)
            .Select(i => StopProfileAsync(i.ProfileName, cancellationToken: cancellationToken))
            .ToList();

        await Task.WhenAll(tasks);
    }

    public async Task ShowWindowAsync(string profileName)
    {
        if (!_instances.TryGetValue(profileName, out var instance) || instance.Process == null) return;
        var hwnd = instance.Process.GameWindow;
        if (hwnd == 0) return;

        var profile = await _profileRepository.GetByKeyAsync(profileName);
        var loc = profile?.WindowLocation;
        if (loc != null)
            _processManager.ShowWindowAt(hwnd, loc.X, loc.Y);
        else
            _processManager.ShowWindow(hwnd);

        await NotifyProfileStateChangedAsync(profileName);
    }

    public async Task HideWindowAsync(string profileName)
    {
        if (!_instances.TryGetValue(profileName, out var instance) || instance.Process == null) return;
        var hwnd = instance.Process.GameWindow;
        if (hwnd == 0) return;
        _processManager.HideWindow(hwnd);
        await NotifyProfileStateChangedAsync(profileName);
    }

    public bool SendMessage(string profileName, MessageType messageType, string message)
    {
        if (!_instances.TryGetValue(profileName, out var instance)) return false;
        // Always reported: this overload only ever carries a user or operator action.
        return SendAndReport(instance, messageType, message, suppressWhileTearingDown: false);
    }

    public bool SendMessage(nint handle, MessageType messageType, string message)
    {
        var instance = GetInstanceByHandle(handle);
        // Suppressed during teardown: this overload carries replies to a bot, and a bot on its way
        // out routinely asks for one it will never read.
        return instance != null && SendAndReport(instance, messageType, message, suppressWhileTearingDown: true);
    }

    /// <summary>
    /// Sends to a profile's game and reports a total delivery failure, naming the profile.
    /// </summary>
    /// <remarks>
    /// These two overloads carry the messages a *user or script action* turns into — Trigger Mule,
    /// Discord /mule, a legacy emit, a reply to a script that is blocked waiting for it. Every one
    /// of their callers discards the bool and reports success regardless, so without this a mule
    /// that never arrived left no evidence anywhere. The per-window failures underneath are Debug
    /// on purpose (see Extensions.SendMessage); this is the aggregate, and it is a real fault.
    /// </remarks>
    /// <param name="instance">The profile whose game should receive the message.</param>
    /// <param name="messageType">WM_COPYDATA dwData value.</param>
    /// <param name="message">Payload.</param>
    /// <param name="suppressWhileTearingDown">
    /// Set only for bot replies. A dying bot's parting "retrieve"/"getProfile" is answered into a
    /// window that is already going away, and warning per message would put back the Stop All
    /// console wall this change removes. Never set for a user action: Trigger Mule and /mule both
    /// report success regardless of the result, so this warning is their only evidence.
    /// </param>
    private bool SendAndReport(
        ProfileInstance instance, MessageType messageType, string message, bool suppressWhileTearingDown)
    {
        if (instance.Process?.SendMessage(messageType, message) == true) return true;

        if (!(suppressWhileTearingDown && instance.TearingDown))
        {
            _logger.LogWarning("Profile {Name} did not accept {MessageType} — its game may be gone or unresponsive",
                instance.ProfileName, messageType);
        }

        return false;
    }

    #region Key Management

    public async Task<bool> RotateKeyAsync(string profileName)
    {
        if (!_instances.TryGetValue(profileName, out var instance))
            return false;

        var profile = await _profileRepository.GetByKeyAsync(profileName);
        if (profile == null || string.IsNullOrEmpty(profile.KeyList))
            return false;

        // Clear current key first (frees it in runtime state)
        instance.KeyName = null;

        // Get next available key
        var key = await AcquireKeyAsync(profile.KeyList);
        if (key == null)
            return false;

        instance.KeyName = key.Name;
        await NotifyProfileStateChangedAsync(profileName);
        await BroadcastKeyListsSnapshotAsync();

        return true;
    }

    public async Task ReleaseKeysAsync(IEnumerable<string> profileNames)
    {
        foreach (var profileName in profileNames)
        {
            if (_instances.TryGetValue(profileName, out var instance))
            {
                instance.KeyName = null;
                await NotifyProfileStateChangedAsync(profileName);
            }
        }
        await BroadcastKeyListsSnapshotAsync();
    }

    public async Task<bool> RotateKeysAsync(IEnumerable<string> profileNames)
    {
        var allSucceeded = true;
        foreach (var profileName in profileNames)
        {
            if (!await RotateKeySingleAsync(profileName))
                allSucceeded = false;
        }
        await BroadcastKeyListsSnapshotAsync();
        return allSucceeded;
    }

    private async Task<bool> RotateKeySingleAsync(string profileName)
    {
        if (!_instances.TryGetValue(profileName, out var instance))
        {
            return false;
        }

        var profile = await _profileRepository.GetByKeyAsync(profileName);
        if (profile == null || string.IsNullOrEmpty(profile.KeyList))
        {
            return false;
        }

        // Clear current key first (frees it in runtime state)
        instance.KeyName = null;

        // Get next available key
        var usedKeys = await GetUsedKeyNamesAsync(profile.KeyList);
        var key = await _keyListRepository.GetNextAvailableKeyAsync(profile.KeyList, usedKeys);
        if (key == null)
        {
            return false;
        }

        instance.KeyName = key.Name;
        await NotifyProfileStateChangedAsync(profileName);

        return true;
    }

    private async Task<CDKey?> AcquireKeyAsync(string keyListName)
    {
        var usedKeys = await GetUsedKeyNamesAsync(keyListName);
        return await _keyListRepository.GetNextAvailableKeyAsync(keyListName, usedKeys);
    }

    private async Task<HashSet<string>> GetUsedKeyNamesAsync(string keyListName)
    {
        var profiles = await _profileRepository.GetAllAsync();
        var used = new HashSet<string>();
        foreach (var p in profiles.Where(p => p.KeyList == keyListName))
        {
            var inst = GetInstance(p.Name);
            if (inst?.KeyName != null)
                used.Add(inst.KeyName);
        }
        return used;
    }

    #endregion

    #region Snapshots

    public async Task<ProfilesSnapshot> BuildProfilesSnapshotAsync()
    {
        var snapshot = new ProfilesSnapshot();
        var profiles = await _profileRepository.GetAllAsync();

        foreach (var profile in profiles)
        {
            var instance = GetInstance(profile.Name);
            var state = instance?.GetState() ?? new ProfileState
            {
                ProfileName = profile.Name,
                State = RunState.Stopped,
                Status = ""
            };

            state.Profile = profile;
            snapshot.Profiles.Add(state);
        }

        return snapshot;
    }

    public async Task BroadcastProfilesSnapshotAsync()
    {
        _eventBroadcaster.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ProfilesSnapshot = await BuildProfilesSnapshotAsync()
        });
    }

    public async Task<KeyListsSnapshot> BuildKeyListsSnapshotAsync()
    {
        var snapshot = new KeyListsSnapshot();
        var keyLists = await _keyListRepository.GetAllAsync();
        var profiles = await _profileRepository.GetAllAsync();

        foreach (var keyList in keyLists)
        {
            var keyListWithUsage = new KeyListWithUsage { KeyList = keyList };

            foreach (var key in keyList.Keys)
            {
                var profileUsingKey = profiles.FirstOrDefault(p =>
                {
                    if (p.KeyList != keyList.Name) return false;
                    var instance = GetInstance(p.Name);
                    return instance?.KeyName == key.Name;
                });

                keyListWithUsage.Usage.Add(new KeyUsage
                {
                    KeyName = key.Name,
                    ProfileName = profileUsingKey?.Name ?? ""
                });
            }

            snapshot.KeyLists.Add(keyListWithUsage);
        }

        return snapshot;
    }

    public async Task BroadcastKeyListsSnapshotAsync()
    {
        _eventBroadcaster.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            KeyListsSnapshot = await BuildKeyListsSnapshotAsync()
        });
    }

    public async Task<ProxiesSnapshot> BuildProxiesSnapshotAsync()
    {
        var snapshot = new ProxiesSnapshot();
        var proxies = (await _proxyRepository.GetAllAsync())
            .OrderBy(p => p.Address, StringComparer.OrdinalIgnoreCase);
        var profiles = await _profileRepository.GetAllAsync();

        foreach (var proxy in proxies)
        {
            var usage = new ProxyWithUsage { Proxy = proxy };
            foreach (var profile in profiles)
            {
                if (profile.Proxy == proxy.Address)
                {
                    usage.ConfiguredProfiles.Add(profile.Name);
                }

                if (GetInstance(profile.Name)?.ProxyName == proxy.Address)
                {
                    usage.ActiveProfiles.Add(profile.Name);
                }
            }

            snapshot.Proxies.Add(usage);
        }

        return snapshot;
    }

    public async Task BroadcastProxiesSnapshotAsync()
    {
        _eventBroadcaster.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ProxiesSnapshot = await BuildProxiesSnapshotAsync()
        });
    }

    public async Task<FrameworksSnapshot> BuildFrameworksSnapshotAsync()
    {
        var snapshot = new FrameworksSnapshot();
        var frameworks = (await _frameworkRepository.GetAllAsync())
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var profiles = await _profileRepository.GetAllAsync();

        foreach (var framework in frameworks)
        {
            var usage = new FrameworkWithUsage { Framework = framework };
            foreach (var profile in profiles)
            {
                if (profile.Framework != framework.Name)
                {
                    continue;
                }

                usage.ConfiguredProfiles.Add(profile.Name);

                if (GetInstance(profile.Name) is { State: RunState.Running or RunState.Starting })
                {
                    usage.ActiveProfiles.Add(profile.Name);
                }
            }

            snapshot.Frameworks.Add(usage);
        }

        return snapshot;
    }

    public async Task BroadcastFrameworksSnapshotAsync()
    {
        _eventBroadcaster.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            FrameworksSnapshot = await BuildFrameworksSnapshotAsync()
        });
    }

    /// <summary>Resolves the framework a profile launches with, or null if unset or missing.</summary>
    public async Task<Framework?> ResolveFrameworkAsync(Profile profile) =>
        string.IsNullOrEmpty(profile.Framework)
            ? null
            : await _frameworkRepository.GetByKeyAsync(profile.Framework);

    #endregion

    public async Task<bool> ResetStatsAsync(string profileName)
    {
        var profile = await _profileRepository.GetByKeyAsync(profileName);
        if (profile == null)
        {
            return false;
        }

        profile.Runs = 0;
        profile.Chickens = 0;
        profile.Deaths = 0;
        profile.Crashes = 0;
        profile.Restarts = 0;
        profile.KeyRuns = 0;
        await _profileRepository.UpdateAsync(profile);
        await NotifyProfileStateChangedAsync(profileName, includeProfile: true);

        return true;
    }

    private async Task RunProfileAsync(ProfileInstance instance)
    {
        var profileName = instance.ProfileName;
        var cancellationToken = instance.GetCancellationToken();

        // Bail out if Stop was called before this task got scheduled
        if (instance.State != RunState.Starting)
        {
            _logger.LogDebug("Profile {Name} no longer in Starting state, aborting run", profileName);
            return;
        }

        // Clear stale status from previous run
        instance.Status = "";
        instance.MissedHeartbeats = 0;
        await NotifyProfileStateChangedAsync(profileName);

        // Set by the launch step below so the catch-all can tell a game that never started from
        // one that started and later failed. Only the former consumes the retry budget.
        var launchFailed = false;

        // The process THIS run launched, so the catch below can tell it apart from whatever
        // instance.Process happens to hold by then — a later run can have replaced (and disposed)
        // it, and killing that one would take down a game this run never owned.
        Process? launched = null;

        try
        {
            var profile = await _profileRepository.GetByKeyAsync(profileName);
            if (profile == null)
            {
                await instance.SetErrorAsync("Profile not found");
                await NotifyProfileStateChangedAsync(profileName);
                return;
            }

            var framework = await ResolveFrameworkAsync(profile);
            if (framework == null)
            {
                await instance.SetErrorAsync(string.IsNullOrEmpty(profile.Framework)
                    ? "No framework assigned. Assign a framework to this profile."
                    : $"Framework '{profile.Framework}' not found. Assign a framework to this profile.");
                await NotifyProfileStateChangedAsync(profileName);
                return;
            }

            // The launched executable is the profile's own Diablo II Path.
            var gamePath = profile.D2Path;
            if (string.IsNullOrWhiteSpace(gamePath))
            {
                await instance.SetErrorAsync("No game executable set. Set the profile's Diablo II Path.");
                await NotifyProfileStateChangedAsync(profileName);
                return;
            }

            if (!File.Exists(gamePath))
            {
                await instance.SetErrorAsync($"Executable: '{gamePath}' does not exist");
                await NotifyProfileStateChangedAsync(profileName);
                return;
            }

            var dllPaths = framework.DllFullPaths();
            var missingDll = dllPaths.FirstOrDefault(p => !File.Exists(p));
            if (missingDll != null)
            {
                await instance.SetErrorAsync($"Inject DLL: '{missingDll}' does not exist");
                await NotifyProfileStateChangedAsync(profileName);
                return;
            }

            // Acquire key if needed
            CDKey? acquiredKey = null;
            if (!string.IsNullOrEmpty(profile.KeyList))
            {
                // Reuse the previously-assigned key if still valid (e.g. restart
                // after a crash without rotation). Skip if the key was Held in
                // the meantime — fall through to rotate to a fresh one.
                if (!string.IsNullOrEmpty(instance.KeyName))
                {
                    var keyList = await _keyListRepository.GetByKeyAsync(profile.KeyList);
                    acquiredKey = keyList?.Keys.FirstOrDefault(k => k.Name == instance.KeyName && !k.Held);
                }

                acquiredKey ??= await AcquireKeyAsync(profile.KeyList);
                if (acquiredKey == null)
                {
                    await instance.SetErrorAsync("No available keys");
                    await NotifyProfileStateChangedAsync(profileName);
                    return;
                }

                instance.KeyName = acquiredKey.Name;
                await BroadcastKeyListsSnapshotAsync();
            }

            // Claim the configured proxy for usage tracking (runtime property, like KeyName).
            instance.ProxyName = string.IsNullOrEmpty(profile.Proxy) ? null : profile.Proxy;
            await BroadcastProxiesSnapshotAsync();
            await BroadcastFrameworksSnapshotAsync();

            await ApplyStartupPacingAsync(instance, cancellationToken);

            // Get current key info for command line
            string? classicKey = null;
            string? expansionKey = null;

            if (acquiredKey != null)
            {
                if (!string.IsNullOrEmpty(acquiredKey.Classic) && !string.IsNullOrEmpty(acquiredKey.Expansion))
                {
                    classicKey = acquiredKey.Classic;
                    expansionKey = acquiredKey.Expansion;
                }
            }

            // Environment: manager env < framework env < profile env (most specific wins).
            // Case-insensitive keys, since Windows environment variable names are. Build via
            // the indexer (not the IDictionary ctor, which throws on case-duplicate keys like
            // "Path"/"PATH" that a case-sensitive protobuf map can legally hold).
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in framework.Environment)
            {
                environment[key] = value;
            }
            foreach (var (key, value) in profile.Environment)
            {
                environment[key] = value;
            }

            var config = new GameLaunchConfig
            {
                GameType = framework.GameType,
                DisableAnalytics = _analyticsDisabled,
                GamePath = gamePath,
                ProfileName = profileName,
                Handle = _messageWindow.Handle.ToString(),

                Parameters = profile.Parameters,
                ClassicKey = classicKey,
                ExpansionKey = expansionKey,
                WindowLocation = profile.WindowLocation,
                Visible = profile.Visible,
                ProxyAddress = profile.Proxy,
                GameVersion = framework.GameVersionOrDefault(),
                DllPaths = dllPaths,
                Environment = environment
            };

            // Launch game. Only a failure to get the game up consumes the retry budget — see
            // HandleCrashAsync. Anything that goes wrong after this point is a runtime fault and
            // is retried indefinitely with backoff instead.
            Process gameProcess;
            try
            {
                gameProcess = await _gameLauncher.LaunchAsync(config, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                launchFailed = true;
                throw;
            }

            launched = gameProcess;
            instance.SetGameProcess(gameProcess);

            // Register handle for message routing
            RegisterHandle(instance, gameProcess.GameWindow);

            // The game is up, so the budget is spent on nothing: clear it. This is what makes
            // the counter mean "consecutive failures to start" rather than "things that have
            // ever gone wrong", and it is why a long-running fleet can no longer ratchet itself
            // into a permanent stop one incident at a time.
            instance.LaunchFailureCount = 0;

            if (!await instance.TransitionToAsync(RunState.Running))
            {
                throw new InvalidOperationException("Failed to transition to Running state");
            }

            await NotifyProfileStateChangedAsync(profileName);

            // Monitor process
            await MonitorProcessAsync(instance, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Profile {Name} run cancelled", profileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running profile {Name}", profileName);

            // Kill the game before recovering. Every step after the launch runs with a live
            // process, so abandoning it here and letting HandleCrashAsync start a fresh one
            // leaves an orphan holding this profile's CD key and window with no manager
            // attached — invisible in the UI, and it survives until something kills the job.
            //
            // Only the process THIS run launched, and only if it is still the instance's current
            // one. A later run can have replaced and disposed it in the meantime, and killing
            // that would take down a healthy game this run never owned.
            //
            // Not during a handoff, for the same reason StopAllAsync bails — a game the successor
            // is about to adopt must survive. QuiesceForHandoff cancels every run token, so a
            // monitor normally leaves via the OperationCanceledException catch above and never
            // reaches here; this covers the other exceptions, which cancellation does not
            // preempt. Note the flag is only set once the successor signals Adopted, so it does
            // not cover the earlier part of a handoff — that gap is pre-existing and shared with
            // the watchdog, which has no such guard at all.
            if (!_handoffInProgress && launched is { } orphan && ReferenceEquals(instance.Process, orphan))
            {
                // Guarded like the other two teardowns. This kill can block for the full WM_CLOSE
                // grace with the routing entry still live and the state still Running — long
                // enough for the dying bot's restartProfile to start a fresh game, which the
                // UnregisterHandles below would then strip of its routing while SetErrorAsync and
                // HandleCrashAsync launched a second one on top of it.
                instance.BeginTeardown();
                try
                {
                    // No HasExited pre-check: TerminateAsync already starts with one, wrapped
                    // against the InvalidOperationException a disposed or never-started Process
                    // throws. Checking here instead put that throw inside our catch and reported
                    // an already-dead game — the common case after a failed run — as a warning.
                    await _processManager.TerminateAsync(
                        orphan, TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (Exception terminateEx)
                {
                    _logger.LogWarning(terminateEx,
                        "Could not terminate the game for {Name} after a run error; it may be orphaned",
                        profileName);
                }
                finally
                {
                    instance.EndTeardown();
                }
            }

            // Clean up handle mapping
            UnregisterHandles(instance);

            // Someone may have stopped the profile while we were killing its game — the terminate
            // above can hold this for the full WM_CLOSE grace. SetErrorAsync assigns State
            // directly, bypassing IsValidTransition, so without this it would drag a completed
            // stop back out of Stopped and HandleCrashAsync would bill the user a crash for it.
            // Mirrors the guard in KillUnresponsiveAndRecoverAsync.
            if (instance.State == RunState.Stopped)
            {
                _logger.LogDebug("Profile {Name} was stopped while its failed run was cleaned up", profileName);
                return;
            }

            await instance.SetErrorAsync(ex.Message);
            await NotifyProfileStateChangedAsync(profileName);

            // Handle crash recovery
            await HandleCrashAsync(instance, cancellationToken, launchFailed);
        }
    }

    private async Task ApplyStartupPacingAsync(ProfileInstance instance, CancellationToken cancellationToken)
    {
        // Snapshot the semaphore so a mid-flight settings change doesn't desync acquire/release.
        var semaphore = _startupSemaphore;
        var delayMs = _startupDelayMs;

        if (semaphore == null && delayMs <= 0)
        {
            return;
        }

        var acquired = false;
        try
        {
            if (semaphore != null)
            {
                instance.Status = "Waiting for my turn";
                await NotifyProfileStateChangedAsync(instance.ProfileName);
                await semaphore.WaitAsync(cancellationToken);
                acquired = true;
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(delayMs);
            while (true)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) break;

                var secondsLeft = (int)Math.Ceiling(remaining.TotalSeconds);
                instance.Status = $"Starting in {secondsLeft}s...";
                await NotifyProfileStateChangedAsync(instance.ProfileName);

                var step = remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
                await Task.Delay(step, cancellationToken);
            }

            instance.Status = "";
            await NotifyProfileStateChangedAsync(instance.ProfileName);
        }
        finally
        {
            if (acquired)
            {
                semaphore!.Release();
            }
        }
    }

    private async Task MonitorProcessAsync(ProfileInstance instance, CancellationToken cancellationToken)
    {
        var process = instance.Process;
        if (process == null) return;

        // Health thresholds are per framework. A heartbeat/unresponsive timeout of 0
        // disables that watchdog (e.g. a framework that doesn't send heartbeats).
        var monitoredProfile = await _profileRepository.GetByKeyAsync(instance.ProfileName);
        var monitoredFramework = monitoredProfile != null ? await ResolveFrameworkAsync(monitoredProfile) : null;
        var heartbeatTimeout = monitoredFramework?.HeartbeatTimeoutOrDefault() ?? 30;
        var maxMissedHeartbeats = monitoredFramework?.MaxMissedHeartbeatsOrDefault() ?? 3;
        var unresponsiveTimeout = monitoredFramework?.UnresponsiveTimeoutOrDefault() ?? 30;
        var heartbeatEnabled = heartbeatTimeout > 0;

        process.SendMessage((MessageType)_messageWindow.Handle, "Handle");

        var lastHeartbeatCheck = DateTime.UtcNow;
        var lastHungCheck = DateTime.UtcNow;
        // The hung-window probe blocks for up to a second on a wedged window, on a thread-pool
        // thread. It feeds a timeout measured in tens of seconds, so 1Hz precision buys nothing
        // and costs a pinned thread per unhealthy profile.
        const int hungCheckIntervalSeconds = 5;
        // Retry handle delivery for ~10s (loop cadence is 1s) even when heartbeats are
        // disabled, then stop so a no-heartbeat framework doesn't ping forever.
        const int maxHandleResends = 10;
        var handleResends = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (process.HasExited)
            {
                _logger.LogDebug("Profile {Name} process exited with code {Code}",
                    instance.ProfileName, process.ExitCode);

                UnregisterHandles(instance);

                if (instance.State == RunState.Running)
                {
                    // Process exited while intended to be running — treat as crash
                    _logger.LogWarning("Profile {Name} exited unexpectedly, treating as crash",
                        instance.ProfileName);
                    await instance.SetErrorAsync("Process exited unexpectedly");
                    await NotifyProfileStateChangedAsync(instance.ProfileName);
                    await HandleCrashAsync(instance, cancellationToken);
                }
                // If state is Stopping, StopProfileAsync handles the cleanup
                return;
            }

            // Re-send the handle until the first heartbeat confirms delivery, capped so a
            // heartbeat-disabled framework (no heartbeat ever arrives) doesn't ping forever
            // while still retrying enough for a D2BS window that wasn't ready at launch.
            if (!instance.LastHeartbeat.HasValue && handleResends < maxHandleResends)
            {
                // Not reported when it fails: delivery here means a window pumped the message,
                // not that D2BS took it (see Extensions.SendMessage), so this cannot distinguish
                // "the DLL never hooked" — the case worth telling a user about — from a game that
                // is simply still loading. The heartbeat watchdog is what notices a bot that
                // never reports in.
                process.SendMessage((MessageType)_messageWindow.Handle, "Handle");
                handleResends++;
            }

            var now = DateTime.UtcNow;

            // Pull liveness from the message pump rather than waiting for the dispatch queue to
            // deliver it. The timestamp is when the heartbeat was *received*; stamping it at
            // dispatch made a backed-up queue indistinguishable from a dead bot, and under the
            // old shared counter that mistake was then recorded as a crash.
            if (instance.GameWindowHandle != 0
                && _messageWindow.TryGetLastHeartbeat(instance.GameWindowHandle, out var seenAt)
                && seenAt > (instance.LastHeartbeat ?? DateTime.MinValue))
            {
                instance.UpdateHeartbeat(seenAt);

                // A run that has been up a while and is reporting in has earned a clean slate.
                // Gated on uptime so a bot that crash-loops while emitting the odd heartbeat
                // can't keep resetting its own backoff.
                if (instance.RuntimeRestartCount > 0
                    && instance.StartedAt.HasValue
                    && (now - instance.StartedAt.Value).TotalSeconds >= 60)
                {
                    instance.RuntimeRestartCount = 0;
                }
            }

            // Check heartbeat every ~10 seconds
            if ((now - lastHeartbeatCheck).TotalSeconds >= 10)
            {
                lastHeartbeatCheck = now;

                // Re-resolve the profile's framework each tick so edits to its health
                // thresholds (or a framework reassignment) take effect on a running profile
                // without requiring a restart. Repository reads are lock-protected, but
                // keep the guard: an exception escaping the watchdog would be misread as
                // a crash and relaunch a healthy game — keep the previous thresholds until
                // the next tick instead.
                try
                {
                    monitoredProfile = await _profileRepository.GetByKeyAsync(instance.ProfileName);
                    monitoredFramework = monitoredProfile != null ? await ResolveFrameworkAsync(monitoredProfile) : null;
                    heartbeatTimeout = monitoredFramework?.HeartbeatTimeoutOrDefault() ?? 30;
                    maxMissedHeartbeats = monitoredFramework?.MaxMissedHeartbeatsOrDefault() ?? 3;
                    unresponsiveTimeout = monitoredFramework?.UnresponsiveTimeoutOrDefault() ?? 30;
                    heartbeatEnabled = heartbeatTimeout > 0;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "Threshold re-resolution failed for {Name}; keeping previous values",
                        instance.ProfileName);
                }

                var elapsed = (now - (instance.LastHeartbeat ?? instance.StartedAt!.Value)).TotalSeconds;
                if (heartbeatEnabled && elapsed > heartbeatTimeout
                    && RepairRoutingIfStale(instance, process))
                {
                    // We were listening on the wrong window, so the silence says nothing about
                    // the bot. Give it another interval on the repaired route before counting a
                    // miss, and re-push our handle in case the game is still aimed elsewhere.
                    process.SendMessage((MessageType)_messageWindow.Handle, "Handle");
                    elapsed = 0;
                }

                if (heartbeatEnabled && elapsed > heartbeatTimeout)
                {
                    process.SendMessage((MessageType)_messageWindow.Handle, "Handle");
                    instance.MissedHeartbeats++;
                    _logger.LogWarning("Profile {Name} missed heartbeat ({Count}/{Max})",
                        instance.ProfileName, instance.MissedHeartbeats, maxMissedHeartbeats);

                    if (instance.MissedHeartbeats >= maxMissedHeartbeats)
                    {
                        await KillUnresponsiveAndRecoverAsync(
                            instance, process, "Process not responding", cancellationToken);
                        return;
                    }
                }
            }

            // Independent of the heartbeat: if the game window stops pumping messages
            // (OS-level "not responding") continuously past the timeout, the bot is hung
            // even though kolbot's background heartbeat thread may still be ticking.
            // Mirrors the reference manager's Process.Responding watchdog.
            // Use the handle captured at launch rather than re-deriving it: Process.GameWindow
            // is an EnumWindows sweep of every top-level window in the session, and it cannot
            // change for a running game.
            var hwnd = instance.GameWindowHandle;
            if (unresponsiveTimeout > 0 && hwnd != 0
                && (now - lastHungCheck).TotalSeconds >= hungCheckIntervalSeconds)
            {
                lastHungCheck = now;
                if (IsGameWindowHung(hwnd))
                {
                    instance.UnresponsiveSince ??= now;
                    if ((now - instance.UnresponsiveSince.Value).TotalSeconds >= unresponsiveTimeout)
                    {
                        await KillUnresponsiveAndRecoverAsync(
                            instance, process, "Game window not responding", cancellationToken);
                        return;
                    }
                }
                else
                {
                    instance.UnresponsiveSince = null;
                }
            }

            await Task.Delay(1000, cancellationToken);
        }
    }

    // IsHungAppWindow is a passive OS heuristic (~5s) that can miss hangs on fullscreen/
    // DirectDraw game windows. Back it with an active WM_NULL ping — a no-op the window must
    // service from its message loop, so a timeout proves it isn't pumping. Short-circuits on
    // IsHungAppWindow, and SMTO_ABORTIFHUNG returns immediately once the OS flags the window, so
    // the ping only blocks (up to timeoutMs) for a window that is stuck but not yet OS-detected.
    // Not Process.Responding: it probes Process.MainWindowHandle (a launcher/splash window for
    // D2, or 0 -> always "responding"); we deliberately probe the class-matched GameWindow.
    private static bool IsGameWindowHung(nint hwnd, uint timeoutMs = 1000)
    {
        if (NativeMethods.IsHungAppWindow(hwnd)) return true;
        return NativeMethods.SendMessageTimeout(
            hwnd, NativeTypes.WM_NULL, 0, 0, NativeTypes.SMTO_ABORTIFHUNG, timeoutMs, out _) == 0;
    }

    /// <summary>
    /// Kills an unresponsive/crashed game and routes it through crash recovery
    /// (restart unless it has exceeded the retry budget). Shared by the missed-heartbeat
    /// and hung-window watchdogs.
    /// </summary>
    private async Task KillUnresponsiveAndRecoverAsync(
        ProfileInstance instance, Process process, string reason, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Profile {Name} {Reason}, treating as crash", instance.ProfileName, reason);

        // Marked before the kill, not after, and deliberately without touching RunState: the
        // routing entry now outlives the process, so the dying bot's last messages are
        // dispatched, and this is what tells D2BSMessageHandler to ignore the lifecycle ones
        // among them. Leaving the state at Running through the kill also keeps the UI's Start
        // button rejected (Running -> Starting is illegal, Error -> Starting is not), so a
        // second game cannot be launched on top of the one we are killing.
        instance.MissedHeartbeats = 0;
        instance.UnresponsiveSince = null;

        instance.BeginTeardown();
        try
        {
            // Kill the unresponsive process. Pass the cancellation token so that if the user
            // clicks Stop while we're waiting out the WM_CLOSE grace period, the wait aborts
            // and we force-kill immediately instead of sitting through up to 5s of sleep.
            await _processManager.TerminateAsync(process, TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (Exception ex)
        {
            // Same reasoning as StopProfileAsync: recovery matters more than the kill. Letting
            // this escape skips the unregister and the restart below, leaving a live, routed,
            // unmonitored game that no longer recovers on its own.
            _logger.LogError(ex, "Failed to kill the unresponsive game for {Name}; recovering anyway",
                instance.ProfileName);
        }
        finally
        {
            instance.EndTeardown();
        }

        // Unregister after the kill, as StopProfileAsync does. We concluded this game was hung,
        // but if it manages to say anything on the way out it is still telling us something true
        // — an item it just found, a run it finished, a console line — and there is no reason to
        // discard that.
        UnregisterHandles(instance);

        // Someone else may have finished stopping this profile while we were killing it. Bail
        // rather than call SetErrorAsync, which assigns State directly and would otherwise drag
        // a completed stop back out of Stopped into Error with nothing left to restart it.
        if (instance.State == RunState.Stopped)
        {
            _logger.LogDebug("Profile {Name} was stopped while being killed; skipping crash recovery",
                instance.ProfileName);
            return;
        }

        await instance.SetErrorAsync(reason);
        await NotifyProfileStateChangedAsync(instance.ProfileName);
        await HandleCrashAsync(instance, cancellationToken);
    }

    /// <summary>
    /// Restarts a profile after a failure.
    /// </summary>
    /// <param name="instance">The failed profile's runtime state.</param>
    /// <param name="cancellationToken">Cancelled when the user stops the profile mid-backoff.</param>
    /// <param name="launchFailure">
    /// True when the game never came up (launch or DLL injection threw). Only these consume the
    /// retry budget, and only consecutively — a successful launch clears the count. A runtime
    /// fault (heartbeat timeout, hung window, unexpected exit) is always retried, with backoff.
    /// <para>
    /// This mirrors D2Bot#, where the budget (<c>Crashed</c>, cap 6) was incremented only from
    /// the two LoadRemoteLibrary catch blocks and cleared on every successful load, while the
    /// heartbeat and Responding watchdogs restarted unconditionally and forever. D2BotNG had
    /// collapsed both into one lifetime counter, which made time-to-give-up a function of
    /// uptime alone: at a couple of transient faults a day, a profile was absorbed in ~2 days
    /// regardless of whether anything was actually wrong with it.
    /// </para>
    /// </param>
    private async Task HandleCrashAsync(
        ProfileInstance instance, CancellationToken cancellationToken, bool launchFailure = false)
    {
        var profileName = instance.ProfileName;

        var profile = await _profileRepository.GetByKeyAsync(profileName);
        if (profile != null)
        {
            profile.Crashes++;
            await _profileRepository.UpdateAsync(profile);
            await NotifyProfileStateChangedAsync(profileName, includeProfile: true);
        }

        var maxCrashRetries = profile != null
            ? (await ResolveFrameworkAsync(profile))?.MaxCrashRetriesOrDefault() ?? 5
            : 5;

        TimeSpan delay;
        if (launchFailure)
        {
            instance.LaunchFailureCount++;
            if (instance.LaunchFailureCount >= maxCrashRetries)
            {
                await GiveUpAfterLaunchFailuresAsync(instance, maxCrashRetries);
                return;
            }

            _logger.LogWarning("Profile {Name} failed to launch, retrying ({Count}/{Max})",
                profileName, instance.LaunchFailureCount, maxCrashRetries);
            delay = TimeSpan.FromSeconds(5);
        }
        else
        {
            // Runtime faults are never fatal. Back off instead so a profile that comes up but
            // can never report in (bad DLL, broken entry script, wrong game version) stops
            // burning keys and shared message-loop time, without ever becoming permanently dead
            // the way a hard cap made it.
            instance.RuntimeRestartCount++;
            delay = RuntimeRestartDelay(instance.RuntimeRestartCount);
            _logger.LogWarning("Profile {Name} crashed, restarting in {Delay} (attempt {Count})",
                profileName, delay, instance.RuntimeRestartCount);
            instance.Status = delay > TimeSpan.FromSeconds(15)
                ? $"Restarting in {FormatDelay(delay)}..."
                : "Restarting...";
            await NotifyProfileStateChangedAsync(profileName);
        }

        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Profile {Name} crash delay interrupted by stop request", profileName);
            return;
        }

        // If state changed during delay (e.g. user stopped), don't restart
        if (instance.State != RunState.Error)
        {
            _logger.LogDebug("Profile {Name} state changed to {State} during crash delay, not restarting",
                profileName, instance.State);
            return;
        }

        if (await instance.TransitionToAsync(RunState.Starting))
        {
            await NotifyProfileStateChangedAsync(profileName);
            _ = RunProfileBackgroundAsync(instance);
        }
    }

    /// <summary>
    /// Stops a profile that has failed to launch <c>maxCrashRetries</c> times in a row.
    /// </summary>
    /// <remarks>
    /// Deliberately does NOT touch <c>ScheduleEnabled</c>. That wrote persisted user
    /// configuration as a side effect of a fault the manager cannot diagnose — it sees "N things
    /// went wrong" without knowing whether that is a dead DLL, a realm-down, or its own message
    /// loop running behind — and it turned a transient failure into one that survived restart
    /// and needed per-profile manual repair. D2Bot# set <c>ScheduleEnable = false</c> from
    /// exactly four places, all deliberate (the script's stopSchedule message, the context menu,
    /// the IRC command, the profile editor), and left an exhausted profile for the scheduler to
    /// recover on its next tick. The <c>stopSchedule</c> handler remains the supported way for a
    /// script — which does know why it is failing — to opt out.
    /// </remarks>
    private async Task GiveUpAfterLaunchFailuresAsync(ProfileInstance instance, int maxCrashRetries)
    {
        _logger.LogError("Profile {Name} failed to launch {Count} times in a row, giving up until restarted",
            instance.ProfileName, maxCrashRetries);

        // Set error status before transitioning to Stopped so the message is preserved.
        // Do NOT use SetErrorAsync here — it would set state to Error, allowing restarts.
        instance.Status = $"Failed to launch {maxCrashRetries} times in a row";
        instance.KeyName = null;
        instance.ProxyName = null;
        await instance.TransitionToAsync(RunState.Stopped);
        await NotifyProfileStateChangedAsync(instance.ProfileName, includeProfile: true);
        await BroadcastKeyListsSnapshotAsync();
        await BroadcastProxiesSnapshotAsync();
        await BroadcastFrameworksSnapshotAsync();
    }

    /// <summary>
    /// Backoff for repeated runtime failures: 5s doubling to a 5 minute ceiling.
    /// </summary>
    private static TimeSpan RuntimeRestartDelay(int consecutiveRestarts)
    {
        const int baseSeconds = 5;
        const int capSeconds = 300;
        // Shift is clamped before it can overflow the exponent.
        var exponent = Math.Min(Math.Max(consecutiveRestarts - 1, 0), 8);
        return TimeSpan.FromSeconds(Math.Min(baseSeconds << exponent, capSeconds));
    }

    private static string FormatDelay(TimeSpan delay) =>
        delay.TotalMinutes >= 1 ? $"{(int)delay.TotalMinutes}m" : $"{(int)delay.TotalSeconds}s";

    public void AddProfile(string profileName)
    {
        _instances.TryAdd(profileName, new ProfileInstance(profileName));
    }

    public void RemoveProfile(string profileName)
    {
        if (_instances.TryRemove(profileName, out var instance))
        {
            instance.Dispose();
        }
    }

    public void RenameProfile(string oldName, string newName)
    {
        if (!_instances.TryRemove(oldName, out var instance))
            return;

        foreach (var kvp in _handleToProfile)
        {
            if (kvp.Value == oldName)
            {
                _handleToProfile[kvp.Key] = newName;
            }
        }

        instance.ProfileName = newName;
        _instances[newName] = instance;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await StopAllAsync(cancellationToken);
    }

    #region Handoff

    /// <summary>
    /// Snapshot of all live profile instances so a successor process can adopt them.
    /// Skips stopped/exited instances.
    /// </summary>
    public List<HandoffProfile> SnapshotInstances()
    {
        var result = new List<HandoffProfile>();
        foreach (var instance in _instances.Values)
        {
            if (instance.Process == null) continue;
            try
            {
                if (instance.Process.HasExited) continue;
            }
            catch
            {
                continue;
            }

            // The handle registered at launch, so the successor can restore it verbatim
            // (Process.MainWindowHandle can drift to a different top-level window than the one
            // D2BS sends from). Read from the instance rather than reverse-looked-up out of the
            // routing map: with more than one row per profile that lookup returned an arbitrary
            // one, so a stale entry could hand the successor a dead handle and silently drop
            // every message from that profile after an update.
            var registeredHandle = instance.GameWindowHandle.ToInt64();

            result.Add(new HandoffProfile
            {
                ProfileName = instance.ProfileName,
                Pid = instance.Process.Id,
                State = instance.State,
                Status = instance.Status,
                KeyName = instance.KeyName,
                ProxyName = instance.ProxyName,
                LaunchFailureCount = instance.LaunchFailureCount,
                StartedAt = instance.StartedAt,
                Handle = registeredHandle
                // MissedHeartbeats and LastHeartbeat intentionally not carried over —
                // the successor resets them so a stale snapshot can't immediately trip
                // the 30s heartbeat timeout on adoption.
            });
        }
        return result;
    }

    /// <summary>
    /// Adopts running game processes described in the handoff manifest by attaching to
    /// their PIDs and resuming the monitor loop. Re-sends the "Handle" message so the
    /// game's D2BS script redirects WM_COPYDATA to this process's MessageWindow.
    /// </summary>
    public async Task RehydrateAsync(IEnumerable<HandoffProfile> profiles)
    {
        foreach (var snapshot in profiles)
        {
            if (!_instances.TryGetValue(snapshot.ProfileName, out var instance))
            {
                _logger.LogWarning("Handoff profile {Name} not found in repository, skipping", snapshot.ProfileName);
                continue;
            }

            Process process;
            try
            {
                process = Process.GetProcessById(snapshot.Pid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cannot find PID {Pid} for profile {Name}, process may have exited", snapshot.Pid, snapshot.ProfileName);
                continue;
            }

            // The predecessor overwrote the game's DACL at launch time so it could inject
            // D2BS and read MainWindowHandle. After handoff that grant is to a now-dead
            // token; re-overwrite the DACL from this process so we can open the handle
            // for SYNCHRONIZE / QUERY_INFORMATION (required by EnableRaisingEvents) and
            // PROCESS_TERMINATE (required if a heartbeat timeout later forces a kill).
            if (!_processManager.EnsureAccess(process))
            {
                _logger.LogWarning("Cannot adopt PID {Pid} for profile {Name}: access denied even after DACL overwrite",
                    snapshot.Pid, snapshot.ProfileName);
                process.Dispose();
                continue;
            }

            try
            {
                process.EnableRaisingEvents = true;
                process.Refresh();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cannot attach to PID {Pid} for profile {Name} after DACL overwrite", snapshot.Pid, snapshot.ProfileName);
                process.Dispose();
                continue;
            }

            // Heartbeats are reset so the new monitor loop doesn't immediately trip the
            // 30s timeout if the LastHeartbeat in the manifest is stale (it can be up to
            // ~60s old: the snapshot is taken at handoff trigger and the manifest may sit
            // for a few seconds before the successor rehydrates).
            instance.RestoreFromHandoff(
                process,
                snapshot.State,
                snapshot.Status,
                snapshot.KeyName,
                snapshot.ProxyName,
                snapshot.LaunchFailureCount,
                missedHeartbeats: 0,
                snapshot.StartedAt,
                lastHeartbeat: DateTime.UtcNow,
                gameWindowHandle: snapshot.Handle != 0 ? (nint)snapshot.Handle : process.GameWindow);

            _logger.LogInformation("Adopted profile {Name} (PID {Pid}, state {State})",
                snapshot.ProfileName, snapshot.Pid, snapshot.State);

            // Restore the predecessor's routing entry verbatim. The HWND D2BS sends
            // from is whatever was registered before — may differ from what we'd read
            // now if Process.MainWindowHandle has drifted to a different top-level.
            // RestoreFromHandoff already resolved this (manifest handle, else the window we can
            // see now for a predecessor whose registration raced with launch).
            RegisterHandle(instance, instance.GameWindowHandle);

            // Proactively push the new manager HWND to the running D2BS script so it
            // redirects future WM_COPYDATA messages to this process's MessageWindow.
            if (!process.SendMessage((MessageType)_messageWindow.Handle, "Handle"))
            {
                _logger.LogWarning("Failed to push new manager handle to adopted profile {Name} (PID {Pid}) — no window accepted WM_COPYDATA",
                    snapshot.ProfileName, snapshot.Pid);
            }

            if (snapshot.State == RunState.Running || snapshot.State == RunState.Starting)
            {
                _ = ResumeMonitoringBackgroundAsync(instance);
            }

            await NotifyProfileStateChangedAsync(snapshot.ProfileName);
        }

        await BroadcastKeyListsSnapshotAsync();
        await BroadcastProxiesSnapshotAsync();
        await BroadcastFrameworksSnapshotAsync();
    }

    private Task ResumeMonitoringBackgroundAsync(ProfileInstance instance)
    {
        return Task.Run(async () =>
        {
            var cancellationToken = instance.GetCancellationToken();
            try
            {
                await MonitorProcessAsync(instance, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on stop
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resuming monitor for {ProfileName}", instance.ProfileName);
            }
        });
    }

    /// <summary>
    /// Stops engine activity in preparation for handoff WITHOUT terminating game processes.
    /// Cancels monitor loops and clears the handle map; sets a flag so the upcoming host
    /// shutdown (triggered by <c>StopApplication</c>) skips game termination.
    /// </summary>
    public void QuiesceForHandoff()
    {
        _handoffInProgress = true;
        foreach (var instance in _instances.Values)
        {
            instance.CancelRun();
        }
        _handleToProfile.Clear();
    }

    #endregion
}
