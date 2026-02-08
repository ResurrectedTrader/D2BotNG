namespace D2BotNG.Data;

/// <summary>
/// Provides centralized path resolution for bot data directories.
/// Uses configurable bot directory from settings, falling back to AppContext.BaseDirectory.
/// </summary>
public class Paths
{
    private readonly SettingsRepository _settingsRepository;

    public Paths(SettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository;
    }

    /// <summary>
    /// Gets the base path. Returns the configured BasePath if set, otherwise AppContext.BaseDirectory.
    /// </summary>
    private string BasePath
    {
        get
        {
            var settings = _settingsRepository.GetAsync().GetAwaiter().GetResult();
            return string.IsNullOrWhiteSpace(settings.BasePath)
                ? AppContext.BaseDirectory
                : settings.BasePath;
        }
    }

    /// <summary>
    /// Gets the data directory for storing profiles, keys, schedules, etc.
    /// </summary>
    public string DataDirectory
    {
        get
        {
            var dir = Path.Combine(BasePath, "data", "ng");
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
            var dir = Path.Combine(BasePath, "d2bs", "kolbot", "mules");
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
