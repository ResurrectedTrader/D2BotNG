namespace D2BotNG.Engine;

/// <summary>
/// Hosted service that initializes and manages the profile and schedule engines
/// </summary>
public class EngineHostedService : IHostedService
{
    private readonly ProfileEngine _profileEngine;
    private readonly ScheduleEngine _scheduleEngine;

    public EngineHostedService(
        ProfileEngine profileEngine,
        ScheduleEngine scheduleEngine)
    {
        _profileEngine = profileEngine;
        _scheduleEngine = scheduleEngine;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _profileEngine.InitializeAsync();
        _scheduleEngine.Start();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _scheduleEngine.Dispose();
        _profileEngine.Dispose();
        return Task.CompletedTask;
    }
}
