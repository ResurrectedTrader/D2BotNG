using D2BotNG.Core.Protos;
using Microsoft.Win32;

namespace D2BotNG.Data;

/// <summary>
/// Ensures a usable framework exists and that every profile references one. Runs at
/// startup and after a base-path change. Idempotent: once profiles have a framework and
/// at least one framework exists, subsequent calls are no-ops.
///
/// This is the migration path from the pre-frameworks data model, where the game
/// directory, d2bs directory, inject DLL, and game version lived in global settings.
/// A "Default" framework is synthesized from those settings and assigned to every
/// profile that has no framework yet. Existing per-profile <c>d2_path</c> values (the
/// launched executable) are preserved unchanged.
///
/// It also self-heals afterwards: while only one framework exists, a profile left
/// without one (a framework delete clears every reference to it) is rebound to it on
/// the next run. Basic mode exposes no framework control at all, so such a profile was
/// otherwise unstartable with no way in the UI to fix it.
/// </summary>
public class FrameworkBootstrap
{
    public const string DefaultFrameworkName = "Default";
    private const string DefaultGameVersion = "1.14d";

    private readonly ILogger<FrameworkBootstrap> _logger;
    private readonly FrameworkRepository _frameworkRepository;
    private readonly ProfileRepository _profileRepository;
    private readonly SettingsRepository _settingsRepository;

    public FrameworkBootstrap(
        ILogger<FrameworkBootstrap> logger,
        FrameworkRepository frameworkRepository,
        ProfileRepository profileRepository,
        SettingsRepository settingsRepository)
    {
        _logger = logger;
        _frameworkRepository = frameworkRepository;
        _profileRepository = profileRepository;
        _settingsRepository = settingsRepository;
    }

    /// <summary>
    /// Ensures at least one framework exists and assigns a framework to any profile
    /// missing one. Returns true if it created a framework or reassigned any profile.
    /// </summary>
    public async Task<bool> EnsureDefaultAsync()
    {
        var frameworks = await _frameworkRepository.GetAllAsync();
        var profiles = await _profileRepository.GetAllAsync();
        var missing = profiles.Where(p => string.IsNullOrEmpty(p.Framework)).ToList();

        // Nothing to migrate and a framework already exists — no work.
        if (frameworks.Count > 0 && missing.Count == 0)
        {
            return false;
        }

        var frameworksExisted = frameworks.Count > 0;
        var changed = false;

        // A framework-less profile is only adopted when there is nothing to choose:
        // either this is a genuine first-run migration (no frameworks yet), or exactly
        // one framework exists — in which case the assignment we would make is the only
        // one the user could have made by hand, so making it cannot be wrong.
        //
        // With two or more, an empty Framework is the deliberate post-delete state and
        // the profile is awaiting reassignment; guessing could launch it against the
        // wrong game directory. We decline, but say so — the profile is unstartable
        // until someone acts, and silence made that look like a bug rather than a choice.
        var canAdopt = frameworks.Count <= 1;

        // The framework new/orphaned profiles will be attached to.
        var targetName = frameworks.FirstOrDefault(f => f.Name == DefaultFrameworkName)?.Name
                         ?? frameworks.FirstOrDefault()?.Name
                         ?? DefaultFrameworkName;

        // Adopt profiles BEFORE creating the framework: a crash between the two
        // writes then leaves a state the next run completes (frameworks.Count is
        // still 0 on retry).
        if (missing.Count > 0 && canAdopt)
        {
            await _profileRepository.MutateAllAsync(list =>
            {
                var adopted = false;
                foreach (var profile in list)
                {
                    if (!string.IsNullOrEmpty(profile.Framework)) continue;
                    profile.Framework = targetName;
                    adopted = true;
                }

                return adopted;
            });
            changed = true;
            _logger.LogInformation(
                "Assigned framework '{Name}' to {Count} profile(s) with no framework",
                targetName, missing.Count);
        }
        else if (missing.Count > 0)
        {
            _logger.LogWarning(
                "{Count} profile(s) have no framework and {FrameworkCount} frameworks exist, so "
                + "none was assigned automatically: {Profiles}. Each must be assigned a framework "
                + "before it can start",
                missing.Count, frameworks.Count, string.Join(", ", missing.Select(p => p.Name)));
        }

        if (!frameworksExisted)
        {
            var settings = await _settingsRepository.GetAsync();
            var framework = BuildDefaultFramework(settings, _settingsRepository.LegacySettings, profiles);
            await _frameworkRepository.CreateAsync(framework);
            changed = true;
            _logger.LogInformation(
                "Created '{Name}' framework (dir '{Dir}', d2bs '{D2bs}')",
                framework.Name, framework.GameDirectory, framework.D2BsPath);
        }

        return changed;
    }

    private static Framework BuildDefaultFramework(
        Settings settings,
        SettingsMigrator.LegacyGameEngine? legacy,
        IReadOnlyList<Profile> profiles)
    {
        var basePath = string.IsNullOrWhiteSpace(settings.BasePath)
            ? AppContext.BaseDirectory
            : settings.BasePath;

        // The game install directory (used for cleanup and as the browse default when
        // picking a profile's executable). Prefer the old install-path setting (recovered
        // by SettingsMigrator), else the directory most profiles' executables live in, else
        // the registry.
        var gameDirectory = legacy?.D2InstallPath;
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            gameDirectory = profiles
                .Where(p => !string.IsNullOrWhiteSpace(p.D2Path))
                .Select(p => Path.GetDirectoryName(p.D2Path))
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .GroupBy(d => d!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();
        }

        gameDirectory ??= DetectD2InstallPath();

        var legacyVersion = legacy?.GameVersion;
        var framework = new Framework
        {
            Name = DefaultFrameworkName,
            GameDirectory = gameDirectory ?? "",
            D2BsPath = Path.Combine(basePath, "d2bs"),
            DllPaths = { "D2BS.dll" },
            GameVersion = string.IsNullOrWhiteSpace(legacyVersion) ? DefaultGameVersion : legacyVersion
        };

        // Carry the old global values over (only when meaningfully set — 0/absent
        // leaves the framework at its own default, e.g. cleanup disabled).
        if (legacy is not null)
        {
            if (legacy.ScreenshotRetentionDays is > 0)
                framework.ScreenshotRetentionDays = legacy.ScreenshotRetentionDays.Value;
            if (legacy.CrashLogRetentionDays is > 0)
                framework.CrashLogRetentionDays = legacy.CrashLogRetentionDays.Value;
            if (legacy.HeartbeatTimeoutSeconds is > 0)
                framework.HeartbeatTimeoutSeconds = legacy.HeartbeatTimeoutSeconds.Value;
            if (legacy.MaxMissedHeartbeats is > 0)
                framework.MaxMissedHeartbeats = legacy.MaxMissedHeartbeats.Value;
            if (legacy.MaxCrashRetries is > 0)
                framework.MaxCrashRetries = legacy.MaxCrashRetries.Value;
            if (legacy.UnresponsiveTimeoutSeconds is > 0)
                framework.UnresponsiveTimeoutSeconds = legacy.UnresponsiveTimeoutSeconds.Value;
        }

        return framework;
    }

    /// <summary>Best-effort Diablo II install directory from the registry (seeds the Default framework only).</summary>
    private static string? DetectD2InstallPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Blizzard Entertainment\Diablo II");
            var installPath = key?.GetValue("InstallPath")?.ToString();
            return string.IsNullOrEmpty(installPath) ? null : installPath;
        }
        catch
        {
            return null;
        }
    }
}
