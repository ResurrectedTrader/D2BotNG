using System.Diagnostics;
using D2BotNG.Core.Protos;
using D2BotNG.Windows;
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

    /// <summary>
    /// Consecutive failures to get the game up (launch or DLL injection). This is the only
    /// counter that feeds the retry budget, and any successful launch zeroes it — a runtime
    /// fault never consumes it. Mirrors D2Bot#'s <c>Crashed</c>, which was incremented only
    /// from the two LoadRemoteLibrary catch blocks and cleared on every successful load.
    /// </summary>
    public int LaunchFailureCount { get; set; }

    /// <summary>
    /// Consecutive restarts caused by a runtime fault (heartbeat timeout, hung window,
    /// unexpected exit). Drives restart backoff only — never a budget, so a failing bot keeps
    /// being retried, just progressively more slowly.
    /// </summary>
    public int RuntimeRestartCount { get; set; }

    public int MissedHeartbeats { get; set; }

    /// <summary>
    /// The game window handle registered for WM_COPYDATA routing, captured once at launch.
    /// Deliberately stored rather than re-derived: <c>Extensions.GameWindow</c> enumerates the
    /// windows owned by the pid, so a process that has already exited yields 0 — and every
    /// removal keyed on a live re-read silently leaks its routing entry instead.
    /// </summary>
    public nint GameWindowHandle { get; set; }

    private int _tearDownDepth;

    /// <summary>
    /// True from the moment we decide to kill this profile's game until the kill is finished.
    /// Marks the window in which the game is still alive and still routed, but nothing it asks
    /// for should be acted on.
    /// </summary>
    /// <remarks>
    /// Deliberately its own state rather than inferred from <see cref="RunState"/>. Error is not
    /// only a teardown state — it is also crash backoff, a failed launch, and the state an
    /// adopted profile can be restored into by a handoff, where the game is alive and no monitor
    /// is watching. Treating those as teardown made a profile permanently deaf to the very
    /// restart request that would have recovered it. It lives only in memory and cannot survive
    /// a handoff. Set by the three paths that kill a game: a stop, a watchdog kill, and the
    /// cleanup after a failed run.
    /// <para>
    /// A depth counter rather than a bool because two teardowns can overlap on one instance — a
    /// user Stop landing while the watchdog is force-killing the same game. With a bool, whichever
    /// finished first cleared it and left the other running unguarded for the rest of its grace.
    /// </para>
    /// </remarks>
    public bool TearingDown => Volatile.Read(ref _tearDownDepth) > 0;

    /// <summary>Marks the start of a teardown. Pair with <see cref="EndTeardown"/> in a finally.</summary>
    public void BeginTeardown() => Interlocked.Increment(ref _tearDownDepth);

    public void EndTeardown() => Interlocked.Decrement(ref _tearDownDepth);

    /// <summary>When the game window first became continuously unresponsive; null while responsive.</summary>
    public DateTime? UnresponsiveSince { get; set; }

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private CancellationTokenSource? _runCts;

    // Key tracking
    public string? KeyName { get; set; }

    // Proxy currently in use - the address the running game launched with; null = direct
    public string? ProxyName { get; set; }

    public ProfileInstance(string profileName)
    {
        ProfileName = profileName;
    }

    public async Task<bool> TransitionToAsync(RunState newState)
    {
        await _stateLock.WaitAsync();
        try
        {
            if (!IsValidTransition(State, newState))
                return false;

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
        Process?.Dispose();
        Process = process;
        StartedAt = DateTime.UtcNow;
        LastHeartbeat = null;
        UnresponsiveSince = null;
    }

    /// <summary>
    /// Records a heartbeat. <paramref name="at"/> is when the message was *received* on the
    /// message pump, not when it was dispatched — see <see cref="MessageWindow"/>. Passing the
    /// receive time is what keeps a backed-up dispatch queue from looking like a dead bot.
    /// </summary>
    public void UpdateHeartbeat(DateTime? at = null)
    {
        LastHeartbeat = at ?? DateTime.UtcNow;
        MissedHeartbeats = 0;
        // Neither retry counter is reset here. LaunchFailureCount is cleared by a successful
        // launch; RuntimeRestartCount only by a run that stays up long enough to count as
        // healthy (see ProfileEngine.MonitorProcessAsync) — a bot that emits a single heartbeat
        // between failures must not be able to zero its own backoff.
    }

    /// <summary>
    /// Restores instance state from a handoff manifest, attaching to an already-running
    /// game process. Skips the normal state-machine transitions.
    /// </summary>
    public void RestoreFromHandoff(
        Process process,
        RunState state,
        string status,
        string? keyName,
        string? proxyName,
        int launchFailureCount,
        int missedHeartbeats,
        DateTime? startedAt,
        DateTime? lastHeartbeat,
        nint gameWindowHandle)
    {
        Process?.Dispose();
        Process = process;
        State = state;
        Status = status;
        KeyName = keyName;
        ProxyName = proxyName;
        LaunchFailureCount = launchFailureCount;
        GameWindowHandle = gameWindowHandle;
        MissedHeartbeats = missedHeartbeats;
        StartedAt = startedAt;
        LastHeartbeat = lastHeartbeat;
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
        nint hwnd = Process?.GameWindow ?? 0;
        return new ProfileState
        {
            ProfileName = ProfileName,
            State = State,
            Status = Status,
            KeyName = KeyName ?? "",
            ProxyName = ProxyName ?? "",
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
            (RunState.Starting, RunState.Stopping) => true,
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
