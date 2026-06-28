using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using D2BotNG.Engine.Handoff;
using static D2BotNG.Windows.NativeMethods;
using static D2BotNG.Windows.NativeTypes;

namespace D2BotNG.Windows;

public class ProcessManager : IDisposable
{
    private readonly ILogger<ProcessManager> _logger;
    private readonly DaclOverwriter _daclOverwriter;
    private nint _jobHandle;

    public ProcessManager(ILogger<ProcessManager> logger, DaclOverwriter daclOverwriter, HandoffContext handoffContext)
    {
        _logger = logger;
        _daclOverwriter = daclOverwriter;

        if (handoffContext.IsTakeover && handoffContext.AdoptedJobHandle != 0)
        {
            // Adopt the job handle duplicated from the predecessor process. The job
            // already has KILL_ON_JOB_CLOSE configured and all running games assigned.
            _jobHandle = handoffContext.AdoptedJobHandle;
            logger.LogInformation("Adopted job handle {Handle} from predecessor", _jobHandle);
            return;
        }

        // Create a job object with KILL_ON_JOB_CLOSE so all child game processes
        // are automatically terminated if D2BotNG exits or crashes.
        _jobHandle = CreateJobObjectW(0, null);
        if (_jobHandle != 0)
        {
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            if (!SetInformationJobObject(_jobHandle, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf(info)))
            {
                logger.LogWarning("Failed to configure job object, child processes may orphan on crash");
            }
        }
        else
        {
            logger.LogWarning("Failed to create job object, child processes may orphan on crash");
        }
    }

    /// <summary>
    /// Returns the underlying job handle so it can be duplicated to a successor process during handoff.
    /// </summary>
    public nint GetJobHandle() => _jobHandle;

    /// <summary>
    /// Ensures the current process can open the given target with at least
    /// <c>PROCESS_QUERY_INFORMATION | SYNCHRONIZE</c>. If a direct <c>OpenProcess</c> fails,
    /// rewrites the target's DACL via owner-rights <c>WRITE_DAC</c> and retries.
    /// Used after handoff to re-grant access to games that were launched by the predecessor.
    /// </summary>
    public bool EnsureAccess(Process process)
    {
        var handle = OpenProcess(PROCESS_QUERY_INFORMATION | SYNCHRONIZE, false, process.Id);
        if (handle != 0)
        {
            CloseHandle(handle);
            return true;
        }

        _logger.LogDebug("OpenProcess failed for {Pid}, attempting DACL overwrite", process.Id);
        if (!_daclOverwriter.OverwriteDacl(process)) return false;

        handle = OpenProcess(PROCESS_QUERY_INFORMATION | SYNCHRONIZE, false, process.Id);
        if (handle == 0)
        {
            _logger.LogWarning("Still cannot open {Pid} after DACL overwrite (error {Error})",
                process.Id, Marshal.GetLastWin32Error());
            return false;
        }
        CloseHandle(handle);
        return true;
    }

    public async Task<bool> InjectDllAsync(Process process, string dllPath)
    {
        if (!File.Exists(dllPath))
        {
            _logger.LogError("DLL not found: {Path}", dllPath);
            return false;
        }

        var fullPath = Path.GetFullPath(dllPath);
        // UTF-16 + LoadLibraryW so non-ASCII paths (e.g. an accented user name) aren't mangled to '?'.
        var pathBytes = Encoding.Unicode.GetBytes(fullPath + '\0');

        try
        {
            var rawProcessHandle = OpenProcess(PROCESS_ALL_ACCESS, false, process.Id);
            if (rawProcessHandle == 0)
            {
                // Try DACL overwrite to gain access
                _logger.LogDebug("OpenProcess failed for {Pid}, attempting DACL overwrite", process.Id);
                if (!_daclOverwriter.OverwriteDacl(process))
                {
                    _logger.LogError("Failed to open process {Pid} for injection", process.Id);
                    return false;
                }

                // Retry after DACL overwrite
                rawProcessHandle = OpenProcess(PROCESS_ALL_ACCESS, false, process.Id);
                if (rawProcessHandle == 0)
                {
                    _logger.LogError("Failed to open process {Pid} even after DACL overwrite", process.Id);
                    return false;
                }
            }

            using var processHandle = new SafeProcessHandle(rawProcessHandle, ownsHandle: true);

            // Allocate memory in target process
            var remoteMemory = VirtualAllocEx(processHandle.DangerousGetHandle(), 0, (nuint)pathBytes.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (remoteMemory == 0)
            {
                _logger.LogError("Failed to allocate memory in target process");
                return false;
            }

            try
            {
                // Write DLL path
                if (!WriteProcessMemory(processHandle.DangerousGetHandle(), remoteMemory, pathBytes, (nuint)pathBytes.Length, out _))
                {
                    _logger.LogError("Failed to write DLL path to target process");
                    return false;
                }

                // Resolve LoadLibraryW for the target. Same-bitness targets use our own kernel32
                // (shared base); a 32-bit target gets the 32-bit kernel32 address from a peer WOW64 process.
                var loadLibraryAddr = RemoteModule.ResolveExportForTarget(processHandle.DangerousGetHandle(), "kernel32.dll", "LoadLibraryW");
                if (loadLibraryAddr == 0)
                {
                    _logger.LogError("Failed to resolve LoadLibraryW for target process {Pid}", process.Id);
                    return false;
                }

                // Create remote thread
                var rawThreadHandle = CreateRemoteThread(processHandle.DangerousGetHandle(), 0, 0, loadLibraryAddr, remoteMemory, 0, out _);
                if (rawThreadHandle == 0)
                {
                    _logger.LogError("Failed to create remote thread");
                    return false;
                }

                using var threadHandle = new SafeProcessHandle(rawThreadHandle, ownsHandle: true);
                // Poll instead of blocking so the thread is released between checks
                await WaitForSingleObjectAsync(threadHandle.DangerousGetHandle(), TimeSpan.FromSeconds(5));

                _logger.LogDebug("Successfully injected {Dll} into process {Pid}", dllPath, process.Id);
                return true;
            }
            finally
            {
                VirtualFreeEx(processHandle.DangerousGetHandle(), remoteMemory, 0, MEM_RELEASE);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inject DLL into process {Pid}", process.Id);
            return false;
        }
    }

    public async Task TerminateAsync(Process process, TimeSpan gracePeriod, CancellationToken cancellationToken = default)
    {
        try
        {
            if (process.HasExited) return;
        }
        catch (InvalidOperationException)
        {
            // Process already gone (e.g. killed by job object)
            return;
        }

        // Try graceful close first. PostMessage WM_CLOSE to every top-level window
        // owned by the PID — Process.MainWindowHandle may have drifted post-handoff
        // to a window that won't act on close (D2 has multiple top-level windows over
        // its lifetime). Broadcasting catches the real game window regardless.
        var windows = process.TopLevelWindows;
        foreach (var hwnd in windows)
        {
            PostMessage(hwnd, WM_CLOSE, 0, 0);
        }

        if (windows.Count > 0)
        {
            var deadline = DateTime.UtcNow + gracePeriod;
            while (!process.HasExited && DateTime.UtcNow < deadline)
            {
                try { await Task.Delay(100, cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        // Force kill if still running
        if (!process.HasExited)
        {
            _logger.LogWarning("Process {Pid} did not exit gracefully, killing", process.Id);
            process.Kill();
        }
    }

    public void ShowWindow(nint hwnd)
    {
        // Async variants post the request to the window's thread and return
        // immediately, so a hung game window can never block the caller (the
        // synchronous ShowWindow/SetWindowPos would block until USER32 gives up).
        ShowWindowAsync(hwnd, SW_SHOW);
    }

    public void HideWindow(nint hwnd)
    {
        ShowWindowAsync(hwnd, SW_HIDE);
    }

    public void SetWindowTitle(nint hwnd, string title)
    {
        // Bounded WM_SETTEXT (the string is marshaled cross-process) rather than
        // SetWindowText, which sends synchronously and blocks on a hung window.
        var ptr = Marshal.StringToHGlobalUni(title);
        try
        {
            SendMessageTimeoutW(hwnd, WM_SETTEXT, 0, ptr, SMTO_ABORTIFHUNG, 1000, out _);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public void MoveWindow(nint hwnd, int x, int y)
    {
        SetWindowPos(hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_ASYNCWINDOWPOS);
    }


    public void ShowWindowAt(nint hwnd, int x, int y)
    {
        SetWindowPos(hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_SHOWWINDOW | SWP_ASYNCWINDOWPOS);
    }

    public IEnumerable<nint> FindWindowsByTitle(string title)
    {
        var windows = new List<nint>();
        nint hwnd = 0;

        while (true)
        {
            hwnd = FindWindowEx(0, hwnd, null, title);
            if (hwnd == 0) break;
            windows.Add(hwnd);
        }

        return windows;
    }

    public Process? CreateSuspended(string path, string arguments, string workingDirectory)
    {
        var commandLine = $"\"{path}\" {arguments}";

        var startupInfo = new STARTUPINFOW { cb = (uint)Marshal.SizeOf<STARTUPINFOW>() };

        if (!CreateProcessW(
            path,
            commandLine,
            0,
            0,
            false,
            CREATE_SUSPENDED,
            0,
            workingDirectory,
            ref startupInfo,
            out var processInfo))
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogError("CreateProcess failed with error {Error}. Command line: '{CommandLine}', working directory: '{WorkingDirectory}'", error, commandLine, workingDirectory);
            return null;
        }

        // Assign to job object before closing handles — process is still suspended,
        // so it's guaranteed to be in the job before it runs any code.
        if (_jobHandle != 0)
        {
            if (!AssignProcessToJobObject(_jobHandle, processInfo.hProcess))
            {
                _logger.LogWarning("Failed to assign process {Pid} to job object", processInfo.dwProcessId);
            }
        }

        // Close handles - we'll use Process object instead
        using (new SafeProcessHandle(processInfo.hThread, ownsHandle: true)) { }
        using (new SafeProcessHandle(processInfo.hProcess, ownsHandle: true)) { }

        try
        {
            return Process.GetProcessById((int)processInfo.dwProcessId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get process by ID {Pid}", processInfo.dwProcessId);
            return null;
        }
    }

    public void ResumeProcess(Process process)
    {
        process.Refresh();
        foreach (ProcessThread thread in process.Threads)
        {
            var rawThreadHandle = OpenThread(THREAD_SUSPEND_RESUME, false, (uint)thread.Id);
            if (rawThreadHandle == 0) continue;

            using var threadHandle = new SafeProcessHandle(rawThreadHandle, ownsHandle: true);
            ResumeThread(threadHandle.DangerousGetHandle());
        }
    }

    public void Dispose()
    {
        if (_jobHandle != 0)
        {
            CloseHandle(_jobHandle);
            _jobHandle = 0;
        }
    }
}
