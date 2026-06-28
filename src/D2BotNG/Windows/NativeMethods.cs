using System.Runtime.InteropServices;
using static D2BotNG.Windows.NativeTypes;

// ReSharper disable InconsistentNaming — Win32 P/Invoke parameter names match API signatures
namespace D2BotNG.Windows;

/// <summary>
/// Win32 API function declarations for native interop.
/// All handle types use nint for consistency.
/// </summary>
public static class NativeMethods
{
    #region kernel32.dll - Process and Memory

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DuplicateHandle(
        nint hSourceProcessHandle,
        nint hSourceHandle,
        nint hTargetProcessHandle,
        out nint lpTargetHandle,
        uint dwDesiredAccess,
        bool bInheritHandle,
        uint dwOptions);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint GetCurrentProcess();


    // dwSize is SIZE_T (pointer-sized: 64-bit on x64), so it must be nuint, not uint.
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint VirtualAllocEx(nint hProcess, nint lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualFreeEx(nint hProcess, nint lpAddress, nuint dwSize, uint dwFreeType);

    // lpflOldProtect is PDWORD (always 32-bit), so out uint is correct; only dwSize (SIZE_T) is pointer-sized.
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualProtectEx(nint hProcess, nint lpAddress, nuint dwSize, uint flNewProtect, out uint lpflOldProtect);

    // nSize and *lpNumberOfBytesWritten are both SIZE_T. The out param is critical: on x64 the
    // kernel writes 8 bytes here, so a 4-byte (int) target would be a stack/heap overrun.
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteProcessMemory(nint hProcess, nint lpBaseAddress, byte[] lpBuffer, nuint nSize, out nuint lpNumberOfBytesWritten);

    // nSize and *lpNumberOfBytesRead are both SIZE_T — pointer-sized on x64. Used to read a target
    // process's PE headers/export table when resolving function addresses inside that process.
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(nint hProcess, nint lpBaseAddress, byte[] lpBuffer, nuint nSize, out nuint lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint GetModuleHandle(string? lpModuleName);

    // Resolves an export in *our own* loaded module — used as the same-bitness fast path, where the
    // address is also valid in a same-flavour target (shared system-DLL base per boot).
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint GetProcAddress(nint hModule, string lpProcName);

    [DllImport("kernel32.dll")]
    public static extern nint LocalFree(nint hMem);

    // Detects a target's bitness: on an x64 OS a WOW64 process is 32-bit. The manager is built x64,
    // so a WOW64 target means cross-bitness injection.
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool IsWow64Process(nint hProcess, out bool wow64Process);

    #endregion

    #region kernel32.dll - Thread

    // dwStackSize is SIZE_T (pointer-sized); lpThreadId is LPDWORD (always 32-bit).
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint CreateRemoteThread(nint hProcess, nint lpThreadAttributes, nuint dwStackSize, nint lpStartAddress, nint lpParameter, uint dwCreationFlags, out uint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint ResumeThread(nint hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    #endregion

    #region kernel32.dll - Process Creation

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcessW(
        string? lpApplicationName,
        string lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        nint lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOW lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    #endregion

    #region kernel32.dll - Job Objects

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint CreateJobObjectW(nint lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetInformationJobObject(nint hJob, int jobObjectInfoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, int cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    #endregion

    #region user32.dll - Window Management

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    // Async (non-blocking) show: posts the request to the window's thread and
    // returns immediately, so a hung game window can't block the caller. The
    // synchronous ShowWindow is intentionally not declared — it blocks on a hung window.
    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern nint FindWindowEx(nint hwndParent, nint hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(nint hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    #endregion

    #region user32.dll - Messaging

    [DllImport("user32.dll")]
    public static extern bool PostMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern nint SendMessageTimeout(
        nint hWnd,
        uint Msg,
        nint wParam,
        nint lParam,
        uint fuFlags,
        uint uTimeout,
        out nint lpdwResult);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint SendMessageTimeoutW(
        nint hWnd,
        uint Msg,
        nint wParam,
        nint lParam,
        uint fuFlags,
        uint uTimeout,
        out nint lpdwResult);

    [DllImport("user32.dll")]
    public static extern nint SendMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    #endregion

    #region user32.dll - Window Info

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hWnd);

    // True when the window has stopped pumping messages (OS hang detection, ~5s).
    // Non-blocking — detects a frozen game whose background heartbeat thread may still tick.
    [DllImport("user32.dll")]
    public static extern bool IsHungAppWindow(nint hWnd);


    #endregion

    #region advapi32.dll - Security

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern uint GetSecurityInfo(
        nint handle,
        SE_OBJECT_TYPE objectType,
        SECURITY_INFORMATION securityInfo,
        out nint ppsidOwner,
        out nint ppsidGroup,
        out nint ppDacl,
        out nint ppSacl,
        out nint ppSecurityDescriptor);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern uint SetSecurityInfo(
        nint handle,
        SE_OBJECT_TYPE objectType,
        SECURITY_INFORMATION securityInfo,
        nint psidOwner,
        nint psidGroup,
        nint pDacl,
        nint pSacl);

    #endregion

    #region psapi.dll - Module Enumeration

    // Reads a remote module's base address from the loader's module list. The base is the HMODULE
    // value (pointer-width), so it must be captured as nint — on x64 a 32-bit thread exit code
    // (the old approach) would truncate it.
    [DllImport("psapi.dll", SetLastError = true)]
    public static extern bool EnumProcessModulesEx(nint hProcess, [Out] nint[] lphModule, uint cb, out uint lpcbNeeded, uint dwFilterFlag);

    [DllImport("psapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint GetModuleFileNameExW(nint hProcess, nint hModule, System.Text.StringBuilder lpFilename, uint nSize);

    #endregion

    /// <summary>
    /// Async polling wrapper around WaitForSingleObject.
    /// Polls instead of blocking so the calling thread is released between checks.
    /// </summary>
    public static async Task<bool> WaitForSingleObjectAsync(nint handle, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (WaitForSingleObject(handle, 0) == 0) // WAIT_OBJECT_0
                return true;
            await Task.Delay(50);
        }
        return false;
    }
}
