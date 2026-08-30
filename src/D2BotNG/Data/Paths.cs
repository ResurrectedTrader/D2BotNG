using D2BotNG.Core.Protos;

namespace D2BotNG.Data;

/// <summary>
/// Provides centralized path resolution for bot data directories.
/// Uses configurable bot directory from settings, falling back to AppContext.BaseDirectory.
/// </summary>
public class Paths
{
    public string BasePath { get; private set; }

    public Paths(SettingsRepository settingsRepository)
    {
        BasePath = ResolveBasePath(settingsRepository.Current);
        settingsRepository.SettingsChanged += (_, settings) => BasePath = ResolveBasePath(settings);
    }

    /// <summary>
    /// A fixed base path, for callers that are not settings-driven — a test pointing a store at
    /// a temp directory, rather than the running manager's data folder.
    /// </summary>
    public Paths(string basePath)
    {
        BasePath = basePath;
    }

    private static string ResolveBasePath(Settings settings) =>
        string.IsNullOrWhiteSpace(settings.BasePath)
            ? AppContext.BaseDirectory
            : settings.BasePath;

    /// <summary>
    /// Gets the legacy data directory (data/) used by the old D2Bot framework.
    /// </summary>
    public string LegacyDataDirectory => Path.Combine(BasePath, "data");

    /// <summary>
    /// Gets the data directory for storing profiles, keys, schedules, etc.
    /// </summary>
    public string DataDirectory
    {
        get
        {
            var dir = Path.Combine(LegacyDataDirectory, "ng");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
