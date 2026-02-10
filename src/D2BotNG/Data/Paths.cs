namespace D2BotNG.Data;

/// <summary>
/// Provides centralized path resolution for bot data directories.
/// Uses configurable bot directory from settings, falling back to AppContext.BaseDirectory.
/// </summary>
public class Paths
{
    private readonly SettingsRepository _settingsRepository;
    private string _basePath;

    public Paths(SettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository;
        _basePath = ResolveBasePath(settingsRepository.GetAsync().GetAwaiter().GetResult());
        _settingsRepository.SettingsChanged += (_, settings) => _basePath = ResolveBasePath(settings);
    }

    private static string ResolveBasePath(Core.Protos.Settings settings) =>
        string.IsNullOrWhiteSpace(settings.BasePath)
            ? AppContext.BaseDirectory
            : settings.BasePath;

    /// <summary>
    /// Gets the data directory for storing profiles, keys, schedules, etc.
    /// </summary>
    public string DataDirectory
    {
        get
        {
            var dir = Path.Combine(_basePath, "data", "ng");
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
            var dir = Path.Combine(_basePath, "d2bs", "kolbot", "mules");
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
            var dir = Path.Combine(_basePath, "d2bs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
