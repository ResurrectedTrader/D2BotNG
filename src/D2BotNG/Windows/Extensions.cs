using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using D2BotNG.Logging;
using static D2BotNG.Windows.NativeMethods;
using static D2BotNG.Windows.NativeTypes;
using ILogger = Serilog.ILogger;

namespace D2BotNG.Windows;

public static class Extensions
{
    private static readonly ILogger Logger = TrackingLoggerFactory.ForContext(typeof(Extensions));

    /// <summary>
    /// Window class names recognized as "the game" by <c>GameWindow</c>. Add new
    /// game variants here as we support them — Project Diablo 2, D2R if we ever do, etc.
    /// </summary>
    private static readonly HashSet<string> GameWindowClassNames = new(StringComparer.Ordinal)
    {
        "Diablo II",
    };

    extension(Process proc)
    {
        /// <summary>
        /// Every top-level window owned by the process.
        /// </summary>
        public IReadOnlyList<nint> TopLevelWindows
        {
            get
            {
                var pid = (uint)proc.Id;
                var windows = new List<nint>();
                EnumWindows((hWnd, _) =>
                {
                    GetWindowThreadProcessId(hWnd, out var windowPid);
                    if (windowPid == pid) windows.Add(hWnd);
                    return true;
                }, 0);
                return windows;
            }
        }

        /// <summary>
        /// The game's primary window, identified by class name match against known game
        /// variants (see <see cref="GameWindowClassNames"/>). Returns 0 if no matching
        /// window exists yet — used as the launch-readiness signal and as the stable
        /// routing key for WM_COPYDATA from D2BS.
        /// </summary>
        /// <remarks>
        /// We deliberately do not fall back to <see cref="Process.MainWindowHandle"/>:
        /// it uses a heuristic (first top-level window meeting certain style criteria)
        /// that can resolve to a non-game window — a launcher/splash hwnd during startup,
        /// or a drifted top-level after a handoff where the successor inspects a
        /// long-running game and gets a different "main" than the predecessor saw at
        /// launch. Either case mis-routes the handle map and silently drops every
        /// message from that profile.
        /// </remarks>
        public nint GameWindow
        {
            get
            {
                var sb = new StringBuilder(256);
                foreach (var hwnd in proc.TopLevelWindows)
                {
                    sb.Clear();
                    var written = GetClassNameW(hwnd, sb, sb.Capacity);
                    if (written > 0 && GameWindowClassNames.Contains(sb.ToString()))
                    {
                        return hwnd;
                    }
                }

                return 0;
            }
        }
    }

    /// <summary>
    /// Sends a WM_COPYDATA message to every top-level window owned by the process.
    /// D2BS only hooks one of them and only that hook will fire; the others receive
    /// an unfamiliar WM_COPYDATA and their default WndProc handling is harmless.
    /// We broadcast because the window D2BS is hooked on isn't reliably the one
    /// <see cref="Process.MainWindowHandle"/> returns (see <c>GameWindow</c>).
    /// </summary>
    public static bool SendMessage(this Process proc, MessageType messageType, string data)
    {
        var windows = proc.TopLevelWindows;
        if (windows.Count == 0)
        {
            // Debug: this is the already-exited process case, not a fault — a launched game
            // always has a window. Keeping the routing entry alive through teardown means a
            // queued reply to a game that has since died lands here routinely.
            Logger.Debug("SendMessage: no top-level windows found for PID {Pid}", proc.Id);
            return false;
        }

        Logger.Debug("Sending {MessageType} {Data} to PID {Pid} ({Count} windows)",
            messageType, data, proc.Id, windows.Count);

        var anySucceeded = false;
        foreach (var hwnd in windows)
        {
            if (SendCopyData(hwnd, messageType, data)) anySucceeded = true;
        }

        // Note what this return actually means. SendMessageTimeout reports whether the *send*
        // completed; the window's own answer goes to lpdwResult, which SendCopyData discards. A
        // window with no D2BS hook runs DefWindowProc, returns promptly, and counts as a success.
        // So "true" means "some window of this process pumped the message", NOT "D2BS took it" —
        // and a false is a hung or vanished window, not an unhooked one.
        //
        // Not logged as a fault at any level, even in aggregate: the states that produce it are a
        // game mid-launch and a game being torn down, both routine, and at fleet scale warning on
        // them filled the console during exactly the restart burst a user is already worried
        // about. Callers that can attribute it to a profile report it instead — see
        // ProfileEngine.SendAndReport and the handoff adoption path.
        if (!anySucceeded)
        {
            Logger.Debug("No window of PID {Pid} pumped {MessageType} ({Count} tried)",
                proc.Id, messageType, windows.Count);
        }

        return anySucceeded;
    }

    private static bool SendCopyData(nint hwnd, MessageType messageType, string data)
    {
        // D2BS reads a null terminated string, add null byte at the end.
        var bytes = Encoding.ASCII.GetBytes(data + '\0');
        var pData = Marshal.AllocHGlobal(bytes.Length);

        try
        {
            Marshal.Copy(bytes, 0, pData, bytes.Length);

            var copyData = new COPYDATASTRUCT
            {
                dwData = (nint)messageType,
                cbData = bytes.Length,
                lpData = pData
            };

            var pCopyData = Marshal.AllocHGlobal(Marshal.SizeOf<COPYDATASTRUCT>());
            try
            {
                Marshal.StructureToPtr(copyData, pCopyData, false);

                var result = SendMessageTimeout(
                    hwnd,
                    WM_COPYDATA,
                    0,
                    pCopyData,
                    SMTO_ABORTIFHUNG,
                    250,
                    out _);

                if (result == 0)
                {
                    // Debug, not Warning: this means the window didn't pump within the timeout,
                    // which for a broadcast across every top-level window a game owns is routine
                    // — some are transient, and a game mid-launch or mid-teardown pumps none.
                    // The aggregate in SendMessage is where a real failure is judged.
                    Logger.Debug("Window {Hwnd} did not pump WM_COPYDATA within the timeout", hwnd);
                    return false;
                }

                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(pCopyData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pData);
        }
    }
}
