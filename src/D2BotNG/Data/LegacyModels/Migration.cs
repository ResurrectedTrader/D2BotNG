using System.Text.Json;
using D2BotNG.Core.Protos;
using D2BotNG.Logging;
using D2BotNG.Utilities;
using Google.Protobuf;

namespace D2BotNG.Data.LegacyModels;

/// <summary>
/// Migrates legacy JSONL data files from data/ to modern protobuf JSON in data/ng/.
/// </summary>
public static class Migration
{
    private static readonly Serilog.ILogger Logger = TrackingLoggerFactory.ForContext(typeof(Migration));
    /// <summary>
    /// Migrates legacy data files individually from data/ to data/ng/.
    /// Each file is migrated independently - if a modern file already exists, it is skipped.
    /// </summary>
    public static void MigrateIfNeeded(string basePath)
    {
        var legacyDir = Path.Combine(basePath, "data");
        var modernDir = Path.Combine(basePath, "data", "ng");

        if (!Directory.Exists(legacyDir))
            return;

        Directory.CreateDirectory(modernDir);

        MigrateProfiles(legacyDir, modernDir);

        MigrateLegacyFile<LegacyKeyList, KeyListCollection>(
            legacyDir, modernDir, "cdkeys.json", "keylists.json",
            (collection, legacy) => collection.KeyLists.Add(legacy.ToModern()),
            c => c.KeyLists.Count, "key lists");

        MigrateLegacyFile<LegacySchedule, ScheduleList>(
            legacyDir, modernDir, "schedules.json", "schedules.json",
            (collection, legacy) => collection.Schedules.Add(legacy.ToModern()),
            c => c.Schedules.Count, "schedules");

        MigrateLegacyFile<LegacyPatch, PatchList>(
            legacyDir, modernDir, "patch.json", "patches.json",
            (collection, legacy) => collection.Patches.Add(legacy.ToModern()),
            c => c.Patches.Count, "patches");
    }

    private static void MigrateLegacyFile<TLegacy, TCollection>(
        string legacyDir,
        string modernDir,
        string legacyFileName,
        string modernFileName,
        Action<TCollection, TLegacy> addItem,
        Func<TCollection, int> getCount,
        string entityName)
        where TCollection : IMessage<TCollection>, new()
    {
        var modernPath = Path.Combine(modernDir, modernFileName);
        if (File.Exists(modernPath)) return;

        var legacyPath = Path.Combine(legacyDir, legacyFileName);
        if (!File.Exists(legacyPath)) return;

        var collection = new TCollection();
        foreach (var line in File.ReadAllLines(legacyPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;

            try
            {
                var legacy = JsonSerializer.Deserialize<TLegacy>(line);
                if (legacy != null)
                    addItem(collection, legacy);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Skipping malformed {EntityName} line during migration", entityName);
            }
        }

        File.WriteAllText(modernPath, ProtobufJsonConfig.Formatter.Format(collection));
        Logger.Information("Migrated {Count} {EntityName}", getCount(collection), entityName);
    }

    /// <summary>
    /// Profiles need special handling: IRC profiles (Type=1) are skipped.
    /// </summary>
    private static void MigrateProfiles(string legacyDir, string modernDir)
    {
        var modernPath = Path.Combine(modernDir, "profiles.json");
        if (File.Exists(modernPath)) return;

        var legacyPath = Path.Combine(legacyDir, "profile.json");
        if (!File.Exists(legacyPath)) return;

        var profiles = new ProfileList();
        foreach (var line in File.ReadAllLines(legacyPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;

            try
            {
                // Skip IRC profiles (Type=1)
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("Type", out var typeProp) && typeProp.GetInt32() == 1)
                    continue;

                var legacy = JsonSerializer.Deserialize<LegacyProfile>(line);
                if (legacy != null)
                    profiles.Profiles.Add(legacy.ToModern());
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Skipping malformed profile line during migration");
            }
        }

        File.WriteAllText(Path.Combine(modernDir, "profiles.json"), ProtobufJsonConfig.Formatter.Format(profiles));
        Logger.Information("Migrated {Count} profiles", profiles.Profiles.Count);
    }
}
