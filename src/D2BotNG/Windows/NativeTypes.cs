using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming — Win32 API names follow C convention
namespace D2BotNG.Windows;

/// <summary>
/// Win32 structs, enums, delegates, and constants for native interop.
/// </summary>
public static class NativeTypes
{
    // Window procedure delegate
    public delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    #region Structs

    [StructLayout(LayoutKind.Sequential)]
    public struct COPYDATASTRUCT
    {
        public nint dwData;
        public int cbData;
        public nint lpData;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFOW
    {
        public uint cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    #endregion

    #region Enums

    [Flags]
    public enum SECURITY_INFORMATION : uint
    {
        DACL_SECURITY_INFORMATION = 0x00000004,
        UNPROTECTED_DACL_SECURITY_INFORMATION = 0x20000000
    }

    public enum SE_OBJECT_TYPE
    {
        SE_KERNEL_OBJECT = 6
    }

    #endregion

    #region Constants

    // Process access rights (DWORD)
    public const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
    public const uint PROCESS_TERMINATE = 0x0001;
    public const uint PROCESS_VM_OPERATION = 0x0008;
    public const uint PROCESS_VM_READ = 0x0010;
    public const uint PROCESS_VM_WRITE = 0x0020;
    public const uint PROCESS_CREATE_THREAD = 0x0002;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;
    public const uint WRITE_DAC = 0x00040000;

    // Thread access rights (DWORD)
    public const uint THREAD_SUSPEND_RESUME = 0x0002;

    // Memory allocation flags (DWORD)
    public const uint MEM_COMMIT = 0x1000;
    public const uint MEM_RESERVE = 0x2000;
    public const uint MEM_RELEASE = 0x8000;

    // Memory protection flags (DWORD)
    public const uint PAGE_READWRITE = 0x04;
    public const uint PAGE_EXECUTE_READWRITE = 0x40;

    // Process creation flags (DWORD)
    public const uint CREATE_SUSPENDED = 0x00000004;

    // Window show commands (int - used with ShowWindow)
    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;

    // Window position flags (UINT)
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_SHOWWINDOW = 0x0040;

    // Window messages (UINT)
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_COPYDATA = 0x004A;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint WM_NCLBUTTONDOWN = 0x00A1;

    // System command values (used as wParam, pointer-sized)
    public const nint SC_CLOSE = 0xF060;

    // SendMessageTimeout flags (UINT)
    public const uint SMTO_NORMAL = 0x0000;
    public const uint SMTO_ABORTIFHUNG = 0x0002;
    public const uint SMTO_NOTIMEOUTIFNOTHUNG = 0x0008;

    // Hit test values (int - LRESULT cast to int)
    public const int HTCAPTION = 2;

    // Special window handles
    public static readonly nint HWND_MESSAGE = -3;

    // Wait constants
    public const uint INFINITE = 0xFFFFFFFF;

    #endregion
}
