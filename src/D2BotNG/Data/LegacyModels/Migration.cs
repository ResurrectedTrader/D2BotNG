using System.Text.Json;
using D2BotNG.Core.Protos;
using Google.Protobuf;
using Serilog;

namespace D2BotNG.Data.LegacyModels;

/// <summary>
/// Migrates legacy JSONL data files from data/ to modern protobuf JSON in data/ng/.
/// </summary>
public static class Migration
{
    private static readonly JsonFormatter JsonFormatter =
        new(JsonFormatter.Settings.Default.WithIndentation());

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
        MigrateKeyLists(legacyDir, modernDir);
        MigrateSchedules(legacyDir, modernDir);
        MigratePatches(legacyDir, modernDir);
    }

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
                Log.Warning(ex, "Skipping malformed profile line during migration");
            }
        }

        File.WriteAllText(Path.Combine(modernDir, "profiles.json"), JsonFormatter.Format(profiles));
        Log.Information("Migrated {Count} profiles", profiles.Profiles.Count);
    }

    private static void MigrateKeyLists(string legacyDir, string modernDir)
    {
        var modernPath = Path.Combine(modernDir, "keylists.json");
        if (File.Exists(modernPath)) return;

        var legacyPath = Path.Combine(legacyDir, "cdkeys.json");
        if (!File.Exists(legacyPath)) return;

        var keyLists = new KeyListCollection();
        foreach (var line in File.ReadAllLines(legacyPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;

            try
            {
                var legacy = JsonSerializer.Deserialize<LegacyKeyList>(line);
                if (legacy != null)
                    keyLists.KeyLists.Add(legacy.ToModern());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Skipping malformed key list line during migration");
            }
        }

        File.WriteAllText(Path.Combine(modernDir, "keylists.json"), JsonFormatter.Format(keyLists));
        Log.Information("Migrated {Count} key lists", keyLists.KeyLists.Count);
    }

    private static void MigrateSchedules(string legacyDir, string modernDir)
    {
        var modernPath = Path.Combine(modernDir, "schedules.json");
        if (File.Exists(modernPath)) return;

        var legacyPath = Path.Combine(legacyDir, "schedules.json");
        if (!File.Exists(legacyPath)) return;

        var schedules = new ScheduleList();
        foreach (var line in File.ReadAllLines(legacyPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;

            try
            {
                var legacy = JsonSerializer.Deserialize<LegacySchedule>(line);
                if (legacy != null)
                    schedules.Schedules.Add(legacy.ToModern());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Skipping malformed schedule line during migration");
            }
        }

        File.WriteAllText(Path.Combine(modernDir, "schedules.json"), JsonFormatter.Format(schedules));
        Log.Information("Migrated {Count} schedules", schedules.Schedules.Count);
    }

    private static void MigratePatches(string legacyDir, string modernDir)
    {
        var modernPath = Path.Combine(modernDir, "patches.json");
        if (File.Exists(modernPath)) return;

        var legacyPath = Path.Combine(legacyDir, "patch.json");
        if (!File.Exists(legacyPath)) return;

        var patches = new PatchList();
        foreach (var line in File.ReadAllLines(legacyPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;

            try
            {
                var legacy = JsonSerializer.Deserialize<LegacyPatch>(line);
                if (legacy != null)
                    patches.Patches.Add(legacy.ToModern());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Skipping malformed patch line during migration");
            }
        }

        File.WriteAllText(Path.Combine(modernDir, "patches.json"), JsonFormatter.Format(patches));
        Log.Information("Migrated {Count} patches", patches.Patches.Count);
    }

}
