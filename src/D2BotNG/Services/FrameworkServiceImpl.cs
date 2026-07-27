using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Engine;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace D2BotNG.Services;

public class FrameworkServiceImpl : FrameworkService.FrameworkServiceBase
{
    // Serializes framework mutations across concurrent RPCs (gRPC services are
    // per-call instances, hence static). Single writes are already repo-lock safe;
    // this makes the multi-step rename/delete sequences atomic relative to each
    // other, so concurrent mutations resolve deterministically instead of
    // interleaving into orphaned records or half-repointed profiles.
    private static readonly SemaphoreSlim MutationLock = new(1, 1);

    private readonly FrameworkRepository _frameworkRepository;
    private readonly ProfileRepository _profileRepository;
    private readonly ProfileEngine _profileEngine;
    private readonly ItemRepository _itemRepository;

    public FrameworkServiceImpl(
        FrameworkRepository frameworkRepository,
        ProfileRepository profileRepository,
        ProfileEngine profileEngine,
        ItemRepository itemRepository)
    {
        _frameworkRepository = frameworkRepository;
        _profileRepository = profileRepository;
        _profileEngine = profileEngine;
        _itemRepository = itemRepository;
    }

    public override async Task<Empty> CreateFramework(Framework request, ServerCallContext context)
    {
        var framework = Normalize(request);
        if (string.IsNullOrEmpty(framework.Name))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Framework name is required"));
        }

        await MutationLock.WaitAsync();
        try
        {
            if (await _frameworkRepository.GetByKeyAsync(framework.Name) != null)
            {
                throw new RpcException(new Status(StatusCode.AlreadyExists, $"Framework '{framework.Name}' already exists"));
            }

            await _frameworkRepository.CreateAsync(framework);
            await ApplySideEffectsAsync(profilesChanged: false);
        }
        finally
        {
            MutationLock.Release();
        }

        return new Empty();
    }

    public override async Task<Empty> UpdateFramework(UpdateFrameworkRequest request, ServerCallContext context)
    {
        var framework = Normalize(request.Framework);
        if (string.IsNullOrEmpty(framework.Name))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Framework name is required"));
        }

        await MutationLock.WaitAsync();
        try
        {
            var oldName = request.HasOriginalName ? request.OriginalName : framework.Name;
            if (await _frameworkRepository.GetByKeyAsync(oldName) == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"Framework '{oldName}' not found"));
            }

            var profilesChanged = false;

            if (oldName != framework.Name)
            {
                if (await _frameworkRepository.GetByKeyAsync(framework.Name) != null)
                {
                    throw new RpcException(new Status(StatusCode.AlreadyExists, $"Framework '{framework.Name}' already exists"));
                }

                // Create the renamed framework before removing the old one, so a crash
                // mid-rename can never lose the definition. Then repoint profiles, then
                // drop the old record.
                await _frameworkRepository.CreateAsync(framework);
                profilesChanged = await PropagateFrameworkChangeAsync(oldName, framework.Name);
                await _frameworkRepository.DeleteAsync(oldName);
            }
            else
            {
                await _frameworkRepository.UpdateAsync(framework);
            }

            await ApplySideEffectsAsync(profilesChanged);
        }
        finally
        {
            MutationLock.Release();
        }

        return new Empty();
    }

    public override async Task<Empty> DeleteFramework(FrameworkName request, ServerCallContext context)
    {
        await MutationLock.WaitAsync();
        try
        {
            if (await _frameworkRepository.GetByKeyAsync(request.Name) == null)
            {
                // Deleting something already gone is a no-op success.
                return new Empty();
            }

            // Repoint profiles BEFORE dropping the record: a crash between the two
            // writes then leaves profiles at the intended post-delete state (empty
            // framework, awaiting reassignment) rather than dangling on a name that
            // no longer exists — the same crash-safe ordering rename uses.
            var profilesChanged = await PropagateFrameworkChangeAsync(request.Name, null);
            await _frameworkRepository.DeleteAsync(request.Name);
            await ApplySideEffectsAsync(profilesChanged);
        }
        finally
        {
            MutationLock.Release();
        }

        return new Empty();
    }

    /// <summary>
    /// Keeps profile references in sync when a framework is renamed (newName set) or
    /// deleted (newName null). Returns true if any profile was updated.
    /// </summary>
    private Task<bool> PropagateFrameworkChangeAsync(string oldName, string? newName)
    {
        return _profileRepository.MutateAllAsync(profiles =>
        {
            var changed = false;
            foreach (var profile in profiles)
            {
                if (profile.Framework != oldName) continue;
                profile.Framework = newName ?? "";
                changed = true;
            }

            return changed;
        });
    }

    /// <summary>
    /// Rewrites every framework's d2bs.ini, refreshes the item (mules) watchers, and
    /// broadcasts fresh snapshots so all clients reflect the change.
    /// </summary>
    private async Task ApplySideEffectsAsync(bool profilesChanged)
    {
        await _profileRepository.RewriteInisAsync();
        await _itemRepository.RefreshAsync();

        await _profileEngine.BroadcastFrameworksSnapshotAsync();
        if (profilesChanged)
        {
            await _profileEngine.BroadcastProfilesSnapshotAsync();
        }
    }

    private static Framework Normalize(Framework framework)
    {
        // A NUL in a path is rejected by Path.GetFullPath and would poison every
        // subsequent IniWriter batch and mules-directory scan.
        RejectNul(framework.Name, "name");
        RejectNul(framework.GameDirectory, "game directory");
        RejectNul(framework.D2BsPath, "D2BS path");
        foreach (var dll in framework.DllPaths)
        {
            RejectNul(dll, "DLL path");
        }

        // A botting framework only works with the game it targets, and its ABI can't run
        // on another — reject an unsupported combination at the write boundary rather
        // than fail cryptically at launch.
        if (!IsValidPairing(framework.GameType, framework.BottingFramework))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"{framework.BottingFramework} does not support {framework.GameType}"));
        }

        var normalized = new Framework
        {
            Name = framework.Name.Trim(),
            GameType = framework.GameType,
            BottingFramework = framework.BottingFramework,
            GameDirectory = framework.GameDirectory.Trim(),
            D2BsPath = framework.D2BsPath.Trim(),
            GameVersion = framework.GameVersion.Trim(),
            ScreenshotRetentionDays = Math.Max(0, framework.ScreenshotRetentionDays),
            CrashLogRetentionDays = Math.Max(0, framework.CrashLogRetentionDays)
        };

        normalized.DllPaths.AddRange(
            framework.DllPaths.Select(d => d.Trim()).Where(d => d.Length > 0));

        if (framework.HasHeartbeatTimeoutSeconds)
        {
            normalized.HeartbeatTimeoutSeconds = framework.HeartbeatTimeoutSeconds;
        }

        if (framework.HasMaxMissedHeartbeats)
        {
            normalized.MaxMissedHeartbeats = framework.MaxMissedHeartbeats;
        }

        if (framework.HasMaxCrashRetries)
        {
            normalized.MaxCrashRetries = framework.MaxCrashRetries;
        }

        if (framework.HasUnresponsiveTimeoutSeconds)
        {
            normalized.UnresponsiveTimeoutSeconds = framework.UnresponsiveTimeoutSeconds;
        }

        foreach (var (key, value) in framework.Environment)
        {
            var trimmedKey = key.Trim();
            // Drop malformed names: empty, containing '=' (would rename the variable) or a
            // NUL (would corrupt the environment block); values may not contain NUL either.
            if (trimmedKey.Length == 0 || trimmedKey.Contains('=')
                || trimmedKey.Contains('\0') || value.Contains('\0'))
            {
                continue;
            }

            normalized.Environment[trimmedKey] = value;
        }

        return normalized;
    }

    private static bool IsValidPairing(GameType game, BottingFramework botting) => game switch
    {
        GameType.D2 => botting == BottingFramework.D2Bs,
        // Unknown enum values reach here from non-UI gRPC callers; protobuf passes them
        // through as ints rather than rejecting them.
        _ => false,
    };

    private static void RejectNul(string value, string fieldName)
    {
        if (value.Contains('\0'))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument, $"Framework {fieldName} contains an invalid character"));
        }
    }
}
