using D2BotNG.Core.Protos;
using D2BotNG.Legacy.Models;
using D2BotNG.Logging;
using D2BotNG.Utilities;
using Google.Protobuf;
using ILogger = Serilog.ILogger;

namespace D2BotNG.Data;

public class SettingsRepository
{
    private static readonly ILogger Logger = TrackingLoggerFactory.ForContext(typeof(SettingsRepository));

    private readonly string _filePath = Path.Combine(AppContext.BaseDirectory, "d2botng.json");
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly DataWriteGate _writeGate;
    private Settings? _settings;
    private bool _loaded;

    public SettingsRepository(DataWriteGate writeGate)
    {
        _writeGate = writeGate;
    }

    /// <summary>
    /// Pre-frameworks game/engine values recovered from a v0 settings file during load,
    /// consumed by FrameworkBootstrap to seed the Default framework. Null for v1+ files.
    /// </summary>
    public SettingsMigrator.LegacyGameEngine? LegacySettings { get; private set; }

    /// <summary>
    /// Raised when settings are updated via UpdateAsync.
    /// </summary>
    public event EventHandler<Settings>? SettingsChanged;

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
                try
                {
                    _settings = ProtobufJsonConfig.Parser.Parse<Settings>(json);
                }
                catch (InvalidProtocolBufferException ex)
                {
                    // Corrupt/truncated settings (see AtomicFile). This is the first
                    // file read at startup, so throwing would make every launch die —
                    // quarantine and boot with defaults instead.
                    QuarantineCorruptFile(ex);
                    _settings = CreateDefault();
                }

                if (_settings.SchemaVersion < SettingsMigrator.CurrentVersion)
                {
                    // Recover the pre-frameworks game/engine values (parsed into the
                    // archived old shape) for FrameworkBootstrap to seed the Default
                    // framework. We deliberately do NOT rewrite the file here: leaving it
                    // at its old schema version keeps these values recoverable if startup
                    // fails before the framework migration completes. The file upgrades on
                    // the next save (SaveInternalAsync always stamps the version), by which
                    // point the bootstrap has already consumed these values.
                    LegacySettings = SettingsMigrator.CaptureLegacy(json, _settings.SchemaVersion);
                    Logger.Information(
                        "Loaded {File} at schema v{Version}; recovered legacy game/engine settings for framework migration",
                        Path.GetFileName(_filePath), _settings.SchemaVersion);
                }
            }
            else
            {
                _settings = CreateDefault();
            }

            EnsureDefaults(_settings);

            _settings.LegacyApi ??= await Migration.MigrateLegacyApiAsync(_settings) ?? new LegacyApiSettings();

            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void QuarantineCorruptFile(Exception ex)
    {
        var quarantinePath = _filePath + ".corrupt";
        try
        {
            File.Move(_filePath, quarantinePath, overwrite: true);
            Logger.Warning(ex,
                "Settings file {FilePath} is unreadable; quarantined to {QuarantinePath} and starting with defaults. It will be rewritten on the next save",
                _filePath, quarantinePath);
        }
        catch (Exception moveEx)
        {
            Logger.Error(moveEx,
                "Settings file {FilePath} is unreadable and could not be quarantined; starting with defaults",
                _filePath);
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
            LegacyApi = new LegacyApiSettings(),
            BasePath = AppContext.BaseDirectory,
            MinimizeToTray = true,
            SchemaVersion = SettingsMigrator.CurrentVersion,
        };
    }

    private static void EnsureDefaults(Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.BasePath))
        {
            settings.BasePath = AppContext.BaseDirectory;
        }

        if (!settings.HasMinimizeToTray)
        {
            settings.MinimizeToTray = true;
        }

        settings.Startup ??= new StartupSettings();
    }

    public async Task<Settings> GetAsync()
    {
        await EnsureLoadedAsync();
        return _settings!;
    }

    /// <summary>
    /// The loaded settings. Program.Main loads them once at startup, before anything that
    /// reads them is resolved, so this is a property read rather than sync-over-async.
    /// Throws if read earlier, which would mean a new caller resolved ahead of that load.
    /// </summary>
    public Settings Current => _loaded
        ? _settings!
        : throw new InvalidOperationException(
            "Settings have not been loaded yet. Program.Main loads them at startup; "
            + "await GetAsync() if you genuinely need them before that.");

    public async Task<Settings> UpdateAsync(Settings settings)
    {
        await _lock.WaitAsync();
        try
        {
            EnsureDefaults(settings);
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
        if (_writeGate.IsClosed)
        {
            // A successor owns the settings file now — and it may already have upgraded
            // its schema. Writing our copy back would undo that. See DataWriteGate.
            Logger.Debug("Handoff in progress; not writing {FilePath}", _filePath);
            return;
        }

        // Every save writes the current schema version so the file stays stamped.
        _settings!.SchemaVersion = SettingsMigrator.CurrentVersion;
        var json = ProtobufJsonConfig.Formatter.Format(_settings);
        await AtomicFile.WriteAllTextAsync(_filePath, json);
    }
}
