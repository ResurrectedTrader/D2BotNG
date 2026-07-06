using D2BotNG.Core.Protos;

namespace D2BotNG.Data;

/// <summary>
/// Path helpers derived from a <see cref="Framework"/>. A framework's <c>d2bs_path</c>
/// is the botting-framework directory (equivalent to the old global <c>&lt;base&gt;/d2bs</c>)
/// that contains d2bs.ini, the inject DLL, and kolbot/.
/// </summary>
public static class FrameworkPaths
{
    private const string DefaultDllName = "D2BS.dll";

    /// <summary>
    /// Absolute paths of the DLLs to inject, in order. Relative entries resolve against
    /// d2bs_path. When none are configured, defaults to a single "D2BS.dll".
    /// </summary>
    public static IReadOnlyList<string> DllFullPaths(this Framework framework)
    {
        var dlls = framework.DllPaths
            .Select(d => d.Trim())
            .Where(d => d.Length > 0)
            .ToList();
        if (dlls.Count == 0)
        {
            dlls.Add(DefaultDllName);
        }

        return dlls
            .Select(dll => Path.IsPathRooted(dll) ? dll : Path.Combine(framework.D2BsPath, dll))
            .ToList();
    }

    /// <summary>Whether the framework reads the manager-written d2bs.ini (absent = true).</summary>
    public static bool UsesIniOrDefault(this Framework framework) =>
        !framework.HasUsesIni || framework.UsesIni;

    /// <summary>Seconds without a heartbeat before a miss (absent = 30). 0 or less = watchdog disabled.</summary>
    public static int HeartbeatTimeoutOrDefault(this Framework framework) =>
        Math.Max(0, framework.HasHeartbeatTimeoutSeconds ? framework.HeartbeatTimeoutSeconds : 30);

    /// <summary>Consecutive missed heartbeats before restart (absent = 3, minimum 1).</summary>
    public static int MaxMissedHeartbeatsOrDefault(this Framework framework) =>
        Math.Max(1, framework.HasMaxMissedHeartbeats ? framework.MaxMissedHeartbeats : 3);

    /// <summary>Restart attempts after a crash before giving up (absent = 5, minimum 0).</summary>
    public static int MaxCrashRetriesOrDefault(this Framework framework) =>
        Math.Max(0, framework.HasMaxCrashRetries ? framework.MaxCrashRetries : 5);

    /// <summary>Seconds a game window may be hung before restart (absent = 30). 0 or less = watchdog disabled.</summary>
    public static int UnresponsiveTimeoutOrDefault(this Framework framework) =>
        Math.Max(0, framework.HasUnresponsiveTimeoutSeconds ? framework.UnresponsiveTimeoutSeconds : 30);

    /// <summary>The game version used for memory-patch selection (absent/blank = "1.14d").</summary>
    public static string GameVersionOrDefault(this Framework framework) =>
        string.IsNullOrWhiteSpace(framework.GameVersion) ? "1.14d" : framework.GameVersion;

    /// <summary>The framework's d2bs.ini path.</summary>
    public static string IniPath(this Framework framework) =>
        Path.Combine(framework.D2BsPath, "d2bs.ini");

    /// <summary>The framework's kolbot/mules directory (item logs).</summary>
    public static string MulesDirectory(this Framework framework) =>
        Path.Combine(framework.D2BsPath, "kolbot", "mules");
}
