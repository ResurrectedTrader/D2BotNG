using D2BotNG.Core.Protos.Legacy;
using D2BotNG.Utilities;

namespace D2BotNG.Data;

/// <summary>
/// Versioned migration for the settings document (d2botng.json). The file carries its
/// version in <c>schema_version</c> (absent = 0); <see cref="SettingsRepository"/> upgrades
/// it on load up to <see cref="CurrentVersion"/> and rewrites it.
///
/// Rather than poke raw JSON, each breaking change archives the old settings shape as a
/// backend-only proto (see Legacy/Protos) and parses the old file into it — typed and
/// field-tolerant. Those archived protos are kept out of protos/ so they don't reach the
/// frontend's generated types.
///
/// Only the settings file is versioned; the repeated-wrapper list files (profiles/keys/…)
/// have no natural place for a version, so their one-off migrations live in bootstraps.
/// </summary>
public static class SettingsMigrator
{
    /// <summary>The schema version this build writes and migrates up to.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Pre-frameworks game/engine values recovered from a v0 file, consumed by
    /// <see cref="FrameworkBootstrap"/> to seed the Default framework.
    /// </summary>
    public sealed record LegacyGameEngine
    {
        public string? D2InstallPath { get; init; }
        public string? GameVersion { get; init; }
        public int? ScreenshotRetentionDays { get; init; }
        public int? CrashLogRetentionDays { get; init; }
        public int? HeartbeatTimeoutSeconds { get; init; }
        public int? MaxMissedHeartbeats { get; init; }
        public int? MaxCrashRetries { get; init; }
        public int? UnresponsiveTimeoutSeconds { get; init; }
    }

    /// <summary>
    /// Recovers the pre-frameworks game/engine settings from a v0 document by parsing it
    /// into the archived <see cref="SettingsV0"/> shape. Returns null for v1+ files or on
    /// any parse error.
    /// </summary>
    public static LegacyGameEngine? CaptureLegacy(string rawJson, int fileVersion)
    {
        if (fileVersion >= 1)
        {
            return null;
        }

        SettingsV0 v0;
        try
        {
            v0 = ProtobufJsonConfig.Parser.Parse<SettingsV0>(rawJson);
        }
        catch
        {
            return null;
        }

        return new LegacyGameEngine
        {
            D2InstallPath = NullIfEmpty(v0.Game?.D2InstallPath),
            GameVersion = NullIfEmpty(v0.Game?.GameVersion),
            ScreenshotRetentionDays = v0.Game?.ScreenshotRetentionDays,
            CrashLogRetentionDays = v0.Game?.CrashLogRetentionDays,
            HeartbeatTimeoutSeconds = v0.Engine?.HeartbeatTimeoutSeconds,
            MaxMissedHeartbeats = v0.Engine?.MaxMissedHeartbeats,
            MaxCrashRetries = v0.Engine?.MaxCrashRetries,
            UnresponsiveTimeoutSeconds = v0.Engine?.UnresponsiveTimeoutSeconds
        };
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
