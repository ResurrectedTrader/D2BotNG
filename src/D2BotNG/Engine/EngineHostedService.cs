namespace D2BotNG.Engine;

/// <summary>
/// Hosted service that initializes and manages the profile and schedule engines
/// </summary>
public class EngineHostedService : IHostedService
{
    private readonly ILogger<EngineHostedService> _logger;
    private readonly ProfileEngine _profileEngine;
    private readonly ScheduleEngine _scheduleEngine;

    public EngineHostedService(
        ILogger<EngineHostedService> logger,
        ProfileEngine profileEngine,
        ScheduleEngine scheduleEngine)
    {
        _logger = logger;
        _profileEngine = profileEngine;
        _scheduleEngine = scheduleEngine;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing engines...");

        await _profileEngine.InitializeAsync();
        _scheduleEngine.Start();

        _logger.LogInformation("Engines initialized");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping engines...");

        _scheduleEngine.Dispose();
        _profileEngine.Dispose();

        _logger.LogInformation("Engines stopped");
        return Task.CompletedTask;
    }
}
