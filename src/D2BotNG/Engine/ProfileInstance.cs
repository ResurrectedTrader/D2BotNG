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
    public string ProfileName { get; set; }
    public RunState State { get; private set; } = RunState.Stopped;
    public Process? Process { get; private set; }
    public string Status { get; set; } = "";
    public DateTime? StartedAt { get; private set; }
    public DateTime? LastHeartbeat { get; private set; }
    public int CrashCount { get; set; }
    public int MissedHeartbeats { get; set; }

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private CancellationTokenSource? _runCts;

    // Key tracking
    public string? KeyName { get; set; }

    public ProfileInstance(string profileName)
    {
        ProfileName = profileName;
    }

    public async Task<bool> TransitionToAsync(RunState newState)
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

    public async Task SetErrorAsync(string error)
    {
        await _stateLock.WaitAsync();
        try
        {
            State = RunState.Error;
            Status = error;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public ProfileState GetState()
    {
        nint hwnd = Process?.MainWindowHandle ?? 0;
        return new ProfileState
        {
            ProfileName = ProfileName,
            State = State,
            Status = Status,
            KeyName = KeyName ?? "",
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

    private static bool IsValidTransition(RunState from, RunState to)
    {
        return (from, to) switch
        {
            (RunState.Stopped, RunState.Starting) => true,
            (RunState.Starting, RunState.Running) => true,
            (RunState.Starting, RunState.Error) => true,
            (RunState.Running, RunState.Stopping) => true,
            (RunState.Running, RunState.Error) => true,
            (RunState.Stopping, RunState.Stopped) => true,
            (RunState.Error, RunState.Stopping) => true,
            (RunState.Error, RunState.Stopped) => true,
            (RunState.Error, RunState.Starting) => true,
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
