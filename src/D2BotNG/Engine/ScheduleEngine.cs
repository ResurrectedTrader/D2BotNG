using D2BotNG.Core.Protos;
using D2BotNG.Data;

namespace D2BotNG.Engine;

/// <summary>
/// Engine for managing schedule-based profile activation
/// </summary>
public class ScheduleEngine : IDisposable
{
    private readonly ILogger<ScheduleEngine> _logger;
    private readonly ScheduleRepository _scheduleRepository;
    private readonly ProfileRepository _profileRepository;
    private readonly ProfileEngine _profileEngine;
    private readonly CancellationTokenSource _shutdownCts = new();
    private Task? _monitorTask;

    public ScheduleEngine(
        ILogger<ScheduleEngine> logger,
        ScheduleRepository scheduleRepository,
        ProfileRepository profileRepository,
        ProfileEngine profileEngine)
    {
        _logger = logger;
        _scheduleRepository = scheduleRepository;
        _profileRepository = profileRepository;
        _profileEngine = profileEngine;
    }

    public void Start()
    {
        _monitorTask = Task.Run(MonitorSchedulesAsync);
        _logger.LogInformation("Schedule engine started");
    }

    private async Task MonitorSchedulesAsync()
    {
        while (!_shutdownCts.IsCancellationRequested)
        {
            try
            {
                await CheckSchedulesAsync();
                await Task.Delay(TimeSpan.FromMinutes(1), _shutdownCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking schedules");
            }
        }
    }

    private async Task CheckSchedulesAsync()
    {
        var now = DateTime.Now;
        var currentHour = (uint)now.Hour;
        var currentMinute = (uint)now.Minute;

        var profiles = await _profileRepository.GetAllAsync();
        var schedules = await _scheduleRepository.GetAllAsync();

        foreach (var profile in profiles.Where(p => p.ScheduleEnabled && !string.IsNullOrEmpty(p.Schedule)))
        {
            var schedule = schedules.FirstOrDefault(s => s.Name == profile.Schedule);
            if (schedule == null) continue;

            var shouldRun = IsWithinSchedule(schedule, currentHour, currentMinute);
            var instance = _profileEngine.GetInstance(profile.Name);

            if (instance == null) continue;

            if (shouldRun && instance.State == ProfileState.Stopped)
            {
                _logger.LogInformation("Schedule starting profile {Name}", profile.Name);
                await _profileEngine.StartProfileAsync(profile.Name);
            }
            else if (!shouldRun && instance.State is ProfileState.Running or ProfileState.Busy)
            {
                _logger.LogInformation("Schedule stopping profile {Name}", profile.Name);
                await _profileEngine.StopProfileAsync(profile.Name);
            }
        }
    }

    private static bool IsWithinSchedule(Schedule schedule, uint hour, uint minute)
    {
        var currentMinutes = hour * 60 + minute;

        foreach (var period in schedule.Periods)
        {
            var startMinutes = period.StartHour * 60 + period.StartMinute;
            var endMinutes = period.EndHour * 60 + period.EndMinute;

            if (endMinutes > startMinutes)
            {
                // Normal case: start < end (e.g., 9:00 - 17:00)
                if (currentMinutes >= startMinutes && currentMinutes < endMinutes)
                    return true;
            }
            else
            {
                // Overnight case: start > end (e.g., 22:00 - 06:00)
                if (currentMinutes >= startMinutes || currentMinutes < endMinutes)
                    return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        _shutdownCts.Cancel();
        _monitorTask?.Wait(TimeSpan.FromSeconds(5));
        _shutdownCts.Dispose();
    }
}
