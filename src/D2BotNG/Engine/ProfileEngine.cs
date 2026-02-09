using System.Collections.Concurrent;
using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Services;
using D2BotNG.Windows;
using Google.Protobuf.WellKnownTypes;

namespace D2BotNG.Engine;

/// <summary>
/// Main engine for managing profile lifecycles
/// </summary>
public class ProfileEngine : IDisposable
{
    private readonly ILogger<ProfileEngine> _logger;
    private readonly ProfileRepository _profileRepository;
    private readonly KeyListRepository _keyListRepository;
    private readonly GameLauncher _gameLauncher;
    private readonly ProcessManager _processManager;
    private readonly EventBroadcaster _eventBroadcaster;
    private readonly MessageWindow _messageWindow;
    private readonly Paths _paths;

    private readonly ConcurrentDictionary<string, ProfileInstance> _instances = new();
    private readonly ConcurrentDictionary<nint, string> _handleToProfile = new();

    private const int MaxCrashRetries = 5;
    private const int HeartbeatTimeoutSeconds = 30;
    private const int MaxMissedHeartbeats = 3;

    public ProfileEngine(
        ILogger<ProfileEngine> logger,
        ProfileRepository profileRepository,
        KeyListRepository keyListRepository,
        GameLauncher gameLauncher,
        ProcessManager processManager,
        EventBroadcaster eventBroadcaster,
        MessageWindow messageWindow,
        Paths paths)
    {
        _logger = logger;
        _profileRepository = profileRepository;
        _keyListRepository = keyListRepository;
        _gameLauncher = gameLauncher;
        _processManager = processManager;
        _eventBroadcaster = eventBroadcaster;
        _messageWindow = messageWindow;
        _paths = paths;
    }

    private async Task<HashSet<string>> GetUsedKeyNamesAsync(string keyListName)
    {
        var profiles = await _profileRepository.GetAllAsync();
        var used = new HashSet<string>();
        foreach (var p in profiles.Where(p => p.KeyList == keyListName))
        {
            if (_instances.TryGetValue(p.Name, out var inst) && inst.KeyName != null)
                used.Add(inst.KeyName);
        }
        return used;
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

    public async Task NotifyProfileStateChangedAsync(string profileName, bool includeProfile = false)
    {
        if (_instances.TryGetValue(profileName, out var instance))
        {
            var state = instance.GetState();
            if (includeProfile)
                state.Profile = await _profileRepository.GetByKeyAsync(profileName);

            _eventBroadcaster.Broadcast(new Event
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                ProfileState = state
            });
        }
    }

    public async Task<bool> StartProfileAsync(string profileName)
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

        await NotifyProfileStateChangedAsync(profileName);

        _ = RunProfileBackgroundAsync(instance);
        return true;
    }

    public async Task<bool> StopProfileAsync(string profileName, bool force = false)
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

        await NotifyProfileStateChangedAsync(profileName);

        instance.CancelRun();

        // Unregister handle before terminating
        _handleToProfile.TryRemove(instance.Process?.MainWindowHandle ?? 0, out _);

        if (instance.Process != null)
        {
            await _processManager.TerminateAsync(
                instance.Process,
                TimeSpan.FromSeconds(5));
        }

        await instance.TransitionToAsync(RunState.Stopped);
        instance.Status = "";
        instance.KeyName = null;
        await NotifyProfileStateChangedAsync(profileName);
        await BroadcastKeyListsSnapshotAsync();

        return true;
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

    public async Task StopAllAsync()
    {
        var tasks = _instances.Values
            .Where(i => i.State != RunState.Stopped)
            .Select(i => StopProfileAsync(i.ProfileName))
            .ToList();

        await Task.WhenAll(tasks);
    }

    public async Task ShowWindowAsync(string profileName)
    {
        if (!_instances.TryGetValue(profileName, out var instance) ||
            instance.Process?.MainWindowHandle == 0) return;

        var profile = await _profileRepository.GetByKeyAsync(profileName);
        var loc = profile?.WindowLocation;
        if (loc != null)
            _processManager.ShowWindowAt(instance.Process!.MainWindowHandle, loc.X, loc.Y);
        else
            _processManager.ShowWindow(instance.Process!.MainWindowHandle);

        await NotifyProfileStateChangedAsync(profileName);
    }

    public async Task HideWindowAsync(string profileName)
    {
        if (!_instances.TryGetValue(profileName, out var instance) ||
            instance.Process?.MainWindowHandle == 0) return;
        _processManager.HideWindow(instance.Process!.MainWindowHandle);
        await NotifyProfileStateChangedAsync(profileName);
    }

    public bool SendMessage(string profileName, MessageType messageType, string message)
    {
        if (!_instances.TryGetValue(profileName, out var instance)) return false;
        return instance.Process?.SendMessage(messageType, message) ?? false;
    }

    public bool SendMessage(nint handle, MessageType messageType, string message)
    {
        return GetInstanceByHandle(handle)?.Process?.SendMessage(messageType, message) ?? false;
    }

    public async Task<bool> RotateKeyAsync(string profileName)
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
        await BroadcastKeyListsSnapshotAsync();

        return true;
    }

    public async Task ReleaseKeyAsync(string profileName)
    {
        if (_instances.TryGetValue(profileName, out var instance))
        {
            instance.KeyName = null;
            await NotifyProfileStateChangedAsync(profileName);
            await BroadcastKeyListsSnapshotAsync();
        }
    }

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

    public async Task<ProfilesSnapshot> BuildProfilesSnapshotAsync()
    {
        var snapshot = new ProfilesSnapshot();
        var profiles = await _profileRepository.GetAllAsync();

        foreach (var profile in profiles)
        {
            var instance = GetInstance(profile.Name);
            var status = instance?.GetState() ?? new ProfileState
            {
                ProfileName = profile.Name,
                State = RunState.Stopped,
                Status = ""
            };

            status.Profile = profile;
            snapshot.Profiles.Add(status);
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

    private async Task RunProfileAsync(ProfileInstance instance)
    {
        var profileName = instance.ProfileName;
        var cancellationToken = instance.GetCancellationToken();

        try
        {
            var profile = await _profileRepository.GetByKeyAsync(profileName);
            if (profile == null)
            {
                await instance.SetErrorAsync("Profile not found");
                await NotifyProfileStateChangedAsync(profileName);
                return;
            }

            // Acquire key if needed
            CDKey? acquiredKey = null;
            if (!string.IsNullOrEmpty(profile.KeyList))
            {
                var usedKeys = await GetUsedKeyNamesAsync(profile.KeyList);
                acquiredKey = await _keyListRepository.GetNextAvailableKeyAsync(profile.KeyList, usedKeys);
                if (acquiredKey == null)
                {
                    await instance.SetErrorAsync("No available keys");
                    await NotifyProfileStateChangedAsync(profileName);
                    return;
                }

                instance.KeyName = acquiredKey.Name;
                await BroadcastKeyListsSnapshotAsync();
            }

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

            var config = new GameLaunchConfig
            {
                GamePath = profile.D2Path,
                D2BSPath = Path.Join(_paths.D2BSDirectory, "D2BS.dll"),
                ProfileName = profileName,
                Handle = _messageWindow.Handle.ToString(),

                Parameters = profile.Parameters,
                ClassicKey = classicKey,
                ExpansionKey = expansionKey,
                WindowLocation = profile.WindowLocation,
                Visible = profile.Visible
            };

            // Launch game
            var gameProcess = await _gameLauncher.LaunchAsync(config, cancellationToken);
            instance.SetGameProcess(gameProcess);

            // Register handle for message routing
            if (gameProcess.MainWindowHandle != 0)
            {
                _handleToProfile[gameProcess.MainWindowHandle] = profileName;
            }

            if (!await instance.TransitionToAsync(RunState.Running))
            {
                throw new InvalidOperationException("Failed to transition to Running state");
            }

            await NotifyProfileStateChangedAsync(profileName);
            instance.CrashCount = 0;

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
            await instance.SetErrorAsync(ex.Message);
            await NotifyProfileStateChangedAsync(profileName);

            // Handle crash recovery
            await HandleCrashAsync(instance);
        }
    }

    private async Task MonitorProcessAsync(ProfileInstance instance, CancellationToken cancellationToken)
    {
        var process = instance.Process;
        if (process == null) return;

        process.SendMessage((MessageType)_messageWindow.Handle, "Handle");

        var lastHeartbeatCheck = DateTime.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (process.HasExited)
            {
                _logger.LogInformation("Profile {Name} process exited with code {Code}",
                    instance.ProfileName, process.ExitCode);

                if (process.ExitCode != 0)
                {
                    await HandleCrashAsync(instance);
                }
                else
                {
                    await instance.TransitionToAsync(RunState.Stopped);
                    await NotifyProfileStateChangedAsync(instance.ProfileName);
                }
                return;
            }

            if (!instance.LastHeartbeat.HasValue)
                process.SendMessage((MessageType)_messageWindow.Handle, "Handle");

            // Check heartbeat every ~10 seconds
            var now = DateTime.UtcNow;
            if ((now - lastHeartbeatCheck).TotalSeconds >= 10)
            {
                lastHeartbeatCheck = now;

                var elapsed = (now - (instance.LastHeartbeat ?? instance.StartedAt!.Value)).TotalSeconds;
                if (elapsed > HeartbeatTimeoutSeconds)
                {
                    process.SendMessage((MessageType)_messageWindow.Handle, "Handle");
                    instance.MissedHeartbeats++;
                    _logger.LogWarning("Profile {Name} missed heartbeat ({Count}/{Max})",
                        instance.ProfileName, instance.MissedHeartbeats, MaxMissedHeartbeats);

                    if (instance.MissedHeartbeats >= MaxMissedHeartbeats)
                    {
                        _logger.LogError("Profile {Name} terminated due to lack of response",
                            instance.ProfileName);
                        await StopProfileAsync(instance.ProfileName, force: true);
                        return;
                    }
                }
            }

            await Task.Delay(1000, cancellationToken);
        }
    }

    private async Task HandleCrashAsync(ProfileInstance instance)
    {
        var profileName = instance.ProfileName;
        instance.CrashCount++;

        var profile = await _profileRepository.GetByKeyAsync(profileName);
        if (profile != null)
        {
            profile.Crashes++;
            await _profileRepository.UpdateAsync(profile);
            await NotifyProfileStateChangedAsync(profileName, includeProfile: true);
        }

        instance.KeyName = null;
        await BroadcastKeyListsSnapshotAsync();

        if (instance.CrashCount < MaxCrashRetries)
        {
            _logger.LogWarning("Profile {Name} crashed, restarting ({Count}/{Max})",
                profileName, instance.CrashCount, MaxCrashRetries);

            await Task.Delay(TimeSpan.FromSeconds(5));

            if (await instance.TransitionToAsync(RunState.Starting))
            {
                await NotifyProfileStateChangedAsync(profileName);
                _ = RunProfileBackgroundAsync(instance);
            }
        }
        else
        {
            _logger.LogError("Profile {Name} exceeded max crash retries", profileName);
            await instance.SetErrorAsync($"Exceeded max crash retries ({MaxCrashRetries})");

            // Disable schedule to prevent ScheduleEngine from restarting
            if (profile is { ScheduleEnabled: true })
            {
                profile.ScheduleEnabled = false;
                await _profileRepository.UpdateAsync(profile);
                _logger.LogWarning("Disabled schedule for profile {Name} due to repeated crashes", profileName);
            }

            await instance.TransitionToAsync(RunState.Stopped);
            await NotifyProfileStateChangedAsync(profileName, includeProfile: true);
        }
    }

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

    public void Dispose()
    {
        foreach (var instance in _instances.Values)
        {
            instance.Dispose();
        }
    }
}
