using System.Diagnostics;
using System.Runtime.InteropServices;
using static D2BotNG.Windows.NativeMethods;
using static D2BotNG.Windows.NativeTypes;

namespace D2BotNG.Windows;

/// <summary>
/// Overwrites the DACL of a target process with the current process's DACL,
/// granting the same access permissions to bypass process protection.
/// </summary>
public class DaclOverwriter
{
    private readonly ILogger<DaclOverwriter> _logger;

    public DaclOverwriter(ILogger<DaclOverwriter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Overwrites the target process's DACL with the current process's DACL.
    /// Call this before injection if OpenProcess with PROCESS_ALL_ACCESS fails.
    /// </summary>
    public bool OverwriteDacl(Process process)
    {
        try
        {
            // Get the DACL of the current process
            using var currentProcess = Process.GetCurrentProcess();
            uint result = GetSecurityInfo(
                currentProcess.Handle,
                SE_OBJECT_TYPE.SE_KERNEL_OBJECT,
                SECURITY_INFORMATION.DACL_SECURITY_INFORMATION,
                out _,
                out _,
                out nint dacl,
                out _,
                out nint rawSecurityDescriptor);

            if (result != 0)
            {
                _logger.LogError("Failed to get current process security info (error {Error})", result);
                return false;
            }

            using var securityDescriptor = new SafeLocalAllocHandle(rawSecurityDescriptor, ownsHandle: true);

            // Open the target process with WRITE_DAC access
            var rawTargetHandle = OpenProcess(WRITE_DAC, false, process.Id);
            if (rawTargetHandle == 0)
            {
                int error = Marshal.GetLastWin32Error();
                _logger.LogError("Failed to open process {Pid} with WRITE_DAC (error {Error})", process.Id, error);
                return false;
            }

            using var targetHandle = new SafeProcessHandle(rawTargetHandle, ownsHandle: true);

            // Set the DACL of the target process
            result = SetSecurityInfo(
                targetHandle.DangerousGetHandle(),
                SE_OBJECT_TYPE.SE_KERNEL_OBJECT,
                SECURITY_INFORMATION.DACL_SECURITY_INFORMATION | SECURITY_INFORMATION.UNPROTECTED_DACL_SECURITY_INFORMATION,
                0,
                0,
                dacl,
                0);

            if (result != 0)
            {
                _logger.LogError("Failed to set DACL on process {Pid} (error {Error})", process.Id, result);
                return false;
            }

            _logger.LogDebug("Successfully overwrote DACL for process {Pid}", process.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while overwriting DACL for process {Pid}", process.Id);
            return false;
        }
    }
}
