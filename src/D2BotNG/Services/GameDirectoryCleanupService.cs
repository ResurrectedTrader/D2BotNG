using D2BotNG.Data;

namespace D2BotNG.Services;

/// <summary>
/// Background service that periodically deletes old screenshots and BlizzardError
/// crash log directories from each framework's game directory. Retention is
/// configured per framework (<c>Framework.screenshot_retention_days</c> /
/// <c>Framework.crash_log_retention_days</c>); 0 disables that cleanup.
/// </summary>
public class GameDirectoryCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);

    private readonly ILogger<GameDirectoryCleanupService> _logger;
    private readonly FrameworkRepository _frameworkRepository;

    public GameDirectoryCleanupService(
        ILogger<GameDirectoryCleanupService> logger,
        FrameworkRepository frameworkRepository)
    {
        _logger = logger;
        _frameworkRepository = frameworkRepository;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Game directory cleanup service started");

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Game directory cleanup pass failed");
            }

            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Game directory cleanup service stopped");
    }

    private async Task RunCleanupAsync()
    {
        var frameworks = await _frameworkRepository.GetAllAsync();

        // Group by each framework's game directory (two frameworks can share an install).
        // For a shared directory, clean using the longest retention among the frameworks
        // that enable cleanup (retention > 0). Note a framework that disables cleanup (0)
        // does NOT shield a shared directory — a co-located framework's window still applies
        // to it. This is the framework's declared install directory, independent of where
        // individual profiles' executables (d2_path) actually live.
        var groups = frameworks
            .Select(f => new
            {
                Dir = f.GameDirectory,
                Screenshot = f.ScreenshotRetentionDays,
                CrashLog = f.CrashLogRetentionDays
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Dir) && Directory.Exists(x.Dir))
            .GroupBy(x => Path.GetFullPath(x.Dir), StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        foreach (var group in groups)
        {
            var installPath = group.Key;
            var screenshotDays = group.Select(x => x.Screenshot).Where(d => d > 0).DefaultIfEmpty(0).Max();
            var crashLogDays = group.Select(x => x.CrashLog).Where(d => d > 0).DefaultIfEmpty(0).Max();

            if (screenshotDays > 0)
            {
                CleanScreenshots(installPath, now - TimeSpan.FromDays(screenshotDays));
            }

            if (crashLogDays > 0)
            {
                CleanCrashLogs(installPath, now - TimeSpan.FromDays(crashLogDays));
            }
        }
    }

    private void CleanScreenshots(string installPath, DateTime cutoffUtc)
    {
        var deleted = 0;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(installPath, "Screenshot*.jpg", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enumerate screenshots in {Path}", installPath);
            return;
        }

        foreach (var file in files)
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoffUtc)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to delete screenshot {File}", file);
            }
        }

        if (deleted > 0)
        {
            _logger.LogInformation("Deleted {Count} old screenshot(s) from {Path}", deleted, installPath);
        }
    }

    private void CleanCrashLogs(string installPath, DateTime cutoffUtc)
    {
        var crashDir = Path.Combine(installPath, "BlizzardError");
        if (!Directory.Exists(crashDir)) return;

        var deleted = 0;
        IEnumerable<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(crashDir);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enumerate crash logs in {Path}", crashDir);
            return;
        }

        foreach (var dir in dirs)
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(dir) < cutoffUtc)
                {
                    Directory.Delete(dir, recursive: true);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to delete crash log directory {Dir}", dir);
            }
        }

        if (deleted > 0)
        {
            _logger.LogInformation("Deleted {Count} old crash log(s) from {Path}", deleted, crashDir);
        }
    }
}
