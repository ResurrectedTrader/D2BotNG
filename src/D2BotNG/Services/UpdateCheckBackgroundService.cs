namespace D2BotNG.Services;

/// <summary>
/// Background service that periodically checks for updates
/// </summary>
public class UpdateCheckBackgroundService : BackgroundService
{
    private readonly ILogger<UpdateCheckBackgroundService> _logger;
    private readonly UpdateManager _updateManager;
    private readonly TimeSpan _checkInterval;

    public UpdateCheckBackgroundService(
        ILogger<UpdateCheckBackgroundService> logger,
        UpdateManager updateManager)
    {
        _logger = logger;
        _updateManager = updateManager;
        _checkInterval = TimeSpan.FromHours(6);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Update check service started. Will check every {Hours} hours", _checkInterval.TotalHours);

        // Initial check after a short delay (let the app fully start)
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Running scheduled update check");
                await _updateManager.CheckForUpdateAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Scheduled update check failed");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Update check service stopped");
    }
}
