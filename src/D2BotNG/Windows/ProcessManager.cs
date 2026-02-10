using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static D2BotNG.Windows.NativeMethods;
using static D2BotNG.Windows.NativeTypes;

namespace D2BotNG.Windows;

public class ProcessManager
{
    private readonly ILogger<ProcessManager> _logger;
    private readonly DaclOverwriter _daclOverwriter;

    public ProcessManager(ILogger<ProcessManager> logger, DaclOverwriter daclOverwriter)
    {
        _logger = logger;
        _daclOverwriter = daclOverwriter;
    }

    public bool InjectDll(Process process, string dllPath)
    {
        if (!File.Exists(dllPath))
        {
            _logger.LogError("DLL not found: {Path}", dllPath);
            return false;
        }

        var fullPath = Path.GetFullPath(dllPath);
        var pathBytes = Encoding.ASCII.GetBytes(fullPath + '\0');

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
            var remoteMemory = VirtualAllocEx(processHandle.DangerousGetHandle(), 0, (uint)pathBytes.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (remoteMemory == 0)
            {
                _logger.LogError("Failed to allocate memory in target process");
                return false;
            }

            try
            {
                // Write DLL path
                if (!WriteProcessMemory(processHandle.DangerousGetHandle(), remoteMemory, pathBytes, (uint)pathBytes.Length, out _))
                {
                    _logger.LogError("Failed to write DLL path to target process");
                    return false;
                }

                // Get LoadLibraryA address
                var kernel32 = GetModuleHandle("kernel32.dll");
                var loadLibraryAddr = GetProcAddress(kernel32, "LoadLibraryA");
                if (loadLibraryAddr == 0)
                {
                    _logger.LogError("Failed to get LoadLibraryA address");
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
                WaitForSingleObject(threadHandle.DangerousGetHandle(), 5000);

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

    public async Task TerminateAsync(Process process, TimeSpan gracePeriod)
    {
        if (process.HasExited) return;

        // Try graceful close first
        if (process.MainWindowHandle != 0)
        {
            PostMessage(process.MainWindowHandle, WM_CLOSE, 0, 0);

            var deadline = DateTime.UtcNow + gracePeriod;
            while (!process.HasExited && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
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
        NativeMethods.ShowWindow(hwnd, SW_SHOW);
    }

    public void HideWindow(nint hwnd)
    {
        NativeMethods.ShowWindow(hwnd, SW_HIDE);
    }

    public void SetWindowTitle(nint hwnd, string title)
    {
        SetWindowText(hwnd, title);
    }

    public void MoveWindow(nint hwnd, int x, int y)
    {
        SetWindowPos(hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
    }

    public void ShowWindowAt(nint hwnd, int x, int y)
    {
        SetWindowPos(hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_SHOWWINDOW);
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
            _logger.LogError("CreateProcess failed with error {Error}", error);
            return null;
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
            if (rawThreadHandle != 0)
            {
                using var threadHandle = new SafeProcessHandle(rawThreadHandle, ownsHandle: true);
                ResumeThread(threadHandle.DangerousGetHandle());
            }
        }
    }
}
