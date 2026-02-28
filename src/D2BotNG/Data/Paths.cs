using D2BotNG.Core.Protos;

namespace D2BotNG.Data;

/// <summary>
/// Provides centralized path resolution for bot data directories.
/// Uses configurable bot directory from settings, falling back to AppContext.BaseDirectory.
/// </summary>
public class Paths
{
    private readonly SettingsRepository _settingsRepository;
    public string BasePath { get; private set; }

    public Paths(SettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository;
        BasePath = ResolveBasePath(settingsRepository.GetAsync().GetAwaiter().GetResult());
        _settingsRepository.SettingsChanged += (_, settings) => BasePath = ResolveBasePath(settings);
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

    /// <summary>
    /// Gets the mules directory for storing item logs.
    /// </summary>
    public string MulesDirectory
    {
        get
        {
            var dir = Path.Combine(D2BSDirectory, "kolbot", "mules");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// Gets the d2bs directory path.
    /// </summary>
    public string D2BSDirectory
    {
        get
        {
            var dir = Path.Combine(BasePath, "d2bs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
