using System.Diagnostics;
using D2BotNG.Core.Protos;
using static D2BotNG.Windows.NativeMethods;

namespace D2BotNG.Engine;

/// <summary>
/// Represents a running profile instance with its associated game process.
/// Holds only runtime state — profile data is always read from the repository.
/// </summary>
public class ProfileInstance : IDisposable
{
    public string ProfileName { get; private set; }
    public ProfileState State { get; private set; } = ProfileState.Stopped;
    public Process? Process { get; private set; }
    public string Status { get; private set; } = "";
    public DateTime? StartedAt { get; private set; }
    public DateTime? LastHeartbeat { get; private set; }
    public int CrashCount { get; private set; }
    public int MissedHeartbeats { get; private set; }

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private CancellationTokenSource? _runCts;

    // Key tracking
    public string? CurrentKeyName { get; private set; }

    public ProfileInstance(string profileName)
    {
        ProfileName = profileName;
    }

    public async Task<bool> TransitionToAsync(ProfileState newState)
    {
        await _stateLock.WaitAsync();
        try
        {
            // Validate transition
            if (!IsValidTransition(State, newState))
            {
                return false;
            }

            State = newState;
            return true;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public void SetGameProcess(Process process)
    {
        Process = process;
        StartedAt = DateTime.UtcNow;
        LastHeartbeat = null;
    }

    public void UpdateHeartbeat()
    {
        LastHeartbeat = DateTime.UtcNow;
        MissedHeartbeats = 0;
    }

    public void IncrementCrashes()
    {
        CrashCount++;
    }

    public void ResetCrashCount()
    {
        CrashCount = 0;
    }

    public void IncrementMissedHeartbeats()
    {
        MissedHeartbeats++;
    }

    public void SetStatus(string status)
    {
        Status = status;
    }

    public async Task SetErrorAsync(string error)
    {
        await _stateLock.WaitAsync();
        try
        {
            State = ProfileState.Error;
            Status = error;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public void SetKey(string keyName)
    {
        CurrentKeyName = keyName;
    }

    public void ClearKey()
    {
        CurrentKeyName = null;
    }

    public void Rename(string newName)
    {
        ProfileName = newName;
    }

    public ProfileStatus GetStatus()
    {
        nint hwnd = Process?.MainWindowHandle ?? 0;
        return new ProfileStatus
        {
            ProfileName = ProfileName,
            State = State,
            Status = Status,
            CurrentKey = CurrentKeyName ?? "",
            WindowVisible = hwnd != 0 && IsWindowVisible(hwnd)
        };
    }

    public CancellationToken GetCancellationToken()
    {
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        return _runCts.Token;
    }

    public void CancelRun()
    {
        _runCts?.Cancel();
    }

    private static bool IsValidTransition(ProfileState from, ProfileState to)
    {
        return (from, to) switch
        {
            (ProfileState.Stopped, ProfileState.Starting) => true,
            (ProfileState.Starting, ProfileState.Running) => true,
            (ProfileState.Starting, ProfileState.Error) => true,
            (ProfileState.Running, ProfileState.Busy) => true,
            (ProfileState.Running, ProfileState.Stopping) => true,
            (ProfileState.Running, ProfileState.Error) => true,
            (ProfileState.Busy, ProfileState.Running) => true,
            (ProfileState.Busy, ProfileState.Stopping) => true,
            (ProfileState.Busy, ProfileState.Error) => true,
            (ProfileState.Stopping, ProfileState.Stopped) => true,
            (ProfileState.Error, ProfileState.Stopping) => true,
            (ProfileState.Error, ProfileState.Stopped) => true,
            (ProfileState.Error, ProfileState.Starting) => true,
            _ => false
        };
    }

    public void Dispose()
    {
        _runCts?.Dispose();
        Process?.Dispose();
        _stateLock.Dispose();
    }
}
