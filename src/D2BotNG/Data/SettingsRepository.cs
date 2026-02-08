using D2BotNG.Core.Protos;
using Google.Protobuf;
using Microsoft.Win32;

namespace D2BotNG.Data;

public class SettingsRepository
{
    private readonly string _filePath = Path.Combine(AppContext.BaseDirectory, "d2botng.json");
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Settings? _settings;
    private bool _loaded;

    /// <summary>
    /// Raised when settings are updated via UpdateAsync.
    /// </summary>
    public event EventHandler<Settings>? SettingsChanged;

    private static readonly JsonFormatter JsonFormatter = new(JsonFormatter.Settings.Default.WithIndentation());
    private static readonly JsonParser JsonParser = new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        await _lock.WaitAsync();
        try
        {
            if (_loaded) return;

            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath);
                _settings = JsonParser.Parse<Settings>(json);
            }
            _settings ??= CreateDefault();
            EnsureDefaults(_settings);
            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static Settings CreateDefault()
    {
        return new Settings
        {
            Server = new ServerSettings
            {
                Host = "127.0.0.1",
                Port = 5000
            },
            Discord = new DiscordSettings(),
            Display = new DisplaySettings
            {
                ShowItemHeader = true,
            },
            Limedrop = new LimedropSettings
            {
                Ip = "127.0.0.1",
                Port = 8080
            },
            Game = new GameSettings
            {
                D2InstallPath = GetDefaultD2InstallPath(),
            },
            BasePath = AppContext.BaseDirectory,
        };
    }

    private static string GetDefaultD2InstallPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Blizzard Entertainment\Diablo II");
            var installPath = key?.GetValue("InstallPath")?.ToString();
            if (!string.IsNullOrEmpty(installPath))
                return installPath;
        }
        catch
        {
            // Registry access failed, fall back to desktop
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    private static void EnsureDefaults(Settings settings)
    {
        // Ensure Game config exists with default D2 install path
        if (settings.Game == null)
        {
            settings.Game = new GameSettings { D2InstallPath = GetDefaultD2InstallPath(), GameVersion = "1.14d" };
        }
        else
        {
            if (string.IsNullOrEmpty(settings.Game.D2InstallPath))
            {
                settings.Game.D2InstallPath = GetDefaultD2InstallPath();
            }
            if (string.IsNullOrEmpty(settings.Game.GameVersion))
            {
                settings.Game.GameVersion = "1.14d";
            }
        }

        if (string.IsNullOrWhiteSpace(settings.BasePath))
        {
            settings.BasePath = AppContext.BaseDirectory;
        }
    }

    public async Task<Settings> GetAsync()
    {
        await EnsureLoadedAsync();
        return _settings!;
    }

    public async Task<Settings> UpdateAsync(Settings settings)
    {
        await _lock.WaitAsync();
        try
        {
            _settings = settings;
            await SaveInternalAsync();
            SettingsChanged?.Invoke(this, settings);
            return settings;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveInternalAsync()
    {
        var json = JsonFormatter.Format(_settings);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
