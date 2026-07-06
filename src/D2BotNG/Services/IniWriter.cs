using System.Text;
using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Utilities;

namespace D2BotNG.Services;

/// <summary>
/// Writes profile configurations to each framework's d2bs.ini file.
/// </summary>
public class IniWriter
{
    // Cross-process lock shared with every d2bsng instance. d2bsng takes a Win32
    // named mutex of this exact name; .NET's Mutex maps to the same kernel object,
    // so the two serialize against each other.
    private const string IniLockName = @"Local\d2bs-ini-lock";
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);
    private const int ReplaceAttempts = 5;
    private const int ReplaceRetryMs = 20;

    private readonly ILogger<IniWriter> _logger;

    public IniWriter(ILogger<IniWriter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Write profiles to each framework's d2bs.ini. Every framework's ini is rewritten
    /// with only the profiles assigned to it, so moving a profile between frameworks
    /// removes it from the old ini and adds it to the new one.
    /// </summary>
    public Task WriteAsync(IReadOnlyList<Profile> profiles, IReadOnlyList<Framework> frameworks)
    {
        // A Win32 named mutex has thread affinity - it must be released by the
        // same thread that acquired it, and no await may sit between acquire and
        // release. So run the whole locked read-modify-write synchronously on one
        // pool thread rather than interleaving it with async file I/O.
        return Task.Run(() => WriteLocked(profiles, frameworks));
    }

    private void WriteLocked(IReadOnlyList<Profile> profiles, IReadOnlyList<Framework> frameworks)
    {
        if (frameworks.Count == 0)
        {
            return;
        }

        // Serialize against d2bsng (and any other cooperating writer) on the shared
        // named mutex once for the whole batch, then commit each ini via temp file +
        // atomic replace so no reader ever sees a half-written file.
        using var mutex = new Mutex(false, IniLockName);
        var owned = false;
        try
        {
            try
            {
                owned = mutex.WaitOne(LockTimeout);
            }
            catch (AbandonedMutexException)
            {
                // A holder crashed mid-transaction; we now own the mutex. Files are
                // still intact because every writer commits atomically.
                owned = true;
            }
            // Proceed even on timeout (!owned): the atomic replace keeps files safe,
            // so the worst case is a lost update under pathological contention.

            // Two frameworks can target the same d2bs.ini (e.g. a shared kolbot install
            // with different DLLs/version/env). Group by the resolved ini path and write
            // the union of every in-group framework's profiles, so one framework's write
            // can't drop the profiles of another that shares the file. Frameworks that opt
            // out of ini writing, or have no d2bs directory, are excluded.
            var writable = frameworks
                .Where(f => f.UsesIniOrDefault() && !string.IsNullOrWhiteSpace(f.D2BsPath))
                .ToList();

            foreach (var group in writable.GroupBy(
                         f => Path.GetFullPath(f.IniPath()), StringComparer.OrdinalIgnoreCase))
            {
                var names = group.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
                var groupProfiles = profiles.Where(p => names.Contains(p.Framework)).ToList();
                // Every framework in the group resolves to the same ini path, so any of
                // them works for locating and writing the file.
                WriteFrameworkIni(group.First(), groupProfiles);
            }
        }
        finally
        {
            if (owned)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private void WriteFrameworkIni(Framework framework, IReadOnlyList<Profile> profiles)
    {
        var iniPath = framework.IniPath();
        if (!File.Exists(iniPath))
        {
            _logger.LogWarning(
                "{iniPath} does not exist; skipping d2bs.ini write for framework '{Name}'",
                iniPath, framework.Name);
            return;
        }

        string? tempPath = null;
        try
        {
            const string marker = "; gateway=";
            var content = File.ReadAllText(iniPath);
            var markerIndex = content.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                _logger.LogError("Could not find marker '{marker}' in {iniPath}", marker, iniPath);
                return;
            }

            content = content[..(markerIndex + marker.Length)] + Environment.NewLine + Environment.NewLine;

            var sb = new StringBuilder(content.Length + profiles.Count * 256);
            sb.Append(content);

            foreach (var profile in profiles)
            {
                WriteProfileSection(sb, profile);
            }

            // Stage on a temp file in the same directory (same volume, so the swap
            // is atomic), then replace d2bs.ini.
            var directory = Path.GetDirectoryName(iniPath)!;
            tempPath = Path.Combine(directory, Path.GetRandomFileName());
            AtomicFile.WriteDurable(tempPath, sb.ToString(), Encoding.Unicode);
            ReplaceWithRetry(tempPath, iniPath);
            tempPath = null; // consumed by the successful replace

            _logger.LogDebug("Wrote {iniPath} with {Count} profiles", iniPath, profiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write {iniPath}", iniPath);
        }
        finally
        {
            if (tempPath is not null)
            {
                TryDelete(tempPath);
            }
        }
    }

    // Atomically swap the staged temp file over d2bs.ini. A reader that briefly
    // holds the file open (the game-side GetPrivateProfileString) can trip a
    // sharing violation, so retry a few times before giving up.
    private static void ReplaceWithRetry(string tempPath, string iniPath)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                // iniPath is known to exist; File.Replace is atomic on NTFS and
                // preserves the destination's attributes.
                File.Replace(tempPath, iniPath, null);
                return;
            }
            catch (IOException) when (attempt < ReplaceAttempts - 1)
            {
                Thread.Sleep(ReplaceRetryMs);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Best-effort cleanup of the staged temp file; nothing to do on failure.
        }
    }

    private static void WriteProfileSection(StringBuilder sb, Profile profile)
    {
        var difficulty = profile.Difficulty.ToIniString();
        var scriptPath = "kolbot"; // Default bot library folder name
        var entryScript = Path.GetFileName(profile.EntryScript);

        sb.AppendLine($"[{profile.Name}]");
        sb.AppendLine($"Mode={profile.Mode.ToIniString()}");
        sb.AppendLine($"Username={profile.Account}");
        sb.AppendLine($"Password={profile.Password}");
        sb.AppendLine($"gateway={profile.Realm.ToIniString()}");
        sb.AppendLine($"character={profile.Character}");
        sb.AppendLine($"ScriptPath={scriptPath}");
        sb.AppendLine("DefaultGameScript=default.dbj");
        sb.AppendLine($"DefaultStarterScript={entryScript}");
        sb.AppendLine($"spdifficulty={difficulty}");
        sb.AppendLine();
    }
}
